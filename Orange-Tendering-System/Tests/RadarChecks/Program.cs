// ORANGE RADAR checks. Pure analytics run on an in-memory synthetic market (no database).
// Optional: --exports checks the Excel / Word / PDF exports against the local team database (read-only).
using System.Diagnostics;
using System.IO.Compression;
using System.Text.Json;
using System.Xml.Linq;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Orange_Tendering_System.Data;
using Orange_Tendering_System.Models.Entities;
using Orange_Tendering_System.Services;
using Orange_Tendering_System.Services.Radar;
using Orange_Tendering_System.Services.Reports;

var failures = 0; var passed = 0;
void Check(bool ok, string name) { if (ok) passed++; else failures++; Console.WriteLine($"{(ok ? "PASS" : "FAIL")}  {name}"); }

var t0 = new DateTime(2026, 1, 10, 9, 0, 0, DateTimeKind.Utc);
var thresholds = new RadarThresholds();

// ---------- synthetic market ----------
RSupplier Sup(int id, string name, string phone, string address, string email, SupplierStatus status = SupplierStatus.Approved, string category = "IT & Electronics") =>
    new(id, name, category, status, email, phone, address, null, $"REG-{id}", t0.AddDays(-100));
var suppliers = new List<RSupplier>
{
    Sup(1, "Alpha", "+962 6 111 1111", "Street 1, Amman", "a@alpha.example"),
    Sup(2, "Bravo", "+962 6 222 2222", "Street 2, Amman", "b@bravo.example"),
    Sup(3, "Charlie", "+962 6 555 0142", "Building 12, King St., Amman", "c@charlie.example"),
    Sup(4, "Delta", "00962-6-555-0142", "building 12 king st amman", "d@delta.example"),
    Sup(5, "Echo", "+962 6 333 3333", "Street 5, Irbid", "e@gmail.com"),
    Sup(6, "Foxtrot", "+962 6 444 4444", "Street 6, Zarqa", "f@gmail.com", SupplierStatus.Pending)
};
var tenders = new List<RTender>(); var rounds = new List<RRound>(); var invitations = new List<RInvitation>(); var bids = new List<RBid>();
var bidId = 100; var invId = 500;
// Five closed rounds: 2 and 3 alternate as winners; 4 always prices 4% above 3 and submits 3 minutes after it; 1 and 5 price identical unit prices.
for (var r = 1; r <= 5; r++)
{
    var date = t0.AddDays(r * 30);
    var award = r <= 4 ? (int?)(r % 2 == 1 ? 2 : 3) : null;
    var offers = new (int Supplier, decimal Factor, int Minutes)[] { (1, 1.05m, 0), (2, r % 2 == 1 ? 0.93m : 1.02m, 500), (3, r % 2 == 1 ? 1.0m : 0.95m, 900), (4, (r % 2 == 1 ? 1.0m : 0.95m) * 1.04m, 903), (5, 1.05m, 2000) };
    var roundBids = new List<RBid>();
    foreach (var (s, f, m) in offers)
    {
        var lines = new List<RBidLine> { new(10 * r + 1, "Laptop", "unit", 10, 10, decimal.Round(800 * f, 2)), new(10 * r + 2, "Monitor", "unit", 10, 10, decimal.Round(200 * f, 2)) };
        var b = new RBid(++bidId, ++invId, r, r, s, 1, "JOD", lines.Sum(l => l.Quantity * l.UnitPrice), date.AddHours(-20).AddMinutes(m), ComplianceStatus.Compliant, 20, date, lines);
        roundBids.Add(b); invitations.Add(new RInvitation(invId, r, r, s, InvitationStatus.Accepted, date.AddDays(-20)));
    }
    invitations.Add(new RInvitation(++invId, r, r, 6, InvitationStatus.Pending, date.AddDays(-20)));
    bids.AddRange(roundBids);
    var awardedBid = award is { } w ? roundBids.First(b => b.SupplierId == w).Id : (int?)null;
    tenders.Add(new RTender(r, $"Tender {r}", award != null ? TenderStatus.Awarded : TenderStatus.Evaluation, "JOD", 10000, 1, "IT", date.AddDays(-30), awardedBid, award != null ? date.AddDays(10) : null, "Mandatory requirements.", "Tender description."));
    rounds.Add(new RRound(r, r, 1, TenderRoundStatus.SentToEvaluation, date, date, date.AddDays(-25), true));
}
// One sealed round (still open): never analysed.
rounds.Add(new RRound(6, 6, 1, TenderRoundStatus.Open, t0.AddDays(400), null, t0.AddDays(380), false));
tenders.Add(new RTender(6, "Open tender", TenderStatus.Bidding, "JOD", 1000, 1, "IT", t0.AddDays(370), null, null, null, "Open."));
var snapshot = new RadarSnapshot { GeneratedAt = t0.AddDays(300), Period = RadarPeriod.All, Suppliers = suppliers, Tenders = tenders, Rounds = rounds, Invitations = invitations, Bids = bids,
    Documents = [], SealedRounds = 1, SealedBids = 2 };

