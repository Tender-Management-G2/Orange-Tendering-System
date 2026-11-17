using System.Text.Json;
using MigraDoc.DocumentObjectModel;
using MigraDoc.DocumentObjectModel.Tables;
using MigraDoc.Rendering;
using Orange_Tendering_System.Models.Entities;

namespace Orange_Tendering_System.Services.Reports;

/// <summary>
/// The Officer-reviewed AI analysis attached to an evaluation meeting, as a PDF the committee downloads with the meeting.
/// Advisory only: it never changes scores, weights or decisions.
/// </summary>
public static class AiAnalysisPdf
{
    public static byte[] Build(AiAnalysis ai, string tenderTitle, int tenderId, EvaluationSession session)
    {
        var doc = new MigraDoc.DocumentObjectModel.Document();
        doc.Info.Title = $"AI analysis #{ai.Id} - T-{tenderId:0000}";
        doc.Styles[StyleNames.Normal]!.Font.Name = "Orange Sans"; doc.Styles[StyleNames.Normal]!.Font.Size = 10;
        var section = doc.AddSection();
        section.PageSetup.PageFormat = PageFormat.A4;
        section.PageSetup.LeftMargin = section.PageSetup.RightMargin = "1.8cm"; section.PageSetup.TopMargin = section.PageSetup.BottomMargin = "1.6cm";
        var orange = new Color(255, 121, 0); var grey = new Color(110, 110, 110);

        var kicker = section.AddParagraph("SOURCING & TENDER MANAGEMENT SYSTEM · EVALUATION MEETING ATTACHMENT");
        kicker.Format.Font.Size = 8; kicker.Format.Font.Color = orange; kicker.Format.Font.Bold = true;
        var title = section.AddParagraph($"AI analysis #{ai.Id}"); title.Format.Font.Size = 20; title.Format.Font.Bold = true; title.Format.SpaceBefore = "4pt";
        var sub = section.AddParagraph($"T-{tenderId:0000} · {tenderTitle}"); sub.Format.Font.Size = 12; sub.Format.SpaceAfter = "10pt";

        var facts = section.AddTable(); facts.Borders.Color = new Color(225, 225, 225); facts.Borders.Width = 0.5;
        facts.AddColumn("5cm"); facts.AddColumn("12.4cm");
        void Fact(string k, string v) { var r = facts.AddRow(); r.Cells[0].AddParagraph(k).Format.Font.Color = grey; r.Cells[1].AddParagraph(v).Format.Font.Bold = true; }
        Fact("Meeting", $"{JordanTime.Display(session.StartsAt)} - {session.EndsAt.ToJordan("HH:mm")}");
        Fact("Status", ai.Status + (ai.ReviewedAt.HasValue ? $" · reviewed by the Sourcing Officer on {JordanTime.Display(ai.ReviewedAt.Value)}" : " · not reviewed"));
        Fact("Model", string.IsNullOrWhiteSpace(ai.Model) ? "-" : ai.Model);
        Fact("Generated", JordanTime.Display(ai.StartedAt));

        var note = section.AddParagraph("Advisory only. The AI analysis supports the committee's reading of the offers; it does not set scores, weights or the recommendation. Each member scores every supplier independently.");
        note.Format.SpaceBefore = "10pt"; note.Format.SpaceAfter = "10pt"; note.Format.Font.Size = 9; note.Format.Font.Italic = true; note.Format.Font.Color = grey;

        var h = section.AddParagraph("Findings"); h.Format.Font.Size = 13; h.Format.Font.Bold = true; h.Format.SpaceAfter = "4pt";
        var rows = Rows(ai.Findings);
        if (rows.Count == 0) section.AddParagraph(string.IsNullOrWhiteSpace(ai.Findings) ? "No findings were recorded." : ai.Findings!);
        else
        {
            var t = section.AddTable(); t.Borders.Color = new Color(225, 225, 225); t.Borders.Width = 0.5;
            t.AddColumn("5cm"); t.AddColumn("12.4cm");
            var head = t.AddRow(); head.HeadingFormat = true; head.Shading.Color = orange; head.Format.Font.Color = Colors.White; head.Format.Font.Bold = true;
            head.Cells[0].AddParagraph("Topic"); head.Cells[1].AddParagraph("Finding");
            foreach (var (label, value) in rows) { var r = t.AddRow(); r.Cells[0].AddParagraph(label).Format.Font.Bold = true; r.Cells[1].AddParagraph(value); }
        }
        var renderer = new PdfDocumentRenderer { Document = doc };
        renderer.RenderDocument();
        using var ms = new MemoryStream(); renderer.PdfDocument.Save(ms, false); return ms.ToArray();
    }

    private static List<(string, string)> Rows(string? json)
    {
        var rows = new List<(string, string)>();
        try
        {
            using var d = JsonDocument.Parse(json ?? "");
            if (d.RootElement.ValueKind != JsonValueKind.Object) return rows;
            foreach (var p in d.RootElement.EnumerateObject()) rows.Add((Words(p.Name), Flatten(p.Value)));
        }
        catch (JsonException) { rows.Clear(); }
        return rows;
    }
    private static string Words(string s) => System.Text.RegularExpressions.Regex.Replace(s, "(?<=[a-z])(?=[A-Z])", " ");
    private static string Flatten(JsonElement e) => e.ValueKind switch
    {
        JsonValueKind.String => e.GetString() ?? "",
        JsonValueKind.Array => string.Join("\n", e.EnumerateArray().Select(x => "• " + Flatten(x))),
        JsonValueKind.Object => string.Join("\n", e.EnumerateObject().Select(p => Words(p.Name) + ": " + Flatten(p.Value))),
        _ => e.ToString()
    };
}
