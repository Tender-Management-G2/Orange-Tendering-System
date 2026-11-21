// ORANGE RADAR checks. Pure analytics run on an in-memory synthetic market (no database).
// Optional: --speech runs a real local Piper -> Whisper round trip (Arabic + English) with the configured model paths.
using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Orange_Tendering_System.Models.Entities;
using Orange_Tendering_System.Services.Radar;

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
var docs = new List<DocFingerprint>
{
    new(1, "proposal.txt", "Bid", 3, 102, 2, 2, "aaaa", "ok", t0), new(2, "proposal-final.txt", "Bid", 4, 103, 2, 2, "aaaa", "ok", t0),
    new(3, "requirements.txt", "Tender", null, null, 3, null, "bbbb", "ok", t0), new(4, "copy-of-requirements.txt", "Bid", 1, 104, 3, 3, "bbbb", "ok", t0),
    new(5, "copy-of-requirements.txt", "Bid", 2, 105, 3, 3, "bbbb", "ok", t0)
};

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
var dna = RadarAnalytics.Dna(stats[6], 5);
Check(dna.Dimensions.First(d => d.Key == "price").Value == null && dna.Dimensions.First(d => d.Key == "compliance").Value == null, "Supplier DNA keeps missing data as null (never imputed)");
var positions = RadarAnalytics.RelativePositions(snapshot);
Check(positions.All(p => p.Comparable == 5) && positions.Count == 25, "relative prices only within a round and currency");
var competition = RadarAnalytics.CompetitionAnalysis(snapshot, 3);
Check(competition.LowCompetitionRounds == 0 && competition.Rounds.All(r => r.Invited == 6 && r.Submitted == 5), "competition separates invited from submitted");
var sim = RadarAnalytics.Simulate(snapshot, [2, 3], 3);
Check(sim.RoundsAffected == 5 && sim.RoundsBelowMinimumAfter == 0 && sim.AwardExposure.Single().Count == 4, "dependency simulation: coverage and award exposure");
Check(sim.HhiAfter > sim.HhiBefore && sim.Notice.Contains("not a forecast"), "simulation reports concentration and is labelled hypothetical");
var simAll = RadarAnalytics.Simulate(snapshot, [1, 2, 3, 4], 3);
Check(simAll.RoundsBelowMinimumAfter == 5 && simAll.RoundsWithoutBidders == 0, "simulation detects rounds falling below the minimum");

// ---------- forensic detectors ----------
var forensic = RadarForensics.Detect(snapshot, thresholds, docs, null);
RadarFinding? Find(string detector, params int[] ids) => forensic.Findings.FirstOrDefault(f => f.Detector == detector && ids.All(f.SupplierIds.Contains));
var phone = forensic.Findings.FirstOrDefault(f => f.Detector == RadarForensics.SharedContact && f.Title.Contains("phone"));
Check(phone != null && phone.SupplierIds.SequenceEqual([3, 4]) && phone.Level == "ReviewSignal", "shared phone found after normalization (+962 / 00962 formats)");
var address = forensic.Findings.FirstOrDefault(f => f.Detector == RadarForensics.SharedContact && f.Title.Contains("address"));
Check(address != null && address.Level == "Context", "shared address is context, not a signal");
Check(!forensic.Findings.Any(f => f.Title.Contains("domain") && f.SupplierIds.Contains(5)), "public mail domains (gmail) are never a shared-contact finding");
Check(forensic.Findings.Where(f => f.Detector == RadarForensics.JointParticipation).All(f => f.Level == "Context"), "joint participation is always context");
var gap = Find(RadarForensics.PriceGap, 3, 4);
Check(gap != null && gap.SampleSize == 5 && gap.Metrics["higher"] == "Delta", "repeated relative price gap 3-4 detected with sample size");
Check(Find(RadarForensics.PriceGap, 1, 2) == null, "no price-gap signal when the direction changes");
var similar = Find(RadarForensics.PriceSimilarity, 1, 5);
Check(similar != null && similar.Metrics["matchingLines"] == "10", "near-identical unit prices 1-5 detected on every comparable line");
var rotation = Find(RadarForensics.AwardRotation, 2, 3);
Check(rotation != null && rotation.Metrics["winnerChanges"] == "3", "award rotation inside the group 2-3 detected");
var timing = Find(RadarForensics.SubmissionTiming, 3, 4);
Check(timing != null && Find(RadarForensics.SubmissionTiming, 1, 2) == null, "submission-time proximity only for the 3-minute pair");
var identical = forensic.Findings.Where(f => f.Detector == RadarForensics.IdenticalDocument).ToList();
Check(identical.Count == 1 && identical[0].SupplierIds.SequenceEqual([3, 4]), "identical files found; files equal to a tender template are excluded");
Check(forensic.Findings.All(f => f.Records.Count > 0 && f.Method.Length > 0 && f.Limitations.Length > 0 && f.Id.StartsWith("RF-")), "every finding has evidence records, method and limitations");
Check(RadarForensics.Detect(snapshot, thresholds, docs, null).Findings.Select(f => f.Id).SequenceEqual(forensic.Findings.Select(f => f.Id)), "finding ids are stable across runs");
Check(forensic.Detectors.Single(d => d.Detector == RadarForensics.SemanticDocument).State == "notRun", "semantic detector reports 'not run' instead of an empty result");

