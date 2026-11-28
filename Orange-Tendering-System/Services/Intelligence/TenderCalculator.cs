// ORANGE INTELLIGENCE - Tab 3 "Tenders": tender lifecycle, bidding cycles, submitted offers and official awards.
// Durations are calculated only between recorded timestamps: a missing milestone stays "not recorded", never estimated.
// Sealed bidding cycles (bidding still open) never expose an offer: snapshot.Bids already excludes them.
using Orange_Tendering_System.Models.Entities;

namespace Orange_Tendering_System.Services.Intelligence;

public static class TenderCalculator
{
    // ==================================================================
    // 1. The Tenders tab: list of tenders + market charts
    // ==================================================================

    public static TendersData Build(ProcurementSnapshot snapshot, int minimumOffers)
    {
        TendersData data = new TendersData();
        data.Context = IntelligenceService.BuildContext(snapshot);
        data.MinimumOffers = minimumOffers;

        // Tender list (newest first) and the tender selected when the tab opens:
        // the first tender that has a closed cycle with offers, otherwise the newest tender.
        List<TenderDetails> tenders = BuildAllTenders(snapshot);
        foreach (TenderDetails tender in tenders)
        {
            TenderListItem item = new TenderListItem();
            item.TenderId = tender.TenderId;
            item.Title = tender.Title;
            item.Department = tender.Department;
            item.CreatedAt = tender.CreatedAt;
            item.State = tender.State;
            item.Status = tender.Status;
            item.DaysToNotification = tender.DaysToNotification;
            data.Tenders.Add(item);

            if (data.DefaultTenderId == null && HasOpenedOffers(tender))
            {
                data.DefaultTenderId = tender.TenderId;
            }
        }
        if (data.DefaultTenderId == null && tenders.Count > 0)
        {
            data.DefaultTenderId = tenders[0].TenderId;
        }

        data.Competition = BuildCompetition(snapshot, minimumOffers);
        data.Departments = BuildDepartments(data.Competition);
        data.RecurringPairs = BuildRecurringPairs(snapshot);
        data.Awards = BuildAwardDistributions(snapshot);
        return data;
    }

    private static bool HasOpenedOffers(TenderDetails tender)
    {
        foreach (TenderRound round in tender.Rounds)
        {
            if (round.Analysed && round.Offers > 0)
            {
                return true;
            }
        }
        return false;
    }

    // Supplier participation per closed bidding cycle (oldest first): invited vs submitted vs compliant.
    public static List<RoundCompetition> BuildCompetition(ProcurementSnapshot snapshot, int minimumOffers)
    {
        List<RoundCompetition> result = new List<RoundCompetition>();
        List<RoundRecord> rounds = snapshot.AnalyzedRounds.OrderBy(round => round.ReferenceDate).ToList();
        foreach (RoundRecord round in rounds)
        {
            TenderRecord? tender = snapshot.Tender(round.TenderId);
            RoundCompetition item = new RoundCompetition();
            item.RoundId = round.Id;
            item.TenderId = round.TenderId;
            item.Tender = IntelligenceService.CleanName(tender?.Title ?? "Tender #" + round.TenderId);
            item.RoundNumber = round.RoundNumber;
            item.Date = round.ReferenceDate;
            item.Department = IntelligenceService.CleanName(tender?.Department ?? "-");

            foreach (InvitationRecord invitation in snapshot.Invitations)
            {
                if (invitation.RoundId == round.Id)
                {
                    item.Invited++;
                }
            }
            List<int> bidderIds = new List<int>();
            foreach (BidRecord bid in snapshot.Bids)
            {
                if (bid.RoundId != round.Id)
                {
                    continue;
                }
                item.Submitted++;
                if (bid.Compliance == ComplianceStatus.Compliant)
                {
                    item.Compliant++;
                }
                if (!bidderIds.Contains(bid.SupplierId))
                {
                    bidderIds.Add(bid.SupplierId);
                }
            }
            bidderIds.Sort();
            foreach (int supplierId in bidderIds)
            {
                item.Bidders.Add(IntelligenceService.CleanName(snapshot.SupplierName(supplierId)));
            }
            // Low competition = fewer offers than IntelligenceService.MinimumOffers (3).
            item.LowCompetition = item.Submitted < minimumOffers;
            result.Add(item);
        }
        return result;
    }

