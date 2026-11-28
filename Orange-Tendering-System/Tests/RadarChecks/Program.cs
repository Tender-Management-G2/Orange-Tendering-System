// ORANGE INTELLIGENCE checks. The Overview / Suppliers / Tenders calculators run on an in-memory synthetic market (no database).
// Optional: --exports also checks the Excel / Word / PDF exports and the supplier statistics against the local team database (read-only).
using System.IO.Compression;
using System.Xml.Linq;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Orange_Tendering_System.Data;
using Orange_Tendering_System.Models.Entities;
using Orange_Tendering_System.Services;
using Orange_Tendering_System.Services.Intelligence;
using Orange_Tendering_System.Services.Reports;

var failures = 0; var passed = 0;
void Check(bool ok, string name) { if (ok) passed++; else failures++; Console.WriteLine($"{(ok ? "PASS" : "FAIL")}  {name}"); }

var t0 = new DateTime(2026, 1, 10, 9, 0, 0, DateTimeKind.Utc);
var noFilter = new SupplierFilters();

// ---------- synthetic market ----------
SupplierRecord Sup(int id, string name, SupplierStatus status = SupplierStatus.Approved, string category = "IT & Electronics") =>
    new(id, name, category, status);
var suppliers = new List<SupplierRecord>
{
    Sup(1, "Alpha"), Sup(2, "Bravo"), Sup(3, "Charlie"), Sup(4, "Delta"), Sup(5, "Echo"), Sup(6, "Foxtrot", SupplierStatus.Pending)
};
var tenders = new List<TenderRecord>(); var rounds = new List<RoundRecord>(); var invitations = new List<InvitationRecord>(); var bids = new List<BidRecord>();
var bidId = 100; var invId = 500;
// Five closed rounds: 2 and 3 alternate as winners; 4 always prices 4% above 3 and submits 3 minutes after it; 1 and 5 price identical unit prices.
for (var r = 1; r <= 5; r++)
{
    var date = t0.AddDays(r * 30);
    var award = r <= 4 ? (int?)(r % 2 == 1 ? 2 : 3) : null;
    var offers = new (int Supplier, decimal Factor, int Minutes)[] { (1, 1.05m, 0), (2, r % 2 == 1 ? 0.93m : 1.02m, 500), (3, r % 2 == 1 ? 1.0m : 0.95m, 900), (4, (r % 2 == 1 ? 1.0m : 0.95m) * 1.04m, 903), (5, 1.05m, 2000) };
    var roundBids = new List<BidRecord>();
    foreach (var (s, f, m) in offers)
    {
        var lines = new List<BidLineRecord> { new(10 * r + 1, "Laptop", "unit", 10, 10, decimal.Round(800 * f, 2)), new(10 * r + 2, "Monitor", "unit", 10, 10, decimal.Round(200 * f, 2)) };
        var b = new BidRecord(++bidId, ++invId, r, r, s, 1, "JOD", lines.Sum(l => l.Quantity * l.UnitPrice), date.AddHours(-20).AddMinutes(m), ComplianceStatus.Compliant, 20, date, lines);
        roundBids.Add(b); invitations.Add(new InvitationRecord(invId, r, r, s, InvitationStatus.Accepted, date.AddDays(-20)));
    }
    invitations.Add(new InvitationRecord(++invId, r, r, 6, InvitationStatus.Pending, date.AddDays(-20)));
    bids.AddRange(roundBids);
    var awardedBid = award is { } w ? roundBids.First(b => b.SupplierId == w).Id : (int?)null;
    tenders.Add(new TenderRecord(r, $"Tender {r}", award != null ? TenderStatus.Awarded : TenderStatus.Evaluation, "JOD", 10000, 1, "IT", date.AddDays(-30), awardedBid, award != null ? date.AddDays(10) : null));
    rounds.Add(new RoundRecord(r, r, 1, TenderRoundStatus.SentToEvaluation, date, date, date.AddDays(-25), true));
}
// One sealed round (still open): never analysed.
rounds.Add(new RoundRecord(6, 6, 1, TenderRoundStatus.Open, t0.AddDays(400), null, t0.AddDays(380), false));
tenders.Add(new TenderRecord(6, "Open tender", TenderStatus.Bidding, "JOD", 1000, 1, "IT", t0.AddDays(370), null, null));
var snapshot = new ProcurementSnapshot { GeneratedAt = t0.AddDays(300), Period = DateRange.All, Suppliers = suppliers, Tenders = tenders, Rounds = rounds, Invitations = invitations, Bids = bids,
    SealedBids = 2 };

