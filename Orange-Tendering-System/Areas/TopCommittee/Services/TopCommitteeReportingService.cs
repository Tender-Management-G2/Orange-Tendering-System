using Microsoft.EntityFrameworkCore;
using Orange_Tendering_System.Areas.TopCommittee.ViewModels;
using Orange_Tendering_System.Data;
using Orange_Tendering_System.Models.Entities;

namespace Orange_Tendering_System.Areas.TopCommittee.Services;

public sealed class TopCommitteeReportingService(ApplicationDbContext db)
{
    public async Task<ReportingData> Read()
    {
        var now = DateTime.UtcNow;
        var firstMonth = new DateTime(now.Year, now.Month, 1).AddMonths(-5);
        var nextMonth = new DateTime(now.Year, now.Month, 1).AddMonths(1);
        // No joins in the entity totals; stored bid revisions count as distinct records.
        var suppliers = await db.Suppliers.CountAsync();
        var tenders = await db.Tenders.CountAsync();
        var requests = await db.PurchaseRequests.CountAsync();
        var bids = await db.Bids.CountAsync();
        var evaluations = db.CommitteeRounds.Where(r => r.CommitteeType == CommitteeType.Evaluation);
        var evaluationCount = await evaluations.CountAsync();
        var finishedEvaluations = await evaluations.Where(r => r.Status != CommitteeRoundStatus.InProgress && r.CompletedAt != null)
            .Select(r => new { r.StartedAt, r.CompletedAt }).ToListAsync();
        var elapsed = finishedEvaluations.Where(r => r.StartedAt > DateTime.MinValue && r.CompletedAt >= r.StartedAt)
            .Select(r => (r.CompletedAt!.Value - r.StartedAt).TotalDays).ToList();
        var acceptedReviews = await db.PurchaseRequestReviews.Where(r => r.Decision == ReviewDecision.Accepted && r.ReviewedAt != null)
            .Select(r => new { r.PurchaseRequestId, r.SubmissionNumber, r.SubmittedAt, r.ReviewedAt }).ToListAsync();
        var approvalDurations = acceptedReviews.GroupBy(r => r.PurchaseRequestId).Select(g => g.OrderByDescending(r => r.SubmissionNumber).First())
            .Where(r => r.SubmittedAt > DateTime.MinValue && r.ReviewedAt >= r.SubmittedAt)
            .Select(r => (r.ReviewedAt!.Value - r.SubmittedAt).TotalDays).ToList();
        var invitationCount = await db.TenderInvitations.CountAsync();
        var submittedInvitations = await db.TenderInvitations.CountAsync(i => i.Bids.Any(b => b.SubmittedAt != null));
        var zeroBids = await db.Tenders.CountAsync(t => !t.Rounds.Any(r => r.Invitations.Any(i => i.Bids.Any(b => b.SubmittedAt != null))));
        var completedVotes = await db.CommitteeVotes.CountAsync(v =>
            (v.CommitteeRound.CommitteeType == CommitteeType.Top || v.CommitteeRound.CommitteeType == TopCommitteeCompatibility.LegacyTop) &&
            (v.CommitteeMember.CommitteeType == CommitteeType.Top || v.CommitteeMember.CommitteeType == TopCommitteeCompatibility.LegacyTop) &&
            v.CommitteeMember.TenderId == v.CommitteeRound.TenderRound.TenderId &&
            (v.Decision == VoteDecision.Approve || v.Decision == VoteDecision.Reject));
        var rawTenderGroups = await db.Tenders.GroupBy(t => t.Status).Select(g => new { Status = g.Key, Count = g.Count() }).ToListAsync();
        var tenderGroups = rawTenderGroups.GroupBy(g => g.Status).ToDictionary(g => g.Key, g => g.Sum(r => r.Count));
        var awarded = tenderGroups.GetValueOrDefault(TenderStatus.Awarded);
        var closed = tenderGroups.GetValueOrDefault(TenderStatus.Cancelled) + tenderGroups.GetValueOrDefault(TenderStatus.Rejected);
        var rawRequestGroups = await db.PurchaseRequests.GroupBy(p => p.Status).Select(g => new { Status = g.Key, Count = g.Count() }).ToListAsync();
        var requestGroups = rawRequestGroups.GroupBy(g => g.Status == PurchaseRequestStatus.Approved ? PurchaseRequestStatus.Accepted : g.Status)
            .ToDictionary(g => g.Key, g => g.Sum(r => r.Count));
        var requestStatuses = new List<ReportPoint>
        {
            new("Draft", requestGroups.GetValueOrDefault(PurchaseRequestStatus.Draft), "#a2a9b0"),
            new("Submitted", requestGroups.GetValueOrDefault(PurchaseRequestStatus.Submitted), "#087dff"),
            new("Under Review", requestGroups.GetValueOrDefault(PurchaseRequestStatus.UnderReview), "#ffbc00"),
            new("Accepted", requestGroups.GetValueOrDefault(PurchaseRequestStatus.Accepted), "#24bb40"),
            new("Rejected", requestGroups.GetValueOrDefault(PurchaseRequestStatus.Rejected), "#ff454f"),
            new("Returned", requestGroups.GetValueOrDefault(PurchaseRequestStatus.Returned), "#8251ff")
        };
        if (requestGroups.GetValueOrDefault(PurchaseRequestStatus.ConvertedToTender) > 0)
            requestStatuses.Add(new("Converted to Tender", requestGroups[PurchaseRequestStatus.ConvertedToTender], "#19a4ad"));
        var approvedSuppliers = await db.Suppliers.CountAsync(s => s.Status == SupplierStatus.Approved);
        var suspendedSuppliers = await db.Suppliers.CountAsync(s => s.Status == SupplierStatus.Suspended);
        var supplierRows = await db.Suppliers.Select(s => new
        {
            s.Id, s.CompanyName, Transactions = s.Invitations.Count(i => i.Bids.Any(b => b.SubmittedAt != null)),
            Awards = db.Tenders.Count(t => t.Status == TenderStatus.Awarded && t.AwardedBidId != null &&
                t.AwardedBid!.TenderInvitation.SupplierId == s.Id)
        }).OrderByDescending(s => s.Transactions).ThenBy(s => s.CompanyName).ThenBy(s => s.Id).ToListAsync();

        var requestMonths = await db.PurchaseRequests.Where(p => p.CreatedAt >= firstMonth && p.CreatedAt < nextMonth)
            .GroupBy(p => new { p.CreatedAt.Year, p.CreatedAt.Month }).Select(g => new { g.Key.Year, g.Key.Month, Count = g.Count() }).ToListAsync();
        var tenderMonths = await db.Tenders.Where(p => p.CreatedAt >= firstMonth && p.CreatedAt < nextMonth)
            .GroupBy(p => new { p.CreatedAt.Year, p.CreatedAt.Month }).Select(g => new { g.Key.Year, g.Key.Month, Count = g.Count() }).ToListAsync();
        var bidMonths = await db.Bids.Where(p => p.CreatedAt >= firstMonth && p.CreatedAt < nextMonth)
            .GroupBy(p => new { p.CreatedAt.Year, p.CreatedAt.Month }).Select(g => new { g.Key.Year, g.Key.Month, Count = g.Count() }).ToListAsync();
        var evalMonths = await evaluations.Where(p => p.StartedAt >= firstMonth && p.StartedAt < nextMonth)
            .GroupBy(p => new { p.StartedAt.Year, p.StartedAt.Month }).Select(g => new { g.Key.Year, g.Key.Month, Count = g.Count() }).ToListAsync();
        var months = Enumerable.Range(0, 6).Select(offset =>
        {
            var date = firstMonth.AddMonths(offset);
            return new ActivityMonth(date.ToString("MMM yy", System.Globalization.CultureInfo.InvariantCulture),
                requestMonths.FirstOrDefault(m => m.Year == date.Year && m.Month == date.Month)?.Count ?? 0,
                tenderMonths.FirstOrDefault(m => m.Year == date.Year && m.Month == date.Month)?.Count ?? 0,
                bidMonths.FirstOrDefault(m => m.Year == date.Year && m.Month == date.Month)?.Count ?? 0,
                evalMonths.FirstOrDefault(m => m.Year == date.Year && m.Month == date.Month)?.Count ?? 0);
        }).ToList();
        return new ReportingData
        {
            Suppliers = suppliers, Tenders = tenders, Requests = requests, Bids = bids, Awards = awarded,
            Evaluations = evaluationCount, CompletedVotes = completedVotes, ZeroBidTenders = zeroBids,
            ParticipationRate = ReportFormat.Ratio(submittedInvitations, invitationCount),
            EvaluationDays = elapsed.Count > 0 ? elapsed.Average() : null,
            RequestApprovalDays = approvalDurations.Count > 0 ? approvalDurations.Average() : null,
            TenderStatuses = [new("Open", tenders - closed - awarded, "#087dff"), new("Closed", closed, "#ff9500"), new("Awarded", awarded, "#22bc36")],
            RequestStatuses = requestStatuses,
            SupplierStatuses = [new("Registered", suppliers, "#087dff"), new("Approved", approvedSuppliers, "#24bb40"), new("Suspended", suspendedSuppliers, "#ff454f")],
            SupplierRows = supplierRows.Select(s => new SupplierPerformance(s.CompanyName, s.Transactions, s.Awards)).ToList(),
            Months = months,
            Summary = [new("Purchase Requests", requests, ReportFormat.Percent(ReportFormat.Ratio(requestGroups.GetValueOrDefault(PurchaseRequestStatus.Accepted), requests)) + " Accepted"),
                new("Tenders", tenders, ReportFormat.Percent(ReportFormat.Ratio(awarded, tenders)) + " Awarded"),
                new("Suppliers", suppliers, ReportFormat.Percent(ReportFormat.Ratio(approvedSuppliers, suppliers)) + " Approved"),
                new("Bids", bids, tenders > 0 ? $"Avg. {(bids / (decimal)tenders):0.#} bid records per tender" : "No tenders"),
                new("Evaluations", evaluationCount, ReportFormat.Percent(ReportFormat.Ratio(finishedEvaluations.Count, evaluationCount)) + " Completed")]
        };
    }
}
