using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using PdfSharp.Pdf.Content;
using PdfSharp.Pdf.Content.Objects;
using PdfSharp.Pdf.IO;

namespace Orange_Tendering_System.Services.Radar;

// Reads a file uploaded in AI Studio into plain text and, for spreadsheets/CSV, a table. In memory only: nothing is stored and
// the content is treated as untrusted user data (the analyst is told so). No new package: XLSX is Open XML read with
// System.IO.Compression, PDF text uses the PDFsharp content parser already referenced for reports.
public sealed record ExtractedFile(string Name, string Kind, string Text, IReadOnlyList<string> Columns, IReadOnlyList<IReadOnlyList<string>> Rows, int TotalRows, string? Notice);

public static class RadarFileExtractor
{
    public const long MaxBytes = 8_000_000;
    private const int MaxRows = 500, MaxText = 20_000;
    public static readonly string[] Extensions = [".pdf", ".xlsx", ".csv", ".txt"];

    public static ExtractedFile Extract(string fileName, Stream stream)
    {
        var name = Path.GetFileName(fileName);
        var ext = Path.GetExtension(name).ToLowerInvariant();
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        buffer.Position = 0;
        return ext switch
        {
            ".xlsx" => Table(name, "Excel", Xlsx(buffer)),
            ".csv" => Table(name, "CSV", Csv(new StreamReader(buffer, Encoding.UTF8, true).ReadToEnd())),
            ".txt" => Text(name, "Text", new StreamReader(buffer, Encoding.UTF8, true).ReadToEnd(), null),
            ".pdf" => Pdf(name, buffer),
            _ => throw new RadarToolException("Upload a PDF, Excel (.xlsx), CSV or text file.")
        };
    }

    private static ExtractedFile Text(string name, string kind, string text, string? notice)
    {
        text = text.Replace("\r\n", "\n").Trim();
        return new ExtractedFile(name, kind, text.Length > MaxText ? text[..MaxText] + "…" : text, [], [], 0, notice);
    }

    private static ExtractedFile Table(string name, string kind, List<List<string>> rows)
    {
        rows = rows.Where(r => r.Any(c => c.Length > 0)).ToList();
        if (rows.Count == 0) return new ExtractedFile(name, kind, "", [], [], 0, "The file contains no readable rows.");
        var width = rows.Max(r => r.Count);
        var header = rows[0].Concat(Enumerable.Repeat("", width - rows[0].Count)).Select((h, i) => h.Length > 0 ? h : $"Column {i + 1}").ToList();
        var body = rows.Skip(1).Select(r => (IReadOnlyList<string>)r.Concat(Enumerable.Repeat("", width - r.Count)).ToList()).ToList();
        var text = new StringBuilder(string.Join(" | ", header)).Append('\n');
        foreach (var r in body.Take(120)) text.Append(string.Join(" | ", r)).Append('\n');
        return new ExtractedFile(name, kind, text.ToString(), header, body.Take(MaxRows).ToList(), body.Count,
            body.Count > MaxRows ? $"Showing the first {MaxRows} of {body.Count} rows." : null);
    }

