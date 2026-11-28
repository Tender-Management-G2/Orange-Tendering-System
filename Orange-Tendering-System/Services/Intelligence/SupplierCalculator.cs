// ORANGE INTELLIGENCE - Tab 2 "Suppliers": every number of the Suppliers tab is calculated here.
// Input: a ProcurementSnapshot (the authorized, closed-round data loaded by IntelligenceService). Output: plain classes from
// IntelligenceModels.cs. Nothing here reads the database, so the same code runs in the tests (Tests/RadarChecks).
// BuildStats is the ONE place where supplier statistics are calculated: the Suppliers tab and the AI Studio facts
// (IntelligenceAiService.GetSupplierFactsAsync) both use it.
using System.Globalization;
using Orange_Tendering_System.Models.Entities;

namespace Orange_Tendering_System.Services.Intelligence;

public static class SupplierCalculator
{
    // ==================================================================
    // 1. Statistics of every supplier
    // ==================================================================

    // One SupplierRow per supplier in scope. Used by the Suppliers tab and by AI Studio.
    public static List<SupplierRow> BuildStats(ProcurementSnapshot snapshot)
    {
        // Closed (unsealed) bidding cycles: only these count as invitations and offers.
        HashSet<int> closedRounds = new HashSet<int>();
        foreach (RoundRecord round in snapshot.Rounds)
        {
            if (round.Unsealed)
            {
                closedRounds.Add(round.Id);
            }
        }

        List<AwardRecord> awards = IntelligenceService.FindAwards(snapshot);
        HashSet<int> awardedTenders = new HashSet<int>();
        foreach (AwardRecord award in awards)
        {
            awardedTenders.Add(award.TenderId);
        }

        List<PricePosition> positions = BuildPricePositions(snapshot);
        Dictionary<string, int> categoryColors = BuildCategoryColors(snapshot);

        List<SupplierRow> rows = new List<SupplierRow>();
        foreach (SupplierRecord supplier in snapshot.Suppliers)
        {
            SupplierRow row = new SupplierRow();
            row.SupplierId = supplier.Id;
            row.Name = IntelligenceService.CleanName(supplier.Name);
            row.Category = CategoryOf(supplier.Category);
            row.CategoryColor = CategoryColor(categoryColors, row.Category);
            row.Status = supplier.Status.ToString();
            row.IsApproved = IsApproved(supplier.Status);

            // Invitations to closed bidding cycles.
            HashSet<int> invitationIds = new HashSet<int>();
            foreach (InvitationRecord invitation in snapshot.Invitations)
            {
                if (invitation.SupplierId == supplier.Id && closedRounds.Contains(invitation.RoundId))
                {
                    invitationIds.Add(invitation.Id);
                }
            }
            row.Invitations = invitationIds.Count;

            // Offers (snapshot.Bids already holds only the latest submitted revision of closed cycles).
            HashSet<int> roundsWithOffer = new HashSet<int>();
            HashSet<int> tendersWithOffer = new HashSet<int>();
            int offersAnsweringInvitations = 0;
            DateTime? first = null;
            DateTime? last = null;
            foreach (BidRecord bid in snapshot.Bids)
            {
                if (bid.SupplierId != supplier.Id)
                {
                    continue;
                }
                roundsWithOffer.Add(bid.RoundId);
                tendersWithOffer.Add(bid.TenderId);
                if (invitationIds.Contains(bid.InvitationId))
                {
                    offersAnsweringInvitations++;
                }
                if (bid.Compliance == ComplianceStatus.Compliant || bid.Compliance == ComplianceStatus.NonCompliant)
                {
                    row.ComplianceChecked++;
                    if (bid.Compliance == ComplianceStatus.Compliant)
                    {
                        row.Compliant++;
                    }
                }
                if (first == null || bid.AvailableAt < first)
                {
                    first = bid.AvailableAt;
                }
                if (last == null || bid.AvailableAt > last)
                {
                    last = bid.AvailableAt;
                }
            }
            row.Participations = roundsWithOffer.Count;
            row.Tenders = tendersWithOffer.Count;
            row.FirstActivity = first;
            row.LastActivity = last;

            // Response rate = offers that answer an invitation / invitations (closed cycles only).
            if (row.Invitations > 0)
            {
                row.ParticipationRate = Math.Round((double)offersAnsweringInvitations / row.Invitations, 3);
            }

            // Win rate = official awards won / awarded tenders the supplier bid on.
            foreach (int tenderId in tendersWithOffer)
            {
                if (awardedTenders.Contains(tenderId))
                {
                    row.AwardedTendersBid++;
                }
            }
            foreach (AwardRecord award in awards)
            {
                if (award.SupplierId == supplier.Id)
                {
                    row.Wins++;
                    AddMoney(row.AwardedValue, award.Currency, award.Value);
                }
            }
            if (row.AwardedTendersBid > 0)
            {
                row.WinRate = Math.Round((double)row.Wins / row.AwardedTendersBid, 3);
            }

            // Relative price = average of (offer / cycle median - 1) over the supplier's comparable offers.
            double relativeSum = 0;
            foreach (PricePosition position in positions)
            {
                if (position.SupplierId == supplier.Id)
                {
                    relativeSum += position.Relative;
                    row.PricedRounds++;
                }
            }
            if (row.PricedRounds > 0)
            {
                row.RelativePrice = Math.Round(relativeSum / row.PricedRounds, 4);
            }

            // Supplier documents (metadata only).
            foreach (SupplierDocumentRecord document in snapshot.SupplierDocuments)
            {
                if (document.SupplierId == supplier.Id)
                {
                    row.Documents++;
                    if (document.Verification == DocumentVerificationStatus.Verified)
                    {
                        row.VerifiedDocuments++;
                    }
                }
            }
            rows.Add(row);
        }

        AssignSupplierColors(rows);
        return rows;
    }

