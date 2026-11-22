// ORANGE RADAR checks. Pure analytics run on an in-memory synthetic market (no database).
// Optional: --speech runs a real local Piper -> Whisper round trip (Arabic + English) with the configured model paths.
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
var compareIntent = RadarIntents.Parse("Compare Alpha and Bravo in tender #1", comparable);
var compareAction = RadarIntents.Resolve(compareIntent!, new RadarPageContext(null, null, null, null, null, null, null, null, null), comparable, false);
Check(compareAction.Navigation is { Destination: "supplier_offer_comparison", TenderId: 1, SupplierId: 1, SecondSupplierId: 2, Comparison: not null },
    "AI selects the exact suppliers and tender for the new comparison");

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

// ---------- grounding guard ----------
var guard = new RadarGrounding();
guard.AddFacts("{\"winRate\":0.333,\"total\":187023.45,\"rounds\":9,\"avg\":3.4}");
Check(guard.Unverified("The win rate is 33.3% over 9 rounds.").Count == 0, "percent of a verified fraction is accepted");
Check(guard.Unverified("Awards total JOD 187,023.").Count == 0 && guard.Unverified("about 0.19 million dinars").Count == 0, "rounding and million shorthand are accepted");
Check(guard.Unverified("Average 3.4 offers per round.").Count == 0, "decimal figures are verified");
Check(guard.Unverified("There were 12 rounds.").SequenceEqual(["12"]), "an invented figure is flagged");
Check(guard.Unverified("بلغ عدد الجولات ٩ جولات").Count == 0 && guard.Unverified("بلغ عدد الجولات ١٢").Count == 1, "Arabic-Indic digits are normalized and checked");
Check(guard.Unverified("1. First point").Count == 0, "list numbering is not treated as a figure");
var (sentences, rest) = RadarGrounding.SplitSentences("Average is 3.4 offers. Next sentence");
Check(sentences.Count == 1 && sentences[0] == "Average is 3.4 offers." && rest == " Next sentence", "sentence splitting keeps decimals intact");
Check(RadarGrounding.SplitSentences("هل هذا صحيح؟ نعم").Sentences.Count == 1, "Arabic question mark ends a sentence");
Check(!RadarGrounding.Speakable("See RF-PG-1A2B3C4D for JOD 500.", false).Contains("RF-"), "finding codes are not read aloud");

// ---------- tool argument validation (model output is untrusted) ----------
bool Rejects(string tool, string json) { try { RadarAnalystService.ValidateArguments(tool, JsonDocument.Parse(json).RootElement); return false; } catch (RadarToolException) { return true; } }
Check(!Rejects("network_view", "{\"view\":\"competition\"}") && Rejects("network_view", "{\"view\":\"hub\"}") && Rejects("network_view", "{}"), "network views restricted to the four semantic views");
Check(Rejects("focus_category", $"{{\"category\":\"{new string('x', 90)}\"}}") && Rejects("focus_category", "{\"category\":3}"), "category focus arguments bounded");
Check(!Rejects("supplier_profile", "{\"supplierId\":3}") && !Rejects("supplier_profile", "{\"supplierId\":\"3\"}"), "valid tool arguments accepted (numeric string tolerated)");
Check(Rejects("supplier_profile", "{}") && Rejects("supplier_profile", "{\"supplierId\":-1}") && Rejects("supplier_profile", "{\"supplierId\":\"DROP TABLE\"}"), "missing, negative or non-numeric ids rejected");
Check(Rejects("show_view", "{\"mode\":\"javascript:alert(1)\"}") && !Rejects("show_view", "{\"mode\":\"network\"}"), "view modes restricted to the whitelist");
Check(Rejects("compare_suppliers", "{\"supplierIds\":[1]}") && Rejects("compare_suppliers", "{\"supplierIds\":[1,2,3,4,5]}"), "supplier lists bounded");
Check(Rejects("run_sql", "{}") && Rejects("execute_javascript", "{\"code\":\"x\"}"), "unknown tools rejected");
Check(Rejects("search_documents", "{\"query\":\"" + new string('x', 300) + "\"}"), "over-long strings rejected");

// ---------- optional: real local speech round trip ----------
if (args.Contains("--speech"))
{
    var options = Options.Create(new RadarOptions());
    if (args.Contains("--cpu")) options.Value.Whisper.Runtime = "cpu";
    if (args.Contains("--full-context")) options.Value.Whisper.AdaptiveAudioContext = false;
    // Run from the web project folder (README): the Piper worker script is resolved against the content root.
    var env = new StubEnvironment(Directory.GetCurrentDirectory());
    using var tts = new RadarSpeechSynthesizer(options, env, NullLogger<RadarSpeechSynthesizer>.Instance);
    using var stt = new RadarSpeechRecognizer(options, NullLogger<RadarSpeechRecognizer>.Instance);
    foreach (var (lang, text) in new[] { ("en", "Show me the supplier network and compare the two largest suppliers."), ("ar", "اعرض لي شبكة الموردين وقارن بين أكبر موردين.") })
    {
        var watch = Stopwatch.StartNew();
        var (wav, ttsMs) = await tts.SynthesizeAsync(text, lang, 1.0, CancellationToken.None);
        var wav16 = To16k(wav);
        var transcript = await stt.TranscribeAsync(new MemoryStream(wav16), "auto", CancellationToken.None);
        Console.WriteLine($"      {lang}: TTS {ttsMs:0} ms -> STT {transcript.ProcessingMs:0} ms for {transcript.AudioSeconds:0.0} s audio, detected '{transcript.Language}': {transcript.Text}");
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(w => w.Trim('.', '،'));
        var recovered = words.Count(w => transcript.Text.Contains(w, StringComparison.OrdinalIgnoreCase)) / (double)words.Count();
        Check(transcript.Language == lang && recovered >= 0.5, $"speech round trip {lang}: language detected, {recovered:P0} of words recovered");
    }
    // Second English call measures the warm (model already loaded) latency.
    var warm = await stt.TranscribeAsync(new MemoryStream(To16k((await tts.SynthesizeAsync("What happens if this supplier is unavailable?", "en", 1.0, CancellationToken.None)).Wav)), "en", CancellationToken.None);
    Console.WriteLine($"      warm English STT: {warm.ProcessingMs:0} ms for {warm.AudioSeconds:0.0} s: {warm.Text}  [{stt.State}]");
}

