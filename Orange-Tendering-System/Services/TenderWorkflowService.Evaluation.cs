using System.Security.Claims;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Orange_Tendering_System.Models.Entities;
using Orange_Tendering_System.ViewModels;

namespace Orange_Tendering_System.Services;

// The Evaluation Committee workspace: one read model for before, during and after the meeting. Reading is allowed as soon
// as the meeting is scheduled; scoring is allowed only while EvaluationOpen is true (checked again by SubmitEvaluation).
public partial class TenderWorkflowService
{
    public async Task<EvaluationWorkspaceModel> EvaluationWorkspace(int tenderId, ClaimsPrincipal principal)
    {
        var details = await Details(tenderId, principal);
        Require(details.IsEvaluator || details.IsOfficer, "Only this tender's Evaluation Committee members and the Sourcing Officer can open the evaluation workspace.");
        var s = details.Session;
        Require(s != null, "No evaluation meeting is scheduled for this tender yet.");
        var t = details.Tender;
        var request = await db.PurchaseRequests.AsNoTracking().Include(x => x.Department).Include(x => x.Items).ThenInclude(x => x.Category)
            .Include(x => x.Documents).AsSplitQuery().SingleAsync(x => x.Id == t.PurchaseRequestId);
        var categories = await db.ProcurementCategories.AsNoTracking().ToDictionaryAsync(x => x.Id, x => x.Name);

        var eligible = s!.EligibleBids.Select(x => x.BidId).ToHashSet();
        var offers = new List<OfferSummary>();
        foreach (var bid in details.Bids.Where(b => eligible.Contains(b.Id)).OrderBy(b => b.TenderInvitation.Supplier.CompanyName))
        {
            var supplier = bid.TenderInvitation.Supplier;
            var record = new SupplierRecord(supplier.Id, supplier.CompanyName, supplier.Category, supplier.Status,
                await db.Documents.CountAsync(d => d.SupplierId == supplier.Id && d.VerificationStatus == DocumentVerificationStatus.Verified),
                await db.Documents.CountAsync(d => d.SupplierId == supplier.Id && d.VerificationStatus == DocumentVerificationStatus.Pending),
                await db.Bids.CountAsync(b => b.Status == BidStatus.Submitted && b.TenderInvitation.SupplierId == supplier.Id && b.TenderInvitation.TenderRound.TenderId != tenderId),
                await db.Tenders.CountAsync(x => x.Id != tenderId && x.AwardedBidId != null && x.AwardedBid!.TenderInvitation.SupplierId == supplier.Id));
            var lines = bid.Items.OrderBy(i => i.TenderItemId).Select(i =>
            {
                var item = t.Items.Single(x => x.Id == i.TenderItemId);
                return new OfferLine(item.Name, item.CategoryId is int c && categories.TryGetValue(c, out var n) ? n : null, item.Quantity, item.Unit, item.Specifications,
                    i.OfferedProduct, i.OfferedSpecifications, i.Quantity, i.UnitPrice, i.Notes);
            }).ToList();
            offers.Add(new OfferSummary(bid, record, lines, details.Documents.Where(d => d.BidId == bid.Id).ToList()));
        }

        var userIds = t.CommitteeMembers.Select(x => x.UserId).ToList();
        var people = await db.Users.AsNoTracking().Where(x => userIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => string.IsNullOrWhiteSpace(x.FullName) ? x.Email ?? "Member" : x.FullName);
        var seat = t.CommitteeMembers.FirstOrDefault(x => x.UserId == details.UserId && x.CommitteeType == CommitteeType.Evaluation);
        var mine = seat == null ? null : s.Invitations.FirstOrDefault(x => x.CommitteeMemberId == seat.Id);
        var requirements = new List<(string, string)>();
        try
        {
            using var json = JsonDocument.Parse(t.Requirements ?? "{}");
            var labels = new Dictionary<string, string> { ["TechnicalRequirements"] = "Technical requirements", ["CommercialConditions"] = "Commercial conditions",
                ["DeliveryExpectations"] = "Delivery expectations", ["MandatoryCompliance"] = "Mandatory requirements" };
            foreach (var p in json.RootElement.EnumerateObject())
                if (p.Value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(p.Value.GetString()))
                    requirements.Add((labels.GetValueOrDefault(p.Name, p.Name), p.Value.GetString()!));
        }
        catch (JsonException) { if (!string.IsNullOrWhiteSpace(t.Requirements)) requirements.Add(("Requirements", t.Requirements)); }

        var model = new EvaluationWorkspaceModel
        {
            Details = details, Request = request, Requirements = requirements, Offers = offers, People = people, MySeat = seat, Mine = mine,
            BeforeStart = Now < s.StartsAt, Ai = details.Analyses.FirstOrDefault(x => x.Id == s.AiAnalysisId),
            CanScore = details.IsEvaluator && mine is { SubmittedAt: null } && t.Status == TenderStatus.Evaluation && EvaluationOpen(s, Now),
            RequiredDocuments = OfferDocumentTypes.Parse(t.RequiredDocuments)
        };
        if (s.ReportId is int reportId) model.Report = await ReviewReport(reportId, principal);
        return model;
    }
}