    // Each offer compared with the median offer of the same bidding cycle and the same currency.
    // Example: offers 900, 1000, 1100 JOD -> median 1000 -> positions -10%, 0%, +10%.
    public static List<PricePosition> BuildPricePositions(ProcurementSnapshot snapshot)
    {
        // Group the offers by "cycle id | currency", keeping the first-seen order.
        Dictionary<string, List<BidRecord>> groups = new Dictionary<string, List<BidRecord>>();
        List<string> groupOrder = new List<string>();
        foreach (BidRecord bid in snapshot.Bids)
        {
            string key = bid.RoundId + "|" + bid.Currency;
            if (!groups.ContainsKey(key))
            {
                groups[key] = new List<BidRecord>();
                groupOrder.Add(key);
            }
            groups[key].Add(bid);
        }

        List<PricePosition> result = new List<PricePosition>();
        foreach (string key in groupOrder)
        {
            List<BidRecord> pricedBids = new List<BidRecord>();
            List<decimal> totals = new List<decimal>();
            foreach (BidRecord bid in groups[key])
            {
                if (bid.Total > 0)
                {
                    pricedBids.Add(bid);
                    totals.Add(bid.Total);
                }
            }
            // A comparison needs at least two offers.
            if (pricedBids.Count < 2)
            {
                continue;
            }
            decimal median = IntelligenceService.Median(totals);
            if (median <= 0)
            {
                continue;
            }
            RoundRecord round = snapshot.Round(pricedBids[0].RoundId)!;
            foreach (BidRecord bid in pricedBids)
            {
                PricePosition position = new PricePosition();
                position.SupplierId = bid.SupplierId;
                position.SupplierName = IntelligenceService.CleanName(snapshot.SupplierName(bid.SupplierId));
                position.RoundId = bid.RoundId;
                position.TenderId = bid.TenderId;
                position.TenderTitle = IntelligenceService.CleanName(snapshot.Tender(bid.TenderId)?.Title ?? "Tender #" + bid.TenderId);
                position.Date = round.ReferenceDate;
                position.Currency = bid.Currency;
                position.Total = bid.Total;
                position.Median = median;
                position.Relative = Math.Round((double)(bid.Total / median) - 1, 4);
                position.Comparable = pricedBids.Count;
                result.Add(position);
            }
        }
        return result;
    }

    // ==================================================================
    // 2. The whole Suppliers tab (filters, KPIs, charts, table, relationships)
    // ==================================================================

    public static SuppliersData Build(ProcurementSnapshot snapshot, SupplierFilters filters, int competitionMinimum, int minimumOffers)
    {
        SuppliersData data = new SuppliersData();
        data.Context = IntelligenceService.BuildContext(snapshot);
        data.MinimumOffers = minimumOffers;
        data.CompetitionMinimum = Math.Max(1, competitionMinimum);

        List<SupplierRow> allRows = BuildStats(snapshot);

        // ---- Filter choices ----
        data.Departments = FindDepartments(snapshot);
        Dictionary<string, int> categoryColors = BuildCategoryColors(snapshot);
        List<string> categoryNames = new List<string>();
        foreach (SupplierRow row in allRows)
        {
            if (!categoryNames.Contains(row.Category))
            {
                categoryNames.Add(row.Category);
            }
        }
        categoryNames.Sort(string.Compare);
        data.Filters = CleanFilters(filters, data.Departments, categoryNames);

        // ---- Suppliers that pass the filters ----
        List<SupplierRow> rows = ApplyFilters(snapshot, allRows, data.Filters);
        HashSet<int> shownIds = new HashSet<int>();
        foreach (SupplierRow row in rows)
        {
            shownIds.Add(row.SupplierId);
        }

        foreach (string name in categoryNames)
        {
            CategoryCount category = new CategoryCount();
            category.Name = name;
            category.Color = CategoryColor(categoryColors, name);
            foreach (SupplierRow row in rows)
            {
                if (row.Category == name)
                {
                    category.Count++;
                }
            }
            data.Categories.Add(category);
        }

        // ---- KPI tiles ----
        Dictionary<string, int> perCategory = new Dictionary<string, int>();
        List<string> categoryOrder = new List<string>();
        foreach (SupplierRow row in rows)
        {
            data.RegisteredSuppliers++;
            if (row.IsApproved)
            {
                data.ApprovedSuppliers++;
            }
            if (row.Participations > 0)
            {
                data.ParticipatingSuppliers++;
            }
            data.Invitations += row.Invitations;
            data.Offers += row.Participations;
            if (!perCategory.ContainsKey(row.Category))
            {
                perCategory[row.Category] = 0;
                categoryOrder.Add(row.Category);
            }
            perCategory[row.Category]++;
        }
        data.CategoryCount = perCategory.Count;
        // Response rate = offers in closed cycles / invitations to closed cycles.
        if (data.Invitations > 0)
        {
            data.ResponseRate = (double)data.Offers / data.Invitations;
        }

        // ---- Market snapshot: biggest four categories + "Others" ----
        List<string> byCount = categoryOrder.OrderByDescending(name => perCategory[name]).ToList();
        if (byCount.Count > 0)
        {
            data.LargestCategory = byCount[0];
            data.LargestCategoryCount = perCategory[byCount[0]];
        }
        int othersCount = 0;
        for (int i = 0; i < byCount.Count; i++)
        {
            if (i < 4)
            {
                CategoryCount slice = new CategoryCount();
                slice.Name = byCount[i];
                slice.Count = perCategory[byCount[i]];
                slice.Color = CategoryColor(categoryColors, byCount[i]);
                data.CategoryChart.Add(slice);
            }
            else
            {
                othersCount += perCategory[byCount[i]];
            }
        }
        if (othersCount > 0)
        {
            CategoryCount others = new CategoryCount();
            others.Name = "Others (" + (byCount.Count - 4) + ")";
            others.Count = othersCount;
            others.IsOthers = true;
            data.CategoryChart.Add(others);
        }
        data.ParticipationTrend = BuildParticipationTrend(snapshot, shownIds);

        // ---- Supplier table: most active first ----
        data.Suppliers = rows.OrderByDescending(row => row.Participations).ThenBy(row => row.Name, StringComparer.CurrentCulture).ToList();

        BuildLandscape(data, snapshot, rows);
        BuildPositionsChart(data, snapshot, shownIds);
        BuildRelationships(data, snapshot, rows, shownIds);

        // ---- Drop-down lists ----
        foreach (SupplierRow row in allRows.OrderBy(row => row.Name, StringComparer.CurrentCulture))
        {
            data.AllSuppliers.Add(ToOption(row));
        }
        foreach (SupplierRow row in allRows.OrderByDescending(row => row.Participations))
        {
            if (row.Participations > 0)
            {
                data.ScenarioSuppliers.Add(ToOption(row));
            }
        }
        return data;
    }