// ---------- supplier offer comparison (Suppliers tab) ----------
// The tender comparison skips newer sealed and incompatible cycles, and keeps both offers in one cycle.
var cycle = rounds[0];
var pairA = bids.First(b => b.RoundId == cycle.Id && b.SupplierId == 1) with { RecordedTotal = 57.125m, SubmittedAt = t0 };
var pairB = bids.First(b => b.RoundId == cycle.Id && b.SupplierId == 2) with { RecordedTotal = 65.375m, SubmittedAt = t0.AddTicks(4200123456) };
var comparable = new ProcurementSnapshot
{
    GeneratedAt = t0, Period = DateRange.All, Suppliers = suppliers, Tenders = [tenders[0]], Invitations = [],
    Rounds = [cycle, cycle with { Id = 62, RoundNumber = 2 }, cycle with { Id = 63, RoundNumber = 3, Unsealed = false }],
    Bids = [pairA, pairB, pairA with { RoundId = 62 }, pairB with { RoundId = 62, Currency = "USD" },
        pairA with { RoundId = 63 }, pairB with { RoundId = 63 }]
};
var compared = SupplierCalculator.CompareOffers(comparable, 1, 2).Single();
Check(compared.RoundId == cycle.Id && compared.LowerOffer == "a" && compared.Difference == 8.25m &&
    compared.RelativeDifference == 8.25m / 65.375m && compared.FirstSubmission == "a" && compared.SecondsApart == 420.0123456m &&
    compared.A.SubmittedAt?.Hour == 12 && compared.A.SubmittedAt?.Offset == TimeSpan.FromHours(3),
    "supplier offers use the latest comparable closed cycle, exact records, higher-offer percentage and Amman time");
Check(SupplierCalculator.CompareOffers(comparable, 1, 1).Count == 0 && SupplierCalculator.CompareOffers(comparable, 1, 6).Count == 0,
    "supplier offers reject duplicate suppliers and pairs without shared eligible offers");
var mixed = new ProcurementSnapshot { GeneratedAt = t0, Period = DateRange.All, Suppliers = suppliers, Tenders = [tenders[0]], Invitations = [],
    Rounds = [cycle], Bids = [pairA, pairB with { Currency = "USD" }] };
Check(SupplierCalculator.CompareOffers(mixed, 1, 2).Single() is { PriceIssue: "different_currency", Difference: null, LowerOffer: null },
    "supplier offers in different currencies are never compared by price");

// ---------- Overview tab ----------
var overview = OverviewCalculator.Build(snapshot, 3);
Check(overview.Context.AnalyzedRounds == 5 && overview.SubmittedOffers == 25, "sealed open round is excluded from analysed rounds and offers");
Check(overview.SealedOffers == 2, "sealed offers are counted, never shown");
Check(overview.TenderCount == 6 && overview.SupplierCount == 6 && overview.ParticipatingSuppliers == 5, "overview KPIs: tenders, suppliers and participating suppliers");
Check(overview.Lifecycle.Select(s => s.Key).SequenceEqual(new[] { "created", "invited", "closed", "awarded", "notified" })
    && overview.Lifecycle[0].Reached == 6 && overview.Lifecycle[3].Reached == 4 && overview.Lifecycle[4].Reached == 0, "lifecycle funnel counts only recorded milestones");