    private static List<List<string>> Csv(string text)
    {
        var rows = new List<List<string>>(); var row = new List<string>(); var cell = new StringBuilder(); var quoted = false;
        var sep = text.Split('\n').FirstOrDefault()?.Count(c => c == ';') > text.Split('\n').FirstOrDefault()?.Count(c => c == ',') ? ';' : ',';
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (quoted) { if (c == '"' && i + 1 < text.Length && text[i + 1] == '"') { cell.Append('"'); i++; } else if (c == '"') quoted = false; else cell.Append(c); }
            else if (c == '"') quoted = true;
            else if (c == sep) { row.Add(cell.ToString().Trim()); cell.Clear(); }
            else if (c == '\n') { row.Add(cell.ToString().Trim()); cell.Clear(); rows.Add(row); row = []; if (rows.Count > MaxRows + 1) return rows; }
            else if (c != '\r') cell.Append(c);
        }
        if (cell.Length > 0 || row.Count > 0) { row.Add(cell.ToString().Trim()); rows.Add(row); }
        return rows;
    }

    // First worksheet only; shared strings and inline strings resolved; formulas use their cached value.
    private static List<List<string>> Xlsx(Stream stream)
    {
        XNamespace m = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        XNamespace r = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        XNamespace pr = "http://schemas.openxmlformats.org/package/2006/relationships";
        try
        {
            using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
            XDocument Load(string path) { var e = zip.GetEntry(path) ?? throw new RadarToolException("The workbook is incomplete."); if (e.Length > 50_000_000) throw new RadarToolException("The workbook is too large."); using var s = e.Open(); return XDocument.Load(s); }
            IReadOnlyList<string> shared = zip.GetEntry("xl/sharedStrings.xml") is null ? [] : Load("xl/sharedStrings.xml").Root!.Elements(m + "si").Select(si => string.Concat(si.Descendants(m + "t").Select(t => t.Value))).ToList();
            var rels = Load("xl/_rels/workbook.xml.rels").Root!.Elements(pr + "Relationship").ToDictionary(x => x.Attribute("Id")?.Value ?? "", x => x.Attribute("Target")?.Value ?? "");
            // Every worksheet (up to 8) is read; the one with the most rows becomes the table, since summaries often come first.
            var best = new List<List<string>>();
            foreach (var sh in Load("xl/workbook.xml").Root!.Element(m + "sheets")?.Elements(m + "sheet").Take(8) ?? [])
            {
                if (!rels.TryGetValue(sh.Attribute(r + "id")?.Value ?? "", out var target) || target.Length == 0) continue;
                var rows = ReadSheet(Load(target.StartsWith('/') ? target.TrimStart('/') : "xl/" + target), m, shared);
                if (rows.Count > best.Count) best = rows;
            }
            return best;
        }
        catch (Exception e) when (e is InvalidDataException or System.Xml.XmlException or NullReferenceException) { throw new RadarToolException("The Excel file could not be read. Save it as .xlsx and try again."); }
    }

    private static List<List<string>> ReadSheet(XDocument sheet, XNamespace m, IReadOnlyList<string> shared)
    {
        {
            var rows = new List<List<string>>();
            foreach (var row in sheet.Descendants(m + "row").Take(MaxRows + 1))
            {
                var cells = new List<string>();
                foreach (var c in row.Elements(m + "c"))
                {
                    var col = ColumnIndex(c.Attribute("r")?.Value);
                    while (col >= 0 && cells.Count < col) cells.Add("");
                    var t = c.Attribute("t")?.Value; var v = c.Element(m + "v")?.Value ?? "";
                    cells.Add(t == "s" && int.TryParse(v, out var si) && si < shared.Count ? shared[si] : t == "inlineStr" ? string.Concat(c.Descendants(m + "t").Select(x => x.Value)) : v);
                }
                rows.Add(cells);
            }
            return rows.Where(r => r.Any(c => c.Length > 0)).ToList();
        }
    }

    private static int ColumnIndex(string? reference)
    {
        if (string.IsNullOrEmpty(reference)) return -1;
        var n = 0; foreach (var ch in reference.TakeWhile(char.IsLetter)) n = n * 26 + (char.ToUpperInvariant(ch) - 'A' + 1);
        return n - 1;
    }

    private static ExtractedFile Pdf(string name, Stream stream)
    {
        try
        {
            using var doc = PdfReader.Open(stream, PdfDocumentOpenMode.Import);
            var text = new StringBuilder();
            foreach (var page in doc.Pages.Cast<PdfSharp.Pdf.PdfPage>().Take(60))
            {
                Walk(ContentReader.ReadContent(page), text);
                text.Append("\n\n");
                if (text.Length > MaxText) break;
            }
            var clean = text.ToString();
            var readable = clean.Count(char.IsLetterOrDigit);
            return Text(name, "PDF", clean, readable < 20 ? "Little or no text could be extracted (scanned image or embedded font encoding). OCR is not available locally." : $"{doc.PageCount} pages read.");
        }
        catch (Exception e) when (e is PdfReaderException or InvalidOperationException or NotImplementedException) { throw new RadarToolException("The PDF could not be read (it may be encrypted or damaged)."); }
    }

    private static void Walk(CSequence sequence, StringBuilder text)
    {
        foreach (var item in sequence)
        {
            switch (item)
            {
                case COperator op when op.OpCode.Name is "Tj" or "'" or "\"":
                    foreach (var s in op.Operands.OfType<CString>()) text.Append(s.Value);
                    break;
                case COperator op when op.OpCode.Name == "TJ":
                    foreach (var arr in op.Operands.OfType<CArray>())
                        foreach (var el in arr)
                            if (el is CString s) text.Append(s.Value);
                            else if (el is CNumber n && n is CInteger { Value: < -200 } or CReal { Value: < -200 }) text.Append(' ');
                    break;
                case COperator op when op.OpCode.Name is "Td" or "TD" or "T*" or "ET":
                    if (text.Length > 0 && text[^1] != '\n') text.Append('\n');
                    break;
                case CSequence nested: Walk(nested, text); break;
            }
        }
    }
}
