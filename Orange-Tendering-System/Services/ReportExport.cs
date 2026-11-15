using System.IO.Compression;
using System.Security;
using System.Text;
using MigraDoc.DocumentObjectModel;
using Orange_Tendering_System.Services.Reports;
using MigraDoc.DocumentObjectModel.Tables;
using MigraDoc.Rendering;

namespace Orange_Tendering_System.Services;

// Excel (.xlsx) and PDF exports of a ReportModel. The xlsx is written directly as Open XML (no extra package); the PDF uses the
// PDFsharp/MigraDoc already used by the signed evaluation reports (its font resolver is initialised by OrangeReportRenderer).
public sealed class ReportExport(OrangeReportRenderer fontsInitialiser)
{
    private readonly OrangeReportRenderer _ = fontsInitialiser;

    private static string Esc(string? s) => SecurityElement.Escape(s ?? "") ?? "";
    private static string Col(int i) { var s = ""; for (i++; i > 0; i = (i - 1) / 26) s = (char)('A' + (i - 1) % 26) + s; return s; }
    private static string SheetName(string title, HashSet<string> used)
    {
        var name = new string(title.Where(c => !"[]:*?/\\".Contains(c)).ToArray()); if (name.Length > 28) name = name[..28]; if (name.Length == 0) name = "Sheet";
        var unique = name; var n = 2; while (!used.Add(unique.ToLowerInvariant())) unique = name[..Math.Min(name.Length, 26)] + " " + n++;
        return unique;
    }
    private static string Cell(int row, int col, string? text, bool header = false)
    {
        var style = header ? " s=\"1\"" : "";
        if (!header && decimal.TryParse(text?.Replace(",", ""), System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var number) && text!.Length < 15 && !text.Contains('-'))
            return $"<c r=\"{Col(col)}{row}\"{style}><v>{number.ToString(System.Globalization.CultureInfo.InvariantCulture)}</v></c>";
        return $"<c r=\"{Col(col)}{row}\" t=\"inlineStr\"{style}><is><t xml:space=\"preserve\">{Esc(text)}</t></is></c>";
    }