Check(overview.MedianLifecycleDays == null && overview.CompletedTenders == 0, "no winner notification sent: the median lifecycle stays undefined");
Check(overview.Attention.Any(a => a.Kind == "silent-suppliers" && a.Count == 1) && !overview.Attention.Any(a => a.Kind == "low-competition"),
    "attention: the invited supplier without offers is listed; no low-competition cycle");

// ---------- Suppliers tab ----------
var suppliersTab = SupplierCalculator.Build(snapshot, noFilter, 2, 3);
Check(suppliersTab.RegisteredSuppliers == 6 && suppliersTab.ApprovedSuppliers == 5, "registered vs approved suppliers are distinguished");
Check(Math.Abs(suppliersTab.ResponseRate!.Value - 25.0 / 30) < 1e-9, "response rate uses invitations (incl. ignored ones) as denominator");
var stats = SupplierCalculator.BuildStats(snapshot).ToDictionary(x => x.SupplierId);
Check(stats[2].Wins == 2 && stats[2].AwardedTendersBid == 4 && Math.Abs(stats[2].WinRate!.Value - 0.5) < 1e-9, "win rate = awards / awarded tenders bid on");
Check(stats[6].WinRate == null && stats[6].ParticipationRate == 0, "supplier with no offers has undefined win rate and 0% response");

// SupplierCalculator.BuildStats is the only implementation used by the Suppliers tab and by AI Studio.
// It is cross-checked here against an independent LINQ version of the same formulas (the former RadarAnalytics.Suppliers).
bool SameStats(ProcurementSnapshot s)
{
    var simple = SupplierCalculator.BuildStats(s).ToDictionary(x => x.SupplierId);
    var analyzed = s.AnalyzedRounds.Select(r => r.Id).ToHashSet();
    var bidsById = s.Bids.ToDictionary(b => b.Id);
    var awards = s.Tenders.Where(t => t.AwardedBidId is { } id && bidsById.ContainsKey(id)).Select(t => bidsById[t.AwardedBidId!.Value]).ToList();
    var awardedTenders = awards.Select(a => a.TenderId).ToHashSet();
    var relative = new List<(int SupplierId, double Relative)>();
    foreach (var group in s.Bids.GroupBy(b => (b.RoundId, b.Currency)))
    {
        var priced = group.Where(b => b.Total > 0).ToList();
        if (priced.Count < 2) continue;
        var sorted = priced.Select(b => b.Total).Order().ToArray();
        var median = sorted.Length % 2 == 1 ? sorted[sorted.Length / 2] : (sorted[sorted.Length / 2 - 1] + sorted[sorted.Length / 2]) / 2;
        if (median <= 0) continue;
        relative.AddRange(priced.Select(b => (b.SupplierId, Math.Round((double)(b.Total / median) - 1, 4))));
    }
    foreach (var sup in s.Suppliers)
    {
        var row = simple[sup.Id];
        var invites = s.Invitations.Where(i => i.SupplierId == sup.Id && analyzed.Contains(i.RoundId)).ToList();
        var bids = s.Bids.Where(b => b.SupplierId == sup.Id).ToList();
        var bidTenders = bids.Select(b => b.TenderId).ToHashSet();
        var awardedBid = bidTenders.Count(awardedTenders.Contains);
        var won = awards.Where(a => a.SupplierId == sup.Id).ToList();
        var mine = relative.Where(p => p.SupplierId == sup.Id).ToList();
        var checkedBids = bids.Where(b => b.Compliance is ComplianceStatus.Compliant or ComplianceStatus.NonCompliant).ToList();
        var docs = s.SupplierDocuments.Where(d => d.SupplierId == sup.Id).ToList();
        var money = won.GroupBy(a => a.Currency).ToDictionary(g => g.Key, g => (Value: g.Sum(a => a.Total), Count: g.Count()));
        var sameMoney = row.AwardedValue.Count == money.Count && row.AwardedValue.All(m => money.TryGetValue(m.Currency, out var v) && v.Value == m.Value && v.Count == m.Count);
        double? winRate = awardedBid == 0 ? null : Math.Round((double)won.Count / awardedBid, 3);
        double? responseRate = invites.Count == 0 ? null : Math.Round((double)bids.Count(b => invites.Any(i => i.Id == b.InvitationId)) / invites.Count, 3);
        double? relativePrice = mine.Count == 0 ? null : Math.Round(mine.Average(p => p.Relative), 4);
        if (!(row.Name == IntelligenceService.CleanName(sup.Name) && row.Category == SupplierCalculator.CategoryOf(sup.Category) && row.Status == sup.Status.ToString()
            && row.Invitations == invites.Count && row.Participations == bids.Select(b => b.RoundId).Distinct().Count() && row.Tenders == bidTenders.Count
            && row.AwardedTendersBid == awardedBid && row.Wins == won.Count && row.WinRate == winRate && row.ParticipationRate == responseRate && sameMoney
            && row.RelativePrice == relativePrice && row.PricedRounds == mine.Count && row.ComplianceChecked == checkedBids.Count
            && row.Compliant == checkedBids.Count(b => b.Compliance == ComplianceStatus.Compliant) && row.Documents == docs.Count
            && row.VerifiedDocuments == docs.Count(d => d.Verification == DocumentVerificationStatus.Verified)
            && row.FirstActivity == (bids.Count == 0 ? null : bids.Min(b => b.AvailableAt)) && row.LastActivity == (bids.Count == 0 ? null : bids.Max(b => b.AvailableAt)))) return false;
    }
    return simple.Count == s.Suppliers.Count;
}
Check(SameStats(snapshot), "supplier statistics (Suppliers tab and AI Studio) equal an independent reference calculation for every supplier");

