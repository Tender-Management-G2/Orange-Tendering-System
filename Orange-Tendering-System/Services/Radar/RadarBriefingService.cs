using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace Orange_Tendering_System.Services.Radar;

public sealed record BriefingFact(string Label, string Value);
public sealed record BriefingScene(int Index, string Id, string Title, string Mode, IReadOnlyList<int> SupplierIds, IReadOnlyList<string> NodeIds,
    IReadOnlyList<string> EdgeIds, string? FindingId, string Focus, IReadOnlyList<BriefingFact> Facts, string Narration, string NarrationSource);
public sealed record Briefing(string Language, string Period, DateTime GeneratedAt, IReadOnlyList<BriefingScene> Scenes, string Notice);

// "Brief Me": a guided visual story built from verified C# results. Scene structure, references and facts are deterministic;
// the local model may only re-word a scene's narration, and a re-worded scene is kept only if every figure in it is verified.
public sealed class RadarBriefingService(RadarService radar, RadarOllamaClient ollama, IOptions<RadarOptions> options, ILogger<RadarBriefingService> logger)
{
    public async Task<Briefing> BuildAsync(RadarPeriod period, string language, bool aiNarration, CancellationToken ct)
    {
        var ar = language == "ar";
        var w = await radar.WorkspaceAsync(period, ct);
        var sum = w.Summary;
        var scenes = new List<BriefingScene>();
        string N(double? v, string format = "0.##") => v?.ToString(format, CultureInfo.InvariantCulture) ?? (ar ? "غير متاح" : "n/a");
        void Add(string id, string title, string mode, IEnumerable<int> suppliers, IEnumerable<string> nodes, IEnumerable<string> edges, string? finding, string focus, List<BriefingFact> facts, string narration)
            => scenes.Add(new BriefingScene(scenes.Count, id, title, mode, suppliers.ToList(), nodes.ToList(), edges.ToList(), finding, focus, facts, narration, "verified template"));

        // 1. Market at a glance
        var value = string.Join(", ", sum.AwardedValue.Select(v => $"{v.Currency} {v.Value:N2}"));
        Add("overview", ar ? "السوق في لمحة" : "The market at a glance", "overview", [], [], [], null, "fit",
        [
            new(ar ? "الموردون المسجلون" : "Registered suppliers", sum.RegisteredSuppliers.ToString()), new(ar ? "المؤهلون" : "Eligible", sum.EligibleSuppliers.ToString()),
            new(ar ? "شاركوا فعلياً" : "Participated", sum.ParticipatingSuppliers.ToString()), new(ar ? "جولات محللة" : "Analysed rounds", sum.AnalyzedRounds.ToString()),
            new(ar ? "عروض مقدمة" : "Submitted offers", sum.SubmittedBids.ToString())
        ],
        ar ? $"يضم النظام {sum.RegisteredSuppliers} مورداً مسجلاً، منهم {sum.EligibleSuppliers} مؤهلون، وقدّم {sum.ParticipatingSuppliers} منهم عروضاً فعلية. حللنا {sum.AnalyzedRounds} جولة مغلقة في {sum.AnalyzedTenders} عطاء بإجمالي {sum.SubmittedBids} عرضاً."
           : $"The system records {sum.RegisteredSuppliers} suppliers, {sum.EligibleSuppliers} of them eligible, and {sum.ParticipatingSuppliers} actually submitted offers. Orange Radar analysed {sum.AnalyzedRounds} closed rounds across {sum.AnalyzedTenders} tenders with {sum.SubmittedBids} offers.");

        // 2. Most active suppliers
        var active = w.Suppliers.Where(x => x.Participations > 0).OrderByDescending(x => x.Participations).ThenByDescending(x => x.Wins).Take(3).ToList();
        if (active.Count > 0)
            Add("activity", ar ? "الموردون الأكثر نشاطاً" : "Most active suppliers", "overview", active.Select(a => a.SupplierId), active.Select(a => $"s-{a.SupplierId}"), [], null, $"s-{active[0].SupplierId}",
                active.Select(a => new BriefingFact(a.Name, ar ? $"{a.Participations} جولات، {a.Wins} ترسيات" : $"{a.Participations} rounds, {a.Wins} awards")).ToList(),
                ar ? $"أكثر الموردين نشاطاً هو {active[0].Name} بمشاركة في {active[0].Participations} جولة" + (active.Count > 1 ? $"، يليه {active[1].Name} بـ {active[1].Participations} جولة." : ".")
                   : $"The most active supplier is {active[0].Name}, with offers in {active[0].Participations} rounds" + (active.Count > 1 ? $", followed by {active[1].Name} with {active[1].Participations}." : "."));

        // 3. Competition
        var comp = w.Competition;
        var lowest = comp.ByDepartment.FirstOrDefault();
        Add("competition", ar ? "مستوى المنافسة" : "Competition", "overview", [], [], [], null, "competition",
        [
            new(ar ? "متوسط العروض لكل جولة" : "Average offers per round", N(comp.AvgBidsPerRound)),
            new(ar ? $"جولات بأقل من {comp.MinimumBids} عروض" : $"Rounds below {comp.MinimumBids} offers", comp.LowCompetitionRounds.ToString()),
            new(ar ? "معدل الاستجابة للدعوات" : "Invitation response rate", sum.ResponseRate is { } rr ? rr.ToString("0%", CultureInfo.InvariantCulture) : "n/a")
        ],
        comp.Rounds.Count == 0 ? (ar ? "لا توجد جولات مغلقة كافية لتحليل المنافسة في هذه الفترة." : "There are no closed rounds in this period, so competition cannot be measured yet.")
        : ar ? $"بلغ متوسط العروض {N(comp.AvgBidsPerRound)} لكل جولة، وكانت {comp.LowCompetitionRounds} جولة دون الحد الأدنى البالغ {comp.MinimumBids} عروض." + (lowest != null ? $" أدنى منافسة كانت في {lowest.Group} بمتوسط {N(lowest.AvgBids)}." : "")
             : $"Rounds received {N(comp.AvgBidsPerRound)} offers on average, and {comp.LowCompetitionRounds} rounds fell below the minimum of {comp.MinimumBids}." + (lowest != null ? $" The lowest competition was in {lowest.Group}, at {N(lowest.AvgBids)} offers per round." : ""));

        // 4. Pricing
        var spread = w.Items.OrderByDescending(i => i.Spread).FirstOrDefault();
        if (spread != null)
            Add("pricing", ar ? "ذكاء الأسعار" : "Price intelligence", "price", spread.Prices.Select(p => p.SupplierId), spread.Prices.Select(p => $"s-{p.SupplierId}"), [], null, "price",
            [
                new(ar ? "البند" : "Item", spread.Item), new(ar ? "أدنى سعر وحدة" : "Lowest unit price", $"{spread.Currency} {spread.Min:N2}"),
                new(ar ? "أعلى سعر وحدة" : "Highest unit price", $"{spread.Currency} {spread.Max:N2}"), new(ar ? "الفارق" : "Spread", spread.Spread.ToString("0%", CultureInfo.InvariantCulture))
            ],
            ar ? $"أكبر تفاوت سعري كان في بند {spread.Item} ضمن عطاء {spread.Tender}: من {spread.Currency} {spread.Min:N2} إلى {spread.Currency} {spread.Max:N2} للوحدة، أي فارق {spread.Spread.ToString("0%", CultureInfo.InvariantCulture)}."
               : $"The widest price spread was for {spread.Item} in {spread.Tender}: unit prices ranged from {spread.Currency} {spread.Min:N2} to {spread.Currency} {spread.Max:N2}, a {spread.Spread.ToString("0%", CultureInfo.InvariantCulture)} spread.");
        else
            Add("pricing", ar ? "ذكاء الأسعار" : "Price intelligence", "price", [], [], [], null, "price", [],
                ar ? "لا تتوفر بعد بنود سعّرها موردان أو أكثر في الجولة نفسها، لذلك لا يمكن مقارنة الأسعار بعد." : "No tender item has yet been priced by two suppliers in the same round, so price comparison is not possible yet.");

        // 5. Relationships (context) and 6. review signals
        var context = w.Findings.Where(f => f.Level == "Context").OrderByDescending(f => f.SampleSize).Take(3).ToList();
        if (context.Count > 0)
        {
            var edges = w.Graph.Edges.Where(e => e.FindingIds.Any(id => context.Any(c => c.Id == id))).Select(e => e.Id).ToList();
            Add("relationships", ar ? "العلاقات المتكررة" : "Recurring relationships", "network", context.SelectMany(c => c.SupplierIds).Distinct(), context.SelectMany(c => c.SupplierIds).Distinct().Select(id => $"s-{id}"), edges, context[0].Id, "selection",
                context.Select(c => new BriefingFact(c.Title, string.Join(" · ", c.SupplierIds.Select(id => w.Suppliers.First(s => s.SupplierId == id).Name)))).ToList(),
                ar ? $"رصدنا {w.Findings.Count(f => f.Level == "Context")} علاقة سياقية، أبرزها: {context[0].Explanation} هذه علاقات واقعية وليست مؤشرات على مخالفة."
                   : $"Orange Radar found {w.Findings.Count(f => f.Level == "Context")} contextual relationships. The strongest: {context[0].Explanation} These are factual relationships, not concerns by themselves.");
        }
        var signals = w.Findings.Where(f => f.Level == "ReviewSignal").Take(3).ToList();
        if (signals.Count > 0)
            foreach (var (signal, i) in signals.Select((s, i) => (s, i)))
            {
                var edges = w.Graph.Edges.Where(e => e.FindingIds.Contains(signal.Id)).Select(e => e.Id).ToList();
                Add($"signal-{i + 1}", (ar ? "مؤشر للمراجعة: " : "Review signal: ") + signal.Title, "forensic", signal.SupplierIds, signal.SupplierIds.Select(id => $"s-{id}"), edges, signal.Id, "selection",
                    signal.Metrics.Select(m => new BriefingFact(m.Key, m.Value)).Take(4).Append(new BriefingFact(ar ? "حجم العينة" : "Sample size", signal.SampleSize.ToString())).ToList(),
                    (ar ? "مؤشر للمراجعة وليس دليلاً: " : "A review signal, not proof: ") + signal.Explanation + (ar ? " تفسير بديل محتمل: " : " A legitimate explanation is possible: ") + signal.Limitations);
            }
        else
        {
            var insufficient = w.Detectors.Where(d => d.State is "insufficient" or "notRun").Select(d => d.Label).ToList();
            Add("signals", ar ? "مؤشرات المراجعة" : "Review signals", "forensic", [], [], [], null, "fit",
                w.Detectors.Select(d => new BriefingFact(d.Label, d.State == "ok" ? $"{d.Findings}" : d.State)).ToList(),
                ar ? "لم تُرصد مؤشرات تستدعي المراجعة بالعتبات الحالية." + (insufficient.Count > 0 ? $" بعض الكواشف تفتقر إلى بيانات كافية: {insufficient.Count}." : "")
                   : "No review signals were detected at the current thresholds." + (insufficient.Count > 0 ? $" {insufficient.Count} detectors do not have enough data yet, so absence of signals is not a clean bill of health." : ""));
        }

        // 7. Follow-up
        var follow = new List<BriefingFact>();
        if (comp.LowCompetitionRounds > 0) follow.Add(new(ar ? "المنافسة" : "Competition", ar ? "توسيع قائمة الدعوات في الجولات منخفضة المنافسة" : "Widen invitation lists where rounds fell below the minimum"));
        if (signals.Count > 0) follow.Add(new(ar ? "الأدلة" : "Evidence", ar ? $"مراجعة أدلة {signals[0].Id} يدوياً قبل أي استنتاج" : $"Review the evidence of {signals[0].Id} manually before drawing conclusions"));
        if (w.DocumentDna.State != "ok") follow.Add(new(ar ? "المستندات" : "Documents", ar ? "تشغيل تحليل الحمض النووي للمستندات" : "Run Document DNA to compare bid documents semantically"));
        if (sum.Awards < options.Value.Thresholds.RotationMinAwards) follow.Add(new(ar ? "البيانات" : "Data", ar ? "أنماط الترسية تحتاج إلى ترسيات رسمية أكثر" : "Award patterns need more official awards"));
        Add("follow-up", ar ? "الخطوات المقترحة" : "Recommended follow-up", "overview", [], [], [], null, "fit", follow,
            ar ? "هذه خطوات مراجعة مقترحة فقط؛ لا يغيّر أورانج رادار أي حالة مورد أو قرار رسمي." : "These are suggested review steps only; Orange Radar never changes a supplier's status or any official decision.");

        if (aiNarration) scenes = await ReWordAsync(scenes, ar, ct);
        return new Briefing(language, sum.Period, DateTime.UtcNow, scenes,
            ar ? "قصة موجهة مبنية على بيانات محققة. المؤشرات ليست اتهامات." : "Guided story built from verified data. Signals are not accusations.");
    }

