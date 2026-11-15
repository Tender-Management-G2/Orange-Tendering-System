// ORANGE TENDER MANAGEMENT SYSTEM
// Module: Top Committee - final decision pages (spec 4.7): review of results, voting entry point and bid history.
// These pages are read-only. The vote and the signature are cast on the signed report page (Reports/Review -> TenderWorkflowService.TopVote),
// which enforces the Manager's prior signature, the committee signatures, one vote per member and the 3-of-4 approval rule.
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Orange_Tendering_System.Areas.TopCommittee.Services;
using Orange_Tendering_System.Models;

<<<<<<< HEAD
namespace Orange_Tendering_System.Areas.TopCommittee.Controllers
=======
namespace Orange_Tendering_System.Areas.TopCommittee.Controllers;

[Area("TopCommittee"), Authorize(Roles = TopExecutiveRoles.Allowed)]
public sealed class DecisionsController(ApplicationDbContext db, UserManager<ApplicationUser> users,
    IWebHostEnvironment environment, ILogger<DecisionsController> logger, IRazorViewEngine views) : TopCommitteeControllerBase(db, users)
>>>>>>> be32000 (temp)
{
    [Area("TopCommittee")]
    [Authorize(Roles = SystemRoles.TopCommittee)]
    public class DecisionsController(TopCommitteeService service) : Controller
    {
<<<<<<< HEAD
        private string UserId => User.FindFirstValue(ClaimTypes.NameIdentifier)!;
=======
        var user = await ActiveUser();
        if (user is null) return Forbid();
        Response.Headers.CacheControl = "no-store";
        q = q?.Trim();
        if (string.IsNullOrEmpty(q)) return Json(new { items = Array.Empty<object>() });
        var rounds = await TopCommitteeSearch.Apply(Committee.RoundList(user.Id).AsNoTracking(), q, null)
            .OrderByDescending(r => r.StartedAt).ThenByDescending(r => r.Id).Take(8)
            .Select(r => new { r.Id, r.TenderRound.TenderId, r.TenderRound.Tender.Title }).ToListAsync();
        return Json(new { items = rounds.Select(r => new {
            title = r.Title, subtitle = $"T-{r.TenderId:0000}",
            url = Url.Action(nameof(Decision), "Decisions", new { area = "TopCommittee", id = r.Id })
        }) });
    }
>>>>>>> be32000 (temp)

        public IActionResult Index() => RedirectToAction(nameof(ReviewEvaluation));

        // GET /TopCommittee/Decisions/ReviewEvaluation/{id?}: the tenders waiting for me, or one tender's full results.
        public async Task<IActionResult> ReviewEvaluation(int? id)
        {
            if (id == null) return View("Cases", (await service.Cases(UserId)).Where(c => c.Outcome == null).ToList());
            var details = await service.Details(UserId, id.Value);
            return details == null ? NotFound() : View(details);
        }

        // GET /TopCommittee/Decisions/Decision/{id?}: voting status of a tender and the entry to the signed report page.
        public async Task<IActionResult> Decision(int? id)
        {
            if (id == null) return View("Cases", (await service.Cases(UserId)).Where(c => c.AwaitingMyVote).ToList());
            var details = await service.Details(UserId, id.Value);
            return details == null ? NotFound() : View(details);
        }

        // GET /TopCommittee/Decisions/History: every tender I was assigned to with its outcome.
        public async Task<IActionResult> History(int? id)
        {
            if (id != null) return RedirectToAction(nameof(ReviewEvaluation), new { id });
            return View((await service.Cases(UserId)).Where(c => c.Outcome != null || c.MyVote != null).ToList());
        }
<<<<<<< HEAD
=======
        catch (JsonException) { return false; }
    }

    [HttpGet]
    public async Task<IActionResult> SummaryPdfPreview(int roundId, CancellationToken cancellationToken)
    {
        var user = await ActiveUser();
        if (user is null) return Forbid();
        var details = await Committee.Load(user.Id, roundId);
        if (details?.Round.TenderReport is not { ReportType: ReportType.ExecutiveSummary, SubmittedAt: not null, Status: not ReportStatus.Draft }) return NotFound();
        try
        {
            var bytes = await GetOrCreateOriginalPdf(details, user.Id, cancellationToken);
            Response.Headers.CacheControl = "no-store";
            Response.Headers["X-Content-Type-Options"] = "nosniff";
            return File(bytes, "application/pdf");
        }
        catch (InvalidOperationException exception) { return Problem(title: "Original report unavailable", detail: exception.Message, statusCode: 503); }
    }

    [HttpGet]
    public Task<IActionResult> SummaryPdf(int id, CancellationToken cancellationToken) => SummaryPdfDownload(id, cancellationToken);

    private async Task<IActionResult> SummaryPdfDownload(int roundId, CancellationToken cancellationToken)
    {
        var user = await ActiveUser();
        if (user is null) return Forbid();
        var details = await Committee.Load(user.Id, roundId);
        if (details?.Round.TenderReport is not { ReportType: ReportType.ExecutiveSummary, SubmittedAt: not null, Status: not ReportStatus.Draft } report) return NotFound();
        try
        {
            var bytes = await GetOrCreateOriginalPdf(details, user.Id, cancellationToken);
            Response.Headers.CacheControl = "no-store";
            Response.Headers["X-Content-Type-Options"] = "nosniff";
            return File(bytes, "application/pdf", $"Manager-Summary-T{details.Tender.Id}-R{report.Id}-V{report.VersionNumber}.pdf");
        }
        catch (InvalidOperationException exception) { return Problem(title: "Original report unavailable", detail: exception.Message, statusCode: 503); }
    }

    [HttpGet, AllowAnonymous]
    public async Task<IActionResult> FinalSignedPdf(int roundId, bool download = true, CancellationToken cancellationToken = default)
    {
        var user = await ActiveUser();
        if (user is null) return Forbid();
        Document? document;
        if (TopExecutiveRoles.IsMember(User))
        {
            var details = await Committee.Load(user.Id, roundId);
            if (details?.FinalSignedPdfReady != true) return NotFound();
            document = details.FinalSignedSummaryPdf!;
        }
        else if (await Users.IsInRoleAsync(user, SystemRoles.SourcingManager))
        {
            var round = await Db.CommitteeRounds.Include(r => r.TenderRound).ThenInclude(r => r.Tender)
                .Include(r => r.TenderReport).ThenInclude(r => r!.Documents)
                .Include(r => r.Votes).AsSplitQuery().SingleOrDefaultAsync(r => r.Id == roundId &&
                    (r.CommitteeType == CommitteeType.Top || r.CommitteeType == TopCommitteeCompatibility.LegacyTop));
            if (round is not { Status: CommitteeRoundStatus.Approved, TenderReport: not null } ||
                round.TenderReport.Status != ReportStatus.Accepted || round.TenderReport.ReportType != ReportType.ExecutiveSummary ||
                round.TenderReport.CreatedByUserId != user.Id ||
                !await Db.CommitteeMembers.AnyAsync(m => m.TenderId == round.TenderRound.TenderId &&
                    m.CommitteeType == CommitteeType.Evaluation && m.Responsibility == CommitteeResponsibility.SourcingManager && m.UserId == user.Id))
                return NotFound();
            var members = await Db.CommitteeMembers.CountAsync(m => m.TenderId == round.TenderRound.TenderId &&
                (m.CommitteeType == CommitteeType.Top || m.CommitteeType == TopCommitteeCompatibility.LegacyTop));
            var original = round.TenderReport.Documents.SingleOrDefault(d => d.DocumentType == "TopCommitteeOriginalSummaryPdf");
            var signatures = round.Votes.Where(v => v.Decision == VoteDecision.Approve && v.SignatureReportId == round.TenderReportId &&
                v.SignatureDocumentHash != null && !string.IsNullOrWhiteSpace(v.ElectronicSignatureSvg)).ToList();
            if (members != 4 || signatures.Count != 4 || signatures.Select(v => v.CommitteeMemberId).Distinct().Count() != 4 ||
                original?.ContentHash is null || signatures.Any(v => v.SignatureDocumentHash != original.ContentHash)) return NotFound();
            document = round.TenderReport.Documents.SingleOrDefault(d => d.DocumentType == "TopCommitteeFinalSignedPdf");
            if (document is null) return NotFound();
        }
        else return Forbid();
        var bytes = await ReadStoredPdf(document, cancellationToken);
        if (bytes is null || document.ContentHash != Convert.ToHexString(SHA256.HashData(bytes))) return NotFound();
        Response.Headers.CacheControl = "no-store";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        return download ? File(bytes, "application/pdf", document.FileName) : File(bytes, "application/pdf");
    }

    [HttpGet]
    public async Task<IActionResult> SignatureProgress(int roundId)
    {
        var user = await ActiveUser();
        if (user is null) return Forbid();
        var details = await Committee.Load(user.Id, roundId);
        if (details?.Round.TenderReport is not { ReportType: ReportType.ExecutiveSummary } report) return NotFound();
        var round = details.Round;
        var members = details.TopMembers.OrderBy(m => m.AssignedAt).ThenBy(m => m.Id).ToList();
        Response.Headers.CacheControl = "no-store";
        var result = members.Select(member =>
        {
            var signature = round.Votes.SingleOrDefault(v => v.CommitteeMemberId == member.Id &&
                v.Decision == VoteDecision.Approve && v.SignatureReportId == report.Id && !string.IsNullOrWhiteSpace(v.SignatureDocumentHash) &&
                !string.IsNullOrWhiteSpace(v.ElectronicSignatureSvg));
            var vote = round.Votes.SingleOrDefault(v => v.CommitteeMemberId == member.Id);
            return new { decision = vote?.Decision.ToString(), comment = vote?.Comment,
                votedAt = vote is null ? null : CommitteeDisplay.Date(vote.VotedAt), id = member.Id, name = details.MemberDisplayName(member.UserId, member.User.FullName), signed = signature is not null,
                signedAt = signature is null ? null : CommitteeDisplay.Date(signature.VotedAt),
                signatureSvgBase64 = string.IsNullOrWhiteSpace(signature?.ElectronicSignatureSvg) ? null :
                    Convert.ToBase64String(Encoding.UTF8.GetBytes(signature.ElectronicSignatureSvg)) };
        }).ToList();
        return Json(new { signedCount = result.Count(m => m.signed), roundStatus = round.Status.ToString(), finalSignedPdfReady = details.FinalSignedPdfReady, members = result });
    }

    private string ReportStorageRoot => Path.Combine(environment.ContentRootPath, "Areas", "TopCommittee", "App_Data", "Reports");

    private async Task<byte[]> GetOrCreateOriginalPdf(RoundDetails details, string userId, CancellationToken cancellationToken)
    {
        var report = details.Round.TenderReport!;
        var fileName = $"TopCommittee-Original-R{details.Round.Id}-Report{report.Id}-V{report.VersionNumber}.pdf";
        var htmlFileName = Path.ChangeExtension(fileName, ".html");
        if (details.OriginalSummaryPdf is { } existing)
        {
            var stored = await ReadStoredPdf(existing, cancellationToken);
            if (stored is not null && existing.ContentHash == Convert.ToHexString(SHA256.HashData(stored)) &&
                await ReadOriginalHtml(details, cancellationToken) is not null) return stored;
            if (details.SignedCount > 0)
                throw new InvalidOperationException("The preserved original report file or its source is missing or has failed its integrity check. Existing signatures remain recorded; contact an administrator.");
        }
        var html = await ReportingExport.HtmlView(this, views, details,
            "/Areas/TopCommittee/Views/Decisions/SummaryPdf.cshtml");
        var pdf = await BrowserReportPdf.Create(html, environment.ContentRootPath, cancellationToken);
        var hash = Convert.ToHexString(SHA256.HashData(pdf));
        var root = Path.GetFullPath(ReportStorageRoot);
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, fileName);
        var htmlPath = Path.Combine(root, htmlFileName);
        var resource = $"Orange:TopCommittee:Tender:{details.Tender.Id}";
        await using var transaction = await Db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, cancellationToken);
        await Db.Database.ExecuteSqlInterpolatedAsync($"DECLARE @result int; EXEC @result = sys.sp_getapplock @Resource={resource}, @LockMode='Exclusive', @LockOwner='Transaction', @LockTimeout=30000; IF @result < 0 THROW 51000, 'Committee operation is busy. Retry.', 1;", cancellationToken);
        Db.ChangeTracker.Clear();
        var tracked = await Db.Documents.SingleOrDefaultAsync(d => d.TenderReportId == report.Id && d.DocumentType == "TopCommitteeOriginalSummaryPdf", cancellationToken);
        if (tracked is not null)
        {
            var oldBytes = await ReadStoredPdf(tracked, cancellationToken);
            if (oldBytes is not null && tracked.ContentHash == Convert.ToHexString(SHA256.HashData(oldBytes)))
            {
                if (System.IO.File.Exists(htmlPath))
                {
                    var oldHtml = await ReadOriginalHtml(details, cancellationToken);
                    if (oldHtml is not null)
                    {
                        await transaction.CommitAsync(cancellationToken);
                        return oldBytes;
                    }
                }
            }
            if (await Db.CommitteeVotes.AnyAsync(v => v.SignatureReportId == report.Id, cancellationToken))
                throw new InvalidOperationException("The original report cannot be replaced after signatures have been recorded.");
            tracked.FileName = fileName;
            tracked.StoragePath = fileName;
            tracked.ContentType = "application/pdf";
            tracked.SizeBytes = pdf.LongLength;
            tracked.ContentHash = hash;
            tracked.UploadedByUserId = details.Manager?.Id ?? userId;
            tracked.UploadedAt = DateTime.UtcNow;
        }
        else
        {
            Db.Documents.Add(new Document
            {
                FileName = fileName, StoragePath = fileName, ContentType = "application/pdf", SizeBytes = pdf.LongLength,
                ContentHash = hash, DocumentType = "TopCommitteeOriginalSummaryPdf", UploadedByUserId = details.Manager?.Id ?? userId,
                UploadedAt = DateTime.UtcNow, TenderReportId = report.Id
            });
        }
        await System.IO.File.WriteAllBytesAsync(path, pdf, cancellationToken);
        await System.IO.File.WriteAllTextAsync(htmlPath, html, cancellationToken);
        Db.AuditLogs.Add(new AuditLog
        {
            UserId = userId, Action = "TopCommitteeOriginalPdfPreserved", EntityName = "TenderReport", EntityId = report.Id.ToString(),
            NewValues = System.Text.Json.JsonSerializer.Serialize(new { report.Id, report.VersionNumber, hash, SourceHash = Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(html))), fileName }), CreatedAt = DateTime.UtcNow
        });
        await Db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return pdf;
    }

    private async Task<string?> ReadOriginalHtml(RoundDetails details, CancellationToken cancellationToken)
    {
        var report = details.Round.TenderReport;
        if (report is null) return null;
        var pdfFileName = $"TopCommittee-Original-R{details.Round.Id}-Report{report.Id}-V{report.VersionNumber}.pdf";
        var root = Path.GetFullPath(ReportStorageRoot);
        var path = Path.GetFullPath(Path.Combine(root, Path.ChangeExtension(pdfFileName, ".html")));
        if (!path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || !System.IO.File.Exists(path)) return null;
        var html = await System.IO.File.ReadAllTextAsync(path, cancellationToken);
        var audit = await Db.AuditLogs.AsNoTracking().Where(a => a.Action == "TopCommitteeOriginalPdfPreserved" &&
            a.EntityName == "TenderReport" && a.EntityId == report.Id.ToString())
            .OrderByDescending(a => a.CreatedAt).Select(a => a.NewValues).FirstOrDefaultAsync(cancellationToken);
        if (audit is null) return null;
        try
        {
            using var json = System.Text.Json.JsonDocument.Parse(audit);
            var expected = json.RootElement.GetProperty("SourceHash").GetString();
            var actual = Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(html)));
            return string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase) ? html : null;
        }
        catch (System.Text.Json.JsonException) { return null; }
        catch (KeyNotFoundException) { return null; }
    }

    private async Task<byte[]?> ReadStoredPdf(Document document, CancellationToken cancellationToken)
    {
        if (Path.GetFileName(document.StoragePath) != document.StoragePath ||
            document.DocumentType is not ("TopCommitteeOriginalSummaryPdf" or "TopCommitteeFinalSignedPdf")) return null;
        var root = Path.GetFullPath(ReportStorageRoot);
        var path = Path.GetFullPath(Path.Combine(root, document.StoragePath));
        if (!path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || !System.IO.File.Exists(path)) return null;
        var bytes = await System.IO.File.ReadAllBytesAsync(path, cancellationToken);
        return bytes.Length >= 5 && bytes.AsSpan(0, 5).SequenceEqual("%PDF-"u8) ? bytes : null;
    }

    public async Task<IActionResult> History(int? id, string? search, CommitteeRoundStatus? status, int? tenderId, int page = 1)
    {
        var user = await ActiveUser();
        if (user is null) return Forbid();
        if (status.HasValue && !Enum.IsDefined(status.Value)) return BadRequest();
        Response.Headers.CacheControl = "no-store";
        search = search?.Trim();
        var query = TopCommitteeSearch.Apply(Committee.RoundList(user.Id).AsNoTracking(), search, status);
        if (tenderId.HasValue) query = query.Where(r => r.TenderRound.TenderId == tenderId);
        var total = await query.CountAsync();
        page = Math.Clamp(page, 1, Math.Max(1, (int)Math.Ceiling(total / 20m)));
        var selected = id.HasValue ? await Committee.Load(user.Id, id.Value) : null;
        if (id.HasValue && selected is null) return NotFound();
        if (selected?.Round.TenderReport is { ReportType: ReportType.FullEvaluation } report)
            return RedirectToAction("Review", "SignedReports", new { area = "TopCommittee", id = report.Id });
        var model = new DecisionHistory
        {
            Search = search, Status = status, TenderId = tenderId, Page = page, Total = total, Selected = selected,
            Rounds = await query.OrderByDescending(r => r.StartedAt).ThenByDescending(r => r.Id)
                .Skip((page - 1) * 20).Take(20).AsSplitQuery().ToListAsync()
        };
        return Request.Headers["X-TopCommittee-Live"] == "1" ? PartialView("_HistoryResults", model) : View(model);
    }
    [HttpGet]
    public async Task<IActionResult> Download(int id, int roundId)
    {
        var user = await ActiveUser();
        if (user is null) return Forbid();
        var details = await Committee.Load(user.Id, roundId);
        var document = details?.Documents.SingleOrDefault(d => d.Id == id);
        if (document is null || document.DocumentType != "DepartmentSupportingDocument" ||
            Path.GetFileName(document.StoragePath) != document.StoragePath) return NotFound();
        // This is the sole implemented storage convention in the current project. Other handlers are a team integration.
        var path = Path.Combine(environment.ContentRootPath, "Areas", "Department", "App_Data", "Uploads", document.StoragePath);
        if (!System.IO.File.Exists(path)) return NotFound();
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        return PhysicalFile(path, "application/octet-stream", document.FileName);
>>>>>>> be32000 (temp)
    }
}
