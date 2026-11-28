// ORANGE INTELLIGENCE - Tab 1 "Overview": the KPI tiles, the tender lifecycle funnel and "Worth your attention".
// It reuses the tender lifecycles (TenderCalculator) and supplier statistics (SupplierCalculator), so the Overview shows
// exactly the same numbers as the Tenders and Suppliers tabs.

namespace Orange_Tendering_System.Services.Intelligence;

public static class OverviewCalculator
{
    // The five funnel steps, in order. "round" (cycle created) is shown on the Tenders tab only.
    private static readonly string[] FunnelSteps = { "created", "invited", "closed", "awarded", "notified" };

    public static OverviewData Build(ProcurementSnapshot snapshot, int minimumOffers)
    {
        OverviewData data = new OverviewData();
        data.Context = IntelligenceService.BuildContext(snapshot);

        List<TenderDetails> tenders = TenderCalculator.BuildAllTenders(snapshot);
        List<SupplierRow> suppliers = SupplierCalculator.BuildStats(snapshot);

        // ---- KPI 1: tenders in scope ----
        List<double> lifecycleDays = new List<double>();
        foreach (TenderDetails tender in tenders)
        {
            data.TenderCount++;
            if (tender.DaysToNotification != null)
            {
                data.CompletedTenders++;
                lifecycleDays.Add(tender.DaysToNotification.Value);
            }
            if (tender.State == "In progress" || tender.State == "Awarded")
            {
                data.InProgressTenders++;
            }
        }

        // ---- KPI 2: suppliers, and how many submitted an offer in a closed cycle ----
        data.SupplierCount = snapshot.Suppliers.Count;
        HashSet<int> bidders = new HashSet<int>();
        foreach (BidRecord bid in snapshot.Bids)
        {
            bidders.Add(bid.SupplierId);
        }
        data.ParticipatingSuppliers = bidders.Count;

        // ---- KPI 3: submitted offers (closed cycles only; sealed offers are counted, never shown) ----
        data.SubmittedOffers = snapshot.Bids.Count;
        data.SealedOffers = snapshot.SealedBids;

        // ---- KPI 4: median lifecycle = median of (winner notification sent - tender created) ----
        // Example: completed tenders took 10, 14 and 30 days -> median 14 days.
        data.MedianLifecycleDays = IntelligenceService.Median(lifecycleDays);

        // ---- Lifecycle funnel ----
        int total = Math.Max(1, tenders.Count);
        foreach (string key in FunnelSteps)
        {
            LifecycleStep step = new LifecycleStep();
            step.Key = key;
            List<double> days = new List<double>();
            foreach (TenderDetails tender in tenders)
            {
                foreach (TenderStage stage in tender.Stages)
                {
                    if (stage.Key == key && stage.At != null)
                    {
                        step.Reached++;
                        days.Add(stage.DaysFromCreation ?? 0);
                    }
                }
            }
            step.Share = (double)step.Reached / total;
            step.MedianDays = IntelligenceService.Median(days);
            data.Lifecycle.Add(step);
        }

        // ---- Worth your attention ----
        // (a) Closed cycles with fewer offers than the minimum.
        List<RoundCompetition> lowRounds = new List<RoundCompetition>();
        foreach (RoundCompetition round in TenderCalculator.BuildCompetition(snapshot, minimumOffers))
        {
            if (round.LowCompetition)
            {
                lowRounds.Add(round);
            }
        }
        if (lowRounds.Count > 0)
        {
            AttentionItem item = new AttentionItem();
            item.Kind = "low-competition";
            item.Count = lowRounds.Count;
            item.MinimumOffers = minimumOffers;
            item.TenderId = lowRounds[0].TenderId;
            for (int i = 0; i < lowRounds.Count && i < 2; i++)
            {
                item.Examples.Add(lowRounds[i].Tender + " · " + IntelligenceService.CycleLabel(lowRounds[i].RoundNumber));
            }
            data.Attention.Add(item);
        }

        // (b) The two oldest tenders still in progress.
        List<TenderDetails> inProgress = new List<TenderDetails>();
        foreach (TenderDetails tender in tenders)
        {
            if (tender.State == "In progress")
            {
                inProgress.Add(tender);
            }
        }
        inProgress = inProgress.OrderBy(tender => tender.CreatedAt).ToList();
        for (int i = 0; i < inProgress.Count && i < 2; i++)
        {
            AttentionItem item = new AttentionItem();
            item.Kind = "in-progress";
            item.TenderId = inProgress[i].TenderId;
            item.TenderTitle = inProgress[i].Title;
            item.Status = inProgress[i].Status;
            item.Days = Math.Round(inProgress[i].DaysSoFar);
            data.Attention.Add(item);
        }

        // (c) Suppliers invited to closed cycles that never submitted an offer.
        int silentSuppliers = 0;
        foreach (SupplierRow supplier in suppliers)
        {
            if (supplier.Invitations > 0 && supplier.Participations == 0)
            {
                silentSuppliers++;
            }
        }
        if (silentSuppliers > 0)
        {
            AttentionItem item = new AttentionItem();
            item.Kind = "silent-suppliers";
            item.Count = silentSuppliers;
            data.Attention.Add(item);
        }
        return data;
    }
}