    // Average offers per closed cycle, per department (lowest average first).
    private static List<DepartmentCompetition> BuildDepartments(List<RoundCompetition> rounds)
    {
        List<DepartmentCompetition> result = new List<DepartmentCompetition>();
        foreach (RoundCompetition round in rounds)
        {
            DepartmentCompetition? department = null;
            foreach (DepartmentCompetition existing in result)
            {
                if (existing.Department == round.Department)
                {
                    department = existing;
                }
            }
            if (department == null)
            {
                department = new DepartmentCompetition();
                department.Department = round.Department;
                result.Add(department);
            }
            department.Rounds++;
            department.Bids += round.Submitted;
        }
        foreach (DepartmentCompetition department in result)
        {
            department.AverageBids = Math.Round((double)department.Bids / department.Rounds, 2);
        }
        return result.OrderBy(department => (double)department.Bids / department.Rounds).ToList();
    }

    // The five supplier pairs that met most often in closed cycles.
    private static List<SupplierPair> BuildRecurringPairs(ProcurementSnapshot snapshot)
    {
        Dictionary<string, List<int>> sharedRounds = IntelligenceService.FindSharedRounds(snapshot);
        List<SupplierPair> pairs = new List<SupplierPair>();
        foreach (string key in sharedRounds.Keys)
        {
            int a = IntelligenceService.FirstOfPair(key);
            int b = IntelligenceService.SecondOfPair(key);
            pairs.Add(SupplierCalculator.MakePair(snapshot, a, b, sharedRounds[key].Count));
        }
        return pairs.OrderByDescending(pair => pair.SharedRounds).Take(5).ToList();
    }

    // Official award value per currency, by supplier and by department.
    // HHI (concentration) = sum of squared shares. Example: shares 60% and 40% -> 0.36 + 0.16 = 0.52.
    private static List<AwardDistribution> BuildAwardDistributions(ProcurementSnapshot snapshot)
    {
        List<AwardRecord> awards = IntelligenceService.FindAwards(snapshot);
        Dictionary<int, int> supplierColors = new Dictionary<int, int>();
        foreach (SupplierRow row in SupplierCalculator.BuildStats(snapshot))
        {
            supplierColors[row.SupplierId] = row.SupplierColor;
        }

        List<AwardDistribution> result = new List<AwardDistribution>();
        foreach (AwardRecord award in awards)
        {
            AwardDistribution? distribution = null;
            foreach (AwardDistribution existing in result)
            {
                if (existing.Currency == award.Currency)
                {
                    distribution = existing;
                }
            }
            if (distribution == null)
            {
                distribution = new AwardDistribution();
                distribution.Currency = award.Currency;
                result.Add(distribution);
            }
            distribution.Total += award.Value;
            distribution.Count++;
            AddShare(distribution.BySupplier, award.SupplierName, award.SupplierId, award.Value);
            AddShare(distribution.ByDepartment, award.Department, null, award.Value);
        }

        foreach (AwardDistribution distribution in result)
        {
            double hhi = 0;
            foreach (AwardShare share in distribution.BySupplier)
            {
                share.Share = distribution.Total == 0 ? 0 : (double)(share.Value / distribution.Total);
                share.SupplierColor = supplierColors.ContainsKey(share.SupplierId!.Value) ? supplierColors[share.SupplierId.Value] : -1;
                hhi += share.Share * share.Share;
            }
            foreach (AwardShare share in distribution.ByDepartment)
            {
                share.Share = distribution.Total == 0 ? 0 : (double)(share.Value / distribution.Total);
            }
            if (distribution.Total != 0)
            {
                distribution.Hhi = Math.Round(hhi, 4);
            }
            distribution.BySupplier = distribution.BySupplier.OrderByDescending(share => share.Value).ToList();
            distribution.ByDepartment = distribution.ByDepartment.OrderByDescending(share => share.Value).ToList();
        }
        return result;
    }