// The tender comparison skips newer sealed and incompatible cycles, and keeps both offers in one cycle.
var cycle = rounds[0];
var pairA = bids.First(b => b.RoundId == cycle.Id && b.SupplierId == 1) with { RecordedTotal = 57.125m, SubmittedAt = t0 };
var pairB = bids.First(b => b.RoundId == cycle.Id && b.SupplierId == 2) with { RecordedTotal = 65.375m, SubmittedAt = t0.AddTicks(4200123456) };
var comparable = new RadarSnapshot
{
    GeneratedAt = t0, Period = RadarPeriod.All, Suppliers = suppliers, Tenders = [tenders[0]], Invitations = [], Documents = [],
    Rounds = [cycle, cycle with { Id = 62, RoundNumber = 2 }, cycle with { Id = 63, RoundNumber = 3, Unsealed = false }],
    Bids = [pairA, pairB, pairA with { RoundId = 62 }, pairB with { RoundId = 62, Currency = "USD" },
        pairA with { RoundId = 63 }, pairB with { RoundId = 63 }]
};
var compared = RadarOfferComparison.Build(comparable, 1, 2).Single();
Check(compared.RoundId == cycle.Id && compared.LowerOffer == "a" && compared.Difference == 8.25m &&
    compared.RelativeDifference == 8.25m / 65.375m && compared.FirstSubmission == "a" && compared.SecondsApart == 420.0123456m &&
    compared.A.SubmittedAt?.Hour == 12 && compared.A.SubmittedAt?.Offset == TimeSpan.FromHours(3),
    "supplier offers use the latest comparable closed cycle, exact records, higher-offer percentage and Amman time");
Check(RadarOfferComparison.Build(comparable, 1, 1).Count == 0 && RadarOfferComparison.Build(comparable, 1, 6).Count == 0,
    "supplier offers reject duplicate suppliers and pairs without shared eligible offers");
// ---------- market analytics ----------
var summary = RadarAnalytics.Summary(snapshot);
Check(summary.RegisteredSuppliers == 6 && summary.EligibleSuppliers == 5, "registered vs eligible suppliers are distinguished");
Check(summary.AnalyzedRounds == 5 && summary.SubmittedBids == 25, "sealed open round is excluded from analysed rounds and offers");
Check(summary.SealedBids == 2 && summary.SealedRounds == 1, "sealed offers are reported, not analysed");
Check(Math.Abs(summary.AvgBidsPerRound!.Value - 5) < 1e-9, "average offers per round = offers / analysed rounds");
Check(Math.Abs(summary.ResponseRate!.Value - 25.0 / 30) < 1e-3, "response rate uses invitations (incl. ignored ones) as denominator");
Check(summary.AwardedValue.Count == 1 && summary.AwardedValue[0].Currency == "JOD" && summary.Awards == 4, "awarded value grouped per currency");
var stats = RadarAnalytics.Suppliers(snapshot).ToDictionary(x => x.SupplierId);
Check(stats[2].Wins == 2 && stats[2].AwardedTendersBid == 4 && Math.Abs(stats[2].WinRate!.Value - 0.5) < 1e-9, "win rate = awards / awarded tenders bid on");
Check(stats[6].WinRate == null && stats[6].ParticipationRate == 0, "supplier with no offers has undefined win rate and 0% response");
var positions = RadarAnalytics.RelativePositions(snapshot);
Check(positions.All(p => p.Comparable == 5) && positions.Count == 25, "relative prices only within a round and currency");
var competition = RadarAnalytics.CompetitionAnalysis(snapshot, 3);
Check(competition.LowCompetitionRounds == 0 && competition.Rounds.All(r => r.Invited == 6 && r.Submitted == 5), "competition separates invited from submitted");
var sim = RadarAnalytics.Simulate(snapshot, [2, 3], 3);
Check(sim.RoundsAffected == 5 && sim.RoundsBelowMinimumAfter == 0 && sim.AwardExposure.Single().Count == 4, "dependency simulation: coverage and award exposure");
Check(sim.HhiAfter > sim.HhiBefore && sim.Notice.Contains("not a forecast"), "simulation reports concentration and is labelled hypothetical");
var simAll = RadarAnalytics.Simulate(snapshot, [1, 2, 3, 4], 3);
Check(simAll.RoundsBelowMinimumAfter == 5 && simAll.RoundsWithoutBidders == 0, "simulation detects rounds falling below the minimum");

