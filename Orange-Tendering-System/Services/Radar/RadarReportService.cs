using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using MigraDoc.DocumentObjectModel;
using MigraDoc.DocumentObjectModel.Tables;
using MigraDoc.Rendering;
using Orange_Tendering_System.Services.Reports;
using MColor = MigraDoc.DocumentObjectModel.Color;

namespace Orange_Tendering_System.Services.Radar;

public sealed record RadarReportImage(string Title, string DataUrl);
public sealed record RadarReportTurn(string Role, string Text, bool? Verified);
public sealed record RadarReportRequest(string? From, string? To, int[]? SupplierIds, string[]? FindingIds, RadarReportTurn[]? Transcript, RadarReportImage[]? Images, bool AiInterpretation = true);

// ORANGE RADAR intelligence report (PDF). Separate from official evaluation reports, committee scoring and final-decision documents:
// it is never stored as a TenderReport and carries a banner saying so. Every figure comes from RadarAnalytics / RadarForensics;
// the AI interpretation section keeps only sentences whose figures pass the grounding guard.
public sealed class RadarReportService(RadarService radar, RadarOllamaClient ollama, OrangeReportRenderer fonts, IOptions<RadarOptions> options, ILogger<RadarReportService> logger)
{
    private static readonly MColor Orange = new(255, 121, 0), Ink = new(24, 28, 33), Muted = new(98, 106, 117), Line = new(222, 226, 232), Navy = new(16, 24, 40);