    // Unknown filter values (for example a department that is no longer in the period) are ignored.
    private static SupplierFilters CleanFilters(SupplierFilters filters, List<string> departments, List<string> categories)
    {
        SupplierFilters clean = new SupplierFilters();
        clean.Department = departments.Contains(filters.Department) ? filters.Department : "";
        clean.Category = categories.Contains(filters.Category) ? filters.Category : "";
        if (filters.Activity == "active" || filters.Activity == "silent" || filters.Activity == "approved")
        {
            clean.Activity = filters.Activity;
        }
        clean.Search = (filters.Search ?? "").Trim();
        return clean;
    }

    private static List<SupplierRow> ApplyFilters(ProcurementSnapshot snapshot, List<SupplierRow> allRows, SupplierFilters filters)
    {
        // Department filter = suppliers that submitted an offer to a tender of that department.
        HashSet<int> departmentBidders = new HashSet<int>();
        if (filters.Department != "")
        {
            foreach (BidRecord bid in snapshot.Bids)
            {
                TenderRecord? tender = snapshot.Tender(bid.TenderId);
                if (tender != null && tender.Department == filters.Department)
                {
                    departmentBidders.Add(bid.SupplierId);
                }
            }
        }

        List<SupplierRow> result = new List<SupplierRow>();
        foreach (SupplierRow row in allRows)
        {
            if (filters.Category != "" && row.Category != filters.Category)
            {
                continue;
            }
            if (filters.Activity == "active" && row.Participations == 0)
            {
                continue;
            }
            if (filters.Activity == "silent" && row.Participations > 0)
            {
                continue;
            }
            if (filters.Activity == "approved" && !row.IsApproved)
            {
                continue;
            }
            if (filters.Department != "" && !departmentBidders.Contains(row.SupplierId))
            {
                continue;
            }
            if (filters.Search != "")
            {
                bool nameMatches = row.Name.Contains(filters.Search, StringComparison.OrdinalIgnoreCase);
                bool categoryMatches = row.Category.Contains(filters.Search, StringComparison.OrdinalIgnoreCase);
                if (!nameMatches && !categoryMatches)
                {
                    continue;
                }
            }
            result.Add(row);
        }
        return result;
    }

    // Departments of the tenders that have a closed bidding cycle in the period.
    private static List<string> FindDepartments(ProcurementSnapshot snapshot)
    {
        List<string> departments = new List<string>();
        foreach (RoundRecord round in snapshot.AnalyzedRounds)
        {
            string department = snapshot.Tender(round.TenderId)?.Department ?? "-";
            if (!departments.Contains(department))
            {
                departments.Add(department);
            }
        }
        departments.Sort(string.CompareOrdinal);
        return departments;
    }

    // Distinct suppliers that submitted an offer, per month in which the cycle closed (empty months included).
    private static List<MonthCount> BuildParticipationTrend(ProcurementSnapshot snapshot, HashSet<int> shownIds)
    {
        Dictionary<string, HashSet<int>> suppliersByMonth = new Dictionary<string, HashSet<int>>();
        foreach (RoundRecord round in snapshot.AnalyzedRounds)
        {
            string month = round.AvailableAt.ToString("yyyy-MM", CultureInfo.InvariantCulture);
            if (!suppliersByMonth.ContainsKey(month))
            {
                suppliersByMonth[month] = new HashSet<int>();
            }
            foreach (BidRecord bid in snapshot.Bids)
            {
                if (bid.RoundId == round.Id && shownIds.Contains(bid.SupplierId))
                {
                    suppliersByMonth[month].Add(bid.SupplierId);
                }
            }
        }

        List<MonthCount> trend = new List<MonthCount>();
        if (suppliersByMonth.Count == 0)
        {
            return trend;
        }
        List<string> months = suppliersByMonth.Keys.ToList();
        months.Sort(string.CompareOrdinal);
        DateTime current = DateTime.ParseExact(months[0] + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture);
        DateTime lastMonth = DateTime.ParseExact(months[months.Count - 1] + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture);
        while (current <= lastMonth)
        {
            string key = current.ToString("yyyy-MM", CultureInfo.InvariantCulture);
            MonthCount point = new MonthCount();
            point.Month = key;
            if (suppliersByMonth.ContainsKey(key))
            {
                point.Suppliers = suppliersByMonth[key].Count;
            }
            trend.Add(point);
            current = current.AddMonths(1);
        }
        return trend;
    }

