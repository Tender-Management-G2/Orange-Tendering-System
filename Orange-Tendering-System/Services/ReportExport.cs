using System.Globalization;
using System.IO.Compression;
using System.Security;
using System.Text;
using System.Text.RegularExpressions;
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
    private static readonly Regex ArabicNamePhrase = new(@"[ء-ي][ء-ي\p{M} -]*");
    private static readonly IReadOnlyDictionary<char, char[]> ArabicLetterForms = BuildArabicLetterForms();

    private static string Esc(string? s)
    {
        var text = new StringBuilder();
        foreach (var rune in (s ?? "").EnumerateRunes())
        {
            if (rune.Value >= 0x10000 || System.Xml.XmlConvert.IsXmlChar((char)rune.Value)) text.Append(rune);
        }
        return SecurityElement.Escape(text.ToString()) ?? "";
    }
    private static string Col(int i) { var s = ""; for (i++; i > 0; i = (i - 1) / 26) s = (char)('A' + (i - 1) % 26) + s; return s; }
    private static string SheetName(string title, HashSet<string> used)
    {
        var name = new string(title.Where(c => !"[]:*?/\\".Contains(c)).ToArray()); if (name.Length > 28) name = name[..28]; if (name.Length == 0) name = "Sheet";
        var unique = name; var n = 2; while (!used.Add(unique.ToLowerInvariant())) unique = name[..Math.Min(name.Length, 26)] + " " + n++;
        return unique;
    }
    private static string Cell(int row, int col, string? text, bool header = false, bool numeric = true)
    {
        var style = header ? " s=\"1\"" : "";
        if (!header && DateTime.TryParseExact(text, ["yyyy-MM-dd HH:mm", "yyyy-MM-dd"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            return $"<c r=\"{Col(col)}{row}\" s=\"2\"><v>{date.ToOADate().ToString(CultureInfo.InvariantCulture)}</v></c>";
        if (numeric && !header && text is { Length: > 1 and < 20 } && text[^1] == '%' && decimal.TryParse(text[..^1], NumberStyles.Number, CultureInfo.InvariantCulture, out var percent))
            return $"<c r=\"{Col(col)}{row}\" s=\"3\"><v>{(percent / 100).ToString(CultureInfo.InvariantCulture)}</v></c>";
        if (numeric && !header && decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out var number) && text!.Length < 20)
            return $"<c r=\"{Col(col)}{row}\"{(style.Length == 0 && Regex.IsMatch(text, @"^-?\d+\.\d{2}$") ? " s=\"4\"" : style)}><v>{number.ToString(System.Globalization.CultureInfo.InvariantCulture)}</v></c>";
        // Inline strings are never formulas. Prefix formula-like user text so spreadsheet apps cannot reinterpret it on edit.
        var safe = text is { Length: > 0 } && "=+-@\t\r".Contains(text[0]) ? "'" + text : text;
        return $"<c r=\"{Col(col)}{row}\" t=\"inlineStr\"{style}><is><t xml:space=\"preserve\">{Esc(safe)}</t></is></c>";
    }

    public byte[] ToXlsx(ReportModel model)
    {
        var sheets = new List<(string Name, string Xml)>();
        var used = new HashSet<string>();
        var summary = new StringBuilder("<?xml version=\"1.0\" encoding=\"UTF-8\"?><worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><cols><col min=\"1\" max=\"1\" width=\"46\" customWidth=\"1\"/><col min=\"2\" max=\"2\" width=\"48\" customWidth=\"1\"/></cols><sheetData>");
        var r = 1;
        void SummaryLine(string label, string value, bool numeric = true)
        {
            summary.Append($"<row r=\"{r}\">{Cell(r, 0, label)}{Cell(r, 1, value, numeric: numeric)}</row>");
            r++;
        }
        if (model.Header != null)
        {
            summary.Append($"<row r=\"{r}\">{Cell(r, 0, AnalyticsReportHeader.CompanyName, true)}</row>"); r++;
            SummaryLine("Company address", AnalyticsReportHeader.CompanyAddress);
            SummaryLine("Website", AnalyticsReportHeader.Website);
            r++;
        }
        summary.Append($"<row r=\"{r}\">{Cell(r, 0, model.Title, true)}</row>"); r++;
        if (!string.IsNullOrWhiteSpace(model.Description)) SummaryLine("Reporting scope", model.Description, numeric: false);
        if (model.Header is { } author)
        {
            SummaryLine("Prepared by", author.PreparedBy, numeric: false);
            if (author.Roles.Count > 0) SummaryLine("Role", string.Join(", ", author.Roles));
        }
        SummaryLine("Generated (Jordan time)", model.GeneratedAt.ToJordan("yyyy-MM-dd HH:mm"));
        foreach (var filter in SelectedFilters(model)) SummaryLine(filter.Label, filter.Value);
        r++;
        foreach (var k in model.Kpis) SummaryLine(k.Label, k.Value);
        summary.Append("</sheetData></worksheet>");
        if (!model.SummaryLast) sheets.Add((SheetName("Summary", used), summary.ToString()));
        foreach (var t in model.Tables)
        {
            var sb = new StringBuilder("<?xml version=\"1.0\" encoding=\"UTF-8\"?><worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><sheetViews><sheetView workbookViewId=\"0\"><pane ySplit=\"1\" topLeftCell=\"A2\" activePane=\"bottomLeft\" state=\"frozen\"/></sheetView></sheetViews><cols>");
            // Widths follow the header and the longest of the first rows, so values are readable without resizing.
            for (var i = 0; i < t.Columns.Length; i++)
            {
                var longest = t.Rows.Take(300).Select(row => i < row.Length ? Math.Min(row[i]?.Length ?? 0, 60) : 0).DefaultIfEmpty(0).Max();
                sb.Append($"<col min=\"{i + 1}\" max=\"{i + 1}\" width=\"{Math.Clamp(Math.Max(t.Columns[i].Length + 4, longest + 2), 10, 62)}\" customWidth=\"1\"/>");
            }
            sb.Append("</cols><sheetData>");
            sb.Append($"<row r=\"1\">{string.Concat(t.Columns.Select((c, i) => Cell(1, i, c, true)))}</row>");
            var rowNumber = 2;
            foreach (var row in t.Rows.SelectMany(cells => SplitCells(cells, 32000)))
            {
                sb.Append($"<row r=\"{rowNumber}\">{string.Concat(row.Select((cell, column) => Cell(rowNumber, column, cell)))}</row>");
                rowNumber++;
            }
            sb.Append($"</sheetData><autoFilter ref=\"A1:{Col(t.Columns.Length - 1)}{rowNumber - 1}\"/></worksheet>");
            sheets.Add((SheetName(t.Title, used), sb.ToString()));
        }
        if (model.SummaryLast) sheets.Add((SheetName("Summary", used), summary.ToString()));
        using var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, true))
        {
            void Add(string path, string content) { var e = zip.CreateEntry(path, CompressionLevel.Optimal); using var w = new StreamWriter(e.Open(), new UTF8Encoding(false)); w.Write(content); }
            Add("[Content_Types].xml", "<?xml version=\"1.0\" encoding=\"UTF-8\"?><Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"><Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/><Default Extension=\"xml\" ContentType=\"application/xml\"/><Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/><Override PartName=\"/xl/styles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml\"/>"
                + string.Concat(sheets.Select((_, i) => $"<Override PartName=\"/xl/worksheets/sheet{i + 1}.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>")) + "</Types>");
            Add("_rels/.rels", "<?xml version=\"1.0\" encoding=\"UTF-8\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"xl/workbook.xml\"/></Relationships>");
            Add("xl/workbook.xml", "<?xml version=\"1.0\" encoding=\"UTF-8\"?><workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"><bookViews><workbookView/></bookViews><sheets>"
                + string.Concat(sheets.Select((s, i) => $"<sheet name=\"{Esc(s.Name)}\" sheetId=\"{i + 1}\" r:id=\"rId{i + 1}\"/>")) + "</sheets></workbook>");
            Add("xl/_rels/workbook.xml.rels", "<?xml version=\"1.0\" encoding=\"UTF-8\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">"
                + string.Concat(sheets.Select((_, i) => $"<Relationship Id=\"rId{i + 1}\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet{i + 1}.xml\"/>"))
                + $"<Relationship Id=\"rId{sheets.Count + 1}\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles\" Target=\"styles.xml\"/></Relationships>");
            Add("xl/styles.xml", "<?xml version=\"1.0\" encoding=\"UTF-8\"?><styleSheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><numFmts count=\"3\"><numFmt numFmtId=\"164\" formatCode=\"yyyy-mm-dd hh:mm\"/><numFmt numFmtId=\"165\" formatCode=\"0.0%\"/><numFmt numFmtId=\"166\" formatCode=\"#,##0.00\"/></numFmts><fonts count=\"2\"><font><sz val=\"11\"/><name val=\"Calibri\"/></font><font><b/><sz val=\"11\"/><color rgb=\"FFFFFFFF\"/><name val=\"Calibri\"/></font></fonts><fills count=\"3\"><fill><patternFill patternType=\"none\"/></fill><fill><patternFill patternType=\"gray125\"/></fill><fill><patternFill patternType=\"solid\"><fgColor rgb=\"FFFF7900\"/></patternFill></fill></fills><borders count=\"1\"><border/></borders><cellStyleXfs count=\"1\"><xf/></cellStyleXfs><cellXfs count=\"5\"><xf/><xf fontId=\"1\" fillId=\"2\" applyFont=\"1\" applyFill=\"1\"/><xf numFmtId=\"164\" applyNumberFormat=\"1\"/><xf numFmtId=\"165\" applyNumberFormat=\"1\"/><xf numFmtId=\"166\" applyNumberFormat=\"1\"/></cellXfs></styleSheet>");
            for (var i = 0; i < sheets.Count; i++) Add($"xl/worksheets/sheet{i + 1}.xml", sheets[i].Xml);
        }
        return stream.ToArray();
    }

    public byte[] ToDocx(ReportModel model)
    {
        const string w = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
        static bool Arabic(string value) => value.Any(c => c is >= '\u0600' and <= '\u06ff');
        static string Run(string value, bool bold = false)
        {
            var rtl = Arabic(value) ? "<w:rtl/>" : "";
            return $"<w:r><w:rPr>{(bold ? "<w:b/>" : "")}{rtl}</w:rPr><w:t xml:space=\"preserve\">{Esc(value)}</w:t></w:r>";
        }
        static string Paragraph(string value, string? style = null, bool bold = false)
        {
            var bidi = Arabic(value) ? "<w:bidi/>" : "";
            return $"<w:p><w:pPr>{(style == null ? "" : $"<w:pStyle w:val=\"{style}\"/>")}{bidi}</w:pPr>{Run(value, bold)}</w:p>";
        }
        static string Table(ReportTable table)
        {
            var b = new StringBuilder("<w:tbl><w:tblPr><w:tblBorders><w:bottom w:val=\"single\" w:sz=\"4\" w:color=\"DDDDDD\"/><w:insideH w:val=\"single\" w:sz=\"4\" w:color=\"DDDDDD\"/></w:tblBorders></w:tblPr>");
            void Row(IEnumerable<string> cells, bool header)
            {
                b.Append("<w:tr>");
                foreach (var cell in cells) b.Append("<w:tc><w:tcPr><w:tcW w:w=\"0\" w:type=\"auto\"/></w:tcPr>").Append(Paragraph(cell ?? "", bold: header)).Append("</w:tc>");
                b.Append("</w:tr>");
            }
            Row(table.Columns, true);
            foreach (var cells in table.Rows) Row(cells, false);
            return b.Append("</w:tbl>").ToString();
        }
        var body = new StringBuilder();
        body.Append(Paragraph(model.Title, "Title"));
        body.Append(Paragraph($"Generated {model.GeneratedAt.ToJordan("yyyy-MM-dd HH:mm")} (Jordan time)"));
        if (!string.IsNullOrWhiteSpace(model.Description)) body.Append(Paragraph(model.Description));
        foreach (var k in model.Kpis) body.Append(Paragraph($"{k.Label}: {k.Value}"));
        foreach (var t in model.Tables) { body.Append(Paragraph(t.Title, "Heading1")); body.Append(t.Rows.Count == 0 ? Paragraph("No records in the authorized scope.") : Table(t)); }
        body.Append(model.Tables.Any(t => t.Columns.Length > 8)
            ? "<w:sectPr><w:pgSz w:w=\"23811\" w:h=\"16838\" w:orient=\"landscape\"/><w:pgMar w:top=\"900\" w:right=\"900\" w:bottom=\"900\" w:left=\"900\"/></w:sectPr>"
            : "<w:sectPr><w:pgSz w:w=\"11906\" w:h=\"16838\"/><w:pgMar w:top=\"900\" w:right=\"900\" w:bottom=\"900\" w:left=\"900\"/></w:sectPr>");
        var document = $"<?xml version=\"1.0\" encoding=\"UTF-8\"?><w:document xmlns:w=\"{w}\"><w:body>{body}</w:body></w:document>";
        var styles = $"<?xml version=\"1.0\" encoding=\"UTF-8\"?><w:styles xmlns:w=\"{w}\"><w:style w:type=\"paragraph\" w:default=\"1\" w:styleId=\"Normal\"><w:name w:val=\"Normal\"/><w:rPr><w:rFonts w:ascii=\"Calibri\" w:hAnsi=\"Calibri\" w:cs=\"Arial\"/><w:sz w:val=\"20\"/></w:rPr></w:style><w:style w:type=\"paragraph\" w:styleId=\"Title\"><w:name w:val=\"Title\"/><w:rPr><w:b/><w:color w:val=\"E65F00\"/><w:sz w:val=\"34\"/></w:rPr></w:style><w:style w:type=\"paragraph\" w:styleId=\"Heading1\"><w:name w:val=\"heading 1\"/><w:rPr><w:b/><w:color w:val=\"E65F00\"/><w:sz w:val=\"26\"/></w:rPr></w:style></w:styles>";
        using var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, true))
        {
            void Add(string path, string content) { var e = zip.CreateEntry(path, CompressionLevel.Optimal); using var sw = new StreamWriter(e.Open(), new UTF8Encoding(false)); sw.Write(content); }
            Add("[Content_Types].xml", "<?xml version=\"1.0\" encoding=\"UTF-8\"?><Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"><Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/><Default Extension=\"xml\" ContentType=\"application/xml\"/><Override PartName=\"/word/document.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml\"/><Override PartName=\"/word/styles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.styles+xml\"/></Types>");
            Add("_rels/.rels", "<?xml version=\"1.0\" encoding=\"UTF-8\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"word/document.xml\"/></Relationships>");
            Add("word/_rels/document.xml.rels", "<?xml version=\"1.0\" encoding=\"UTF-8\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles\" Target=\"styles.xml\"/></Relationships>");
            Add("word/document.xml", document);
            Add("word/styles.xml", styles);
        }
        return stream.ToArray();
    }

    public byte[] ToPdf(ReportModel model)
    {
        var doc = new Document();
        doc.Info.Title = model.Title;
        if (model.Header is { } header) doc.Info.Author = header.PreparedBy;
        doc.Styles[StyleNames.Normal]!.Font.Name = "Orange Sans"; doc.Styles[StyleNames.Normal]!.Font.Size = 9;
        var section = doc.AddSection();
        section.PageSetup.Orientation = Orientation.Landscape; section.PageSetup.PageFormat = model.Tables.Any(t => t.Columns.Length > 10) ? PageFormat.A3 : PageFormat.A4;
        section.PageSetup.LeftMargin = section.PageSetup.RightMargin = "1.4cm"; section.PageSetup.TopMargin = "1.4cm"; section.PageSetup.BottomMargin = "1.4cm";
        var orange = new Color(255, 121, 0);
        if (model.Header is { } letterhead) AddLetterhead(section, letterhead, orange);
        var title = section.AddParagraph(model.Title); title.Format.Font.Size = 20; title.Format.Font.Bold = true; title.Format.Font.Color = orange;
        if (model.Header != null) title.Format.SpaceBefore = "12pt";
        section.AddParagraph($"Sourcing & Tender Management System · generated {model.GeneratedAt.ToJordan("yyyy-MM-dd HH:mm")} (Jordan time) from live system records").Format.Font.Color = Colors.Gray;
        foreach (var filter in SelectedFilters(model)) section.AddParagraph($"{filter.Label} — {filter.Value}").Format.Font.Color = Colors.Gray;
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
            var width = (section.PageSetup.PageFormat == PageFormat.A3 ? 38.5 : 26.9) / t.Columns.Length;
            foreach (var _ in t.Columns) table.AddColumn($"{width.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)}cm");
            var head = table.AddRow(); head.HeadingFormat = true; head.Shading.Color = orange; head.Format.Font.Color = Colors.White; head.Format.Font.Bold = true;
            for (var i = 0; i < t.Columns.Length; i++) head.Cells[i].AddParagraph(t.Columns[i]);
            foreach (var cells in t.Rows.SelectMany(cells => SplitCells(cells, 420)))
            {
                var row = table.AddRow();
                for (var i = 0; i < cells.Length; i++) row.Cells[i].AddParagraph(cells[i]);
            }
        }
        var renderer = new PdfDocumentRenderer { Document = doc };
        renderer.RenderDocument();
        using var ms = new MemoryStream(); renderer.PdfDocument.Save(ms, false); return ms.ToArray();
    }

    private static void AddLetterhead(Section section, AnalyticsReportHeader header, Color orange)
    {
        var table = section.AddTable();
        table.AddColumn("2.3cm");
        table.AddColumn("13cm");
        table.AddColumn("11.6cm");
        table.TopPadding = "6pt";
        table.BottomPadding = "6pt";
        table.LeftPadding = "7pt";
        table.RightPadding = "7pt";
        var row = table.AddRow();
        row.VerticalAlignment = VerticalAlignment.Center;
        row.Height = "48pt";
        row.Shading.Color = new Color(247, 248, 249);
        row.Cells[0].Shading.Color = orange;
        var logo = row.Cells[0].AddParagraph("orange");
        logo.Format.Font.Size = 15;
        logo.Format.Font.Bold = true;
        logo.Format.Font.Color = Colors.White;
        var company = row.Cells[1].AddParagraph(AnalyticsReportHeader.CompanyName);
        company.Format.Font.Bold = true;
        company.Format.Font.Size = 13;
        row.Cells[1].AddParagraph(AnalyticsReportHeader.CompanyAddress);
        row.Cells[1].AddParagraph(AnalyticsReportHeader.Website);
        row.Cells[2].AddParagraph("Prepared by").Format.Font.Color = new Color(84, 95, 104);
        row.Cells[2].AddParagraph(FormatPdfName(header.PreparedBy)).Format.Font.Bold = true;
        if (header.Roles.Count > 0) row.Cells[2].AddParagraph(string.Join(", ", header.Roles));
    }

    private static Dictionary<char, char[]> BuildArabicLetterForms()
    {
        // The basic Arabic presentation forms are ordered isolated, final, initial, medial.
        var forms = Enumerable.Range(0xFE80, 0xFEF4 - 0xFE80 + 1).Select(code => (char)code)
            .GroupBy(form => form.ToString().Normalize(NormalizationForm.FormKC)[0])
            .ToDictionary(group => group.Key, group => group.ToArray());
        forms['ـ'] = ['ـ', 'ـ', 'ـ', 'ـ'];
        // Lam-alef ligatures accept a connection from the preceding letter only.
        for (var form = 0xFEF5; form <= 0xFEFB; form += 2)
            forms[(char)form] = [(char)form, (char)(form + 1)];
        return forms;
    }

    // PDFsharp paints the supplied glyphs in order. Shape the Arabic portions of a
    // display name while keeping its original Unicode value in the document metadata.
    private static string FormatPdfName(string name) => ArabicNamePhrase.Replace(name, match =>
    {
        var phrase = match.Value.TrimEnd(' ', '-');
        var joined = phrase.Replace("لآ", "ﻵ").Replace("لأ", "ﻷ").Replace("لإ", "ﻹ").Replace("لا", "ﻻ");
        var starts = StringInfo.ParseCombiningCharacters(joined);
        var result = new StringBuilder(joined.Length);
        for (var i = starts.Length - 1; i >= 0; i--)
        {
            var start = starts[i];
            var end = i + 1 < starts.Length ? starts[i + 1] : joined.Length;
            var letter = joined[start];
            if (ArabicLetterForms.TryGetValue(letter, out var forms))
            {
                var previous = i > 0 ? ArabicLetterForms.GetValueOrDefault(joined[starts[i - 1]]) : null;
                var next = i + 1 < starts.Length ? ArabicLetterForms.GetValueOrDefault(joined[starts[i + 1]]) : null;
                var joinsPrevious = forms.Length >= 2 && previous?.Length == 4;
                var joinsNext = forms.Length == 4 && next?.Length >= 2;
                letter = forms[joinsNext ? (joinsPrevious ? 3 : 2) : (joinsPrevious ? 1 : 0)];
            }
            result.Append(letter).Append(joined, start + 1, end - start - 1);
        }
        return result.Append(match.Value, phrase.Length, match.Value.Length - phrase.Length).ToString();
    });

    private static IEnumerable<(string Label, string Value)> SelectedFilters(ReportModel model)
    {
        foreach (var (key, value) in model.Filter.RouteValues())
        {
            var label = char.ToUpperInvariant(key[0]) + key[1..];
            yield return (ReportingService.Words(label), value);
        }
    }

    private static IEnumerable<string[]> SplitCells(string[] cells, int limit)
    {
        if (cells.All(cell => cell != null && cell.Length <= limit))
        {
            yield return cells;
            yield break;
        }
        var offsets = new int[cells.Length];
        do
        {
            var row = new string[cells.Length];
            for (var column = 0; column < cells.Length; column++)
            {
                var cell = cells[column] ?? "";
                var count = Math.Min(limit, cell.Length - offsets[column]);
                if (count > 0 && offsets[column] + count < cell.Length && char.IsHighSurrogate(cell[offsets[column] + count - 1])) count--;
                row[column] = cell.Substring(offsets[column], count);
                offsets[column] += count;
            }
            yield return row;
        } while (cells.Where((cell, column) => offsets[column] < (cell?.Length ?? 0)).Any());
    }
}