    public byte[] ToXlsx(ReportModel model)
    {
        var sheets = new List<(string Name, string Xml)>();
        var used = new HashSet<string>();
        var summary = new StringBuilder("<?xml version=\"1.0\" encoding=\"UTF-8\"?><worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><cols><col min=\"1\" max=\"1\" width=\"46\" customWidth=\"1\"/><col min=\"2\" max=\"2\" width=\"28\" customWidth=\"1\"/></cols><sheetData>");
        var r = 1;
        summary.Append($"<row r=\"{r}\">{Cell(r, 0, model.Title, true)}</row>"); r++;
        summary.Append($"<row r=\"{r}\">{Cell(r, 0, "Generated (UTC)")}{Cell(r, 1, model.GeneratedAt.ToString("yyyy-MM-dd HH:mm"))}</row>"); r += 2;
        foreach (var k in model.Kpis) { summary.Append($"<row r=\"{r}\">{Cell(r, 0, k.Label)}{Cell(r, 1, k.Value)}</row>"); r++; }
        summary.Append("</sheetData></worksheet>");
        sheets.Add((SheetName("Summary", used), summary.ToString()));
        foreach (var t in model.Tables)
        {
            var sb = new StringBuilder("<?xml version=\"1.0\" encoding=\"UTF-8\"?><worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><sheetData>");
            sb.Append($"<row r=\"1\">{string.Concat(t.Columns.Select((c, i) => Cell(1, i, c, true)))}</row>");
            for (var i = 0; i < t.Rows.Count; i++) sb.Append($"<row r=\"{i + 2}\">{string.Concat(t.Rows[i].Select((c, j) => Cell(i + 2, j, c)))}</row>");
            sb.Append("</sheetData></worksheet>");
            sheets.Add((SheetName(t.Title, used), sb.ToString()));
        }
        using var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, true))
        {
            void Add(string path, string content) { var e = zip.CreateEntry(path, CompressionLevel.Optimal); using var w = new StreamWriter(e.Open(), new UTF8Encoding(false)); w.Write(content); }
            Add("[Content_Types].xml", "<?xml version=\"1.0\" encoding=\"UTF-8\"?><Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"><Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/><Default Extension=\"xml\" ContentType=\"application/xml\"/><Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/><Override PartName=\"/xl/styles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml\"/>"
                + string.Concat(sheets.Select((_, i) => $"<Override PartName=\"/xl/worksheets/sheet{i + 1}.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>")) + "</Types>");
            Add("_rels/.rels", "<?xml version=\"1.0\" encoding=\"UTF-8\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"xl/workbook.xml\"/></Relationships>");
            Add("xl/workbook.xml", "<?xml version=\"1.0\" encoding=\"UTF-8\"?><workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"><sheets>"
                + string.Concat(sheets.Select((s, i) => $"<sheet name=\"{Esc(s.Name)}\" sheetId=\"{i + 1}\" r:id=\"rId{i + 1}\"/>")) + "</sheets></workbook>");
            Add("xl/_rels/workbook.xml.rels", "<?xml version=\"1.0\" encoding=\"UTF-8\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">"
                + string.Concat(sheets.Select((_, i) => $"<Relationship Id=\"rId{i + 1}\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet{i + 1}.xml\"/>"))
                + $"<Relationship Id=\"rId{sheets.Count + 1}\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles\" Target=\"styles.xml\"/></Relationships>");
            Add("xl/styles.xml", "<?xml version=\"1.0\" encoding=\"UTF-8\"?><styleSheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><fonts count=\"2\"><font><sz val=\"11\"/><name val=\"Calibri\"/></font><font><b/><sz val=\"11\"/><color rgb=\"FFFFFFFF\"/><name val=\"Calibri\"/></font></fonts><fills count=\"3\"><fill><patternFill patternType=\"none\"/></fill><fill><patternFill patternType=\"gray125\"/></fill><fill><patternFill patternType=\"solid\"><fgColor rgb=\"FFFF7900\"/></patternFill></fill></fills><borders count=\"1\"><border/></borders><cellStyleXfs count=\"1\"><xf/></cellStyleXfs><cellXfs count=\"2\"><xf/><xf fontId=\"1\" fillId=\"2\" applyFont=\"1\" applyFill=\"1\"/></cellXfs></styleSheet>");
            for (var i = 0; i < sheets.Count; i++) Add($"xl/worksheets/sheet{i + 1}.xml", sheets[i].Xml);
        }
        return stream.ToArray();
    }

    public byte[] ToPdf(ReportModel model)
    {
        var doc = new Document();
        doc.Info.Title = model.Title;
        doc.Styles[StyleNames.Normal]!.Font.Name = "Orange Sans"; doc.Styles[StyleNames.Normal]!.Font.Size = 9;
        var section = doc.AddSection();
        section.PageSetup.Orientation = Orientation.Landscape; section.PageSetup.PageFormat = PageFormat.A4;
        section.PageSetup.LeftMargin = section.PageSetup.RightMargin = "1.4cm"; section.PageSetup.TopMargin = "1.4cm"; section.PageSetup.BottomMargin = "1.4cm";
        var orange = new Color(255, 121, 0);
        var title = section.AddParagraph(model.Title); title.Format.Font.Size = 20; title.Format.Font.Bold = true; title.Format.Font.Color = orange;
        section.AddParagraph($"Sourcing & Tender Management System · generated {model.GeneratedAt:yyyy-MM-dd HH:mm} UTC from live system records").Format.Font.Color = Colors.Gray;
        section.AddParagraph().Format.SpaceAfter = "4pt";
        if (model.Kpis.Count > 0)
        {
            var kt = section.AddTable(); kt.Borders.Color = new Color(220, 220, 220); kt.Borders.Width = 0.5; kt.AddColumn("9cm"); kt.AddColumn("5cm");
            foreach (var k in model.Kpis) { var row = kt.AddRow(); row.Cells[0].AddParagraph(k.Label); var p = row.Cells[1].AddParagraph(k.Value); p.Format.Font.Bold = true; row.Cells[0].Format.Font.Color = Colors.DarkGray; }
        }
        foreach (var t in model.Tables)
        {
            var h = section.AddParagraph(t.Title); h.Format.Font.Size = 12; h.Format.Font.Bold = true; h.Format.SpaceBefore = "14pt"; h.Format.SpaceAfter = "4pt"; h.Format.KeepWithNext = true;
            if (t.Rows.Count == 0) { section.AddParagraph("No records.").Format.Font.Color = Colors.Gray; continue; }
            var table = section.AddTable(); table.Borders.Color = new Color(220, 220, 220); table.Borders.Width = 0.5;
            var width = 26.9 / t.Columns.Length;
            foreach (var _ in t.Columns) table.AddColumn($"{width.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)}cm");
            var head = table.AddRow(); head.HeadingFormat = true; head.Shading.Color = orange; head.Format.Font.Color = Colors.White; head.Format.Font.Bold = true;
            for (var i = 0; i < t.Columns.Length; i++) head.Cells[i].AddParagraph(t.Columns[i]);
            foreach (var cells in t.Rows) { var row = table.AddRow(); for (var i = 0; i < cells.Length; i++) row.Cells[i].AddParagraph(cells[i] ?? ""); }
        }
        var renderer = new PdfDocumentRenderer { Document = doc };
        renderer.RenderDocument();
        using var ms = new MemoryStream(); renderer.PdfDocument.Save(ms, false); return ms.ToArray();
    }
}