    // Chart "Participation vs win rate": one dot per supplier with an offer, one colour (series) per category.
    // Win rate needs official awards; without awards the chart uses the response rate instead.
    private static void BuildLandscape(SuppliersData data, ProcurementSnapshot snapshot, List<SupplierRow> rows)
    {
        List<SupplierRow> active = new List<SupplierRow>();
        foreach (SupplierRow row in rows)
        {
            if (row.Participations > 0)
            {
                active.Add(row);
            }
        }
        data.NotPlotted = rows.Count - active.Count;

        bool anyWinRate = false;
        foreach (SupplierRow row in active)
        {
            if (row.WinRate != null)
            {
                anyWinRate = true;
            }
        }
        data.UseWinRate = IntelligenceService.FindAwards(snapshot).Count > 0 && anyWinRate;

        data.MaxAwardedValue = 1;
        foreach (SupplierRow row in active)
        {
            decimal awarded = row.AwardedValue.Count > 0 ? row.AwardedValue[0].Value : 0;
            if (awarded > data.MaxAwardedValue)
            {
                data.MaxAwardedValue = awarded;
            }

            double? y = data.UseWinRate ? row.WinRate : row.ParticipationRate;
            if (y == null)
            {
                continue;
            }
            LandscapeSeries? series = null;
            foreach (LandscapeSeries existing in data.Landscape)
            {
                if (existing.Category == row.Category)
                {
                    series = existing;
                }
            }
            if (series == null)
            {
                series = new LandscapeSeries();
                series.Category = row.Category;
                series.Color = row.CategoryColor;
                data.Landscape.Add(series);
            }
            series.Points.Add(row);
        }

        // "Most rounds with an offer" takeaway.
        SupplierRow? leader = null;
        foreach (SupplierRow row in active)
        {
            if (leader == null || row.Participations > leader.Participations)
            {
                leader = row;
            }
        }
        data.LeaderName = leader == null ? "" : leader.Name;
    }

    // Chart "Price position over time": the four suppliers with the most comparable offers.
    private static void BuildPositionsChart(SuppliersData data, ProcurementSnapshot snapshot, HashSet<int> shownIds)
    {
        List<PricePosition> positions = new List<PricePosition>();
        foreach (PricePosition position in BuildPricePositions(snapshot))
        {
            if (shownIds.Contains(position.SupplierId))
            {
                positions.Add(position);
            }
        }
        data.PositionRows = positions;

        Dictionary<int, int> countBySupplier = new Dictionary<int, int>();
        List<int> supplierOrder = new List<int>();
        foreach (PricePosition position in positions)
        {
            if (!countBySupplier.ContainsKey(position.SupplierId))
            {
                countBySupplier[position.SupplierId] = 0;
                supplierOrder.Add(position.SupplierId);
            }
            countBySupplier[position.SupplierId]++;
        }
        List<int> shownSuppliers = supplierOrder.OrderByDescending(id => countBySupplier[id]).Take(4).ToList();

        Dictionary<int, SupplierRow> rowsById = new Dictionary<int, SupplierRow>();
        foreach (SupplierRow row in data.Suppliers)
        {
            rowsById[row.SupplierId] = row;
        }

        double relativeSum = 0;
        double? lowest = null;
        double? highest = null;
        foreach (int supplierId in shownSuppliers)
        {
            PositionSeries series = new PositionSeries();
            series.SupplierId = supplierId;
            series.Name = IntelligenceService.CleanName(snapshot.SupplierName(supplierId));
            series.SupplierColor = rowsById.ContainsKey(supplierId) ? rowsById[supplierId].SupplierColor : -1;
            double supplierSum = 0;
            foreach (PricePosition position in positions)
            {
                if (position.SupplierId != supplierId)
                {
                    continue;
                }
                series.Points.Add(position);
                supplierSum += position.Relative;
                relativeSum += position.Relative;
                data.ShownOffers++;
                if (lowest == null || position.Relative < lowest)
                {
                    lowest = position.Relative;
                }
                if (highest == null || position.Relative > highest)
                {
                    highest = position.Relative;
                }
            }
            series.Points = series.Points.OrderBy(point => point.Date).ToList();
            data.Positions.Add(series);

            // The supplier with the lowest average position ("cheapest on average").
            double supplierAverage = supplierSum / series.Points.Count;
            if (data.LowestAverage == null || supplierAverage < data.LowestAverage)
            {
                data.LowestAverage = supplierAverage;
                data.LowestAverageName = series.Name;
            }
        }
        if (data.ShownOffers > 0)
        {
            data.AverageRelative = relativeSum / data.ShownOffers;
            data.RelativeSpread = highest - lowest;
        }
    }