    private static void AddShare(List<AwardShare> shares, string name, int? supplierId, decimal value)
    {
        foreach (AwardShare share in shares)
        {
            bool sameSupplier = supplierId != null && share.SupplierId == supplierId;
            bool sameDepartment = supplierId == null && share.Name == IntelligenceService.CleanName(name);
            if (sameSupplier || sameDepartment)
            {
                share.Value += value;
                share.Count++;
                return;
            }
        }
        AwardShare newShare = new AwardShare();
        newShare.Name = IntelligenceService.CleanName(name);
        newShare.SupplierId = supplierId;
        newShare.Value = value;
        newShare.Count = 1;
        shares.Add(newShare);
    }

    // ==================================================================
    // 2. One tender: lifecycle, bidding cycles and offers
    // ==================================================================

    public static List<TenderDetails> BuildAllTenders(ProcurementSnapshot snapshot)
    {
        Dictionary<int, AwardRecord> awardByTender = LatestAwardPerTender(snapshot);
        List<TenderDetails> result = new List<TenderDetails>();
        foreach (TenderRecord tender in snapshot.Tenders.OrderByDescending(tender => tender.CreatedAt))
        {
            result.Add(BuildTender(snapshot, tender, awardByTender));
        }
        return result;
    }

    public static TenderDetails? BuildTender(ProcurementSnapshot snapshot, int tenderId)
    {
        TenderRecord? tender = snapshot.Tender(tenderId);
        if (tender == null)
        {
            return null;
        }
        return BuildTender(snapshot, tender, LatestAwardPerTender(snapshot));
    }

    private static Dictionary<int, AwardRecord> LatestAwardPerTender(ProcurementSnapshot snapshot)
    {
        Dictionary<int, AwardRecord> result = new Dictionary<int, AwardRecord>();
        foreach (AwardRecord award in IntelligenceService.FindAwards(snapshot))
        {
            if (!result.ContainsKey(award.TenderId) || award.AwardedAt > result[award.TenderId].AwardedAt)
            {
                result[award.TenderId] = award;
            }
        }
        return result;
    }