var positions = SupplierCalculator.BuildPricePositions(snapshot);
Check(positions.All(p => p.Comparable == 5) && positions.Count == 25, "relative prices only within a round and currency");
Check(suppliersTab.Positions.Count == 4 && suppliersTab.ShownOffers == 20 && suppliersTab.AverageRelative != null, "price-position chart shows the four suppliers with most comparable offers");
Check(SupplierCalculator.Build(snapshot, new SupplierFilters { Activity = "silent" }, 2, 3).Suppliers.Select(x => x.SupplierId).SequenceEqual([6]), "filter: no offer yet");
Check(SupplierCalculator.Build(snapshot, new SupplierFilters { Activity = "approved" }, 2, 3).Suppliers.Count == 5, "filter: approved only");
Check(SupplierCalculator.Build(snapshot, new SupplierFilters { Search = "alp" }, 2, 3).Suppliers.Single().Name == "Alpha", "filter: search by name");
Check(SupplierCalculator.Build(snapshot, new SupplierFilters { Department = "IT" }, 2, 3).Suppliers.Count == 5
    && SupplierCalculator.Build(snapshot, new SupplierFilters { Department = "Unknown" }, 2, 3) is { Filters.Department: "", Suppliers.Count: 6 }, "filter: department bidders; unknown values are ignored");
Check(suppliersTab.ParticipationTrend.Count == 5 && suppliersTab.ParticipationTrend.All(m => m.Suppliers == 5), "participation trend: one point per month, empty months included");

// Relationships (what the old 2D/3D network showed)
Check(suppliersTab.PairCount == 10 && suppliersTab.Pairs.All(p => p.SharedRounds == 5) && !suppliersTab.Pairs.Any(p => p.SupplierA == 6 || p.SupplierB == 6),
    "relationships: every pair that met counts its distinct shared closed cycles; no invented link");