    public async Task<byte[]> BuildAsync(RadarReportRequest request, string officer, CancellationToken ct)
    {
        _ = fonts; // constructing the shared renderer registers the report font resolver used below
        var period = RadarAnalystService.ParsePeriod(request.From, request.To);
        var w = await radar.WorkspaceAsync(period, ct);
        var s = w.Summary;
        var selected = (request.SupplierIds ?? []).Where(id => w.Suppliers.Any(x => x.SupplierId == id)).Distinct().Take(6).ToList();
        var chosen = (request.FindingIds ?? []).Select(id => w.Findings.FirstOrDefault(f => f.Id == id)).Where(f => f != null).Select(f => f!).ToList();
        var findings = chosen.Concat(w.Findings.Where(f => f.Level == "ReviewSignal")).Concat(w.Findings).DistinctBy(f => f.Id).Take(12).ToList();

        var doc = new Document();
        doc.Info.Title = "ORANGE RADAR - Supplier market intelligence report";
        doc.Info.Author = "Orange Sourcing & Tender Management System";
        var normal = doc.Styles[StyleNames.Normal]!; normal.Font.Name = "Orange Sans"; normal.Font.Size = 9.5; normal.Font.Color = Ink;
        var section = doc.AddSection();
        section.PageSetup.PageFormat = PageFormat.A4;
        section.PageSetup.LeftMargin = section.PageSetup.RightMargin = "1.7cm"; section.PageSetup.TopMargin = "1.5cm"; section.PageSetup.BottomMargin = "1.8cm";
        var footer = section.Footers.Primary.AddParagraph();
        footer.AddText("ORANGE RADAR intelligence report - not an official tender evaluation, scoring or award document.   Page ");
        footer.AddPageField(); footer.AddText(" / "); footer.AddNumPagesField();
        footer.Format.Font.Size = 7.5; footer.Format.Font.Color = Muted;

        // Cover band
        var band = section.AddTable(); band.AddColumn("17.6cm");
        var cell = band.AddRow().Cells[0]; cell.Shading.Color = Navy; cell.Format.LeftIndent = "0.5cm"; cell.Format.RightIndent = "0.5cm";
        var k = cell.AddParagraph("ORANGE RADAR"); k.Format.Font.Size = 24; k.Format.Font.Bold = true; k.Format.Font.Color = Orange; k.Format.SpaceBefore = "12pt";
        var sub = cell.AddParagraph("AI-Powered Supplier Market Intelligence - See the market. Speak to your data."); sub.Format.Font.Size = 10.5; sub.Format.Font.Color = Colors.White;
        var meta = cell.AddParagraph($"Scope: suppliers and procurement activity recorded in this system  |  Period: {s.Period}  |  Generated {JordanTime.Display(DateTime.UtcNow)} (Jordan time) by {officer}");
        meta.Format.Font.Size = 8; meta.Format.Font.Color = new MColor(200, 206, 214); meta.Format.SpaceBefore = "6pt"; meta.Format.SpaceAfter = "12pt";
        Note(section, "Intelligence report for review purposes. It is kept separate from official evaluation reports, committee scores and final decisions, and it changes no procurement record. Detected patterns are review signals, not evidence of wrongdoing.");

        Heading(section, "1. Market overview");
        Table(section, ["Metric", "Value", "Definition"], [
            ["Registered suppliers", s.RegisteredSuppliers.ToString(), w.Definitions["registeredSuppliers"]],
            ["Eligible suppliers", s.EligibleSuppliers.ToString(), w.Definitions["eligibleSuppliers"]],
            ["Participating suppliers", s.ParticipatingSuppliers.ToString(), w.Definitions["participatingSuppliers"]],
            ["Analysed tenders / rounds", $"{s.AnalyzedTenders} / {s.AnalyzedRounds}", w.Definitions["analyzedTenders"]],
            ["Submitted offers", s.SubmittedBids.ToString(), w.Definitions["submittedBids"]],
            ["Average offers per round", Num(s.AvgBidsPerRound), w.Definitions["avgBidsPerRound"]],
            ["Historical awarded value", s.AwardedValue.Count == 0 ? "No official awards" : string.Join("; ", s.AwardedValue.Select(v => $"{v.Currency} {v.Value:N2} ({v.Count})")), w.Definitions["awardedValue"]],
            ["Sealed (excluded)", $"{s.SealedRounds} open rounds, {s.SealedBids} offers", "Offers in rounds whose bidding is still open are never analysed."]
        ], [4.2, 3.6, 9.8]);
        Images(section, request.Images, 0, 2);

        Heading(section, "2. Competition");
        var c = w.Competition;
        Body(section, $"Average offers per closed round: {Num(c.AvgBidsPerRound)}; per tender: {Num(c.AvgBidsPerTender)}. Rounds below the minimum of {c.MinimumBids} offers: {c.LowCompetitionRounds} of {c.Rounds.Count}. Departments are used as tender groups because tenders carry no category of their own.");
        if (c.Rounds.Count > 0)
            Table(section, ["Tender / round", "Department", "Invited", "Submitted", "Compliant"], c.Rounds.Take(14).Select(r => new[] { $"{r.Tender} · R{r.RoundNumber}", r.Department, r.Invited.ToString(), r.Submitted.ToString(), r.Compliant.ToString() }).ToArray(), [7, 4, 2.2, 2.2, 2.2]);
        foreach (var a in w.Awards)
            Table(section, [$"Supplier ({a.Currency})", "Awarded value", "Share", "Awards"], a.BySupplier.Select(x => new[] { x.Name, $"{x.Value:N2}", x.Share.ToString("0.0%", CultureInfo.InvariantCulture), x.Count.ToString() }).ToArray(), [7.6, 4, 3, 3]);

        Heading(section, "3. Supplier comparison");
        var compare = (selected.Count > 0 ? w.Suppliers.Where(x => selected.Contains(x.SupplierId)) : w.Suppliers.OrderByDescending(x => x.Participations).Take(5)).ToList();
        Body(section, selected.Count > 0 ? "Suppliers selected during the analysis." : "No supplier was selected; the five most active suppliers are shown.");
        Table(section, ["Supplier", "Invited", "Offers in rounds", "Awards / awarded bid", "Rel. price", "Compliance"],
            compare.Select(x => new[] { x.Name, x.Invitations.ToString(), x.Participations.ToString(), $"{x.Wins} / {x.AwardedTendersBid}", x.RelativePrice is { } r ? r.ToString("+0.0%;-0.0%", CultureInfo.InvariantCulture) : "n/a", x.ComplianceChecked == 0 ? "n/a" : $"{x.Compliant}/{x.ComplianceChecked}" }).ToArray(),
            [5.6, 2, 2.6, 3, 2.2, 2.2]);
        Images(section, request.Images, 2, 4);

        Heading(section, "4. Relationships and review signals");
        if (findings.Count == 0) Body(section, "No relationships or review signals were detected at the configured thresholds.");
        foreach (var f in findings)
        {
            var p = section.AddParagraph(); p.Format.SpaceBefore = "6pt"; p.Format.KeepWithNext = true;
            var title = p.AddFormattedText($"{f.Id}  {f.Title}", TextFormat.Bold); title.Color = f.Level == "ReviewSignal" ? Orange : Ink;
            p.AddFormattedText($"   {(f.Level == "ReviewSignal" ? "Review signal" : "Context")} · sample {f.SampleSize} · {f.Period}", TextFormat.NotBold).Color = Muted;
            Body(section, f.Explanation);
            Body(section, "Metrics: " + string.Join("; ", f.Metrics.Select(m => $"{m.Key} {m.Value}")));
            Body(section, "Method: " + f.Method);
            Body(section, "Alternative explanation / limitation: " + f.Limitations, italic: true);
            Body(section, "Evidence records: " + string.Join("; ", f.Records.Take(8).Select(r => $"{r.Type} #{r.Id} ({r.Label})")) + (f.Records.Count > 8 ? $"; +{f.Records.Count - 8} more" : ""), small: true);
        }
        Heading(section, "Detector coverage");
        Table(section, ["Detector", "State", "Findings", "Threshold / note"], w.Detectors.Select(d => new[] { d.Label, d.State, d.Findings.ToString(), $"{d.Threshold}. {d.Note}" }).ToArray(), [4.6, 2.2, 1.8, 9]);

        Heading(section, "5. AI interpretation");
        foreach (var line in await InterpretationAsync(w, findings, request.AiInterpretation, ct)) Body(section, line);

        var turns = (request.Transcript ?? []).Where(t => !string.IsNullOrWhiteSpace(t.Text)).TakeLast(16).ToList();
        if (turns.Count > 0)
        {
            Heading(section, "6. Conversation record");
            foreach (var t in turns)
            {
                var text = RadarGrounding.IsArabic(t.Text) ? "[Arabic turn - the PDF font engine cannot shape Arabic; see the Orange Radar transcript]" : Clip(t.Text, 600);
                var p = section.AddParagraph(); p.Format.SpaceBefore = "3pt";
                p.AddFormattedText(t.Role == "user" ? "Officer: " : "Orange Radar: ", TextFormat.Bold);
                p.AddText(text + (t.Role == "assistant" && t.Verified == false ? "  [contains a figure that could not be verified]" : ""));
            }
        }

        Heading(section, "7. Data limitations");
        foreach (var l in new[]
        {
            "The market means the suppliers and procurement activity recorded in this system, not the external supplier market.",
            $"Offers in rounds still open for bidding are sealed and excluded ({s.SealedBids} offers in {s.SealedRounds} rounds).",
            "Prices are compared only within the same round, tender item, quantity, unit and currency; totals include extra costs.",
            "Awarded value is the official award amount, not payments. Currencies are never added together.",
            $"Document DNA: {w.DocumentDna.State} ({w.DocumentDna.Readable} readable of {w.DocumentDna.BidDocuments} bid documents). Only TXT, CSV and DOCX text is extracted; PDF and images are matched by SHA-256 only.",
            "Thresholds are conservative demonstration settings (OrangeRadar:Thresholds) and small samples limit every statistic."
        }) Body(section, "- " + l);

        var renderer = new PdfDocumentRenderer { Document = doc };
        renderer.RenderDocument();
        using var ms = new MemoryStream(); renderer.PdfDocument.Save(ms, false);
        return ms.ToArray();
    }

