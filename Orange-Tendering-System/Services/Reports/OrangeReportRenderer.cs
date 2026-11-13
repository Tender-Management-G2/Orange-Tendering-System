using System.Globalization;
using Microsoft.Extensions.Options;
using MigraDoc.DocumentObjectModel;
using MigraDoc.DocumentObjectModel.Tables;
using MigraDoc.Rendering;
using PdfSharp.Drawing;
using PdfSharp.Drawing.Layout;
using PdfSharp.Fonts;
using PdfSharp.Pdf.IO;
using Orange_Tendering_System.Models.Entities;
using MColor = MigraDoc.DocumentObjectModel.Color;

namespace Orange_Tendering_System.Services.Reports;

public class OrangeReportRenderer
{
    public const string TemplateVersion = "Orange-report-1";
    private static readonly object FontLock = new();
    private static readonly XColor Orange = XColor.FromArgb(255, 121, 0);
    private static readonly string[] Slots = ["Evaluation:SourcingManager", "Evaluation:DepartmentRepresentative", "Evaluation:FinancialRepresentative", "Evaluation:TechnicalRepresentative", "Manager:Manager", "Top:1", "Top:2", "Top:3", "Top:4"];
    public OrangeReportRenderer(IOptions<ReportPdfOptions> options)
    {
        lock (FontLock) GlobalFontSettings.FontResolver ??= new ReportFontResolver(options.Value.FontDirectory);
    }
    public RenderedReport Render(FrozenReport frozen)
    {
        var data = frozen.Inputs;
        var document = new MigraDoc.DocumentObjectModel.Document();
        document.Info.Title = $"Orange Tender {data.TenderId} - Evaluation Report v{data.Version}";
        document.Styles[StyleNames.Normal]!.Font.Name = "Orange Sans";
        document.Styles[StyleNames.Normal]!.Font.Size = 9;
        document.Styles[StyleNames.Normal]!.ParagraphFormat.SpaceAfter = 5;
        var section = document.AddSection();
        section.PageSetup.PageFormat = PageFormat.A4;
        section.PageSetup.LeftMargin = Unit.FromPoint(48); section.PageSetup.RightMargin = Unit.FromPoint(48);
        section.PageSetup.TopMargin = Unit.FromPoint(66); section.PageSetup.BottomMargin = Unit.FromPoint(52);
        var brand = section.Headers.Primary.AddParagraph("orange   |   Sourcing & Tender Management");
        brand.Format.Font.Color = MColor.FromRgb(210, 89, 0); brand.Format.Font.Bold = true; brand.Format.Font.Size = 11;
        Heading(section, "TENDER EVALUATION REPORT", 19);
        Heading(section, data.Title, 14);
        Body(section, $"Tender #{data.TenderId}  |  Purchase Request #{data.PurchaseRequestId}  |  Report #{data.ReportId}  |  Version {data.Version}");
        Body(section, $"Generated from verified SQL Server records - {DisplayUtc(data.CreatedUtc)}");
        Body(section, data.Description);
        Heading(section, "1. Requirements and approved evaluation criteria");
        Table(section, ["Approved criteria", "Value"], [
            ["Financial weight", Number(data.FinancialWeight) + "%"], ["Technical weight", Number(data.TechnicalWeight) + "%"],
            ["Total", "100%"], ["Requirements / weight version", data.WeightVersion.ToString()],
            ["Approved by", data.WeightApprover], ["Approved UTC", DisplayUtc(data.WeightApprovedUtc)]]);
        foreach (var line in ReadableJson(data.Requirements)) Body(section, line);
        Table(section, ["Product / service", "Quantity", "Specifications"], data.Items);
        Heading(section, "2. Supplier comparison and compliance");
        Table(section, ["Bid / supplier", "Total offer", "Delivery", "Compliance"], data.Bids.Select(b => new[] { $"#{b.Id} - {b.Supplier}", Money(b.Total) + " " + b.Currency, b.DeliveryDays + " days", b.Compliance }).ToArray());
        foreach (var bid in data.Bids)
        {
            Heading(section, $"Bid #{bid.Id}: {bid.Supplier}", 11);
            Body(section, "Technical offer: " + bid.Technical);
            Body(section, "Payment / commercial terms: " + bid.Payment);
            Body(section, $"Bid revision {bid.Revision} | Submitted: {DisplayUtc(bid.SubmittedUtc)} | Valid until: {DisplayUtc(bid.ValidUntilUtc)}");
            Table(section, ["Additional cost", "Amount (" + bid.Currency + ")"], bid.AdditionalCosts);
            Body(section, $"Compliance: {bid.Compliance}. {bid.Notes} Missing requirements: {bid.Missing}");
            Table(section, ["Item", "Quantity", "Unit price", "Offered specification"], bid.Items);
            Body(section, "Supporting documents: " + (bid.Documents.Length == 0 ? "None recorded" : string.Join("; ", bid.Documents)));
        }
        Heading(section, "3. Four-member independent evaluation");
        Body(section, "Member score = (Financial score x Approved financial weight + Technical score x Approved technical weight) / 100. Supplier final score = the sum of all four member scores / 4. Official calculations retain full decimal precision; display rounding does not determine ranking.");
        foreach (var member in data.Committee)
        {
            Heading(section, SeatLabel(member.Seat) + " - " + member.Name, 11);
            Table(section, ["Bid", "Financial / 100", "Technical / 100", "Weighted result"], data.Scores.Where(x => x.MemberId == member.MemberId).Select(x => new[] { "#" + x.BidId, Number(x.Financial), Number(x.Technical), Number(x.Weighted) }).ToArray());
            foreach (var score in data.Scores.Where(x => x.MemberId == member.MemberId)) Body(section, $"Bid #{score.BidId} - observations: {score.Observations}");
        }
        Heading(section, "4. Exact ranking and recommendation");
        Table(section, ["Rank", "Bid / supplier", "Final weighted score", "Outcome"], data.Ranking.Select(x => new[] { x.Rank.ToString(), $"#{x.BidId} - {data.Bids.Single(b => b.Id == x.BidId).Supplier}", Number(x.Score), x.Tied ? "Exact score tie" : x.Rank == 1 ? "Recommended Winner" : "Ranked" }).ToArray());
        if (data.Ranking.Count(x => x.Rank == 1) != 1) Body(section, "AWARD BLOCKED: exact highest-score tie requires an authorized resolution policy.");
        Body(section, "Recommendation justification / permitted revision: " + data.Explanation);
        Body(section, "A recommendation is not an award. Required approvals must be completed separately.");
        Heading(section, "5. Supplementary AI assistance");
        Body(section, "Model: " + data.AiModel + ". Human-reviewed supplementary assistance. AI does not set weights, enter scores, exclude bids or select an official winner.");
        foreach (var line in ReadableJson(data.AiFindings)) Body(section, line);
        Heading(section, "AI source references and extraction limitations", 10);
        foreach (var line in ReadableJson(data.AiEvidence)) Body(section, line);
        Heading(section, "6. Version and approval history at generation");
        Table(section, ["Version / stage", "Decision / feedback", "User / UTC"], frozen.ApprovalHistory);
        Body(section, "Later endorsements appear in the predefined signature ledger on protected viewing copies. They bind this immutable canonical version. Electronic acknowledgments are not certificate-backed PDF signatures.");
        var renderer = new PdfDocumentRenderer { Document = document };
        renderer.RenderDocument();
        var pdf = renderer.PdfDocument;
        var signaturesPage = pdf.AddPage(); signaturesPage.Size = PdfSharp.PageSize.A4;
        var pageIndex = pdf.PageCount - 1;
        using (var graphics = XGraphics.FromPdfPage(signaturesPage))
        {
            graphics.DrawString("orange", new XFont("Orange Sans", 23, XFontStyleEx.Bold), new XSolidBrush(Orange), 48, 46);
            graphics.DrawString("7. Signatures and approval ledger", new XFont("Orange Sans", 15, XFontStyleEx.Bold), XBrushes.Black, 48, 82);
            graphics.DrawString($"Report #{data.ReportId} - Version {data.Version}", new XFont("Orange Sans", 9), XBrushes.Black, 48, 101);
            for (var i = 0; i < Slots.Length; i++)
            {
                var y = 122 + i * 52;
                graphics.DrawRectangle(new XPen(XColors.LightGray), 48, y, 499, 52);
                graphics.DrawLine(new XPen(XColors.LightGray), 214, y, 214, y + 52);
                var label = Slots[i];
                if (i < 4)
                {
                    var seat = data.Committee.Single(x => "Evaluation:" + x.Seat == Slots[i]);
                    var name = Clip(seat.Name, 40);
                    if (graphics.MeasureString(name, new XFont("Orange Sans", 8)).Width > 152) name = SafeText(name, 18);
                    label = SeatLabel(seat.Seat) + "\n" + name;
                }
                if (i == 4) label = "Sourcing Manager\nManagement review";
                if (i > 4) label = "Top Committee\nAssigned seat " + (i - 4);
                new XTextFormatter(graphics).DrawString(label, new XFont("Orange Sans", 8), XBrushes.Black, new XRect(54, y + 7, 152, 40));
            }
            new XTextFormatter(graphics).DrawString("Signatures are recorded only after explicit authenticated review. An electronic acknowledgment is a database-backed approval record. Where a trusted personal certificate is configured, a detached CMS signature signs the version-and-document-hash manifest; this is not an embedded PAdES signature. Viewing watermarks never modify the canonical signed bytes.", new XFont("Orange Sans", 8), XBrushes.DimGray, new XRect(48, 610, 499, 85));
        }
        for (var i = 0; i < pdf.PageCount; i++)
        {
            using var g = XGraphics.FromPdfPage(pdf.Pages[i]);
            g.DrawLine(new XPen(Orange, 1.2), 48, 783, 547, 783);
            g.DrawString($"Orange Sourcing - Confidential | Report {data.ReportId} v{data.Version}", new XFont("Orange Sans", 8), XBrushes.DimGray, 48, 798);
            g.DrawString($"{i + 1} / {pdf.PageCount}", new XFont("Orange Sans", 8), XBrushes.DimGray, 505, 798);
        }
        using var output = new MemoryStream(); pdf.Save(output, false);
        return new(output.ToArray(), pageIndex);
    }
    public byte[] AccessCopy(TenderReport report, IReadOnlyList<ReportSignature> signatures, string identity, DateTime accessedAt, bool? topRequired)
    {
        using var input = new MemoryStream(report.PdfContent!);
        using var pdf = PdfReader.Open(input, PdfDocumentOpenMode.Modify);
        var stamp = $"CONFIDENTIAL - {identity} - {accessedAt:yyyy-MM-dd HH:mm:ss} UTC";
        pdf.Info.Subject = stamp;
        foreach (var page in pdf.Pages)
        {
            using var g = XGraphics.FromPdfPage(page);
            // Reserved side margin and footer band: never overprint report data or signature boxes.
            g.Save(); g.TranslateTransform(15, 740); g.RotateTransform(-90);
            g.DrawString(stamp, new XFont("Orange Sans", 6), new XSolidBrush(XColor.FromArgb(150, 150, 150)), 0, 0); g.Restore();
            new XTextFormatter(g).DrawString(stamp, new XFont("Orange Sans", 6), XBrushes.DimGray, new XRect(48, 810, 499, 25));
        }
        using (var g = XGraphics.FromPdfPage(pdf.Pages[report.SignaturePageIndex!.Value]))
        {
            for (var i = 0; i < Slots.Length; i++)
            {
                var signature = signatures.SingleOrDefault(x => x.Stage + ":" + x.Slot == Slots[i]);
                var waiting = i > 4 && topRequired != true ? topRequired == false ? "Not required by the approved final-approval policy." : "Conditional seat: awaiting an explicit final-approval policy." : "Awaiting assigned user's explicit review and signing.";
                var text = signature == null ? waiting :
                    $"{Clip(signature.SignerName, 35)} ({Clip(signature.UserId, 25)})\n{signature.Decision} - {signature.SignedAt:yyyy-MM-dd HH:mm:ss} UTC\n{signature.Method} | Receipt #{signature.Id}" + (signature.InvalidatedAt != null ? " | OBSOLETE VERSION" : "");
                new XTextFormatter(g).DrawString(text, new XFont("Orange Sans", 7), XBrushes.Black, new XRect(220, 129 + i * 52, 321, 42));
            }
            new XTextFormatter(g).DrawString("Personalized viewing copy. Canonical SHA-256: " + report.PdfSha256, new XFont("Orange Sans", 7), XBrushes.DimGray, new XRect(48, 711, 499, 40));
        }
        using var output = new MemoryStream(); pdf.Save(output, false); return output.ToArray();
    }
    private static void Heading(Section section, string text, int size = 13)
    {
        var p = section.AddParagraph(SafeText(text, size >= 18 ? 24 : size >= 14 ? 32 : 40)); p.Format.Font.Size = size; p.Format.Font.Bold = true;
        p.Format.Font.Color = MColor.FromRgb(166, 70, 0); p.Format.SpaceBefore = 12; p.Format.SpaceAfter = 7; p.Format.KeepWithNext = true;
    }
    private static void Table(Section section, string[] headings, string[][] rows)
    {
        if (rows.Length == 0) { section.AddParagraph("No records for this section."); return; }
        var table = section.AddTable(); table.Borders.Width = .4; table.Borders.Color = MColor.FromRgb(210, 215, 220);
        table.Format.Font.Size = 8; table.Format.SpaceAfter = 3; table.TopPadding = 5; table.BottomPadding = 5;
        foreach (var _ in headings) table.AddColumn(Unit.FromPoint(499d / headings.Length));
        var header = table.AddRow(); header.HeadingFormat = true; header.Shading.Color = MColor.FromRgb(255, 232, 211); header.Format.Font.Bold = true;
        for (var i = 0; i < headings.Length; i++) header.Cells[i].AddParagraph(headings[i]);
        foreach (var values in rows)
        {
            // MigraDoc repeats headers, but cannot split one oversized table row. Chunk long
            // cells into automatically generated continuation rows so no evidence is clipped.
            var cells = Enumerable.Range(0, headings.Length).Select(i =>
                (values.ElementAtOrDefault(i) ?? "").Replace('\r', ' ').Replace('\n', ' ').EnumerateRunes().Chunk(160)
                    .Select(chunk => string.Concat(chunk.Select(rune => rune.ToString()))).DefaultIfEmpty("").ToArray()).ToArray();
            for (var part = 0; part < cells.Max(x => x.Length); part++)
            {
                var row = table.AddRow();
                for (var i = 0; i < headings.Length; i++)
                {
                    var text = cells[i].ElementAtOrDefault(part) ?? (i == 0 && part > 0 ? "(continued)" : "");
                    text = System.Text.RegularExpressions.Regex.Replace(text, @"\S{15,}", match => string.Join("\n", match.Value.EnumerateRunes().Chunk(14).Select(chunk => string.Concat(chunk.Select(rune => rune.ToString())))));
                    row.Cells[i].AddParagraph(text);
                }
            }
        }
        section.AddParagraph().Format.SpaceAfter = 2;
    }
    private static string Number(decimal number) => number.ToString("0.##############", CultureInfo.InvariantCulture);
    private static string Money(decimal number) => number.ToString("N2", CultureInfo.InvariantCulture);
    private static string DisplayUtc(string value) => DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var date) ? date.ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss 'UTC'", CultureInfo.InvariantCulture) : value;
    private static string Clip(string value, int length) => value.EnumerateRunes().Count() <= length ? value : string.Concat(value.EnumerateRunes().Take(length).Select(rune => rune.ToString())) + "...";
    private static string SeatLabel(string seat) => seat switch { "SourcingManager" => "Sourcing Manager", "DepartmentRepresentative" => "Department Representative", "FinancialRepresentative" => "Financial Representative", "TechnicalRepresentative" => "Technical Representative", _ => seat };
    private static string SafeText(string value, int width) => System.Text.RegularExpressions.Regex.Replace(value, @"\S{" + (width + 1) + @",}", match => string.Join("\n", match.Value.EnumerateRunes().Chunk(width).Select(chunk => string.Concat(chunk.Select(rune => rune.ToString())))));
    private static Paragraph Body(Section section, string text) => section.AddParagraph(SafeText(text, 48));
    private static IEnumerable<string> ReadableJson(string json)
    {
        var lines = new List<string>();
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            void Walk(System.Text.Json.JsonElement e, string name, int depth)
            {
                if (depth > 12) { lines.Add(name + ": [nested evidence available in the source record]"); return; }
                if (e.ValueKind == System.Text.Json.JsonValueKind.Object) foreach (var p in e.EnumerateObject()) Walk(p.Value, p.Name, depth + 1);
                else if (e.ValueKind == System.Text.Json.JsonValueKind.Array) foreach (var v in e.EnumerateArray()) Walk(v, name, depth + 1);
                else
                {
                    var value = e.ValueKind == System.Text.Json.JsonValueKind.String ? e.GetString() ?? "" : e.ToString();
                    if (value.StartsWith('{') || value.StartsWith('[')) { try { using var nested = System.Text.Json.JsonDocument.Parse(value); Walk(nested.RootElement, name, depth + 1); return; } catch (System.Text.Json.JsonException) { } }
                    lines.Add((name.Length == 0 ? "" : name + ": ") + value);
                }
            }
            Walk(doc.RootElement, "", 0);
        }
        catch (System.Text.Json.JsonException) { lines.Add(json); }
        return lines;
    }
    private sealed class ReportFontResolver(string? directory) : IFontResolver
    {
        private readonly string root = directory ?? Environment.GetFolderPath(Environment.SpecialFolder.Fonts);
        public FontResolverInfo ResolveTypeface(string familyName, bool bold, bool italic) => new(bold ? "OrangeBold" : "OrangeRegular", false, italic);
        public byte[] GetFont(string faceName)
        {
            var files = faceName == "OrangeBold" ? new[] { "arialbd.ttf", "DejaVuSans-Bold.ttf" } : new[] { "arial.ttf", "DejaVuSans.ttf" };
            var path = files.Select(x => Path.Combine(root, x)).FirstOrDefault(File.Exists);
            if (path == null) throw new WorkflowException("Configure ReportPdf:FontDirectory with Arial or DejaVu Sans regular and bold fonts.");
            return File.ReadAllBytes(path);
        }
    }
}