    // Market relationships: supplier pairs that met in closed bidding cycles, and the groups they form.
    private static void BuildRelationships(SuppliersData data, ProcurementSnapshot snapshot, List<SupplierRow> rows, HashSet<int> shownIds)
    {
        Dictionary<string, List<int>> sharedRounds = IntelligenceService.FindSharedRounds(snapshot);
        List<SupplierPair> pairs = new List<SupplierPair>();
        foreach (string key in sharedRounds.Keys)
        {
            int a = IntelligenceService.FirstOfPair(key);
            int b = IntelligenceService.SecondOfPair(key);
            if (shownIds.Contains(a) && shownIds.Contains(b))
            {
                pairs.Add(MakePair(snapshot, a, b, sharedRounds[key].Count));
            }
        }
        pairs = pairs.OrderByDescending(pair => pair.SharedRounds).ThenBy(pair => pair.NameA, StringComparer.CurrentCulture).ToList();
        data.PairCount = pairs.Count;
        data.Pairs = pairs.Take(25).ToList();

        // Groups: suppliers linked by pairs with at least CompetitionMinimum shared cycles (breadth-first search).
        Dictionary<int, List<int>> neighbours = new Dictionary<int, List<int>>();
        foreach (SupplierRow row in rows)
        {
            neighbours[row.SupplierId] = new List<int>();
        }
        foreach (SupplierPair pair in pairs)
        {
            if (pair.SharedRounds >= data.CompetitionMinimum)
            {
                neighbours[pair.SupplierA].Add(pair.SupplierB);
                neighbours[pair.SupplierB].Add(pair.SupplierA);
            }
        }
        HashSet<int> visited = new HashSet<int>();
        foreach (SupplierRow row in rows)
        {
            if (visited.Contains(row.SupplierId))
            {
                continue;
            }
            List<int> members = new List<int>();
            Queue<int> queue = new Queue<int>();
            queue.Enqueue(row.SupplierId);
            visited.Add(row.SupplierId);
            while (queue.Count > 0)
            {
                int current = queue.Dequeue();
                members.Add(current);
                foreach (int next in neighbours[current])
                {
                    if (!visited.Contains(next))
                    {
                        visited.Add(next);
                        queue.Enqueue(next);
                    }
                }
            }
            if (members.Count == 1)
            {
                data.UnconnectedSuppliers++;
                continue;
            }
            CompetitionGroup group = new CompetitionGroup();
            foreach (int member in members)
            {
                group.Names.Add(IntelligenceService.CleanName(snapshot.SupplierName(member)));
            }
            group.Names.Sort(string.Compare);
            data.Groups.Add(group);
        }
        data.Groups = data.Groups.OrderByDescending(group => group.Names.Count).ToList();
    }

    // ==================================================================
    // 3. Supplier details panel
    // ==================================================================

    public static SupplierDetails? BuildDetails(ProcurementSnapshot snapshot, int supplierId)
    {
        if (snapshot.Supplier(supplierId) == null)
        {
            return null;
        }
        SupplierDetails details = new SupplierDetails();
        foreach (SupplierRow row in BuildStats(snapshot))
        {
            if (row.SupplierId == supplierId)
            {
                details.Supplier = row;
            }
        }

        // Who won each closed cycle (official awards only).
        Dictionary<int, int> winnerByRound = new Dictionary<int, int>();
        foreach (AwardRecord award in IntelligenceService.FindAwards(snapshot))
        {
            if (!winnerByRound.ContainsKey(award.RoundId))
            {
                winnerByRound[award.RoundId] = award.SupplierId;
            }
        }

        // Participation: closed cycles with an offer from this supplier, newest first.
        List<RoundRecord> rounds = snapshot.AnalyzedRounds.OrderBy(round => round.AvailableAt).ThenBy(round => round.Id).ToList();
        foreach (RoundRecord round in rounds)
        {
            HashSet<int> bidders = new HashSet<int>();
            foreach (BidRecord bid in snapshot.Bids)
            {
                if (bid.RoundId == round.Id)
                {
                    bidders.Add(bid.SupplierId);
                }
            }
            if (!bidders.Contains(supplierId))
            {
                continue;
            }
            TenderRecord? tender = snapshot.Tender(round.TenderId);
            SupplierRound item = new SupplierRound();
            item.RoundId = round.Id;
            item.TenderId = round.TenderId;
            item.Tender = IntelligenceService.CleanName(tender?.Title ?? "Tender #" + round.TenderId);
            item.RoundNumber = round.RoundNumber;
            item.Date = round.AvailableAt;
            item.Department = IntelligenceService.CleanName(tender?.Department ?? "-");
            item.Offers = bidders.Count;
            item.Awarded = winnerByRound.ContainsKey(round.Id) && winnerByRound[round.Id] == supplierId;
            if (item.Offers > details.MaxOffers)
            {
                details.MaxOffers = item.Offers;
            }
            details.Rounds.Insert(0, item);
        }

        // Pricing: every comparable offer of this supplier, oldest first.
        foreach (PricePosition position in BuildPricePositions(snapshot))
        {
            if (position.SupplierId == supplierId)
            {
                details.Prices.Add(position);
            }
        }
        details.Prices = details.Prices.OrderBy(position => position.Date).ToList();

        // Relationships: suppliers that met this supplier in closed cycles, most shared cycles first.
        Dictionary<string, List<int>> sharedRounds = IntelligenceService.FindSharedRounds(snapshot);
        foreach (string key in sharedRounds.Keys)
        {
            int a = IntelligenceService.FirstOfPair(key);
            int b = IntelligenceService.SecondOfPair(key);
            if (a == supplierId)
            {
                details.Competitors.Add(MakePair(snapshot, a, b, sharedRounds[key].Count));
            }
            else if (b == supplierId)
            {
                details.Competitors.Add(MakePair(snapshot, b, a, sharedRounds[key].Count));
            }
        }
        details.Competitors = details.Competitors.OrderByDescending(pair => pair.SharedRounds).ToList();
        return details;
    }

    // ==================================================================
    // 4. Supplier offer comparison (two suppliers, their shared tenders)
    // ==================================================================

    public const string BusinessZone = "Asia/Amman";
    private static readonly TimeZoneInfo Amman = TimeZoneInfo.FindSystemTimeZoneById(BusinessZone);