    private async Task<List<string>> InterpretationAsync(RadarWorkspace w, List<RadarFinding> findings, bool useAi, CancellationToken ct)
    {
        const string fallback = "AI interpretation was not generated; all figures in this report come from C# calculations over recorded data.";
        var model = string.IsNullOrWhiteSpace(options.Value.Models.DeepModel) ? options.Value.Models.ChatModel : options.Value.Models.DeepModel!;
        if (!useAi || !await ollama.HasModelAsync(model, ct)) return [fallback];
        var facts = System.Text.Json.JsonSerializer.Serialize(new
        {
            w.Summary, competition = new { w.Competition.AvgBidsPerRound, w.Competition.LowCompetitionRounds, w.Competition.MinimumBids },
            findings = findings.Take(6).Select(f => new { f.Id, f.Title, f.Level, f.Explanation, f.Limitations, f.SampleSize })
        });
        var prompt = "Write a 4-6 sentence neutral interpretation of this procurement market analysis for a sourcing officer. Use only the figures given; do not calculate new numbers. Describe signals as review signals, never proof, and mention one alternative explanation. Plain English, no markdown.\nData: " + facts;
        var text = new StringBuilder();
        try
        {
            await foreach (var chunk in ollama.StreamChatAsync(model, [new { role = "user", content = prompt }], null, 0.2, ct, maxTokens: 700)) text.Append(chunk.Content);
        }
        catch (RadarAiUnavailableException e) { logger.LogInformation("Report interpretation skipped: {Message}", e.Message); return [fallback]; }
        var guard = new RadarGrounding(); guard.AddFacts(facts);
        var (sentences, rest) = RadarGrounding.SplitSentences(text.ToString().Trim() + " ");
        if (rest.Trim().Length > 0) sentences.Add(rest.Trim());
        var kept = sentences.Where(x => guard.Unverified(x).Count == 0).ToList();
        var dropped = sentences.Count - kept.Count;
        kept.Add($"(Generated locally by {model}; {dropped} sentence(s) removed because they contained figures that could not be verified.)");
        return kept;
    }