// ---------- approved behavior patterns ----------
var forensic = RadarForensics.Detect(snapshot, thresholds);
RadarFinding? Find(string detector, params int[] ids) => forensic.Findings.FirstOrDefault(f => f.Detector == detector && ids.All(f.SupplierIds.Contains));
Check(forensic.Detectors.Select(d => d.Detector).Order().SequenceEqual(new[] { RadarForensics.JointParticipation, RadarForensics.PriceGap, RadarForensics.SubmissionTiming }.Order()), "only the three approved behavior patterns run");
Check(forensic.Findings.Where(f => f.Detector == RadarForensics.JointParticipation).All(f => f.Level == "Context"), "joint participation is always context");
var gap = Find(RadarForensics.PriceGap, 3, 4);
Check(gap != null && gap.SampleSize == 5 && gap.Metrics["higher"] == "Delta", "repeated relative price gap 3-4 detected with sample size");
Check(Find(RadarForensics.PriceGap, 1, 2) == null, "no price-gap signal when the direction changes");
var timing = Find(RadarForensics.SubmissionTiming, 3, 4);
Check(timing != null && Find(RadarForensics.SubmissionTiming, 1, 2) == null, "submission-time proximity only for the 3-minute pair");
Check(forensic.Findings.All(f => f.Records.Count > 0 && f.Method.Length > 0 && f.Limitations.Length > 0 && f.Id.StartsWith("RF-")), "each retained pattern is tied to actual records and a stated method");
Check(RadarForensics.Detect(snapshot, thresholds).Findings.Select(f => f.Id).SequenceEqual(forensic.Findings.Select(f => f.Id)), "pattern ids are stable across runs");
var behavior = RadarBehavior.Build(snapshot);
Check(behavior.Count == 50 && behavior.All(o => o.PriceComparable && o.MinutesApart != null), "pair observations use recorded offers and submission times from closed rounds");

// Insufficient data: a market with a single round and no awards.
var tiny = new RadarSnapshot { GeneratedAt = t0, Period = RadarPeriod.All, Suppliers = suppliers, Tenders = tenders.Take(1).Select(t => t with { AwardedBidId = null }).ToList(),
    Rounds = rounds.Take(1).ToList(), Invitations = invitations.Where(i => i.RoundId == 1).ToList(), Bids = bids.Where(b => b.RoundId == 1).ToList(), Documents = [] };
var tinyResult = RadarForensics.Detect(tiny, thresholds);
Check(!tinyResult.Findings.Any(f => f.Level == "ReviewSignal"), "no statistical signal is produced from a single round");
var emptyMarket = new RadarSnapshot { GeneratedAt = t0, Period = RadarPeriod.All, Suppliers = [], Tenders = [], Rounds = [], Invitations = [], Bids = [], Documents = [] };
Check(RadarAnalytics.Summary(emptyMarket).AvgBidsPerRound == null && RadarGraph.Build(emptyMarket).Nodes.Count == 0, "empty market gives empty states, not invented data");

