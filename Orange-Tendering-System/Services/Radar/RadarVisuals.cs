using System.Globalization;

namespace Orange_Tendering_System.Services.Radar;

// Generative UI for the AI conversation. The model only chooses a dataset, a metric, ids and a chart kind from fixed lists;
// every value is computed here from the same snapshot the page shows, and the browser renders the result with its own
// allowlisted chart/table components. No model-written HTML, script, SQL or number ever reaches the page.
public sealed record VisualSeries(string Name, IReadOnlyList<double?> Values);
public sealed record RadarVisual(string Kind, string Title, string? Subtitle, string? Unit, IReadOnlyList<string> Labels, IReadOnlyList<VisualSeries> Series,
    IReadOnlyList<string> Columns, IReadOnlyList<IReadOnlyList<string>> Rows, string Source, string? Note, RadarVisualSpec? Spec = null);
// What was asked for, so "Excel" on a chat chart rebuilds the complete table on the server instead of uploading the page's copy.
public sealed record RadarVisualSpec(string Dataset, string Metric, int[] SupplierIds, int TenderId, string Chart);

public static class RadarVisuals
{
    public static readonly string[] Datasets = ["supplier_metric", "category_distribution", "competition_rounds", "tender_lifecycle", "tender_offers", "awards_by_supplier", "supplier_table", "findings_by_detector"];
    public static readonly string[] Metrics = ["", "participations", "wins", "winRate", "participationRate", "invitations", "relativePrice"];
    public static readonly string[] Kinds = ["auto", "bar", "line", "pie", "table"];

