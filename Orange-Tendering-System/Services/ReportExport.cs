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
    private const int BodyStyle = 0;
    private const int WordmarkStyle = 1;
    private const int SystemStyle = 2;
    private const int OfficialStyle = 3;
    private const int BannerTitleStyle = 4;
    private const int BannerSubtitleStyle = 5;
    private const int MetaLabelStyle = 6;
    private const int MetaValueStyle = 7;
    private const int SectionStyle = 8;
    private const int HeaderStyle = 9;
    private const int AlternateRowStyle = 10;
    private const int NumberStyle = 11;
    private const int PercentageStyle = 12;
    private const int DateStyle = 13;
    private const int FooterStyle = 14;

    private static string Cell(int row, int col, string? text, int style = BodyStyle, bool dateColumn = false)
    {
        var safeText = text?.Trim() ?? "";
        var reference = $"{Col(col)}{row}";
        var styleAttribute = $" s=\"{style}\"";
        if (dateColumn && DateTime.TryParse(safeText, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeLocal, out var date))
            return $"<c r=\"{reference}\"{styleAttribute}><v>{date.ToOADate().ToString(System.Globalization.CultureInfo.InvariantCulture)}</v></c>";
        if (safeText.EndsWith('%') && decimal.TryParse(safeText[..^1], System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var percentage))
            return $"<c r=\"{reference}\" s=\"{PercentageStyle}\"><v>{(percentage / 100m).ToString(System.Globalization.CultureInfo.InvariantCulture)}</v></c>";
        if (decimal.TryParse(safeText.Replace(",", ""), System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var number) && safeText.Length < 15 && !safeText.Contains('-'))
            return $"<c r=\"{reference}\" s=\"{NumberStyle}\"><v>{number.ToString(System.Globalization.CultureInfo.InvariantCulture)}</v></c>";
        return $"<c r=\"{reference}\" t=\"inlineStr\"{styleAttribute}><is><t xml:space=\"preserve\">{Esc(text)}</t></is></c>";
    }

    private static bool IsDateColumn(string column) => column.Contains("date", StringComparison.OrdinalIgnoreCase)
        || column.Contains("time", StringComparison.OrdinalIgnoreCase)
        || column.Contains("expiry", StringComparison.OrdinalIgnoreCase)
        || column.Contains("created", StringComparison.OrdinalIgnoreCase)
        || column.Contains("updated", StringComparison.OrdinalIgnoreCase);

    private static string Merge(int firstRow, int lastRow, int firstColumn, int lastColumn)
        => $"<mergeCell ref=\"{Col(firstColumn)}{firstRow}:{Col(lastColumn)}{lastRow}\"/>";

    private static string PageSetupXml() => "<pageMargins left=\"0.3\" right=\"0.3\" top=\"0.55\" bottom=\"0.55\" header=\"0.2\" footer=\"0.2\"/><pageSetup paperSize=\"9\" orientation=\"landscape\" fitToWidth=\"1\" fitToHeight=\"0\"/><printOptions horizontalCentered=\"0\" verticalCentered=\"0\"/>";

    private static void AppendBrandHeader(StringBuilder sheet, List<string> merges, ReportModel model, ref int row, int lastColumn)
    {
        sheet.Append($"<row r=\"{row}\" ht=\"27\" customHeight=\"1\">{Cell(row, 0, "orange", WordmarkStyle)}{Cell(row, 1, "Sourcing & Tender Management System", SystemStyle)}</row>");
        merges.Add(Merge(row, row, 1, lastColumn)); row++;
        sheet.Append($"<row r=\"{row}\" ht=\"4\" customHeight=\"1\"><c r=\"A{row}\" s=\"{SectionStyle}\"/></row>");
        merges.Add(Merge(row, row, 0, lastColumn)); row++;
        sheet.Append($"<row r=\"{row}\" ht=\"18\" customHeight=\"1\">{Cell(row, 0, "OFFICIAL REPORT", OfficialStyle)}</row>");
        merges.Add(Merge(row, row, 0, lastColumn)); row++;
        sheet.Append($"<row r=\"{row}\" ht=\"27\" customHeight=\"1\">{Cell(row, 0, model.Title.ToUpperInvariant(), BannerTitleStyle)}</row>");
        merges.Add(Merge(row, row, 0, lastColumn)); row++;
        sheet.Append($"<row r=\"{row}\" ht=\"22\" customHeight=\"1\">{Cell(row, 0, model.Description, BannerSubtitleStyle)}</row>");
        merges.Add(Merge(row, row, 0, lastColumn)); row += 2;
        sheet.Append($"<row r=\"{row}\">{Cell(row, 0, "System", MetaLabelStyle)}{Cell(row, 1, "Sourcing & Tender Management System", MetaValueStyle)}{Cell(row, 3, "Report", MetaLabelStyle)}{Cell(row, 4, model.Key, MetaValueStyle)}</row>"); row++;
        sheet.Append($"<row r=\"{row}\">{Cell(row, 0, "Generated", MetaLabelStyle)}{Cell(row, 1, model.GeneratedAt.ToJordan("yyyy-MM-dd HH:mm") + " Jordan time", MetaValueStyle)}{Cell(row, 3, "Reporting period", MetaLabelStyle)}{Cell(row, 4, ReportingPeriod(model.Filters), MetaValueStyle)}</row>"); row += 2;
    }

    private static string ReportingPeriod(ReportFilters filters)
    {
        var from = filters.From?.ToString("yyyy-MM-dd") ?? "All time";
        var to = filters.To?.ToString("yyyy-MM-dd") ?? "Present";
        return $"{from} to {to}";
    }

    public byte[] ToXlsx(ReportModel model)
    {
        var sheets = new List<(string Name, string Xml)>();
        var used = new HashSet<string>();
        var summary = new StringBuilder("<?xml version=\"1.0\" encoding=\"UTF-8\"?><worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><sheetViews><sheetView workbookViewId=\"0\"><pane ySplit=\"10\" topLeftCell=\"A11\" activePane=\"bottomLeft\" state=\"frozen\"/></sheetView></sheetViews><cols><col min=\"1\" max=\"1\" width=\"30\" customWidth=\"1\"/><col min=\"2\" max=\"2\" width=\"30\" customWidth=\"1\"/><col min=\"3\" max=\"3\" width=\"4\" customWidth=\"1\"/><col min=\"4\" max=\"4\" width=\"18\" customWidth=\"1\"/><col min=\"5\" max=\"5\" width=\"30\" customWidth=\"1\"/></cols><sheetData>");
        var summaryMerges = new List<string>(); var r = 1;
        AppendBrandHeader(summary, summaryMerges, model, ref r, 4);
        summary.Append($"<row r=\"{r}\" ht=\"20\" customHeight=\"1\">{Cell(r, 0, "REPORT SUMMARY", SectionStyle)}</row>"); summaryMerges.Add(Merge(r, r, 0, 4)); r++;
        summary.Append($"<row r=\"{r}\">{Cell(r, 0, "Metric", HeaderStyle)}{Cell(r, 1, "Value", HeaderStyle)}</row>");
        var firstKpiRow = r; r++;
        foreach (var k in model.Kpis)
        {
            var style = (r - firstKpiRow) % 2 == 0 ? AlternateRowStyle : BodyStyle;
            summary.Append($"<row r=\"{r}\">{Cell(r, 0, k.Label, style)}{Cell(r, 1, k.Value, style)}</row>"); r++;
        }
        summary.Append($"<row r=\"{r + 1}\" ht=\"18\" customHeight=\"1\">{Cell(r + 1, 0, "Sourcing & Tender Management System · " + model.Title + " · Generated " + model.GeneratedAt.ToJordan("yyyy-MM-dd HH:mm") + " Jordan time", FooterStyle)}</row>");
        summaryMerges.Add(Merge(r + 1, r + 1, 0, 4));
        summary.Append("</sheetData>");
        if (model.Kpis.Count > 0) summary.Append($"<autoFilter ref=\"A{firstKpiRow}:B{r - 1}\"/>");
        summary.Append("<mergeCells count=\"" + summaryMerges.Count + "\">" + string.Concat(summaryMerges) + "</mergeCells>" + PageSetupXml() + "<headerFooter><oddFooter>&amp;LSourcing &amp; Tender Management System&amp;C" + Esc(model.Title) + "&amp;RPage &amp;P of &amp;N</oddFooter></headerFooter></worksheet>");
        sheets.Add((SheetName("Summary", used), summary.ToString()));
        foreach (var t in model.Tables)
        {
            var lastColumn = Math.Max(1, t.Columns.Length) - 1;
            // The branded metadata block needs five columns even when a report table has fewer fields.
            var headerLastColumn = Math.Max(4, lastColumn);
            var widths = string.Concat(Enumerable.Range(1, headerLastColumn + 1).Select(i => $"<col min=\"{i}\" max=\"{i}\" width=\"{(i == 1 ? 28 : 20)}\" customWidth=\"1\"/>"));
            var sb = new StringBuilder("<?xml version=\"1.0\" encoding=\"UTF-8\"?><worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><sheetViews><sheetView workbookViewId=\"0\"><pane ySplit=\"10\" topLeftCell=\"A11\" activePane=\"bottomLeft\" state=\"frozen\"/></sheetView></sheetViews><cols>" + widths + "</cols><sheetData>");
            var merges = new List<string>(); r = 1;
            AppendBrandHeader(sb, merges, model, ref r, headerLastColumn);
            sb.Append($"<row r=\"{r}\" ht=\"20\" customHeight=\"1\">{Cell(r, 0, t.Title.ToUpperInvariant(), SectionStyle)}</row>"); merges.Add(Merge(r, r, 0, headerLastColumn)); r++;
            var headerRow = r;
            sb.Append($"<row r=\"{r}\" ht=\"22\" customHeight=\"1\">{string.Concat(t.Columns.Select((c, i) => Cell(r, i, c, HeaderStyle)))}</row>"); r++;
            for (var i = 0; i < t.Rows.Count; i++)
            {
                var style = i % 2 == 1 ? AlternateRowStyle : BodyStyle;
                sb.Append($"<row r=\"{r}\" ht=\"20\" customHeight=\"1\">{string.Concat(t.Rows[i].Select((c, j) => Cell(r, j, c, style, j < t.Columns.Length && IsDateColumn(t.Columns[j]))))}</row>"); r++;
            }
            sb.Append($"<row r=\"{r + 1}\" ht=\"18\" customHeight=\"1\">{Cell(r + 1, 0, "Sourcing & Tender Management System · " + model.Title + " · Generated " + model.GeneratedAt.ToJordan("yyyy-MM-dd HH:mm") + " Jordan time", FooterStyle)}</row>"); merges.Add(Merge(r + 1, r + 1, 0, headerLastColumn));
            sb.Append("</sheetData>");
            if (t.Columns.Length > 0) sb.Append($"<autoFilter ref=\"A{headerRow}:{Col(lastColumn)}{Math.Max(headerRow, r - 1)}\"/>");
            sb.Append("<mergeCells count=\"" + merges.Count + "\">" + string.Concat(merges) + "</mergeCells>" + PageSetupXml() + "<headerFooter><oddFooter>&amp;LSourcing &amp; Tender Management System&amp;C" + Esc(model.Title) + "&amp;RPage &amp;P of &amp;N</oddFooter></headerFooter></worksheet>");
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
            Add("xl/styles.xml", "<?xml version=\"1.0\" encoding=\"UTF-8\"?><styleSheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><numFmts count=\"2\"><numFmt numFmtId=\"164\" formatCode=\"#,##0.00\"/><numFmt numFmtId=\"165\" formatCode=\"yyyy-mm-dd\"/></numFmts><fonts count=\"5\"><font><sz val=\"10\"/><color rgb=\"FF222222\"/><name val=\"Arial\"/></font><font><b/><sz val=\"16\"/><color rgb=\"FFFFFFFF\"/><name val=\"Arial\"/></font><font><b/><sz val=\"10\"/><color rgb=\"FFFFFFFF\"/><name val=\"Arial\"/></font><font><b/><sz val=\"11\"/><color rgb=\"FFFF7900\"/><name val=\"Arial\"/></font><font><i/><sz val=\"9\"/><color rgb=\"FF666666\"/><name val=\"Arial\"/></font></fonts><fills count=\"5\"><fill><patternFill patternType=\"none\"/></fill><fill><patternFill patternType=\"gray125\"/></fill><fill><patternFill patternType=\"solid\"><fgColor rgb=\"FFFF7900\"/></patternFill></fill><fill><patternFill patternType=\"solid\"><fgColor rgb=\"FFFF7F2A\"/></patternFill></fill><fill><patternFill patternType=\"solid\"><fgColor rgb=\"FFFFF4E8\"/></patternFill></fill></fills><borders count=\"3\"><border/><border><left style=\"thin\"><color rgb=\"FFD9D9D9\"/></left><right style=\"thin\"><color rgb=\"FFD9D9D9\"/></right><top style=\"thin\"><color rgb=\"FFD9D9D9\"/></top><bottom style=\"thin\"><color rgb=\"FFD9D9D9\"/></bottom></border><border><bottom style=\"thin\"><color rgb=\"FFFF7900\"/></bottom></border></borders><cellStyleXfs count=\"1\"><xf/></cellStyleXfs><cellXfs count=\"15\"><xf fontId=\"0\" fillId=\"0\" borderId=\"1\" applyFont=\"1\" applyBorder=\"1\" applyAlignment=\"1\"><alignment vertical=\"center\" wrapText=\"1\"/></xf><xf fontId=\"1\" fillId=\"2\" applyFont=\"1\" applyFill=\"1\" applyAlignment=\"1\"><alignment horizontal=\"center\" vertical=\"center\"/></xf><xf fontId=\"3\" borderId=\"2\" applyFont=\"1\" applyBorder=\"1\" applyAlignment=\"1\"><alignment vertical=\"center\"/></xf><xf fontId=\"2\" fillId=\"2\" applyFont=\"1\" applyFill=\"1\" applyAlignment=\"1\"><alignment horizontal=\"left\" vertical=\"center\"/></xf><xf fontId=\"1\" fillId=\"2\" applyFont=\"1\" applyFill=\"1\" applyAlignment=\"1\"><alignment horizontal=\"left\" vertical=\"center\"/></xf><xf fontId=\"2\" fillId=\"2\" applyFont=\"1\" applyFill=\"1\" applyAlignment=\"1\"><alignment horizontal=\"left\" vertical=\"center\" wrapText=\"1\"/></xf><xf fontId=\"3\" applyFont=\"1\" applyAlignment=\"1\"><alignment vertical=\"center\"/></xf><xf fontId=\"0\" applyFont=\"1\" applyAlignment=\"1\"><alignment vertical=\"center\"/></xf><xf fontId=\"3\" borderId=\"2\" applyFont=\"1\" applyBorder=\"1\" applyAlignment=\"1\"><alignment vertical=\"center\"/></xf><xf fontId=\"2\" fillId=\"2\" borderId=\"1\" applyFont=\"1\" applyFill=\"1\" applyBorder=\"1\" applyAlignment=\"1\"><alignment horizontal=\"center\" vertical=\"center\" wrapText=\"1\"/></xf><xf fontId=\"0\" fillId=\"4\" borderId=\"1\" applyFont=\"1\" applyFill=\"1\" applyBorder=\"1\" applyAlignment=\"1\"><alignment vertical=\"center\" wrapText=\"1\"/></xf><xf fontId=\"0\" borderId=\"1\" numFmtId=\"164\" applyFont=\"1\" applyBorder=\"1\" applyNumberFormat=\"1\" applyAlignment=\"1\"><alignment horizontal=\"right\" vertical=\"center\"/></xf><xf fontId=\"0\" borderId=\"1\" numFmtId=\"10\" applyFont=\"1\" applyBorder=\"1\" applyNumberFormat=\"1\" applyAlignment=\"1\"><alignment horizontal=\"right\" vertical=\"center\"/></xf><xf fontId=\"0\" borderId=\"1\" numFmtId=\"165\" applyFont=\"1\" applyBorder=\"1\" applyNumberFormat=\"1\" applyAlignment=\"1\"><alignment horizontal=\"left\" vertical=\"center\"/></xf><xf fontId=\"4\" borderId=\"2\" applyFont=\"1\" applyBorder=\"1\" applyAlignment=\"1\"><alignment vertical=\"center\"/></xf></cellXfs></styleSheet>");
            for (var i = 0; i < sheets.Count; i++) Add($"xl/worksheets/sheet{i + 1}.xml", sheets[i].Xml);
        }
        return stream.ToArray();
    }

    public byte[] ToPdf(ReportModel model)
    {
        var orange = new Color(255, 121, 0);
        var cream = new Color(255, 244, 232);
        var rule = new Color(229, 229, 229);
        var doc = new Document();
        doc.Info.Title = model.Title;
        doc.Styles[StyleNames.Normal]!.Font.Name = "Orange Sans";
        doc.Styles[StyleNames.Normal]!.Font.Size = 9;
        doc.Styles[StyleNames.Normal]!.ParagraphFormat.SpaceAfter = "4pt";

        var section = doc.AddSection();
        section.PageSetup.PageFormat = PageFormat.A4;
        section.PageSetup.Orientation = Orientation.Landscape;
        section.PageSetup.LeftMargin = Unit.FromPoint(42);
        section.PageSetup.RightMargin = Unit.FromPoint(42);
        section.PageSetup.TopMargin = Unit.FromPoint(76);
        section.PageSetup.BottomMargin = Unit.FromPoint(54);

        var header = section.Headers.Primary.AddTable();
        header.AddColumn(Unit.FromPoint(78)); header.AddColumn(Unit.FromPoint(620));
        var headerRow = header.AddRow(); headerRow.Height = Unit.FromPoint(29); headerRow.VerticalAlignment = VerticalAlignment.Center;
        headerRow.Cells[0].Shading.Color = orange; headerRow.Cells[0].Format.Alignment = ParagraphAlignment.Center;
        var logo = headerRow.Cells[0].AddParagraph("orange"); logo.Format.Font.Bold = true; logo.Format.Font.Size = 15; logo.Format.Font.Color = Colors.White;
        headerRow.Cells[1].Format.LeftIndent = Unit.FromPoint(10); headerRow.Cells[1].Borders.Bottom.Color = orange; headerRow.Cells[1].Borders.Bottom.Width = 1.2;
        var brand = headerRow.Cells[1].AddParagraph("Sourcing & Tender Management System"); brand.Format.Font.Bold = true; brand.Format.Font.Size = 10; brand.Format.Font.Color = new Color(34, 34, 34);

        var footer = section.Footers.Primary.AddTable();
        footer.AddColumn(Unit.FromPoint(590)); footer.AddColumn(Unit.FromPoint(108));
        var footerRow = footer.AddRow(); footerRow.Cells[0].Borders.Top.Color = orange; footerRow.Cells[1].Borders.Top.Color = orange; footerRow.Cells[0].Borders.Top.Width = 1;
        footerRow.Cells[1].Borders.Top.Width = 1; footerRow.Cells[0].Format.SpaceBefore = "5pt"; footerRow.Cells[1].Format.SpaceBefore = "5pt";
        var footerText = footerRow.Cells[0].AddParagraph($"Sourcing & Tender Management System · {model.Title} · {model.GeneratedAt.ToJordan("yyyy-MM-dd HH:mm")} Jordan time"); footerText.Format.Font.Size = 7; footerText.Format.Font.Color = new Color(102, 102, 102);
        footerRow.Cells[1].Format.Alignment = ParagraphAlignment.Right;
        var page = footerRow.Cells[1].AddParagraph(); page.Format.Font.Size = 7; page.Format.Font.Color = new Color(102, 102, 102); page.AddText("Page "); page.AddPageField(); page.AddText(" / "); page.AddNumPagesField();

        var banner = section.AddTable(); banner.AddColumn(Unit.FromPoint(698)); banner.TopPadding = 15; banner.BottomPadding = 15; banner.LeftPadding = 17; banner.RightPadding = 17;
        var bannerRow = banner.AddRow(); bannerRow.Shading.Color = orange;
        var official = bannerRow.Cells[0].AddParagraph("OFFICIAL REPORT"); official.Format.Font.Bold = true; official.Format.Font.Size = 9; official.Format.Font.Color = cream;
        var title = bannerRow.Cells[0].AddParagraph(model.Title.ToUpperInvariant()); title.Format.Font.Bold = true; title.Format.Font.Size = 20; title.Format.Font.Color = Colors.White; title.Format.SpaceBefore = "3pt";
        var subtitle = bannerRow.Cells[0].AddParagraph(model.Description); subtitle.Format.Font.Size = 10; subtitle.Format.Font.Color = Colors.White; subtitle.Format.SpaceBefore = "3pt";
        section.AddParagraph().Format.SpaceAfter = "2pt";

        var metadata = section.AddParagraph(); metadata.Format.Font.Size = 8; metadata.Format.Font.Color = new Color(102, 102, 102);
        metadata.AddText("System: Sourcing & Tender Management System    |    ");
        metadata.AddText($"Report: {model.Key}    |    Generated: {model.GeneratedAt.ToJordan("yyyy-MM-dd HH:mm")} Jordan time");

        if (model.Kpis.Count > 0)
        {
            AddHeading(section, "Report Summary", orange);
            AddTable(section, ["Metric", "Value"], model.Kpis.Select(k => new[] { k.Label, k.Value }).ToArray(), orange, cream, rule);
        }

        foreach (var source in model.Tables)
        {
            AddHeading(section, source.Title, orange);
            if (source.Rows.Count == 0)
            {
                var empty = section.AddParagraph("No records for the selected filters."); empty.Format.Font.Color = new Color(102, 102, 102); empty.Format.SpaceAfter = "8pt";
                continue;
            }
            AddTable(section, source.Columns, source.Rows.ToArray(), orange, cream, rule);
        }

        var renderer = new PdfDocumentRenderer { Document = doc };
        renderer.RenderDocument();
        using var stream = new MemoryStream(); renderer.PdfDocument.Save(stream, false); return stream.ToArray();
    }

    private static void AddHeading(Section section, string text, Color orange)
    {
        var heading = section.AddParagraph(text); heading.Format.Font.Size = 12; heading.Format.Font.Bold = true; heading.Format.Font.Color = orange;
        heading.Format.SpaceBefore = "13pt"; heading.Format.SpaceAfter = "3pt"; heading.Format.KeepWithNext = true;
        heading.Format.Borders.Bottom.Color = orange; heading.Format.Borders.Bottom.Width = .8;
    }

    private static void AddTable(Section section, IReadOnlyList<string> columns, IReadOnlyList<string[]> rows, Color orange, Color cream, Color rule)
    {
        if (columns.Count == 0) return;
        var table = section.AddTable(); table.Borders.Color = rule; table.Borders.Width = .45; table.TopPadding = 5; table.BottomPadding = 5; table.LeftPadding = 5; table.RightPadding = 5;
        const double usableWidth = 698;
        foreach (var _ in columns) table.AddColumn(Unit.FromPoint(usableWidth / columns.Count));
        var head = table.AddRow(); head.HeadingFormat = true; head.Shading.Color = orange; head.Format.Font.Bold = true; head.Format.Font.Color = Colors.White; head.Format.Font.Size = 8; head.VerticalAlignment = VerticalAlignment.Center;
        for (var i = 0; i < columns.Count; i++) head.Cells[i].AddParagraph(columns[i] ?? "");
        for (var rowIndex = 0; rowIndex < rows.Count; rowIndex++)
        {
            var row = table.AddRow(); row.VerticalAlignment = VerticalAlignment.Center; if (rowIndex % 2 == 1) row.Shading.Color = cream;
            var values = rows[rowIndex];
            for (var i = 0; i < columns.Count; i++)
            {
                var cell = row.Cells[i].AddParagraph(i < values.Length ? values[i] ?? "" : ""); cell.Format.Font.Size = 8;
            }
        }
    }
}