Check(suppliersTab.Groups.Count == 1 && suppliersTab.Groups[0].Names.Count == 5 && suppliersTab.UnconnectedSuppliers == 1, "competition groups: connected suppliers, isolated ones counted separately");
Check(SupplierCalculator.Build(snapshot, noFilter, 6, 3) is { Groups.Count: 0, UnconnectedSuppliers: 6 }, "a minimum above the shared-cycle count leaves every supplier on its own");
var duplicated = new ProcurementSnapshot { GeneratedAt = t0, Period = DateRange.All, Suppliers = suppliers, Tenders = tenders, Rounds = rounds, Invitations = invitations,
    Bids = bids.Concat(bids.Where(b => b.RoundId == 1).Select(b => b with { Id = b.Id + 1000 })).ToList() };
Check(IntelligenceService.FindSharedRounds(duplicated)["1-2"].Count == 5, "two offers in one round never count as two shared rounds");
var details2 = SupplierCalculator.BuildDetails(snapshot, 2)!;
Check(details2.Rounds.Count == 5 && details2.Rounds.Count(r => r.Awarded) == 2 && details2.Competitors.Count == 4 && details2.Prices.Count == 5, "supplier details: cycles, official awards, competitors and prices");
Check(SupplierCalculator.BuildDetails(snapshot, 6) is { Rounds.Count: 0, Competitors.Count: 0 } && SupplierCalculator.BuildDetails(snapshot, 999) == null, "supplier details: no invented activity; unknown supplier is refused");

// What-if scenario
var sim = SupplierCalculator.Simulate(snapshot, [2, 3], 3);
Check(sim.RoundsAffected == 5 && sim.RoundsBelowMinimumAfter == 0 && sim.AwardExposure.Single().Count == 4, "dependency simulation: coverage and award exposure");
Check(sim.HhiAfter > sim.HhiBefore && sim.Notice.Contains("not a forecast"), "simulation reports concentration and is labelled hypothetical");
var simAll = SupplierCalculator.Simulate(snapshot, [1, 2, 3, 4], 3);
Check(simAll.RoundsBelowMinimumAfter == 5 && simAll.RoundsWithoutBidders == 0, "simulation detects rounds falling below the minimum");

// ---------- Tenders tab ----------
var tendersTab = TenderCalculator.Build(snapshot, 3);
Check(tendersTab.Competition.Select(r => r.RoundId).SequenceEqual([1, 2, 3, 4, 5]) && tendersTab.Competition.All(r => r.Invited == 6 && r.Submitted == 5 && !r.LowCompetition),
    "competition: closed rounds only, invited separated from submitted");
Check(tendersTab.Departments.Single().AverageBids == 5, "average offers per closed cycle = offers / closed cycles");
Check(tendersTab.Awards.Count == 1 && tendersTab.Awards[0].Currency == "JOD" && tendersTab.Awards[0].Count == 4, "awarded value grouped per currency");
Check(tendersTab.DefaultTenderId == 5 && tendersTab.Tenders.Count == 6, "tender list: newest first; the tab opens a tender with opened offers");
var tender1 = TenderCalculator.BuildTender(snapshot, 1)!;
var group1 = tender1.Rounds[0].Groups.Single();
Check(group1.Offers.Count == 5 && group1.Lowest.SupplierId == 2 && tender1.Rounds[0].Bids.Single(b => b.Awarded).SupplierId == 2,
    "offers: one comparable group, lowest offer, and the award from the official decision only");

var awardedTender = tenders.First(t => t.AwardedBidId != null);
var notifiedAt = awardedTender.AwardedAt!.Value.AddDays(2);
var withNotice = new ProcurementSnapshot { GeneratedAt = snapshot.GeneratedAt, Period = DateRange.All, Suppliers = suppliers, Tenders = tenders, Rounds = rounds, Invitations = invitations, Bids = bids,
    WinnerNotifiedAt = new Dictionary<int, DateTime> { [awardedTender.Id] = notifiedAt } };