// ---------- optional: speech-recognition benchmark on recorded utterances ----------
// --stt-bench <corpus>: manifest.json ({id, text, language}) plus folders of 16 kHz WAVs written by stt-pipeline.js.
// Every decoding setting hears identical audio; results are word/character error rates against the spoken text.
var benchAt = Array.IndexOf(args, "--stt-bench");
if (benchAt >= 0)
{
    var corpus = args[benchAt + 1];
    var items = JsonSerializer.Deserialize<List<SttItem>>(File.ReadAllText(Path.Combine(corpus, "manifest.json")), new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
    var whisperOptions = Options.Create(new RadarOptions());
    using var recognizer = new RadarSpeechRecognizer(whisperOptions, NullLogger<RadarSpeechRecognizer>.Instance);
    const string DomainPrompt = "Orange Intelligence procurement assistant. Requests about suppliers, tenders, bids, competitors, categories, financial offers, price patterns and submission timing; generate an Excel sheet, a PDF or a Word report.";
    const string DomainPromptAr = "مساعد المشتريات في أورنج. أسئلة عن الموردين والمناقصات والعروض المالية والمنافسين وتوقيت تقديم العروض، مع تقارير Excel و PDF و Word.";
    var legacy = new RadarWhisperOptions { AdaptiveAudioContext = true, BeamSize = 1, EnglishPrompt = null, ArabicPrompt = null, LanguageConfidence = 0 };
    var settings = new (string Name, RadarWhisperOptions Options)[]
    {
        ("legacy", legacy),
        ("full-context", Clone(legacy, o => o.AdaptiveAudioContext = false)),
        ("+beam5", Clone(legacy, o => { o.AdaptiveAudioContext = false; o.BeamSize = 5; })),
        ("+prompt", Clone(legacy, o => { o.AdaptiveAudioContext = false; o.BeamSize = 5; o.EnglishPrompt = DomainPrompt; o.ArabicPrompt = DomainPromptAr; })),
        ("final", new RadarWhisperOptions()),
        ("beam5+arbitration", Clone(legacy, o => { o.BeamSize = 5; o.LanguageConfidence = 0.8; })),
    };
    var only = args.SkipWhile(a => a != "--settings").Skip(1).FirstOrDefault()?.Split(',');
    var folders = args.SkipWhile(a => a != "--folders").Skip(1).FirstOrDefault()?.Split(',') ?? ["old-clean", "old-noisy", "old-quiet", "new-clean", "new-noisy", "new-quiet"];
    var rows = new List<object>();
    Console.WriteLine($"{"setting",-15}{"audio",-11}{"WER",7}{"CER",7}{"lang",6}{"excel",7}{"terms",7}{"ms avg",8}{"ms p90",8}{"flag wrong",11}{"flag right",11}");
    await recognizer.WarmAsync(CancellationToken.None);
    foreach (var (name, setting) in settings.Where(s => only == null || only.Contains(s.Name)))
        foreach (var folder in folders.Where(f => name == "legacy" || f.StartsWith("new-")))
        {
            Copy(setting, whisperOptions.Value.Whisper);
            var results = new List<(SttItem Item, RadarTranscript T, double Wer, double Cer)>();
            foreach (var item in items)
            {
                var file = Path.Combine(corpus, folder, item.Id + ".wav");
                var t = File.Exists(file) ? await recognizer.TranscribeAsync(File.OpenRead(file), "auto", CancellationToken.None) : new RadarTranscript("", "", 0, 0);
                results.Add((item, t, Stt.ErrorRate(Stt.Words(item.Text), Stt.Words(t.Text)), Stt.ErrorRate(Stt.Chars(item.Text), Stt.Chars(t.Text))));
                rows.Add(new { setting = name, folder, item.Id, item.Text, heard = t.Text, t.Language, t.Confidence, t.Reliable, t.LanguageProbability, t.LanguageArbitrated, t.ProcessingMs });
            }
            var langOk = results.Count(r => r.T.Language == (r.Item.Language == "mixed" ? "ar" : r.Item.Language)) / (double)results.Count;
            var excel = results.Where(r => r.Item.Id.EndsWith("-excel")).Count(r => Stt.Words(r.T.Text).SequenceEqual(Stt.Words(r.Item.Text)));
            var terms = Stt.TermRecall(results.Select(r => (r.Item.Text, r.T.Text)));
            var ms = results.Select(r => r.T.ProcessingMs).Order().ToList();
            var wrong = results.Where(r => r.Wer > 0.2).ToList(); var right = results.Where(r => r.Wer <= 0.2).ToList();
            Console.WriteLine($"{name,-15}{folder,-11}{results.Average(r => r.Wer),7:P1}{results.Average(r => r.Cer),7:P1}{langOk,6:P0}{excel + "/" + results.Count(r => r.Item.Id.EndsWith("-excel")),7}{terms,7:P0}{ms.Average(),8:0}{ms[(int)(ms.Count * 0.9)],8:0}" +
                $"{(wrong.Count == 0 ? "-" : $"{wrong.Count(r => !r.T.Reliable)}/{wrong.Count}"),11}{(right.Count == 0 ? "-" : $"{right.Count(r => !r.T.Reliable)}/{right.Count}"),11}");
            File.WriteAllText(Path.Combine(corpus, "stt-results.json"), JsonSerializer.Serialize(rows, new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
        }
    File.WriteAllText(Path.Combine(corpus, "stt-results.json"), JsonSerializer.Serialize(rows, new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
    return 0;

    static RadarWhisperOptions Clone(RadarWhisperOptions source, Action<RadarWhisperOptions> change) { var o = new RadarWhisperOptions(); Copy(source, o); change(o); return o; }
    static void Copy(RadarWhisperOptions from, RadarWhisperOptions to)
    {
        to.AdaptiveAudioContext = from.AdaptiveAudioContext; to.BeamSize = from.BeamSize; to.EnglishPrompt = from.EnglishPrompt; to.ArabicPrompt = from.ArabicPrompt;
        to.LanguageConfidence = from.LanguageConfidence; to.ReliableConfidence = from.ReliableConfidence; to.Runtime = from.Runtime; to.ModelPath = from.ModelPath;
    }
}

// ---------- optional: intent benchmark on the local team database (read-only) ----------
// --intent-bench [--models a,b]: labelled requests (English, Arabic, misheard speech, page context, two-turn clarifications) are
// routed by the rules alone, by each local model alone (thinking off / on), and by the shipped hybrid (rules, model only when
// the rules leave a request unclear). A case passes only if the resolved action and record ids are right.
if (args.Contains("--intent-bench"))
{
    var config = new ConfigurationBuilder().SetBasePath(Directory.GetCurrentDirectory()).AddJsonFile("appsettings.json")
        .AddJsonFile("appsettings.Development.json", optional: true).AddUserSecrets(System.Reflection.Assembly.Load("Orange-Tendering-System"), optional: true).Build();
    var connection = new SqlConnectionStringBuilder(config.GetConnectionString("Default Connection")) { InitialCatalog = TeamDevDatabase.DefaultName };
    if (!TeamDevDatabase.IsLocalTeamDatabase(connection.ConnectionString)) throw new InvalidOperationException("The intent benchmark requires the local team development database.");
    await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlServer(connection.ConnectionString).Options);
    using var cache = new MemoryCache(new MemoryCacheOptions());
    var radarOptions = Options.Create(new RadarOptions());
    var live = await new RadarService(new RadarDataService(db, cache, radarOptions, TimeProvider.System), radarOptions).SnapshotAsync(RadarPeriod.All, CancellationToken.None);
    int Code(string code) => RadarIntents.ResolveSupplier(code, live) ?? throw new InvalidOperationException($"benchmark supplier {code} missing");
    int TenderLike(string word) => live.Tenders.First(t => t.Title.Contains(word, StringComparison.OrdinalIgnoreCase)).Id;
    var anyTender = live.Tenders.OrderByDescending(t => t.Id).First().Id;
    var servers = TenderLike("servers");
    IntentCase R(string text, string format, string report, int supplier = 0, int tender = 0, int supplier2 = 0) => new(text, "report", format, report, null, supplier, tender, supplier2);
    IntentCase N(string text, string destination, int supplier = 0, int tender = 0) => new(text, "navigate", null, null, destination, supplier, tender);
    var cases = new List<IntentCase>
    {
        R("Generate an Excel sheet for all authorized suppliers.", "xlsx", "suppliers"),
        R("Please generate a PDF that shows every supplier and the tender.", "pdf", "supplier_participation"),
        R("Generate an Excel sheet showing which suppliers participated in which tenders.", "xlsx", "supplier_participation"),
        R("Could you please generate an Excel sheet that shows the suppliers market right now? I want it like a summary.", "xlsx", "market_overview"),
        R("Call you please generate an exit sheet that shows that in the unsubliers market right now, I want it like an assembly in the exit sheet.", "xlsx", "market_overview"),
        R("Generate an exit sheet comparing the suppliers.", "xlsx", "supplier_comparison"),
        R("Make a Word report comparing supplier A21 and A22.", "docx", "supplier_comparison", Code("A21"), 0, Code("A22")),
        R("I need a PDF about supplier A21.", "pdf", "supplier_profile", Code("A21")),
        R($"Export the financial offers of tender #{anyTender} to Excel.", "xlsx", "tender_offers", 0, anyTender),
        R("Create a PDF of the lifecycle of the servers tender.", "pdf", "tender_lifecycle", 0, servers),
        R("Give me a spreadsheet of all tenders.", "xlsx", "tenders"),
        R("Generate a Word document on price patterns.", "docx", "price_patterns"),
        R("Generate an Excel of submission timing for the last three months.", "xlsx", "submission_timing"),
        R("Put the offers of all suppliers across tenders in an Excel file.", "xlsx", "supplier_offers"),
        new("Generate an Excel report of invoices.", "unsupported"),
        N("Show me the suppliers market.", "supplier_market"),
        N("Show me the supplier categories.", "categories"),
        N("Show A21's competitors.", "competition", Code("A21")),
        N($"Show the lifecycle of tender #{anyTender}.", "tender_lifecycle", 0, anyTender),
        N("Show the submission timing analysis.", "submission_timing"),
        N("Take me to price patterns.", "price_patterns"),
        N("Open the tenders list.", "tenders"),
        new("Who competes most often?", "question"),
        new("Which supplier won the most tenders?", "question"),
        new("Why is A21 connected to other suppliers?", "question"),
        new("Draw a chart of supplier wins.", "question"),
        R("أنشئ ملف إكسل يوضح جميع الموردين والمناقصات", "xlsx", "supplier_participation"),
        N("اعرض لي سوق الموردين", "supplier_market"),
        R("حضر تقرير PDF عن مشاركة الموردين خلال الأشهر الثلاثة الماضية", "pdf", "supplier_participation"),
        N("اعرض تحليل توقيت تقديم العروض", "submission_timing"),
        new("ما هو المورد الذي فاز بأكبر عدد من المناقصات؟", "question"),
        R("اعمل لي Excel sheet فيه كل الموردين", "xlsx", "suppliers"),
        R("صدّر العروض المالية لجميع الموردين إلى ملف وورد", "docx", "supplier_offers"),
        R("all suppliers and their tenders", "xlsx", "supplier_participation") with { First = "Generate an Excel sheet." },
        R("the servers one", "pdf", "tender_lifecycle", 0, servers) with { First = "Create a PDF of the tender lifecycle." },
        R("Generate a Word report about this tender.", "docx", "tender_summary", 0, anyTender) with { SelectedTender = anyTender },
        R("Generate an exit sheet.", "xlsx", "supplier_comparison") with { Last = "supplier_comparison" },
    };
    // --cases N keeps every Nth-spread sample for slow settings (thinking); --think on|off limits the thinking modes run.
    if (int.TryParse(args.SkipWhile(a => a != "--cases").Skip(1).FirstOrDefault(), out var sample) && sample > 0 && sample < cases.Count)
        cases = cases.Where((_, i) => i % (cases.Count / sample) == 0).Take(sample).ToList();
    var thinkOnly = args.SkipWhile(a => a != "--think").Skip(1).FirstOrDefault();
    var ollamaClient = new RadarOllamaClient(new HttpClient(), radarOptions, NullLogger<RadarOllamaClient>.Instance);
    var routerClient = new RadarIntentRouter(ollamaClient, radarOptions);
    var installed = (await ollamaClient.StatusAsync(CancellationToken.None)).Models;
    var models = (args.SkipWhile(a => a != "--models").Skip(1).FirstOrDefault()?.Split(',') ?? ["qwen3.5:4b", "qwen3:8b", "gemma3:4b"]).Where(installed.Contains).ToList();
    bool Pass(IntentCase c, RadarAction act) => c.Kind switch
    {
        "question" => act.Intent.Kind == "question" && act.Export == null && act.Navigation == null,
        "unsupported" => act.Refusal != null && act.Export == null && act.Navigation == null,
        "report" => act.Export is { } e && e.Format == c.Format && e.ReportType == c.Report && (c.Tender == 0 || e.TenderId == c.Tender)
            && new[] { c.Supplier, c.Supplier2 }.Where(x => x > 0).All(x => (e.SupplierIds ?? []).Contains(x)),
        _ => act.Navigation is { } n && n.Destination == c.Destination && (c.Supplier == 0 || n.SupplierId == c.Supplier) && (c.Tender == 0 || n.TenderId == c.Tender)
    };
    var report = new List<object>();
    async Task Run(string name, Func<string, RadarPageContext, RadarIntent?, Task<(RadarIntent? Intent, double Ms, bool Valid)>> route)
    {
        int ok = 0, valid = 0, calls = 0; var ms = new List<double>(); var failed = new List<string>();
        foreach (var c in cases)
        {
            var ctx = new RadarPageContext(null, [], c.SelectedTender > 0 ? c.SelectedTender : null, null, null, null, null, null, null, LastReport: c.Last);
            RadarIntent? pending = null;
            if (c.First != null)
            {
                var (first, firstMs, firstValid) = await route(c.First, ctx, null);
                pending = RadarIntents.Resolve(first ?? new RadarIntent("question"), ctx, live, false).Pending;
            }
            var (intent, elapsed, isValid) = await route(c.Text, ctx with { Pending = pending }, pending);
            if (elapsed > 0) { ms.Add(elapsed); calls++; }
            if (isValid) valid++;
            var act = RadarIntents.Resolve(intent ?? new RadarIntent("question"), ctx with { Pending = pending }, live, RadarGrounding.IsArabic(c.Text));
            if (Pass(c, act)) ok++; else failed.Add($"{c.Text[..Math.Min(48, c.Text.Length)]} -> {RadarIntents.Serialize(act.Intent)}");
            report.Add(new { setting = name, c.Text, expected = c, got = act.Intent, ms = elapsed, pass = Pass(c, act) });
        }
        ms.Sort();
        Console.WriteLine($"{name,-30}{ok,4}/{cases.Count}{(double)valid / cases.Count,8:P0}{(ms.Count == 0 ? 0 : ms.Average()),9:0}{(ms.Count == 0 ? 0 : ms[(int)(ms.Count * 0.9)]),9:0}{calls,7}");
        foreach (var f in failed) Console.WriteLine($"      x {f}");
    }
    Console.WriteLine($"{"setting",-30}{"correct",8}{"json ok",8}{"ms avg",9}{"ms p90",9}{"calls",7}");
    var rulesOnly = new Func<string, RadarPageContext, RadarIntent?, Task<(RadarIntent?, double, bool)>>((text, ctx, pending) =>
        Task.FromResult<(RadarIntent?, double, bool)>((RadarIntents.Parse(text, live, pending, ctx.LastReport), 0, true)));
    await Run("rules only", rulesOnly);
    foreach (var model in models)
        foreach (var think in (model.StartsWith("gemma") ? new bool?[] { null } : [false, true]).Where(t => thinkOnly == null || t == null || t == (thinkOnly == "on")))
        {
            await ollamaClient.WarmAsync(model, CancellationToken.None);
            async Task<(RadarIntent?, double, bool)> Model(string text, RadarPageContext ctx, RadarIntent? pending)
            {
                try { var (i, ms) = await routerClient.RouteAsync(text, ctx, live, null, pending, CancellationToken.None, model, think); return (i, ms, i != null); }
                catch (RadarAiUnavailableException) { return (null, 0, false); }
            }
            await Run($"model {model} think={(think?.ToString() ?? "n/a")}", Model);
            await Run($"hybrid {model} think={(think?.ToString() ?? "n/a")}", async (text, ctx, pending) =>
            {
                var rules = RadarIntents.Parse(text, live, pending, ctx.LastReport);
                if (!RadarIntents.NeedsModel(text, rules, pending)) return (rules, 0, true);
                var (m, ms, ok) = await Model(text, ctx, pending);
                return (RadarIntentRouter.Merge(rules, m), ms, ok);
            });
        }
    File.WriteAllText(Path.Combine(Path.GetTempPath(), "orange-intent-bench.json"), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
    return 0;
}

// ---------- optional: end-to-end file scenarios on the local team database (read-only) ----------
// --scenarios: the user's own sentences go through the real analyst turn (intent -> resolution -> export -> reply). The generated
// file is taken from the download cache, opened, and its populated rows are compared with independent SQL over the same tables.
if (args.Contains("--scenarios"))
{
    var config = new ConfigurationBuilder().SetBasePath(Directory.GetCurrentDirectory()).AddJsonFile("appsettings.json")
        .AddJsonFile("appsettings.Development.json", optional: true).AddUserSecrets(System.Reflection.Assembly.Load("Orange-Tendering-System"), optional: true).Build();
    var connection = new SqlConnectionStringBuilder(config.GetConnectionString("Default Connection")) { InitialCatalog = TeamDevDatabase.DefaultName };
    if (!TeamDevDatabase.IsLocalTeamDatabase(connection.ConnectionString)) throw new InvalidOperationException("Scenarios require the local team development database.");
    await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlServer(connection.ConnectionString).Options);
    using var cache = new MemoryCache(new MemoryCacheOptions());
    var radarOptions = Options.Create(new RadarOptions());
    var data = new RadarDataService(db, cache, radarOptions, TimeProvider.System);
    var radar = new RadarService(data, radarOptions);
    var ollamaClient = new RadarOllamaClient(new HttpClient(), radarOptions, NullLogger<RadarOllamaClient>.Instance);
    var exports = new RadarExportService(radar, data, db, new ReportExport(new OrangeReportRenderer(Options.Create(new ReportPdfOptions()))));
    using var downloads = new MemoryCache(new MemoryCacheOptions());
    // File and navigation turns never reach document similarity, so it is not constructed here.
    RadarAnalystService Analyst() => new(radar, null!, ollamaClient, radarOptions, NullLogger<RadarAnalystService>.Instance, exports, new RadarIntentRouter(ollamaClient, radarOptions), downloads);

    // Independent SQL (no Orange Intelligence code): suppliers, invitations, effective submitted bids, sealing by round status/deadline.
    await using var sql = new SqlConnection(connection.ConnectionString); await sql.OpenAsync();
    async Task<List<object?[]>> Query(string text) { await using var cmd = new SqlCommand(text, sql); await using var r = await cmd.ExecuteReaderAsync(); var rows = new List<object?[]>(); while (await r.ReadAsync()) { var v = new object?[r.FieldCount]; for (var i = 0; i < v.Length; i++) v[i] = r.IsDBNull(i) ? null : r.GetValue(i); rows.Add(v); } return rows; }
    var dbSuppliers = (await Query("SELECT Id, CompanyName, Status FROM Suppliers")).Select(r => (Id: (int)r[0]!, Name: (string)r[1]!, Status: (string)r[2]!)).ToList();
    var dbInvitations = (await Query(@"SELECT i.Id, i.SupplierId, r.TenderId, i.TenderRoundId, r.Status, CASE WHEN r.Status IN ('Closed','Failed','SentToEvaluation') OR (r.Status = 'Open' AND r.Deadline <= SYSUTCDATETIME()) THEN 1 ELSE 0 END
        FROM TenderInvitations i JOIN TenderRounds r ON r.Id = i.TenderRoundId")).Select(r => (Id: (int)r[0]!, Supplier: (int)r[1]!, Tender: (int)r[2]!, Round: (int)r[3]!, Unsealed: (int)r[5]! == 1)).ToList();
    var dbSubmitted = (await Query("SELECT DISTINCT TenderInvitationId FROM Bids WHERE Status = 'Submitted'")).Select(r => (int)r[0]!).ToHashSet();
    var dbTenders = (await Query("SELECT Id, Title FROM Tenders WHERE Id IN (SELECT TenderId FROM TenderRounds)")).Select(r => (Id: (int)r[0]!, Title: (string)r[1]!)).ToList();

    async Task<(List<JsonElement> Events, RadarExportFile? File, string Reply)> Turn(string text, RadarPageContext? ctx = null)
    {
        var events = new List<JsonElement>();
        await Analyst().AskAsync(new RadarAskRequest(text, ctx ?? new RadarPageContext(null, [], null, null, null, null, null, null, null), [], null), "scenario-user",
            e => { events.Add(JsonSerializer.SerializeToElement(e, new JsonSerializerOptions(JsonSerializerDefaults.Web))); return Task.CompletedTask; }, CancellationToken.None);
        var token = events.Where(e => e.GetProperty("type").GetString() == "command" && e.GetProperty("command").TryGetProperty("token", out _)).Select(e => e.GetProperty("command").GetProperty("token").GetString()).FirstOrDefault();
        var file = token != null && downloads.TryGetValue($"oi-file:{token}", out RadarStoredFile? stored) && stored!.Owner == "scenario-user" ? stored.File : null;
        var reply = events.LastOrDefault(e => e.GetProperty("type").GetString() == "done") is { ValueKind: JsonValueKind.Object } d ? d.GetProperty("text").GetString() ?? "" : "";
        Console.WriteLine($"      \"{text}\"\n        -> {reply}");
        return (events, file, reply);
    }
    static Dictionary<string, List<string[]>> Workbook(byte[] bytes)
    {
        using var zip = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
        XNamespace x = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var names = XDocument.Load(zip.GetEntry("xl/workbook.xml")!.Open()).Descendants(x + "sheet").Select(e => (string)e.Attribute("name")!).ToList();
        return names.Select((n, i) => (n, XDocument.Load(zip.GetEntry($"xl/worksheets/sheet{i + 1}.xml")!.Open()).Descendants(x + "row")
            .Select(r => r.Elements(x + "c").Select(c => c.Element(x + "v")?.Value ?? string.Concat(c.Descendants(x + "t").Select(t => t.Value))).ToArray()).ToList())).ToDictionary(p => p.n, p => p.Item2);
    }

    // A. Excel of all authorized suppliers
    var (_, fileA, replyA) = await Turn("Generate an Excel sheet for all authorized suppliers.");
    var bookA = fileA == null ? null : Workbook(fileA.Bytes);
    var rowsA = bookA?.GetValueOrDefault("Suppliers")?.Skip(1).ToList() ?? [];
    Check(bookA != null && bookA.Keys.First() == "Suppliers" && rowsA.Count == dbSuppliers.Count && dbSuppliers.All(s => rowsA.Any(r => r[0] == s.Id.ToString() && r[1] == s.Name && r[3] == s.Status))
        && replyA.Contains($"Suppliers ({dbSuppliers.Count} rows)"), $"A: Excel lists all {dbSuppliers.Count} suppliers with id, name and status exactly as in SQL; reply states the count");

    // B. Excel of supplier-to-tender participation
    var (_, fileB, replyB) = await Turn("Generate an Excel sheet showing supplier-to-tender participation.");
    var rowsB = fileB == null ? [] : Workbook(fileB.Bytes).GetValueOrDefault("Tender participation")?.Skip(1).ToList() ?? [];
    var expectedB = dbInvitations.Select(i => $"{i.Supplier}|{i.Tender}|{i.Round}|{(!i.Unsealed ? "Sealed until the round closes" : dbSubmitted.Contains(i.Id) ? "Submitted" : "No offer")}").Order().ToList();
    var gotB = rowsB.Select(r => $"{r[0]}|{r[2]}|{r[4]}|{r[9]}").Order().ToList();
    Check(gotB.SequenceEqual(expectedB) && replyB.Contains($"Tender participation ({dbInvitations.Count} rows)"),
        $"B: Excel participation rows equal the {dbInvitations.Count} SQL invitations (supplier, tender, round, offer status; sealed rounds hidden)");

    // C. PDF of every supplier and the tenders, without asking for one tender
    var (eventsC, fileC, replyC) = await Turn("Please generate a PDF that shows every supplier and the tender.");
    // The extractor yields one word per line; whitespace is normalised before matching names.
    var pdfC = fileC == null ? "" : System.Text.RegularExpressions.Regex.Replace(RadarFileExtractor.Extract("c.pdf", new MemoryStream(fileC.Bytes)).Text, @"\s+", " ");
    Check(fileC != null && fileC.Bytes.AsSpan(0, 4).SequenceEqual("%PDF"u8) && !eventsC.Any(e => e.GetProperty("type").GetString() == "pending")
        && dbSuppliers.All(s => pdfC.Contains(s.Name)) && dbInvitations.Select(i => i.Tender).Distinct().All(t => pdfC.Contains(t.ToString())) && !pdfC.Contains("Supplier network", StringComparison.OrdinalIgnoreCase),
        "C: PDF created without demanding a tender; every SQL supplier and invited tender appears; no supplier-network graph");

    // D. Editable Word report
    var (_, fileD, _) = await Turn("Generate a Word report comparing all suppliers.");
    string Entry(byte[] b, string path) { using var z = new ZipArchive(new MemoryStream(b), ZipArchiveMode.Read); using var r = new StreamReader(z.GetEntry(path)?.Open() ?? Stream.Null); return r.ReadToEnd(); }
    var docD = fileD == null ? "" : Entry(fileD.Bytes, "word/document.xml");
    var settingsD = fileD == null ? "" : Entry(fileD.Bytes, "word/settings.xml");
    var tableRowsD = System.Text.RegularExpressions.Regex.Matches(docD, "<w:tr[ >]").Count;
    Check(fileD != null && fileD.FileName.EndsWith(".docx") && docD.Contains("<w:tbl>") && !docD.Contains("documentProtection") && !settingsD.Contains("documentProtection")
        && dbSuppliers.All(s => docD.Contains(System.Security.SecurityElement.Escape(s.Name)!)) && tableRowsD >= dbSuppliers.Count + 1,
        $"D: Word file is an unprotected, editable DOCX table with every supplier ({tableRowsD} table rows)");

    // E. Two scopes give different, individually correct data
    var offerTender = dbInvitations.Where(i => i.Unsealed && dbSubmitted.Contains(i.Id)).GroupBy(i => i.Tender).OrderByDescending(g => g.Count()).First();
    var (_, fileE, replyE) = await Turn($"Export the financial offers of tender #{offerTender.Key} to Excel.");
    var bookE = fileE == null ? null : Workbook(fileE.Bytes);
    var rowsE = bookE?.Values.First().Skip(1).ToList() ?? [];
    Check(bookE != null && rowsE.Count == offerTender.Count() && rowsE.All(r => r[0] == offerTender.Key.ToString()) && !bookE.Keys.SequenceEqual(bookA?.Keys.ToArray() ?? [])
        && rowsE.Select(r => r[4]).Order().SequenceEqual(offerTender.Select(i => i.Supplier.ToString()).Order()),
        $"E: tender #{offerTender.Key} offers file has its {offerTender.Count()} SQL offers only and differs from the supplier list");

    // F. zero records: a valid query with no matches says so
    var (_, fileF, replyF) = await Turn("Generate an Excel of submission timing for the last 1 months.");
    var bookF = fileF == null ? null : Workbook(fileF.Bytes);
    Check(bookF != null && (bookF.Values.First().Count > 1 || (replyF.Contains("no authorized records match") && bookF["Summary"].Any(r => r.Contains("No authorized records match these filters. Nothing was omitted or invented.")))),
        "F: an empty valid query is stated explicitly in the reply and the file, never padded");
    Console.WriteLine($"\n{passed} passed, {failures} failed");
    return failures == 0 ? 0 : 1;
}

// ---------- Orange Intelligence: tender lifecycle and generative visuals ----------
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
var winViz = RadarVisuals.Build(snapshot, forensic, "supplier_metric", "wins", [], 0, "auto", thresholds.MinimumBidsForCompetition);
var vizStats = RadarAnalytics.Suppliers(snapshot);
Check(winViz.Labels.Count > 0 && winViz.Labels.Select((l, i) => (l, v: winViz.Series[0].Values[i])).All(x => vizStats.First(s => s.Name == x.l).Wins == (int)x.v!), "visual: values equal the server analytics");
var tableViz = RadarVisuals.Build(snapshot, forensic, "category_distribution", "", [], 0, "table", thresholds.MinimumBidsForCompetition);
Check(tableViz.Kind == "table" && tableViz.Rows.Sum(r => int.Parse(r[1])) == suppliers.Count, "visual: table form keeps every registered supplier");
Check(RadarVisuals.Build(snapshot, forensic, "tender_lifecycle", "", [], 0, "auto", thresholds.MinimumBidsForCompetition).Labels.Count == 0, "visual: no lifecycle bars without sent notifications");
var visualArgs = RadarAnalystService.ValidateArguments("build_visual", JsonDocument.Parse("""{"dataset":"supplier_metric"}""").RootElement);
Check((string)visualArgs["chart"] == "auto" && ((int[])visualArgs["supplierIds"]).Length == 0 && (int)visualArgs["tenderId"] == 0, "build_visual: optional arguments default safely");
var rejected = false; try { RadarAnalystService.ValidateArguments("build_visual", JsonDocument.Parse("""{"dataset":"raw_sql"}""").RootElement); } catch (RadarToolException) { rejected = true; }
Check(rejected, "build_visual: datasets restricted to the allowlist");

var page = new RadarPageContext("network", [1], 1, null, null, null, null, null, null);
var noTenderPage = page with { SelectedTenderId = null, SelectedSupplierIds = [] };
RadarAction Act(string text, RadarPageContext? ctx = null, RadarIntent? pending = null, string? last = null) =>
    RadarIntents.Resolve(RadarIntents.Parse(text, snapshot, pending, last) ?? new RadarIntent("question"), ctx ?? page, snapshot, RadarGrounding.IsArabic(text));
var a = Act("Please generate a PDF that shows every supplier and the tender.", noTenderPage);
Check(a.Export is { Format: "pdf", ReportType: "supplier_participation", TenderId: null } && a.Question == null && a.Refusal == null, "intent: 'every supplier and the tender' is a general participation PDF, no tender demanded");
a = Act("Generate an Excel sheet that shows the suppliers market right now. I want it like a summary in the Excel sheet.");
Check(a.Export is { Format: "xlsx", ReportType: "market_overview" }, "intent: suppliers market Excel = market overview");
a = Act("Generate an Excel sheet.", noTenderPage);
Check(a.Export == null && a.Question != null && a.Pending is { Format: "xlsx" }, "intent: bare 'Generate an Excel sheet' asks one focused question and keeps the format");
var answered = Act("all suppliers and their tenders", noTenderPage, a.Pending);
Check(answered.Export is { Format: "xlsx", ReportType: "supplier_participation" }, "intent: the answer completes the pending request without repeating the format");
Check(RadarIntents.Parse("Who won the most tenders?", snapshot, a.Pending) == null, "intent: a new question while a clarification is pending is answered, not swallowed");
a = Act("Generate an exit sheet.", noTenderPage, null, "supplier_comparison");
Check(a.Export is { Format: "xlsx", ReportType: "supplier_comparison" }, "intent: misheard 'exit sheet' + previous chart topic exports that topic");
a = Act("Generate an Excel report comparing submitted offers for Tender #1");
Check(a.Export is { Format: "xlsx", ReportType: "tender_offers", TenderId: 1 }, "intent: offers of a named tender");
a = Act("Generate a Word report about selected tender");
Check(a.Export is { Format: "docx", ReportType: "tender_summary", TenderId: 1 }, "intent: selected tender resolved from the page");
a = Act("Generate an Excel report comparing supplier participation over the past three months");
Check(a.Export is { Format: "xlsx", ReportType: "supplier_participation", TenderId: null } && a.Export.From != null, "intent: participation with relative period, no tender");
a = Act("Generate an Excel report of invoices");
Check(a.Refusal != null && a.Export == null, "intent: unsupported subject refused, not replaced by another report");
a = Act("Generate a Word comparison of supplier offers", noTenderPage);
Check(a.Export is { Format: "docx", ReportType: "supplier_offers", TenderId: null }, "intent: cross-tender offers without a tender");
a = Act("Generate a PDF of the tender lifecycle", noTenderPage);
Check(a.Question != null && a.Pending is { Report: "tender_lifecycle" }, "intent: tender-specific report without a tender asks which tender");
Check(Act("Tender 3", noTenderPage, a.Pending).Export is { ReportType: "tender_lifecycle", TenderId: 3 }, "intent: the named tender completes the lifecycle request");
a = Act("Compare Alpha and Bravo in a Word report");
Check(a.Export is { Format: "docx", ReportType: "supplier_comparison" } && a.Export.SupplierIds!.Order().SequenceEqual([1, 2]), "intent: two named suppliers compared");
a = Act("أنشئ ملف إكسل يوضح جميع الموردين والمناقصات", noTenderPage);
Check(a.Export is { Format: "xlsx", ReportType: "supplier_participation" }, "intent: Arabic Excel request for all suppliers and tenders");
a = Act("Generate a PDF for supplier Zulu", noTenderPage);
Check(a.Refusal != null && a.Export == null, "intent: an unknown supplier is reported, never substituted");
Check(RadarIntents.Parse("Who competes most often?", snapshot) == null, "intent: an analytical question goes to the analyst");
var nav = Act("Show me the suppliers market");
Check(nav.Navigation is { Destination: "supplier_market", SupplierId: 0 } && !nav.Intent.AlsoAnswer, "navigation: suppliers market opens the Supplier Universe (not a supplier record)");
Check(Act("Show me the supplier categories").Navigation is { Destination: "categories" }, "navigation: categories");
Check(Act("Show Delta's competitors").Navigation is { Destination: "competition", SupplierId: 4 }, "navigation: a named supplier's competitors");
Check(Act("Show me supplier Alpha").Navigation is { Destination: "supplier", SupplierId: 1 }, "navigation: authorized supplier selected");
Check(Act("Show the lifecycle of Tender 2").Navigation is { Destination: "tender_lifecycle", TenderId: 2 }, "navigation: tender lifecycle");
Check(Act("Show the submission timing analysis").Navigation is { Destination: "submission_timing" }, "navigation: submission timing");
Check(Act("Show me tender #999 lifecycle").Refusal != null, "navigation: unavailable tender is rejected");
Check(Act("Show Alpha's historical prices and explain them").Intent.AlsoAnswer, "navigation: a view plus a question also gets an analysed answer");
Check(RadarIntents.Sanitize(new RadarIntent("report", "exe", "raw_sql", "evidence")) is { Format: null, Report: null, Destination: null }, "intent: page-supplied intent is re-validated");
Check(RadarIntentRouter.Merge(new RadarIntent("unclear", "xlsx"), new RadarIntent("report", null, "suppliers")) is { Kind: "report", Format: "xlsx", Report: "suppliers" }, "router: model fills only what the rules left open");
rejected = false; try { RadarAnalystService.ValidateArguments("navigate_to_section", JsonDocument.Parse("""{"destination":"evidence"}""").RootElement); } catch (RadarToolException) { rejected = true; }
Check(rejected, "AI navigation: removed destination is rejected");

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
        "AI Excel: different report request produces different data and structure");
    var supplierOffersFile = await files.BuildAsync(new RadarExportRequest("xlsx", "supplier_offers", null, null, null, []), CancellationToken.None);
    var allOfferXml = XDocument.Parse(ZipEntry(supplierOffersFile.Bytes, "xl/worksheets/sheet1.xml"));
    Check(allOfferXml.Descendants(ox + "row").Skip(1).Count() == actual.Bids.Count,
        "cross-tender offers Excel: every authorized effective closed-round bid is present");
    var wordFile = await files.BuildAsync(new RadarExportRequest("docx", "tender_summary", null, null, actualTender.TenderId, []), CancellationToken.None);
    var wordXml = ZipEntry(wordFile.Bytes, "word/document.xml");
    Check(wordFile.Bytes.AsSpan(0, 2).SequenceEqual("PK"u8) && XDocument.Parse(wordXml).Descendants().Any(e => e.Name.LocalName == "tbl")
        && wordXml.Contains(actualTender.Title) && wordXml.Contains(actualRound.Bids[0].Supplier), "AI Word: valid editable DOCX with selected tender and real offer table");
    var lifecycleFile = await files.BuildAsync(new RadarExportRequest("xlsx", "tender_lifecycle", null, null, actualTender.TenderId, []), CancellationToken.None);
    var lifeXml = ZipEntry(lifecycleFile.Bytes, "xl/worksheets/sheet1.xml");
    Check(lifeXml.Contains(actualTender.TenderId.ToString()) && lifeXml.Contains("Bidding &amp; Supplier Submissions") && lifeXml.Contains(actualRound.RoundId.ToString()),
        "manual lifecycle Excel: actual tender, round and workflow records present");
    var pdfFile = await files.BuildAsync(new RadarExportRequest("pdf", "tender_summary", null, null, actualTender.TenderId, []), CancellationToken.None);
    var pdf = RadarFileExtractor.Extract("report.pdf", new MemoryStream(pdfFile.Bytes));
    Check(pdfFile.Bytes.AsSpan(0, 4).SequenceEqual("%PDF"u8) && !pdf.Text.Contains("Supplier network", StringComparison.OrdinalIgnoreCase)
        && pdf.Text.Contains(actualTender.TenderId.ToString()), "AI PDF: readable tender data, no supplier network graph");
    rejected = false; try { await files.BuildAsync(new RadarExportRequest("xlsx", "tender_offers", null, null, int.MaxValue, []), CancellationToken.None); } catch (RadarToolException) { rejected = true; }
    Check(rejected, "export: unavailable tender does not produce a file");
    rejected = false; try { await files.BuildAsync(new RadarExportRequest("xlsx", "supplier_participation", null, null, null, [int.MaxValue]), CancellationToken.None); } catch (RadarToolException) { rejected = true; }
    Check(rejected, "export: unavailable supplier does not produce a file");
}

Console.WriteLine($"\n{passed} passed, {failures} failed");
return failures == 0 ? 0 : 1;

// 22.05 kHz Piper WAV -> 16 kHz mono 16-bit WAV (linear interpolation), as the browser pipeline produces.
static byte[] To16k(byte[] wav)
{
    var rate = BitConverter.ToInt32(wav, 24);
    var dataStart = 44; var samples = (wav.Length - dataStart) / 2;
    var input = new float[samples]; for (var i = 0; i < samples; i++) input[i] = BitConverter.ToInt16(wav, dataStart + i * 2) / 32768f;
    var outLength = (int)(samples * 16000L / rate); var output = new short[outLength];
    for (var i = 0; i < outLength; i++) { var pos = i * (double)rate / 16000; var j = (int)pos; var f = pos - j; var a = input[Math.Min(j, samples - 1)]; var b = input[Math.Min(j + 1, samples - 1)]; output[i] = (short)Math.Clamp((a + (b - a) * f) * 32767, -32768, 32767); }
    using var ms = new MemoryStream(); using var w = new BinaryWriter(ms);
    w.Write("RIFF"u8); w.Write(36 + outLength * 2); w.Write("WAVE"u8); w.Write("fmt "u8); w.Write(16); w.Write((short)1); w.Write((short)1); w.Write(16000); w.Write(32000); w.Write((short)2); w.Write((short)16);
    w.Write("data"u8); w.Write(outLength * 2); foreach (var s in output) w.Write(s);
    return ms.ToArray();
}

sealed record SttItem(string Id, string Text, string Language, string Kind);
sealed record IntentCase(string Text, string Kind, string? Format = null, string? Report = null, string? Destination = null, int Supplier = 0, int Tender = 0, int Supplier2 = 0,
    string? First = null, int SelectedTender = 0, string? Last = null);

// Transcript scoring: case, punctuation, Arabic diacritics/letter variants and Arabic-script spellings of the product names are
// normalised, so only recognition errors count.
static class Stt
{
    private static readonly Dictionary<string, string> Spellings = new()
    {
        ["إكسل"] = "excel", ["اكسل"] = "excel", ["اكسيل"] = "excel", ["بي دي اف"] = "pdf", ["بيدياف"] = "pdf", ["وورد"] = "word", ["ورد"] = "word", ["شيت"] = "sheet"
    };
    public static string Normalize(string s)
    {
        s = s.ToLowerInvariant();
        s = System.Text.RegularExpressions.Regex.Replace(s, "[ً-ْٰـ]", "");
        s = s.Replace('أ', 'ا').Replace('إ', 'ا').Replace('آ', 'ا').Replace('ى', 'ي').Replace('ة', 'ه').Replace('ؤ', 'و').Replace('ئ', 'ي');
        foreach (var (k, v) in Spellings) s = s.Replace(k.Replace('إ', 'ا').Replace('ى', 'ي'), v);
        s = new string(s.Select(c => c is >= '٠' and <= '٩' ? (char)('0' + (c - '٠')) : char.IsLetterOrDigit(c) ? c : ' ').ToArray());
        return System.Text.RegularExpressions.Regex.Replace(s, @"\s+", " ").Trim();
    }
    public static string[] Words(string s) => Normalize(s).Split(' ', StringSplitOptions.RemoveEmptyEntries);
    public static string[] Chars(string s) => Normalize(s).Replace(" ", "").Select(c => c.ToString()).ToArray();
    public static double ErrorRate(string[] expected, string[] heard)
    {
        if (expected.Length == 0) return heard.Length == 0 ? 0 : 1;
        var d = new int[heard.Length + 1];
        for (var j = 0; j <= heard.Length; j++) d[j] = j;
        for (var i = 1; i <= expected.Length; i++)
        {
            var prev = d[0]; d[0] = i;
            for (var j = 1; j <= heard.Length; j++) { var tmp = d[j]; d[j] = Math.Min(Math.Min(d[j] + 1, d[j - 1] + 1), prev + (expected[i - 1] == heard[j - 1] ? 0 : 1)); prev = tmp; }
        }
        return Math.Min(1, d[heard.Length] / (double)expected.Length);
    }
    // Words that decide what the assistant does.
    private static readonly string[] Terms = ["excel", "pdf", "word", "sheet", "supplier", "suppliers", "tender", "tenders", "market", "categories", "competitors", "timing", "lifecycle", "offers", "price",
        "الموردين", "المناقصات", "العروض", "توقيت", "سوق", "تقرير", "المورد", "المالية"];
    private static readonly HashSet<string> TermSet = Terms.Select(Normalize).ToHashSet();
    public static double TermRecall(IEnumerable<(string Expected, string Heard)> pairs)
    {
        int found = 0, total = 0;
        foreach (var (e, h) in pairs)
        {
            var heard = Words(h).ToHashSet();
            foreach (var w in Words(e).Where(TermSet.Contains)) { total++; if (heard.Contains(w)) found++; }
        }
        return total == 0 ? 1 : found / (double)total;
    }
}

sealed class StubEnvironment(string root) : Microsoft.AspNetCore.Hosting.IWebHostEnvironment
{
    public string WebRootPath { get; set; } = Path.Combine(root, "wwwroot");
    public Microsoft.Extensions.FileProviders.IFileProvider WebRootFileProvider { get; set; } = new Microsoft.Extensions.FileProviders.NullFileProvider();
    public string ApplicationName { get; set; } = "Orange-Tendering-System";
    public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = new Microsoft.Extensions.FileProviders.NullFileProvider();
    public string ContentRootPath { get; set; } = root;
    public string EnvironmentName { get; set; } = "Development";
}