// Insufficient data: a market with a single round and no awards.
var tiny = new RadarSnapshot { GeneratedAt = t0, Period = RadarPeriod.All, Suppliers = suppliers, Tenders = tenders.Take(1).Select(t => t with { AwardedBidId = null }).ToList(),
    Rounds = rounds.Take(1).ToList(), Invitations = invitations.Where(i => i.RoundId == 1).ToList(), Bids = bids.Where(b => b.RoundId == 1).ToList(), Documents = [] };
var tinyResult = RadarForensics.Detect(tiny, thresholds, [], null);
Check(tinyResult.Detectors.Single(d => d.Detector == RadarForensics.AwardRotation).State == "insufficient", "award rotation reports insufficient data with < 3 awards");
Check(tinyResult.Detectors.Single(d => d.Detector == RadarForensics.IdenticalDocument).State == "insufficient", "document detector reports insufficient data without files");
Check(!tinyResult.Findings.Any(f => f.Level == "ReviewSignal" && f.Detector != RadarForensics.SharedContact), "no statistical signal is produced from a single round");
var emptyMarket = new RadarSnapshot { GeneratedAt = t0, Period = RadarPeriod.All, Suppliers = [], Tenders = [], Rounds = [], Invitations = [], Bids = [], Documents = [] };
Check(RadarAnalytics.Summary(emptyMarket).AvgBidsPerRound == null && RadarGraph.Build(emptyMarket, []).Nodes.Count == 0, "empty market gives empty states, not invented data");

// ---------- graph ----------
var graph = RadarGraph.Build(snapshot, forensic.Findings);
Check(graph.Edges.Select(e => e.Id).Distinct().Count() == graph.Edges.Count, "graph edge ids are unique (no duplicated relationships)");
Check(graph.Edges.Where(e => e.Source.StartsWith("s-") && e.Target.StartsWith("s-")).All(e => string.CompareOrdinal(e.Source, e.Target) < 0), "undirected supplier edges are normalized");
Check(graph.Edges.Where(e => e.Kind == "signal").All(e => e.FindingIds.Count > 0), "every signal edge carries its supporting finding ids");

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

sealed class StubEnvironment(string root) : Microsoft.AspNetCore.Hosting.IWebHostEnvironment
{
    public string WebRootPath { get; set; } = Path.Combine(root, "wwwroot");
    public Microsoft.Extensions.FileProviders.IFileProvider WebRootFileProvider { get; set; } = new Microsoft.Extensions.FileProviders.NullFileProvider();
    public string ApplicationName { get; set; } = "Orange-Tendering-System";
    public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = new Microsoft.Extensions.FileProviders.NullFileProvider();
    public string ContentRootPath { get; set; } = root;
    public string EnvironmentName { get; set; } = "Development";
}