var lifecycle = TenderCalculator.BuildAllTenders(withNotice);
var done = lifecycle.First(t => t.TenderId == awardedTender.Id);
Check(done.State == "Completed" && done.DaysToNotification == Math.Round((notifiedAt - awardedTender.CreatedAt).TotalDays, 1), "lifecycle: creation to winner notification sent, from recorded timestamps");
Check(lifecycle.Where(t => t.TenderId != awardedTender.Id).All(t => t.DaysToNotification == null), "lifecycle: no duration without a sent notification");
Check(lifecycle.First(t => t.TenderId == 6).Rounds.All(r => !r.Analysed && r.Offers == 0 && r.Bids.Count == 0 && r.Groups.Count == 0), "lifecycle: sealed round exposes no offer");
Check(lifecycle.First(t => t.TenderId == 6).Stages.First(s => s.Key == "notified").At == null, "lifecycle: missing milestone stays unrecorded");
Check(OverviewCalculator.Build(withNotice, 3).MedianLifecycleDays == done.DaysToNotification, "overview median lifecycle uses the same tender lifecycle");

// ---------- empty market ----------
var emptyMarket = new ProcurementSnapshot { GeneratedAt = t0, Period = DateRange.All, Suppliers = [], Tenders = [], Rounds = [], Invitations = [], Bids = [] };
Check(OverviewCalculator.Build(emptyMarket, 3) is { TenderCount: 0, MedianLifecycleDays: null, Attention.Count: 0 }
    && SupplierCalculator.Build(emptyMarket, noFilter, 2, 3) is { PairCount: 0, Groups.Count: 0, ResponseRate: null, Suppliers.Count: 0 }
    && TenderCalculator.Build(emptyMarket, 3) is { DefaultTenderId: null, Competition.Count: 0, Awards.Count: 0 }, "empty market gives empty states, not invented data");

// ---------- sealing rule ----------
var now = t0.AddDays(100);
Check(!IntelligenceService.IsUnsealed(TenderRoundStatus.Open, now.AddDays(1), now), "open round before its deadline stays sealed");
Check(IntelligenceService.IsUnsealed(TenderRoundStatus.Open, now.AddMinutes(-1), now) && IntelligenceService.IsUnsealed(TenderRoundStatus.Closed, null, now), "closed rounds and passed deadlines are analysable");
Check(!IntelligenceService.IsUnsealed(TenderRoundStatus.Draft, null, now), "draft rounds are never analysed");

// ---------- date filter ----------
var reversed = DateRange.Parse("2026-06-30", "2026-01-01");
Check(reversed.From == new DateTime(2026, 1, 1) && reversed.To == new DateTime(2026, 6, 30) && reversed.Contains(new DateTime(2026, 6, 30, 23, 0, 0))
    && !reversed.Contains(new DateTime(2026, 7, 1)) && DateRange.Parse("bad", "").From == null, "date filter: reversed range swapped, end day included, invalid dates ignored");