    // One JSON call re-words all narrations; each one survives only if the grounding guard verifies every figure in it.
    private async Task<List<BriefingScene>> ReWordAsync(List<BriefingScene> scenes, bool ar, CancellationToken ct)
    {
        if (!await ollama.HasModelAsync(options.Value.Models.ChatModel, ct)) return scenes;
        var payload = JsonSerializer.Serialize(scenes.Select(s => new { s.Id, s.Title, facts = s.Facts, narration = s.Narration }));
        var prompt = $"Rewrite each scene narration as a natural spoken briefing line (max 2 sentences) in {(ar ? "Arabic" : "English")}. Use ONLY the figures already present; do not add numbers, names or claims. Keep the words 'review signal' / 'not proof' where present. Return JSON {{\"scenes\":[{{\"id\":\"...\",\"narration\":\"...\"}}]}}.\nScenes: {payload}";
        var text = new StringBuilder();
        try
        {
            await foreach (var chunk in ollama.StreamChatAsync(options.Value.Models.ChatModel, [new { role = "user", content = prompt }], null, 0.3, ct, maxTokens: 1800))
                text.Append(chunk.Content);
            using var json = JsonDocument.Parse(ExtractJson(text.ToString()));
            var rewritten = json.RootElement.GetProperty("scenes").EnumerateArray()
                .Where(x => x.TryGetProperty("id", out _) && x.TryGetProperty("narration", out _))
                .ToDictionary(x => x.GetProperty("id").GetString() ?? "", x => x.GetProperty("narration").GetString() ?? "");
            return scenes.Select(s =>
            {
                if (!rewritten.TryGetValue(s.Id, out var line) || line.Length is < 10 or > 500) return s;
                var guard = new RadarGrounding(); guard.AddFacts(s.Narration); foreach (var f in s.Facts) guard.AddFacts(f.Value);
                return guard.Unverified(line).Count == 0 ? s with { Narration = line, NarrationSource = "AI-worded, figures verified" } : s;
            }).ToList();
        }
        catch (Exception e) when (e is RadarAiUnavailableException or JsonException or KeyNotFoundException or InvalidOperationException)
        {
            logger.LogInformation("Briefing narration kept as verified templates: {Message}", e.Message);
            return scenes;
        }
    }

    private static string ExtractJson(string text)
    {
        var start = text.IndexOf('{'); var end = text.LastIndexOf('}');
        return start >= 0 && end > start ? text[start..(end + 1)] : "{}";
    }
}