    private static void Heading(Section s, string text)
    {
        var p = s.AddParagraph(text); p.Format.Font.Size = 13; p.Format.Font.Bold = true; p.Format.SpaceBefore = "14pt"; p.Format.SpaceAfter = "5pt"; p.Format.KeepWithNext = true;
        p.Format.Borders.Bottom.Color = Orange; p.Format.Borders.Bottom.Width = 1;
    }
    private static void Body(Section s, string text, bool italic = false, bool small = false)
    {
        var p = s.AddParagraph(Clip(text, 1500)); p.Format.SpaceAfter = "2pt"; p.Format.Font.Italic = italic;
        if (small) { p.Format.Font.Size = 8; p.Format.Font.Color = Muted; }
    }
    private static void Note(Section s, string text)
    {
        var p = s.AddParagraph(text); p.Format.SpaceBefore = "8pt"; p.Format.Font.Size = 8.5; p.Format.Font.Italic = true; p.Format.Font.Color = Muted;
    }
    private static void Table(Section s, string[] head, string[][] rows, double[] widths)
    {
        var t = s.AddTable(); t.Borders.Color = Line; t.Borders.Width = 0.5; t.Format.Font.Size = 8.5;
        foreach (var w in widths) t.AddColumn(Unit.FromCentimeter(w));
        var h = t.AddRow(); h.HeadingFormat = true; h.Shading.Color = Orange; h.Format.Font.Bold = true; h.Format.Font.Color = Colors.White;
        for (var i = 0; i < head.Length; i++) h.Cells[i].AddParagraph(head[i]);
        foreach (var r in rows) { var row = t.AddRow(); for (var i = 0; i < head.Length && i < r.Length; i++) row.Cells[i].AddParagraph(Clip(r[i], 400)); }
        s.AddParagraph().Format.SpaceAfter = "4pt";
    }
    private static void Images(Section s, RadarReportImage[]? images, int from, int to)
    {
        foreach (var img in (images ?? []).Skip(from).Take(to - from))
        {
            var match = Regex.Match(img.DataUrl ?? "", @"^data:image/(png|jpeg);base64,([A-Za-z0-9+/=]+)$");
            if (!match.Success || match.Groups[2].Value.Length > 4_000_000) continue;
            var caption = s.AddParagraph(Clip(img.Title, 120)); caption.Format.Font.Bold = true; caption.Format.SpaceBefore = "6pt"; caption.Format.KeepWithNext = true;
            var image = s.AddImage("base64:" + match.Groups[2].Value);
            image.LockAspectRatio = true; image.Width = "17cm";
        }
    }
    private static string Num(double? v) => v?.ToString("0.##", CultureInfo.InvariantCulture) ?? "n/a";
    private static string Clip(string? t, int max) => t == null ? "" : t.Length <= max ? t : t[..max] + "…";
}