    // One row per tender in which BOTH suppliers submitted an offer in the same closed cycle.
    // When a tender has several such cycles, the latest comparable one is used.
    public static List<OfferComparison> CompareOffers(ProcurementSnapshot snapshot, int supplierA, int supplierB)
    {
        List<OfferComparison> result = new List<OfferComparison>();
        if (supplierA == supplierB || snapshot.Supplier(supplierA) == null || snapshot.Supplier(supplierB) == null)
        {
            return result;
        }

        // Every shared cycle, grouped by tender.
        Dictionary<int, List<OfferComparison>> byTender = new Dictionary<int, List<OfferComparison>>();
        foreach (RoundRecord round in snapshot.AnalyzedRounds)
        {
            TenderRecord? tender = snapshot.Tender(round.TenderId);
            if (tender == null)
            {
                continue;
            }
            foreach (BidRecord bidA in snapshot.Bids)
            {
                if (bidA.SupplierId != supplierA || bidA.RoundId != round.Id || bidA.TenderId != round.TenderId)
                {
                    continue;
                }
                foreach (BidRecord bidB in snapshot.Bids)
                {
                    if (bidB.SupplierId != supplierB || bidB.RoundId != round.Id || bidB.TenderId != round.TenderId)
                    {
                        continue;
                    }
                    if (!byTender.ContainsKey(tender.Id))
                    {
                        byTender[tender.Id] = new List<OfferComparison>();
                    }
                    byTender[tender.Id].Add(CompareTwoOffers(snapshot, round, tender, bidA, bidB));
                }
            }
        }

        // Per tender: prefer a cycle whose prices are comparable, then the latest cycle.
        foreach (int tenderId in byTender.Keys)
        {
            OfferComparison best = byTender[tenderId]
                .OrderBy(row => row.PriceIssue != null)
                .ThenByDescending(row => row.RoundNumber)
                .First();
            result.Add(best);
        }
        return result.OrderBy(row => row.Title, StringComparer.CurrentCultureIgnoreCase).ThenBy(row => row.TenderId).ToList();
    }

    private static OfferComparison CompareTwoOffers(ProcurementSnapshot snapshot, RoundRecord round, TenderRecord tender, BidRecord a, BidRecord b)
    {
        OfferComparison row = new OfferComparison();
        row.TenderId = tender.Id;
        row.Title = IntelligenceService.CleanName(tender.Title);
        row.RoundId = round.Id;
        row.RoundNumber = round.RoundNumber;
        row.A = DescribeOffer(snapshot, round, a);
        row.B = DescribeOffer(snapshot, round, b);
        row.Deadline = ToAmmanTime(round.Deadline);

        // Price: compared only when both offers are directly comparable.
        row.PriceIssue = FindPriceIssue(a, b);
        if (row.PriceIssue == null)
        {
            decimal totalA = a.RecordedTotal!.Value;
            decimal totalB = b.RecordedTotal!.Value;
            decimal difference = Math.Abs(totalA - totalB);
            row.Difference = difference;
            // Percentage = difference / the HIGHER offer. Example: 900 vs 1000 -> 100 / 1000 = 10%.
            row.RelativeDifference = difference / Math.Max(totalA, totalB);
            if (difference != 0)
            {
                row.LowerOffer = totalA < totalB ? "a" : "b";
            }
        }

        // Timing: seconds from A's submission to B's submission (positive = A submitted first).
        decimal? apart = SecondsBetween(a.SubmittedAt, b.SubmittedAt);
        if (apart != null)
        {
            row.SecondsApart = Math.Abs(apart.Value);
            if (apart > 0)
            {
                row.FirstSubmission = "a";
            }
            else if (apart < 0)
            {
                row.FirstSubmission = "b";
            }
        }
        return row;
    }

    private static ComparedOffer DescribeOffer(ProcurementSnapshot snapshot, RoundRecord round, BidRecord bid)
    {
        ComparedOffer offer = new ComparedOffer();
        offer.BidId = bid.Id;
        offer.Supplier = IntelligenceService.CleanName(snapshot.SupplierName(bid.SupplierId));
        offer.Currency = NormalizeCurrency(bid.Currency);
        offer.Total = bid.RecordedTotal;
        offer.SubmittedAt = ToAmmanTime(bid.SubmittedAt);
        offer.SecondsBeforeDeadline = SecondsBetween(bid.SubmittedAt, round.Deadline);
        if (offer.SubmittedAt != null)
        {
            offer.RecordedTime = offer.SubmittedAt.Value.ToString("HH:mm:ss.FFFFFFF", CultureInfo.InvariantCulture);
        }
        return offer;
    }

    // Returns null when the two offers can be compared, otherwise the reason they cannot.
    private static string? FindPriceIssue(BidRecord a, BidRecord b)
    {
        string? currencyA = NormalizeCurrency(a.Currency);
        string? currencyB = NormalizeCurrency(b.Currency);
        if (currencyA == null || currencyB == null)
        {
            return "missing_currency";
        }
        if (currencyA != currencyB)
        {
            return "different_currency";
        }
        if (a.Lines.Count == 0 || b.Lines.Count == 0 || a.RecordedTotal == null || a.RecordedTotal <= 0 || b.RecordedTotal == null || b.RecordedTotal <= 0)
        {
            return "missing_price";
        }
        if (HasInvalidLine(a) || HasInvalidLine(b) || IntelligenceService.StructureKey(a) != IntelligenceService.StructureKey(b))
        {
            return "different_structure";
        }
        if (Specifications(a) != Specifications(b) || NormalizeText(a.Deviations) != NormalizeText(b.Deviations))
        {
            return "different_specifications";
        }
        return null;
    }