    private static TenderDetails BuildTender(ProcurementSnapshot snapshot, TenderRecord tender, Dictionary<int, AwardRecord> awardByTender)
    {
        TenderDetails details = new TenderDetails();
        details.TenderId = tender.Id;
        details.Title = IntelligenceService.CleanName(tender.Title);
        details.Department = IntelligenceService.CleanName(tender.Department);
        details.Status = tender.Status.ToString();
        details.Currency = tender.Currency;
        details.EstimatedValue = tender.EstimatedValue;
        details.CreatedAt = tender.CreatedAt;
        details.AwardedAt = tender.AwardedAt;
        details.DaysSoFar = (snapshot.GeneratedAt - tender.CreatedAt).TotalDays;

        AwardRecord? award = awardByTender.ContainsKey(tender.Id) ? awardByTender[tender.Id] : null;
        if (award != null)
        {
            details.AwardedSupplierId = award.SupplierId;
            details.AwardedSupplier = IntelligenceService.CleanName(snapshot.SupplierName(award.SupplierId));
            details.AwardedValue = award.Value;
            details.AwardCurrency = award.Currency;
        }
        if (snapshot.WinnerNotifiedAt.ContainsKey(tender.Id))
        {
            details.WinnerNotifiedAt = snapshot.WinnerNotifiedAt[tender.Id];
        }
        details.DaysToNotification = DaysFromCreation(tender, details.WinnerNotifiedAt);
        details.DaysToAward = DaysFromCreation(tender, tender.AwardedAt);

        // State: Completed (winner notified) > Stopped (cancelled / rejected) > Awarded > In progress.
        if (details.WinnerNotifiedAt != null)
        {
            details.State = "Completed";
        }
        else if (tender.Status == TenderStatus.Cancelled || tender.Status == TenderStatus.Rejected)
        {
            details.State = "Stopped";
        }
        else if (tender.AwardedAt != null)
        {
            details.State = "Awarded";
        }
        else
        {
            details.State = "In progress";
        }

        List<RoundRecord> rounds = new List<RoundRecord>();
        foreach (RoundRecord round in snapshot.Rounds)
        {
            if (round.TenderId == tender.Id)
            {
                rounds.Add(round);
            }
        }
        rounds = rounds.OrderBy(round => round.RoundNumber).ToList();

        // ---- Lifecycle milestones (each from its own database column) ----
        DateTime? firstRoundCreated = null;
        DateTime? lastClosed = null;
        foreach (RoundRecord round in rounds)
        {
            if (round.RoundNumber == 1 && (firstRoundCreated == null || round.CreatedAt < firstRoundCreated))
            {
                firstRoundCreated = round.CreatedAt;
            }
            if (round.ClosedAt != null && (lastClosed == null || round.ClosedAt > lastClosed))
            {
                lastClosed = round.ClosedAt;
            }
        }
        DateTime? firstInvitation = null;
        foreach (InvitationRecord invitation in snapshot.Invitations)
        {
            if (invitation.TenderId == tender.Id)
            {
                details.TotalInvitations++;
                if (firstInvitation == null || invitation.InvitedAt < firstInvitation)
                {
                    firstInvitation = invitation.InvitedAt;
                }
            }
        }
        AddStage(details, tender, "created", "Tender created", tender.CreatedAt, "Tenders.CreatedAt");
        AddStage(details, tender, "round", "Initial Bidding cycle created", firstRoundCreated, "TenderRounds.CreatedAt");
        AddStage(details, tender, "invited", "First invitation", firstInvitation, "TenderInvitations.InvitedAt");
        AddStage(details, tender, "closed", "Bidding closed", lastClosed, "TenderRounds.ClosedAt");
        AddStage(details, tender, "awarded", "Official award", tender.AwardedAt, "Tenders.AwardedAt");
        AddStage(details, tender, "notified", "Winner notification sent", details.WinnerNotifiedAt, "FinalDecisionNotices.SuppliersNotifiedAt");

        // ---- Bidding cycles ----
        foreach (RoundRecord round in rounds)
        {
            details.Rounds.Add(BuildRound(snapshot, round, award));
        }
        return details;
    }

    private static void AddStage(TenderDetails details, TenderRecord tender, string key, string label, DateTime? at, string source)
    {
        TenderStage stage = new TenderStage();
        stage.Key = key;
        stage.Label = label;
        stage.At = at;
        stage.Source = source;
        if (at != null)
        {
            stage.DaysFromCreation = (at.Value - tender.CreatedAt).TotalDays;
        }
        details.Stages.Add(stage);
    }

    private static double? DaysFromCreation(TenderRecord tender, DateTime? end)
    {
        if (end == null)
        {
            return null;
        }
        return Math.Round((end.Value - tender.CreatedAt).TotalDays, 1);
    }