if (args.Contains("--exports"))
{
    var config = new ConfigurationBuilder().SetBasePath(Directory.GetCurrentDirectory()).AddJsonFile("appsettings.json")
        .AddJsonFile("appsettings.Development.json", optional: true).AddUserSecrets(System.Reflection.Assembly.Load("Orange-Tendering-System"), optional: true).Build();
    var connection = new SqlConnectionStringBuilder(config.GetConnectionString("Default Connection")) { InitialCatalog = TeamDevDatabase.DefaultName };
    if (!TeamDevDatabase.IsLocalTeamDatabase(connection.ConnectionString)) throw new InvalidOperationException("Export checks require the local team development database.");
    var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlServer(connection.ConnectionString).Options;
    await using var db = new ApplicationDbContext(options);
    using var cache = new MemoryCache(new MemoryCacheOptions());
    // No HttpContext = no signed-in user = whole market (the same scope as a Sourcing Officer).
    var intelligence = new IntelligenceService(db, cache, new HttpContextAccessor());
    var writer = new ReportExport(new OrangeReportRenderer(Options.Create(new ReportPdfOptions())));
    var injection = new ReportModel { Key = "check", Title = "Formula safety", GeneratedAt = DateTime.UtcNow };
    injection.Tables.Add(new ReportTable("values", "Values", ["Text", "Amount", "Date"], [["=HYPERLINK(\"bad\")", "-5.25", "2026-11-21 10:30"]]));
    var safeXlsx = ZipEntry(writer.ToXlsx(injection), "xl/worksheets/sheet2.xml");
    Check(safeXlsx.Contains("&apos;=HYPERLINK") && !safeXlsx.Contains("<f>") && safeXlsx.Contains("-5.25") && safeXlsx.Contains("s=\"2\""),
        "Excel content: unsafe formulas remain text; negative numbers and dates retain formats");
    var arabicWord = new ReportModel { Key = "check", Title = "تقرير الموردين", GeneratedAt = DateTime.UtcNow };
    arabicWord.Tables.Add(new ReportTable("values", "البيانات", ["المورد"], [["شركة الاختبار"]]));
    Check(ZipEntry(writer.ToDocx(arabicWord), "word/document.xml").Contains("<w:bidi/>"), "Word content: Arabic paragraphs use RTL direction");
    var files = new IntelligenceExportService(intelligence, db, writer);
    var actual = await intelligence.LoadSnapshotAsync(null, null, false, CancellationToken.None);
    Check(SameStats(actual), "actual database: supplier statistics equal the independent reference calculation for every supplier");
    // AI Studio facts come from the same statistics, contain no contact details, and are refused outside the scope.
    // (No Gemini request is made: GetSupplierFactsAsync only reads our own data.)
    var ai = new IntelligenceAiService(new HttpClient(), config, intelligence);
    if (actual.Suppliers.Count > 0)
    {
        var firstSupplier = SupplierCalculator.BuildStats(actual).First();
        var facts = await ai.GetSupplierFactsAsync(firstSupplier.SupplierId, CancellationToken.None) ?? "";
        Check(facts.Contains("Supplier name: " + firstSupplier.Name) && facts.Contains("Official awards won: " + firstSupplier.Wins) && !facts.Contains("@"),
            "AI facts: built from SupplierCalculator.BuildStats, no e-mail or contact data");
    }
    Check(await ai.GetSupplierFactsAsync(int.MaxValue, CancellationToken.None) == null, "AI facts: a supplier outside the scope gets no facts (nothing is sent to Gemini)");
    var actualTender = TenderCalculator.BuildAllTenders(actual).First(t => t.Rounds.Any(r => r.Analysed && r.Bids.Count > 0));
    var actualRound = actualTender.Rounds.First(r => r.Analysed && r.Bids.Count > 0);
    static string ZipEntry(byte[] bytes, string path)
    {
        using var zip = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
        using var reader = new StreamReader(zip.GetEntry(path)?.Open() ?? throw new InvalidDataException($"Missing {path}"));
        return reader.ReadToEnd();
    }
    XNamespace ox = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    var offerFile = await files.BuildAsync(new ExportRequest { Format = "xlsx", ReportType = "tender_offers", TenderId = actualTender.TenderId, RoundId = actualRound.RoundId }, CancellationToken.None);
    var offerXml = XDocument.Parse(ZipEntry(offerFile.Bytes, "xl/worksheets/sheet1.xml"));
    var offerRows = offerXml.Descendants(ox + "row").Skip(1).ToList();
    Check(offerRows.Count == actualRound.Bids.Count && offerRows.Select(r => string.Concat(r.Descendants(ox + "t").Select(x => x.Value))).Any(r => r.Contains(actualRound.Bids[0].Supplier)),
        "actual Excel offers: all selected-round suppliers and rows match database snapshot");
    Check(offerXml.ToString().Contains(actualRound.Bids[0].Total.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture))
        && ZipEntry(offerFile.Bytes, "xl/worksheets/sheet2.xml").Contains(actualTender.TenderId.ToString()), "actual Excel offers: totals and tender scope match source records");
    var allOffersFile = await files.BuildAsync(new ExportRequest { Format = "xlsx", ReportType = "tender_offers", TenderId = actualTender.TenderId }, CancellationToken.None);
    Check(XDocument.Parse(ZipEntry(allOffersFile.Bytes, "xl/worksheets/sheet1.xml")).Descendants(ox + "row").Skip(1).Count() == actualTender.Rounds.Where(r => r.Analysed).Sum(r => r.Bids.Count),
        "Excel offers without a cycle: every effective closed-cycle offer of the tender is present");
    var wordFile = await files.BuildAsync(new ExportRequest { Format = "docx", ReportType = "tender_summary", TenderId = actualTender.TenderId }, CancellationToken.None);
    var wordXml = ZipEntry(wordFile.Bytes, "word/document.xml");
    Check(wordFile.Bytes.AsSpan(0, 2).SequenceEqual("PK"u8) && XDocument.Parse(wordXml).Descendants().Any(e => e.Name.LocalName == "tbl")
        && wordXml.Contains(actualTender.Title) && wordXml.Contains(actualRound.Bids[0].Supplier), "Word: valid editable DOCX with selected tender and real offer table");
    var lifecycleFile = await files.BuildAsync(new ExportRequest { Format = "xlsx", ReportType = "tender_lifecycle", TenderId = actualTender.TenderId }, CancellationToken.None);
    var lifeXml = ZipEntry(lifecycleFile.Bytes, "xl/worksheets/sheet1.xml");
    Check(lifeXml.Contains(actualTender.TenderId.ToString()) && lifeXml.Contains("Bidding &amp; Supplier Submissions") && lifeXml.Contains(actualRound.RoundId.ToString()),
        "manual lifecycle Excel: actual tender, round and workflow records present");
    Check(!offerFile.Bytes.SequenceEqual(lifecycleFile.Bytes) && !lifeXml.Contains(actualRound.Bids[0].Total.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)),
        "Excel: a different report request produces different data and structure");
    var pdfFile = await files.BuildAsync(new ExportRequest { Format = "pdf", ReportType = "tender_summary", TenderId = actualTender.TenderId }, CancellationToken.None);
    using var pdfDocument = UglyToad.PdfPig.PdfDocument.Open(pdfFile.Bytes);
    var pdfText = string.Concat(pdfDocument.GetPages().Select(page => page.Text));
    Check(pdfFile.Bytes.AsSpan(0, 4).SequenceEqual("%PDF"u8) && !pdfText.Contains("Supplier network", StringComparison.OrdinalIgnoreCase)
        && pdfText.Contains(actualTender.TenderId.ToString()), "tender PDF: readable tender data, no supplier network graph");
    async Task<bool> Rejected(ExportRequest request) { try { await files.BuildAsync(request, CancellationToken.None); return false; } catch (IntelligenceExportException) { return true; } }
    Check(await Rejected(new ExportRequest { Format = "xlsx", ReportType = "tender_offers", TenderId = int.MaxValue }), "export: unavailable tender does not produce a file");
    Check(await Rejected(new ExportRequest { Format = "xlsx", ReportType = "supplier_participation", TenderId = actualTender.TenderId }), "export: unsupported report type does not produce a file");
    var otherRound = actual.Rounds.FirstOrDefault(r => r.TenderId != actualTender.TenderId);
    Check(otherRound == null || await Rejected(new ExportRequest { Format = "xlsx", ReportType = "tender_offers", TenderId = actualTender.TenderId, RoundId = otherRound.Id }),
        "export: a cycle of another tender does not produce a file");
}

Console.WriteLine($"\n{passed} passed, {failures} failed");
return failures == 0 ? 0 : 1;
