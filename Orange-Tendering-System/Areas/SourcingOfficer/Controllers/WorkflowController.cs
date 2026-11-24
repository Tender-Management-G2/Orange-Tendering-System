using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Orange_Tendering_System.Controllers;
using Orange_Tendering_System.Data;
using Orange_Tendering_System.Models;
using Orange_Tendering_System.Models.Entities;
using Orange_Tendering_System.Services;
using Orange_Tendering_System.ViewModels;

namespace Orange_Tendering_System.Areas.SourcingOfficer.Controllers;

[Area("SourcingOfficer"), Authorize(Roles = SystemRoles.SourcingOfficer)]
public class WorkflowController(TenderWorkflowService workflow, ProtectedDocumentStore documents, OllamaBidAnalysisService ai, EvaluationSchedulingService scheduling, ApplicationDbContext db)
    : WorkflowControllerBase(workflow, documents)
{
    // The tender form is a popup on the list page (Views/Shared/_TenderCreateDialog.cshtml); this old address only opens that popup.
    [HttpGet] public IActionResult Create(int? id) => RedirectToAction(nameof(Index), new { request = (int?)(id > 0 ? id : null), @new = (int?)(id > 0 ? null : 1) });
    [HttpPost] public async Task<IActionResult> Create(RequirementsInput input)
    {
        var ajax = string.Equals(Request.Headers["X-Requested-With"], "XMLHttpRequest", StringComparison.OrdinalIgnoreCase);
        var ar = UiLanguage.IsArabic(HttpContext);
        if (!ModelState.IsValid)
        {
            if (!ajax) return RedirectToAction(nameof(Index), new { request = (int?)(input.PurchaseRequestId > 0 ? input.PurchaseRequestId : null) });
            return BadRequest(new { fields = ModelState.Where(entry => entry.Value is { Errors.Count: > 0 }).ToDictionary(entry => entry.Key,
                entry => entry.Value!.Errors.Select(e => ar ? "أدخل قيمة صحيحة لهذا الحقل."
                    : string.IsNullOrWhiteSpace(e.ErrorMessage) ? "A field has an invalid value." : e.ErrorMessage).Distinct().ToArray()) });
        }
        try
        {
            var id = await Workflow.Create(input, User);
            var created = OrangeFeedback.Popup(ar ? "تم إنشاء المناقصة" : "Tender created", $"T-{id:0000}", OrangeVariant.Save);
            if (ajax) return Json(new { ok = true, id, feedback = created.ToJsonElement() });
            TempData.Orange(created);
            return RedirectToAction(nameof(Index), new { created = id, open = id });
        }
        catch (TenderCreationFieldException e) when (ajax)
        {
            return BadRequest(new { fields = new Dictionary<string, string[]> { [e.Field] = [ar ? e.ArabicMessage : e.Message] } });
        }
        catch (WorkflowException e) when (ajax) { return BadRequest(new { errors = new[] { e.Message } }); }
    }
    [HttpPost] public Task<IActionResult> RequestWeights(int id) => Execute(id, () => Workflow.RequestWeights(id, User));
    [HttpPost] public Task<IActionResult> ApproveWeights(int id, List<TechnicalCriterionInput> criteria) => Execute(id, () =>
    {
        if (!ModelState.IsValid) throw new WorkflowException("Enter a valid weight for each technical criterion.");
        return Workflow.ApproveWeights(id, User, criteria);
    });
    [HttpPost] public Task<IActionResult> FinalizeFinancialReview(int id) => Execute(id, () => Workflow.FinalizeFinancialReview(id, User));
    [HttpPost] public Task<IActionResult> ReturnWeights(int id, int[] memberIds, string note) => Execute(id, () => Workflow.ReturnWeights(id, memberIds, note, User));
    [HttpPost] public Task<IActionResult> SubmitPreTender(int id, string? note) => Execute(id, () => Workflow.SubmitPreTender(id, note, User));
    [HttpPost] public Task<IActionResult> OpenBidding(int id, DateTimeOffset deadline) => Execute(id, () => Workflow.OpenBidding(id, deadline, User));
    [HttpPost] public Task<IActionResult> SetMissingDeadline(int id, DateTimeOffset deadline) => Execute(id, () => Workflow.SetMissingBiddingDeadline(id, deadline, User));
    // One click: invite every approved supplier that is not yet invited in the current round (each invitation follows the normal rules and is audited).
    [HttpPost] public Task<IActionResult> InviteAll(int id, int[]? supplierIds) => Execute(id, async () =>
    {
        var round = await db.TenderRounds.AsNoTracking().Where(r => r.TenderId == id).OrderByDescending(r => r.RoundNumber).FirstOrDefaultAsync();
        TenderWorkflowService.Require(round != null, "Tender round not found.");
        var already = await db.TenderInvitations.AsNoTracking().Where(i => i.TenderRoundId == round!.Id).Select(i => i.SupplierId).ToListAsync();
        var wanted = supplierIds is { Length: > 0 } ? supplierIds.ToHashSet() : null;
        var ids = await db.Suppliers.AsNoTracking().Where(s => s.Status == SupplierStatus.Approved
            && db.Users.Any(u => u.SupplierId == s.Id && u.IsActive && u.EmailConfirmed)
            && !already.Contains(s.Id) && (wanted == null || wanted.Contains(s.Id))).OrderBy(s => s.CompanyName).Select(s => s.Id).ToListAsync();
        TenderWorkflowService.Require(ids.Count > 0, wanted == null ? "Every approved supplier has already been invited." : "Choose at least one supplier that is not invited yet.");
        foreach (var supplierId in ids) await Workflow.Invite(id, supplierId, User);
        ActionFeedback = OrangeFeedback.Popup("Invitations sent!", $"{ids.Count} supplier{(ids.Count == 1 ? " was" : "s were")} invited to this round.", OrangeVariant.Send);
    });
    [HttpPost] public Task<IActionResult> Invite(int id, int supplierId) => Execute(id, () => Workflow.Invite(id, supplierId, User));
    // The compliance check is automatic: an offer with at least one attached document is compliant; an offer without any document cannot be
    // (the server refuses Compliant when documents are missing). Every result is recorded and audited as the Officer's check. Offers already checked are left alone.
    private async Task AutoCheckOffers(int id)
    {
        if (await db.Tenders.AnyAsync(t => t.Id == id && t.PurchaseRequest.EvaluationMethod == TenderEvaluationMethod.LowestPrice)) return;
        var bids = await db.Bids.AsNoTracking().Where(b => b.Status == BidStatus.Submitted && b.ComplianceStatus == ComplianceStatus.NotChecked && b.TenderInvitation.TenderRound.TenderId == id && b.TenderInvitation.TenderRound.Status == TenderRoundStatus.Closed)
            .Select(b => new { b.Id, Docs = db.Documents.Count(d => d.BidId == b.Id) }).ToListAsync();
        foreach (var bid in bids)
        {
            try
            {
                if (bid.Docs > 0) await Workflow.Compliance(id, bid.Id, ComplianceStatus.Compliant, "Checked automatically: the offer and its supporting documents were received.", "", User);
                else await Workflow.Compliance(id, bid.Id, ComplianceStatus.NonCompliant, "Checked automatically: no supporting document was attached.", "Supporting document", User);
            }
            catch (WorkflowException) { /* leave this offer unchecked; the Officer sees its status in the offers table */ }
        }
    }
    [HttpPost] public Task<IActionResult> CloseBidding(int id) => Execute(id, async () => { await Workflow.CloseBidding(id, User); await AutoCheckOffers(id); });
    [HttpPost] public Task<IActionResult> Retender(int id, string reason) => Execute(id, () => Workflow.Retender(id, reason, User));
    [HttpPost] public Task<IActionResult> Proceed(int id) => Execute(id, async () => { await AutoCheckOffers(id); await Workflow.Proceed(id, User); });
    [HttpPost] public Task<IActionResult> RequestProceed(int id, string justification) => Execute(id, () => Workflow.RequestProceedException(id, justification, User));
    [HttpPost] public Task<IActionResult> InviteExternal(int id, string companyName, string email, string? phone = null) => Execute(id, () => Workflow.AddExternalSupplier(id, companyName, email, phone, User));
    [HttpPost] public Task<IActionResult> ReviseReport(int id, string correction) => Execute(id, () => Workflow.ReviseReport(id, correction, User));
    [HttpPost] public Task<IActionResult> Compliance(int id, int bidId, ComplianceStatus status, string notes, string missing = "") => Execute(id, () => Workflow.Compliance(id, bidId, status, notes, missing, User));
    [HttpPost] public Task<IActionResult> Setup(int id, SessionInput input, CancellationToken ct) => Execute(id, () => scheduling.Schedule(id, input, User, ct));
    [HttpPost] public Task<IActionResult> EndEvaluation(int id, bool confirmed, string reason) => Execute(id, () => Workflow.EndEvaluation(id, confirmed, reason, User));
    [HttpPost] public Task<IActionResult> RescheduleEvaluation(int id, DateTimeOffset startsAt, DateTimeOffset endsAt, string reason) => Execute(id, () => Workflow.RescheduleEvaluation(id, startsAt, endsAt, reason, User));
    // The analysis keeps running even if the browser tab is closed or navigated away (the service has its own timeout), so a click is never lost.
    [HttpPost] public Task<IActionResult> Analyze(int id) => Execute(id, async () => { await AutoCheckOffers(id); await ai.Analyze(id, User, CancellationToken.None); });
    [HttpPost] public Task<IActionResult> ReviewAi(int id, int analysisId) => Execute(id, () => Workflow.ReviewAi(id, analysisId, User));
    [HttpPost] public Task<IActionResult> Forward(int id) => Execute(id, () => Workflow.Forward(id, User));
    [HttpPost] public Task<IActionResult> AssignTop(int id, Dictionary<TopExecutivePosition, string> executives) => Execute(id, () => Workflow.AssignTop(id, executives, User));
    [HttpPost] public Task<IActionResult> Award(int id) => Execute(id, () => Workflow.Award(id, User));
    [HttpPost, RequestSizeLimit(21 * 1024 * 1024)] public Task<IActionResult> Upload(int id, IFormFile file, CancellationToken ct) => Execute(id, () => Documents.Upload(id, null, file, User, ct));
}