    private static bool HasInvalidLine(BidRecord bid)
    {
        foreach (BidLineRecord line in bid.Lines)
        {
            if (line.Quantity <= 0 || line.UnitPrice < 0 || string.IsNullOrWhiteSpace(line.Unit))
            {
                return true;
            }
        }
        return false;
    }

    // All offered specifications of the bid, item by item, as one normalized text.
    private static string Specifications(BidRecord bid)
    {
        List<string> parts = new List<string>();
        foreach (BidLineRecord line in bid.Lines.OrderBy(line => line.TenderItemId))
        {
            parts.Add(NormalizeText(line.OfferedSpecifications));
        }
        return string.Join("\n", parts);
    }

    // "  red   steel " -> "RED STEEL"
    private static string NormalizeText(string? value)
    {
        string[] words = (value ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        return string.Join(' ', words).ToUpperInvariant();
    }

    private static string? NormalizeCurrency(string? currency)
    {
        if (string.IsNullOrWhiteSpace(currency))
        {
            return null;
        }
        return currency.Trim().ToUpperInvariant();
    }

    // Exact seconds (with fractions) from one recorded time to another; null when either is missing.
    private static decimal? SecondsBetween(DateTime? from, DateTime? to)
    {
        if (from == null || to == null)
        {
            return null;
        }
        long ticks = (to.Value - from.Value).Ticks;
        return ticks / (decimal)TimeSpan.TicksPerSecond;
    }

    // Database times are UTC; the comparison shows them in Amman time.
    private static DateTimeOffset? ToAmmanTime(DateTime? utc)
    {
        if (utc == null)
        {
            return null;
        }
        DateTimeOffset value = new DateTimeOffset(DateTime.SpecifyKind(utc.Value, DateTimeKind.Utc));
        return TimeZoneInfo.ConvertTime(value, Amman);
    }

    // ==================================================================
    // 5. What-if scenario: replay recorded history without some suppliers
    // ==================================================================

    public static SimulationResult Simulate(ProcurementSnapshot snapshot, List<int> excludedIds, int minimumOffers)
    {
        HashSet<int> excluded = new HashSet<int>(excludedIds);
        List<AwardRecord> awards = IntelligenceService.FindAwards(snapshot);
        SimulationResult result = new SimulationResult();
        result.MinimumOffers = minimumOffers;

        // Count the bidders of every closed cycle before and after removing the chosen suppliers.
        List<SimulatedRound> rounds = new List<SimulatedRound>();
        foreach (RoundRecord round in snapshot.AnalyzedRounds)
        {
            HashSet<int> bidders = new HashSet<int>();
            foreach (BidRecord bid in snapshot.Bids)
            {
                if (bid.RoundId == round.Id)
                {
                    bidders.Add(bid.SupplierId);
                }
            }
            if (bidders.Count == 0)
            {
                continue;
            }
            int after = 0;
            foreach (int supplierId in bidders)
            {
                if (!excluded.Contains(supplierId))
                {
                    after++;
                }
            }
            SimulatedRound item = new SimulatedRound();
            item.RoundId = round.Id;
            item.Tender = IntelligenceService.CleanName(snapshot.Tender(round.TenderId)?.Title ?? "-");
            item.RoundNumber = round.RoundNumber;
            item.Before = bidders.Count;
            item.After = after;
            item.DropsBelowMinimum = item.Before >= minimumOffers && after < minimumOffers;
            item.LosesAllBidders = after == 0;
            foreach (AwardRecord award in awards)
            {
                if (award.RoundId == round.Id && excluded.Contains(award.SupplierId))
                {
                    item.LosesAward = true;
                }
            }
            rounds.Add(item);
        }

        foreach (SimulatedRound round in rounds)
        {
            if (round.After < round.Before)
            {
                result.RoundsAffected++;
                result.Rounds.Add(round);
            }
            if (round.Before < minimumOffers)
            {
                result.RoundsBelowMinimumBefore++;
            }
            if (round.After < minimumOffers)
            {
                result.RoundsBelowMinimumAfter++;
            }
            if (round.LosesAllBidders)
            {
                result.RoundsWithoutBidders++;
            }
        }

        // Share of all offers that came from the excluded suppliers.
        List<BidRecord> remainingBids = new List<BidRecord>();
        int removedBids = 0;
        foreach (BidRecord bid in snapshot.Bids)
        {
            if (excluded.Contains(bid.SupplierId))
            {
                removedBids++;
            }
            else
            {
                remainingBids.Add(bid);
            }
        }
        if (snapshot.Bids.Count > 0)
        {
            result.BidShareRemoved = Math.Round((double)removedBids / snapshot.Bids.Count, 4);
        }
        result.HhiBefore = ConcentrationIndex(snapshot.Bids.ToList());
        result.HhiAfter = ConcentrationIndex(remainingBids);

        // Award value previously won by the excluded suppliers, per currency.
        foreach (AwardRecord award in awards)
        {
            if (excluded.Contains(award.SupplierId))
            {
                AddMoney(result.AwardExposure, award.Currency, award.Value);
            }
        }

        // Approved suppliers of the same categories that could replace them.
        HashSet<string> categories = new HashSet<string>();
        foreach (SupplierRecord supplier in snapshot.Suppliers)
        {
            if (excluded.Contains(supplier.Id))
            {
                categories.Add(supplier.Category);
            }
        }
        Dictionary<int, SupplierRow> stats = new Dictionary<int, SupplierRow>();
        foreach (SupplierRow row in BuildStats(snapshot))
        {
            stats[row.SupplierId] = row;
        }
        List<SupplierAlternative> alternatives = new List<SupplierAlternative>();
        foreach (SupplierRecord supplier in snapshot.Suppliers)
        {
            if (excluded.Contains(supplier.Id) || supplier.Status != SupplierStatus.Approved || !categories.Contains(supplier.Category))
            {
                continue;
            }
            SupplierAlternative alternative = new SupplierAlternative();
            alternative.SupplierId = supplier.Id;
            alternative.Name = IntelligenceService.CleanName(supplier.Name);
            alternative.Category = supplier.Category;
            alternative.Participations = stats[supplier.Id].Participations;
            alternative.ParticipationRate = stats[supplier.Id].ParticipationRate;
            alternatives.Add(alternative);
        }
        result.Alternatives = alternatives.OrderByDescending(item => item.Participations).ThenBy(item => item.Name).Take(8).ToList();

        result.Excluded = excluded.OrderBy(id => id).ToList();
        foreach (int id in result.Excluded)
        {
            result.ExcludedNames.Add(IntelligenceService.CleanName(snapshot.SupplierName(id)));
        }
        result.Notice = "Hypothetical replay of recorded history only. It is not a forecast and changes no supplier status or procurement decision.";
        return result;
    }

    // Herfindahl-Hirschman Index of the offers: sum of (each supplier's share of offers)².
    // Example: 4 offers, supplier A has 2 and B and C have 1 -> 0.5² + 0.25² + 0.25² = 0.375. 1.0 = one supplier only.
    private static double? ConcentrationIndex(List<BidRecord> bids)
    {
        if (bids.Count == 0)
        {
            return null;
        }
        Dictionary<int, int> offersBySupplier = new Dictionary<int, int>();
        foreach (BidRecord bid in bids)
        {
            if (!offersBySupplier.ContainsKey(bid.SupplierId))
            {
                offersBySupplier[bid.SupplierId] = 0;
            }
            offersBySupplier[bid.SupplierId]++;
        }
        double sum = 0;
        foreach (int count in offersBySupplier.Values)
        {
            double share = (double)count / bids.Count;
            sum += share * share;
        }
        return Math.Round(sum, 4);
    }

    // ==================================================================
    // Colours: the browser turns these numbers into CSS colour variables.
    // ==================================================================

    // Category colour = position of the category in the alphabetical list (0..7, repeating). -1 = grey.
    // Example: categories "Furniture", "IT", "Medical" -> colours 0, 1, 2.
    private static Dictionary<string, int> BuildCategoryColors(ProcurementSnapshot snapshot)
    {
        List<string> categories = new List<string>();
        foreach (SupplierRecord supplier in snapshot.Suppliers)
        {
            string category = CategoryOf(supplier.Category);
            if (category != Uncategorized && !categories.Contains(category))
            {
                categories.Add(category);
            }
        }
        categories.Sort(string.Compare);
        Dictionary<string, int> colors = new Dictionary<string, int>();
        for (int i = 0; i < categories.Count; i++)
        {
            colors[categories[i]] = i % 8;
        }
        return colors;
    }

    private static int CategoryColor(Dictionary<string, int> colors, string category)
    {
        if (colors.ContainsKey(category))
        {
            return colors[category];
        }
        return -1;
    }

    // Supplier colour: the eight most active suppliers get their own colour (0..7); everyone else is grey (-1).
    private static void AssignSupplierColors(List<SupplierRow> rows)
    {
        List<SupplierRow> ranked = rows
            .Where(row => row.Participations > 0)
            .OrderByDescending(row => row.Participations)
            .ThenBy(row => row.SupplierId)
            .Take(8)
            .ToList();
        for (int i = 0; i < ranked.Count; i++)
        {
            ranked[i].SupplierColor = i;
        }
    }

    public static SupplierPair MakePair(ProcurementSnapshot snapshot, int a, int b, int sharedRounds)
    {
        SupplierPair pair = new SupplierPair();
        pair.SupplierA = a;
        pair.SupplierB = b;
        pair.NameA = IntelligenceService.CleanName(snapshot.SupplierName(a));
        pair.NameB = IntelligenceService.CleanName(snapshot.SupplierName(b));
        pair.SharedRounds = sharedRounds;
        return pair;
    }

    private static SupplierOption ToOption(SupplierRow row)
    {
        SupplierOption option = new SupplierOption();
        option.SupplierId = row.SupplierId;
        option.Name = row.Name;
        option.Category = row.Category;
        option.Participations = row.Participations;
        option.CategoryColor = row.CategoryColor;
        return option;
    }

    // ==================================================================
    // Small rules used only by this calculator
    // ==================================================================

    public const string Uncategorized = "Uncategorized";

    // A supplier without a registered category is shown in the "Uncategorized" group.
    public static string CategoryOf(string? category)
    {
        if (string.IsNullOrWhiteSpace(category))
        {
            return Uncategorized;
        }
        return category.Trim();
    }

    private static bool IsApproved(SupplierStatus status)
    {
        return status == SupplierStatus.Approved;
    }

    // Adds a value to the running total of its currency (one MoneyValue per currency): currencies are never added together.
    private static void AddMoney(List<MoneyValue> totals, string currency, decimal value)
    {
        foreach (MoneyValue total in totals)
        {
            if (total.Currency == currency)
            {
                total.Value += value;
                total.Count++;
                return;
            }
        }
        MoneyValue newTotal = new MoneyValue();
        newTotal.Currency = currency;
        newTotal.Value = value;
        newTotal.Count = 1;
        totals.Add(newTotal);
    }
}
