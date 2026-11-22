using System.Security.Claims;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Orange_Tendering_System.Models.Entities;

namespace Orange_Tendering_System.Services;

public partial class TenderWorkflowService
{
    private Task<bool> IsLowestPrice(Tender tender) => db.PurchaseRequests.AnyAsync(
        r => r.Id == tender.PurchaseRequestId && r.EvaluationMethod == TenderEvaluationMethod.LowestPrice);

    internal static decimal TotalOfferPrice(Bid bid) => bid.Items.Sum(item => item.Quantity * item.UnitPrice)
        + bid.ShippingCost + bid.InstallationCost + bid.MaintenanceCost + bid.TaxAmount + bid.OtherCost;

    internal static IReadOnlyList<RankedBid> RankPrices(IEnumerable<Bid> bids)
    {
        var result = new List<RankedBid>();
        foreach (var group in bids.OrderBy(TotalOfferPrice).ThenBy(bid => bid.Id).GroupBy(TotalOfferPrice))
        {
            var rank = result.Count + 1;
            var tied = group.Count() > 1;
            result.AddRange(group.Select(bid => new RankedBid(bid.Id, group.Key, rank, tied)));
        }
        return result;
    }

    public Task FinalizeFinancialReview(int id, ClaimsPrincipal principal) => Change(id, principal,
        "FinancialRecommendationSubmitted", async (tender, actor) =>
        {
            await Officer(tender, actor);
            var round = await Round(id);
            Require(await IsLowestPrice(tender) && tender.Status == TenderStatus.Bidding && round.Status == TenderRoundStatus.Closed
                && !await db.Set<EvaluationSession>().AnyAsync(s => s.TenderRoundId == round.Id),
                "Only a closed financial-only round can be sent to Manager Review.");
            var bids = await Submitted(round.Id);
            Require(bids.All(b => b.ComplianceStatus is ComplianceStatus.Compliant or ComplianceStatus.NonCompliant),
                "Complete compliance screening for every submitted offer first.");
            var eligible = bids.Where(b => b.ComplianceStatus == ComplianceStatus.Compliant
                && b.TenderInvitation.Supplier.Status == SupplierStatus.Approved).ToList();
            Require(eligible.Count > 0, "No compliant offers from approved suppliers. The system cannot make a recommendation.");
            Require(eligible.All(b => b.CurrencyCode == tender.Currency), "Eligible offers must use the tender currency.");
            var ranking = RankPrices(eligible);
            Require(!ranking[0].Tied, "The lowest total price is tied. The system cannot make a recommendation.");
            var manager = await AssignedManagerId(id, null);
            Require(manager != null, "Assign a Sourcing Manager for review.");
            var weights = await Weights(id);
            Require(weights.State == WeightState.Approved && weights.ApprovedFinancial == 100m && weights.ApprovedTechnical == 0m,
                "The financial-only evaluation method must be locked before bidding.");
            var report = new TenderReport
            {
                TenderRoundId = round.Id, ReportType = ReportType.FullEvaluation, Status = ReportStatus.Draft,
                CreatedAt = Now, CreatedByUserId = actor.Id, RecommendedBidId = ranking[0].BidId,
                RankingSnapshotJson = JsonSerializer.Serialize(ranking),
                Content = JsonSerializer.Serialize(new { Justification = "The system recommends the unique lowest total price among compliant offers from approved suppliers." })
            };
            var session = new EvaluationSession
            {
                TenderRound = round, WeightRound = weights, Report = report, State = SessionState.Finalized,
                StartsAt = Now, EndsAt = Now, ClosedAt = Now, ClosedByUserId = actor.Id, ReportSentAt = Now,
                CommitteeRound = new CommitteeRound
                {
                    TenderRound = round, CommitteeType = CommitteeType.FinancialReview, RoundNumber = 1, TenderReport = report,
                    Status = CommitteeRoundStatus.Approved, StartedAt = Now, CompletedAt = Now, FinancialWeight = 100m, TechnicalWeight = 0m
                },
                EligibleBids = eligible.Select(bid => new SessionBid { BidId = bid.Id }).ToList()
            };
            db.Add(session);
            await db.SaveChangesAsync();
            await reportDocuments.Prepare(report, session);
            report.Status = ReportStatus.Submitted;
            report.SubmittedAt = Now;
            round.Status = TenderRoundStatus.SentToEvaluation;
            tender.Status = TenderStatus.ManagerReview;
            Notify(manager!, id, "Financial recommendation ready", "Review the lowest compliant total and the offer comparison.");
        });
}