// ---------- graph ----------
var graph = RadarGraph.Build(snapshot);
Check(graph.Edges.Select(e => e.Id).Distinct().Count() == graph.Edges.Count, "graph edge ids are unique (no duplicated relationships)");
Check(graph.Nodes.All(n => n.Type is "supplier" or "tender") && graph.Edges.All(e => e.Type is "participation" or "award"), "removed forensic nodes and links do not enter the graph");
Check(suppliers.All(x => graph.Nodes.Any(n => n.Id == $"s-{x.Id}")) && graph.Nodes.Count(n => n.Type == "supplier") == suppliers.Count, "network shows every registered supplier once, including isolated ones");
Check(graph.Nodes.Single(n => n.Id == "s-6").Weight == 0 && !graph.Edges.Any(e => e.Source == "s-6" || e.Target == "s-6"), "a supplier with no closed-round offer stays an isolated node (no invented link)");

// ---------- network views ----------
var network = RadarGraph.Network(snapshot, 2);
Check(network.Rounds.Select(r => r.RoundId).Order().SequenceEqual([1, 2, 3, 4, 5]), "tender view rounds are closed rounds only (sealed round excluded)");
Check(network.Rounds.All(r => r.SupplierIds.SequenceEqual(bids.Where(b => b.RoundId == r.RoundId).Select(b => b.SupplierId).Distinct().Order())), "round incidence equals the submitted offers of that round");
Check(network.Rounds.Count(r => r.AwardedSupplierId != null) == 4 && network.Rounds.Single(r => r.RoundId == 1).AwardedSupplierId == 2, "round award comes from the official award only");
var link12 = network.CompetitionLinks.Single(l => l.SupplierA == 1 && l.SupplierB == 2);
Check(link12.RoundIds.Count == 5 && link12.RoundIds.Distinct().Count() == 5 && link12.FirstAt <= link12.LastAt, "competition link counts distinct shared closed rounds");
Check(network.CompetitionLinks.Select(l => l.Id).Distinct().Count() == network.CompetitionLinks.Count && network.CompetitionLinks.All(l => string.CompareOrdinal($"s-{l.SupplierA}", $"s-{l.SupplierB}") < 0), "competition links are unique and normalized");
Check(!network.CompetitionLinks.Any(l => l.SupplierA == 6 || l.SupplierB == 6), "no competition link without a shared closed round");
Check(network.CompetitionGroups.SelectMany(g => g).Order().SequenceEqual(suppliers.Select(x => x.Id).Order()) && network.CompetitionGroups.Any(g => g.SequenceEqual([6])), "competition groups partition all suppliers; singletons stay visible");
var duplicated = new RadarSnapshot { GeneratedAt = t0, Period = RadarPeriod.All, Suppliers = suppliers, Tenders = tenders, Rounds = rounds, Invitations = invitations,
    Bids = bids.Concat(bids.Where(b => b.RoundId == 1).Select(b => b with { Id = b.Id + 1000 })).ToList(), Documents = [] };
Check(RadarGraph.Network(duplicated, 2).CompetitionLinks.Single(l => l.SupplierA == 1 && l.SupplierB == 2).RoundIds.Count == 5, "two offers in one round never count as two shared rounds");
Check(RadarGraph.Network(snapshot, 6).CompetitionGroups.All(g => g.Count == 1), "a minimum above the shared-round count leaves every supplier on its own");
Check(RadarGraph.Network(emptyMarket, 2) is { Rounds.Count: 0, CompetitionLinks.Count: 0, CompetitionGroups.Count: 0 }, "empty market: no rounds, links or groups");

// ---------- sealing rule ----------
var now = t0.AddDays(100);
Check(!RadarDataService.IsUnsealed(TenderRoundStatus.Open, now.AddDays(1), now), "open round before its deadline stays sealed");
Check(RadarDataService.IsUnsealed(TenderRoundStatus.Open, now.AddMinutes(-1), now) && RadarDataService.IsUnsealed(TenderRoundStatus.Closed, null, now), "closed rounds and passed deadlines are analysable");
Check(!RadarDataService.IsUnsealed(TenderRoundStatus.Draft, null, now), "draft rounds are never analysed");

// ---------- Orange Intelligence: tender lifecycle ----------
var awardedTender = tenders.First(t => t.AwardedBidId != null);
var notifiedAt = awardedTender.AwardedAt!.Value.AddDays(2);
var withNotice = new RadarSnapshot { GeneratedAt = snapshot.GeneratedAt, Period = RadarPeriod.All, Suppliers = suppliers, Tenders = tenders, Rounds = rounds, Invitations = invitations, Bids = bids,
    Documents = [], WinnerNotifiedAt = new Dictionary<int, DateTime> { [awardedTender.Id] = notifiedAt } };