    private static TenderRound BuildRound(ProcurementSnapshot snapshot, RoundRecord round, AwardRecord? award)
    {
        TenderRound view = new TenderRound();
        view.RoundId = round.Id;
        view.RoundNumber = round.RoundNumber;
        view.Status = round.Status.ToString();
        view.Deadline = round.Deadline;
        view.ClosedAt = round.ClosedAt;
        view.Analysed = round.Unsealed;
        foreach (InvitationRecord invitation in snapshot.Invitations)
        {
            if (invitation.RoundId == round.Id)
            {
                view.Invited++;
            }
        }

        // A sealed cycle reports only its invitations: even an offer count would leak bidding activity.
        if (!round.Unsealed)
        {
            return view;
        }

        List<BidRecord> bids = new List<BidRecord>();
        foreach (BidRecord bid in snapshot.Bids)
        {
            if (bid.RoundId == round.Id)
            {
                bids.Add(bid);
            }
        }

        // Comparable groups: offers with recorded items and a positive total, grouped by currency + item structure.
        Dictionary<string, List<BidRecord>> groupBids = new Dictionary<string, List<BidRecord>>();
        List<string> groupOrder = new List<string>();
        foreach (BidRecord bid in bids)
        {
            if (bid.Total <= 0 || bid.Lines.Count == 0)
            {
                continue;
            }
            string key = bid.Currency + "#" + IntelligenceService.StructureKey(bid);
            if (!groupBids.ContainsKey(key))
            {
                groupBids[key] = new List<BidRecord>();
                groupOrder.Add(key);
            }
            groupBids[key].Add(bid);
        }
        Dictionary<int, int> groupIdByBid = new Dictionary<int, int>();
        Dictionary<int, decimal> medianByGroup = new Dictionary<int, decimal>();
        Dictionary<int, int> sizeByGroup = new Dictionary<int, int>();
        for (int i = 0; i < groupOrder.Count; i++)
        {
            int groupId = i + 1;
            List<decimal> totals = new List<decimal>();
            foreach (BidRecord bid in groupBids[groupOrder[i]])
            {
                groupIdByBid[bid.Id] = groupId;
                totals.Add(bid.Total);
            }
            medianByGroup[groupId] = IntelligenceService.Median(totals);
            sizeByGroup[groupId] = totals.Count;
        }

        // Offers, cheapest first.
        foreach (BidRecord bid in bids.OrderBy(bid => bid.Total))
        {
            TenderOffer offer = new TenderOffer();
            offer.BidId = bid.Id;
            offer.SupplierId = bid.SupplierId;
            offer.Supplier = IntelligenceService.CleanName(snapshot.SupplierName(bid.SupplierId));
            offer.Currency = bid.Currency;
            offer.Total = bid.Total;
            offer.Awarded = award != null && award.RoundId == round.Id && award.SupplierId == bid.SupplierId;
            offer.Compliance = bid.Compliance.ToString();
            offer.DeliveryDays = bid.DeliveryDays;
            if (!groupIdByBid.ContainsKey(bid.Id))
            {
                offer.ComparisonNote = "Missing item structure or non-positive total";
            }
            else
            {
                int groupId = groupIdByBid[bid.Id];
                offer.ComparisonGroup = groupId;
                if (sizeByGroup[groupId] < 2)
                {
                    offer.ComparisonNote = "No second comparable offer";
                }
                else
                {
                    offer.ComparisonNote = "Comparable within this bidding cycle and currency";
                    decimal median = medianByGroup[groupId];
                    if (median > 0)
                    {
                        // Relative to median: (offer - median) / median. Example: 1100 vs median 1000 -> +10%.
                        offer.RelativeToMedian = (double)((bid.Total - median) / median);
                    }
                }
            }
            view.Bids.Add(offer);
        }
        view.Offers = view.Bids.Count;

        // The chart shows one comparable group at a time: largest group first.
        for (int i = 0; i < groupOrder.Count; i++)
        {
            int groupId = i + 1;
            OfferGroup group = new OfferGroup();
            group.GroupId = groupId;
            group.Median = medianByGroup[groupId];
            foreach (TenderOffer offer in view.Bids)
            {
                if (offer.ComparisonGroup == groupId)
                {
                    group.Offers.Add(offer);
                }
            }
            group.Offers = group.Offers.OrderByDescending(offer => offer.Total).ToList();
            group.Currency = group.Offers[0].Currency;
            group.Highest = group.Offers[0];
            group.Lowest = group.Offers[group.Offers.Count - 1];
            foreach (TenderOffer offer in group.Offers)
            {
                if (offer.Total == group.Median)
                {
                    group.MedianOwners++;
                    group.MedianOwnerName = offer.Supplier;
                }
            }
            view.Groups.Add(group);
        }
        view.Groups = view.Groups.OrderByDescending(group => group.Offers.Count).ThenBy(group => group.GroupId).ToList();
        return view;
    }
}