    // complete = true (exports) lifts the top-N limits that keep in-chat charts readable.
    public static RadarVisual Build(RadarSnapshot s, ForensicResult forensic, string dataset, string metric, int[] supplierIds, int tenderId, string kind, int minimumBids, bool complete = false)
    {
        var top = complete ? int.MaxValue : 10;
        var stats = RadarAnalytics.Suppliers(s);
        var picked = supplierIds.Where(id => s.Supplier(id) != null).Distinct().Take(8).ToHashSet();
        RadarVisual v;
        switch (dataset)
        {
            case "supplier_metric":
            {
                var m = string.IsNullOrEmpty(metric) ? "participations" : metric;
                double? Value(RadarAnalytics.SupplierStats x) => m switch
                {
                    "wins" => x.Wins, "winRate" => x.WinRate * 100, "participationRate" => x.ParticipationRate * 100, "invitations" => x.Invitations,
                    "relativePrice" => x.RelativePrice * 100, _ => x.Participations
                };
                var rows = stats.Where(x => picked.Count == 0 ? Value(x) is > 0 or < 0 : picked.Contains(x.SupplierId))
                    .OrderByDescending(x => Value(x) ?? double.MinValue).Take(top).ToList();
                var (title, unit) = m switch
                {
                    "wins" => ("Official awards per supplier", "awards"), "winRate" => ("Win rate per supplier", "%"), "participationRate" => ("Response rate per supplier", "%"),
                    "invitations" => ("Invitations per supplier", "invitations"), "relativePrice" => ("Average price vs bidding cycle median", "% vs median"), _ => ("Bidding Cycles Participated In", "rounds")
                };
                v = Chart(title, picked.Count == 0 ? "Top suppliers in the current period" : "Selected suppliers", unit, rows.Select(x => x.Name).ToList(),
                    [new VisualSeries(title, rows.Select(x => Round(Value(x))).ToList())], "RadarAnalytics.Suppliers", m == "relativePrice" ? "Below 0 = cheaper than the bidding cycle median; same currency only." : null);
                break;
            }
            case "category_distribution":
            {
                var groups = s.Suppliers.GroupBy(x => string.IsNullOrWhiteSpace(x.Category) ? "Uncategorized" : x.Category.Trim()).OrderByDescending(g => g.Count()).ToList();
                var active = stats.Where(x => x.Participations > 0).Select(x => x.SupplierId).ToHashSet();
                v = Chart("Suppliers by registered category", "Registered vs. bidding in closed rounds", "suppliers", groups.Select(g => g.Key).ToList(),
                    [new("Registered", groups.Select(g => (double?)g.Count()).ToList()), new("Submitted an offer", groups.Select(g => (double?)g.Count(x => active.Contains(x.Id))).ToList())],
                    "Suppliers.Category", "Category is a registered classification, not a relationship.");
                break;
            }
            case "competition_rounds":
            {
                var c = RadarAnalytics.CompetitionAnalysis(s, minimumBids);
                var rounds = c.Rounds.Where(r => tenderId == 0 || r.TenderId == tenderId).OrderBy(r => r.Date).TakeLast(16).ToList();
                v = Chart("Supplier Participation by Bidding Cycle", $"Minimum expected: {c.MinimumBids} offers", "offers", rounds.Select(r => $"{Short(r.Tender)} · {RadarOfferComparison.CycleLabel(r.RoundNumber)}").ToList(),
                    [new("Invited", rounds.Select(r => (double?)r.Invited).ToList()), new("Submitted", rounds.Select(r => (double?)r.Submitted).ToList())], "TenderInvitations, Bids (closed rounds)", null);
                break;
            }
            case "tender_lifecycle":
            {
                var tenders = RadarTenders.Build(s);
                if (tenderId > 0 && tenders.FirstOrDefault(t => t.TenderId == tenderId) is { } one)
                {
                    v = Table($"Lifecycle · {one.Title}", one.State, ["Stage", "Recorded at (UTC)", "Days from creation", "Source"],
                        one.Stages.Select(st => (IReadOnlyList<string>)[st.Label, st.At?.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) ?? "Not recorded",
                            st.At == null ? "—" : Math.Round((st.At.Value - one.CreatedAt).TotalDays, 1).ToString(CultureInfo.InvariantCulture), st.Source]).ToList(), "Tender lifecycle records");
                    break;
                }
                var done = tenders.Where(t => t.DaysToNotification != null).OrderBy(t => t.CreatedAt).TakeLast(12).ToList();
                v = Chart("Days from tender creation to winner notification", done.Count == 0 ? "No tender in scope has a sent winner notification yet" : $"{done.Count} completed tenders",
                    "days", done.Select(t => Short(t.Title)).ToList(), [new("Days to notification", done.Select(t => t.DaysToNotification).ToList()), new("Days to award", done.Select(t => t.DaysToAward).ToList())],
                    "Tenders.CreatedAt → FinalDecisionNotices.SuppliersNotifiedAt", "Tenders without a sent notification are in progress and excluded.");
                break;
            }
            case "tender_offers":
            {
                var t = RadarTenders.Build(s).FirstOrDefault(x => x.TenderId == tenderId) ?? throw new RadarToolException("Give the id of a tender in scope (see the Tenders tab).");
                var round = t.Rounds.LastOrDefault(r => r.Analysed && r.Bids.Count > 0) ?? throw new RadarToolException("This tender has no closed round with offers yet; sealed offers are never shown.");
                var currency = round.Bids[0].Currency;
                var bids = round.Bids.Where(b => b.Currency == currency).ToList();
                v = Chart($"Offers · {t.Title} · {RadarOfferComparison.CycleLabel(round.RoundNumber)}", $"Total offer value, {currency}", currency, bids.Select(b => b.Supplier).ToList(),
                    [new("Offer total", bids.Select(b => (double?)(double)b.Total).ToList())], "Bids (closed round)", bids.FirstOrDefault(b => b.Awarded) is { } w ? $"Official award: {w.Supplier}." : null);
                break;
            }
            case "awards_by_supplier":
            {
                var a = RadarAnalytics.AwardDistributions(s).FirstOrDefault();
                if (a == null) { v = Table("Official awards", "No official award recorded in this period", ["Supplier", "Awards", "Value"], [], "Tenders.AwardedBidId"); break; }
                var rows = a.BySupplier.OrderByDescending(x => x.Value).Take(top).ToList();
                v = Chart($"Official awarded value by supplier ({a.Currency})", $"{rows.Sum(r => r.Count)} awards", a.Currency, rows.Select(r => r.Name).ToList(),
                    [new("Awarded value", rows.Select(r => (double?)(double)r.Value).ToList())], "Tenders.AwardedBidId", "Values are never added across currencies.");
                break;
            }
            case "findings_by_detector":
            {
                var d = forensic.Detectors.ToList();
                v = Chart("Behavior patterns by detector", "Patterns for human review - not proof of wrongdoing", "patterns", d.Select(x => x.Label).ToList(),
                    [new("Review signals", d.Select(x => (double?)forensic.Findings.Count(f => f.Detector == x.Detector && f.Level == "ReviewSignal")).ToList()),
                     new("Context", d.Select(x => (double?)forensic.Findings.Count(f => f.Detector == x.Detector && f.Level == "Context")).ToList())], "RadarForensics", null);
                break;
            }
            default: // supplier_table
            {
                var rows = stats.Where(x => picked.Count == 0 ? x.Participations > 0 : picked.Contains(x.SupplierId)).OrderByDescending(x => x.Participations).Take(complete ? int.MaxValue : 12).ToList();
                v = Table("Supplier comparison", picked.Count == 0 ? "Suppliers that bid in the current period" : "Selected suppliers",
                    ["Supplier", "Category", "Invitations", "Offers", "Awards", "Win rate", "Price vs median"],
                    rows.Select(x => (IReadOnlyList<string>)[x.Name, x.Category, x.Invitations.ToString(), x.Participations.ToString(), x.Wins.ToString(), Pct(x.WinRate), Pct(x.RelativePrice, true)]).ToList(),
                    "RadarAnalytics.Suppliers");
                break;
            }
        }
        if (kind == "table" && v.Kind != "table")
            v = v with { Kind = "table", Columns = ["", .. v.Series.Select(x => x.Name)], Rows = v.Labels.Select((l, i) => (IReadOnlyList<string>)[l, .. v.Series.Select(x => x.Values[i]?.ToString("0.##", CultureInfo.InvariantCulture) ?? "—")]).ToList() };
        else if (kind is "bar" or "line" or "pie" && v.Kind != "table") v = v with { Kind = kind == "pie" && v.Series.Count > 1 ? "bar" : kind };
        return v with { Title = v.Title.Replace("RADAR DEMO ", ""), Labels = v.Labels.Select(l => l.Replace("RADAR DEMO ", "")).ToList(), Source = SourceLabel(v.Source),
            Spec = new RadarVisualSpec(dataset, metric, [.. picked], tenderId, kind) };
    }

    // The card and its export name the records in plain words, never internal class or column names.
    private static string SourceLabel(string key) => key switch
    {
        "RadarAnalytics.Suppliers" => "Authorized supplier records and closed-round offers",
        "RadarForensics" => "Recorded closed-round behaviour patterns",
        "Tenders.AwardedBidId" => "Official awards",
        "TenderInvitations, Bids (closed rounds)" => "Invitations and offers in closed rounds",
        "Bids (closed round)" => "Offers in the closed round",
        _ when key.Contains('.') => "Authorized procurement records",
        _ => key
    };

    private static RadarVisual Chart(string title, string? subtitle, string? unit, IReadOnlyList<string> labels, IReadOnlyList<VisualSeries> series, string source, string? note) =>
        new("bar", title, subtitle, unit, labels, series, [], [], source, note);
    private static RadarVisual Table(string title, string? subtitle, IReadOnlyList<string> columns, IReadOnlyList<IReadOnlyList<string>> rows, string source) =>
        new("table", title, subtitle, null, [], [], columns, rows, source, null);
    private static double? Round(double? v) => v == null ? null : Math.Round(v.Value, 2);
    private static string Pct(double? v, bool signed = false) => v == null ? "—" : (signed && v > 0 ? "+" : "") + (v.Value * 100).ToString("0.#", CultureInfo.InvariantCulture) + "%";
    private static string Short(string title) { title = title.Replace("RADAR DEMO ", ""); return title.Length <= 28 ? title : title[..27] + "…"; }
}