var lifecycle = RadarTenders.Build(withNotice);
var done = lifecycle.First(t => t.TenderId == awardedTender.Id);
Check(done.State == "Completed" && done.DaysToNotification == Math.Round((notifiedAt - awardedTender.CreatedAt).TotalDays, 1), "lifecycle: creation to winner notification sent, from recorded timestamps");
Check(lifecycle.Where(t => t.TenderId != awardedTender.Id).All(t => t.DaysToNotification == null), "lifecycle: no duration without a sent notification");
Check(lifecycle.First(t => t.TenderId == 6).Rounds.All(r => !r.Analysed && r.Offers == 0 && r.Bids.Count == 0), "lifecycle: sealed round exposes no offer");
Check(lifecycle.First(t => t.TenderId == 6).Stages.First(s => s.Key == "notified").At == null, "lifecycle: missing milestone stays unrecorded");

if (args.Contains("--exports"))
{
    var config = new ConfigurationBuilder().SetBasePath(Directory.GetCurrentDirectory()).AddJsonFile("appsettings.json")
        .AddJsonFile("appsettings.Development.json", optional: true).AddUserSecrets(System.Reflection.Assembly.Load("Orange-Tendering-System"), optional: true).Build();
    var connection = new SqlConnectionStringBuilder(config.GetConnectionString("Default Connection")) { InitialCatalog = TeamDevDatabase.DefaultName };
    if (!TeamDevDatabase.IsLocalTeamDatabase(connection.ConnectionString)) throw new InvalidOperationException("Export checks require the local team development database.");
    var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlServer(connection.ConnectionString).Options;
    await using var db = new ApplicationDbContext(options);
    using var cache = new MemoryCache(new MemoryCacheOptions());
    var data = new RadarDataService(db, cache, Options.Create(new RadarOptions()), TimeProvider.System);
    var radar = new RadarService(data, Options.Create(new RadarOptions()));
    var writer = new ReportExport(new OrangeReportRenderer(Options.Create(new ReportPdfOptions())));
    var injection = new ReportModel { Key = "check", Title = "Formula safety", GeneratedAt = DateTime.UtcNow };
    injection.Tables.Add(new ReportTable("values", "Values", ["Text", "Amount", "Date"], [["=HYPERLINK(\"bad\")", "-5.25", "2026-11-21 10:30"]]));
    var safeXlsx = ZipEntry(writer.ToXlsx(injection), "xl/worksheets/sheet2.xml");
    Check(safeXlsx.Contains("&apos;=HYPERLINK") && !safeXlsx.Contains("<f>") && safeXlsx.Contains("-5.25") && safeXlsx.Contains("s=\"2\""),
        "Excel content: unsafe formulas remain text; negative numbers and dates retain formats");
    var arabicWord = new ReportModel { Key = "check", Title = "تقرير الموردين", GeneratedAt = DateTime.UtcNow };
    arabicWord.Tables.Add(new ReportTable("values", "البيانات", ["المورد"], [["شركة الاختبار"]]));
    Check(ZipEntry(writer.ToDocx(arabicWord), "word/document.xml").Contains("<w:bidi/>"), "Word content: Arabic paragraphs use RTL direction");
    var files = new RadarExportService(radar, data, db, writer);
    var actual = await radar.SnapshotAsync(RadarPeriod.All, CancellationToken.None);
    var actualTender = RadarTenders.Build(actual).First(t => t.Rounds.Any(r => r.Analysed && r.Bids.Count > 0));
    var actualRound = actualTender.Rounds.First(r => r.Analysed && r.Bids.Count > 0);
    static string ZipEntry(byte[] bytes, string path)
    {
        using var zip = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
        using var reader = new StreamReader(zip.GetEntry(path)?.Open() ?? throw new InvalidDataException($"Missing {path}"));
        return reader.ReadToEnd();
    }
    var offerFile = await files.BuildAsync(new RadarExportRequest("xlsx", "tender_offers", null, null, actualTender.TenderId, [], actualRound.RoundId), CancellationToken.None);
    var offerXml = XDocument.Parse(ZipEntry(offerFile.Bytes, "xl/worksheets/sheet1.xml"));
    XNamespace ox = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    var offerRows = offerXml.Descendants(ox + "row").Skip(1).ToList();
    Check(offerRows.Count == actualRound.Bids.Count && offerRows.Select(r => string.Concat(r.Descendants(ox + "t").Select(x => x.Value))).Any(r => r.Contains(actualRound.Bids[0].Supplier)),
        "actual Excel offers: all selected-round suppliers and rows match database snapshot");
    Check(offerXml.ToString().Contains(actualRound.Bids[0].Total.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture))
        && ZipEntry(offerFile.Bytes, "xl/worksheets/sheet2.xml").Contains(actualTender.TenderId.ToString()), "actual Excel offers: totals and tender scope match source records");
    var participationFile = await files.BuildAsync(new RadarExportRequest("xlsx", "supplier_participation", null, null, null, []), CancellationToken.None);
    Check(!offerFile.Bytes.SequenceEqual(participationFile.Bytes) && ZipEntry(participationFile.Bytes, "xl/worksheets/sheet1.xml").Contains("Response rate"),
        "Excel: different report request produces different data and structure");
    var supplierOffersFile = await files.BuildAsync(new RadarExportRequest("xlsx", "supplier_offers", null, null, null, []), CancellationToken.None);
    var allOfferXml = XDocument.Parse(ZipEntry(supplierOffersFile.Bytes, "xl/worksheets/sheet1.xml"));
    Check(allOfferXml.Descendants(ox + "row").Skip(1).Count() == actual.Bids.Count,
        "cross-tender offers Excel: every authorized effective closed-round bid is present");
    var wordFile = await files.BuildAsync(new RadarExportRequest("docx", "tender_summary", null, null, actualTender.TenderId, []), CancellationToken.None);
    var wordXml = ZipEntry(wordFile.Bytes, "word/document.xml");
    Check(wordFile.Bytes.AsSpan(0, 2).SequenceEqual("PK"u8) && XDocument.Parse(wordXml).Descendants().Any(e => e.Name.LocalName == "tbl")
        && wordXml.Contains(actualTender.Title) && wordXml.Contains(actualRound.Bids[0].Supplier), "Word: valid editable DOCX with selected tender and real offer table");
    var lifecycleFile = await files.BuildAsync(new RadarExportRequest("xlsx", "tender_lifecycle", null, null, actualTender.TenderId, []), CancellationToken.None);
    var lifeXml = ZipEntry(lifecycleFile.Bytes, "xl/worksheets/sheet1.xml");
    Check(lifeXml.Contains(actualTender.TenderId.ToString()) && lifeXml.Contains("Bidding &amp; Supplier Submissions") && lifeXml.Contains(actualRound.RoundId.ToString()),
        "manual lifecycle Excel: actual tender, round and workflow records present");
    var pdfFile = await files.BuildAsync(new RadarExportRequest("pdf", "tender_summary", null, null, actualTender.TenderId, []), CancellationToken.None);
    using var pdfDocument = UglyToad.PdfPig.PdfDocument.Open(pdfFile.Bytes);
    var pdfText = string.Concat(pdfDocument.GetPages().Select(page => page.Text));
    Check(pdfFile.Bytes.AsSpan(0, 4).SequenceEqual("%PDF"u8) && !pdfText.Contains("Supplier network", StringComparison.OrdinalIgnoreCase)
        && pdfText.Contains(actualTender.TenderId.ToString()), "tender PDF: readable tender data, no supplier network graph");
    var rejected = false; try { await files.BuildAsync(new RadarExportRequest("xlsx", "tender_offers", null, null, int.MaxValue, []), CancellationToken.None); } catch (RadarToolException) { rejected = true; }
    Check(rejected, "export: unavailable tender does not produce a file");
    rejected = false; try { await files.BuildAsync(new RadarExportRequest("xlsx", "supplier_participation", null, null, null, [int.MaxValue]), CancellationToken.None); } catch (RadarToolException) { rejected = true; }
    Check(rejected, "export: unavailable supplier does not produce a file");
}

Console.WriteLine($"\n{passed} passed, {failures} failed");
return failures == 0 ? 0 : 1;
