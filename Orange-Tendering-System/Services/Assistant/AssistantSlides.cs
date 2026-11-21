using System.Security;
using System.Text;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;

namespace Orange_Tendering_System.Services.Assistant;

// Real .pptx through the Open XML SDK package API (MIT): one master, one blank layout, an Orange theme, and slides written as
// PresentationML. PowerPoint shapes Arabic itself; RTL paragraphs use rtl="1".
public static class AssistantSlides
{
    private const long W = 12192000, H = 6858000;
    private const string Ns = "xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\" xmlns:p=\"http://schemas.openxmlformats.org/presentationml/2006/main\"";

    public static byte[] Build(AssistantDataset data, ReportMeta meta)
    {
        var ar = meta.Arabic;
        var slides = new List<string> { TitleSlide(meta) };
        var tiles = new List<(string, string)> { (ar ? "عدد السجلات" : "Records", data.Truncated ? $"{data.Rows.Count}/{data.TotalCount}" : data.Rows.Count.ToString()) };
        tiles.AddRange(data.Summary.Take(5));
        slides.Add(TilesSlide(ar ? "الملخص" : "Summary", tiles, ar));
        if (data.Chart.Count > 1) slides.Add(BarsSlide(data.ChartTitle ?? (ar ? "التوزيع" : "Breakdown"), data.Chart.Take(8).ToList(), ar));
        if (data.Rows.Count == 0)
            slides.Add(TextSlide(ar ? "البيانات" : "Records", data.Empty ?? (ar ? "لا توجد سجلات مطابقة ضمن نطاق صلاحياتك." : "No records match this request in your authorized scope."), ar));
        else
        {
            var cols = Enumerable.Range(0, data.Columns.Count).Take(6).ToArray();
            const int perSlide = 8; var pages = Math.Min(6, (data.Rows.Count + perSlide - 1) / perSlide);
            for (var page = 0; page < pages; page++)
            {
                var rows = data.Rows.Skip(page * perSlide).Take(perSlide).Select(r => cols.Select(i => OrangeBrand.Cell(r.ElementAtOrDefault(i), data.Columns[i].Kind)).ToArray()).ToList();
                var shown = Math.Min(data.Rows.Count, pages * perSlide);
                var footnote = page == pages - 1 && (data.Rows.Count > shown || data.Columns.Count > cols.Length || data.Truncated)
                    ? (ar ? $"تعرض الشرائح {shown} سجلًا و{cols.Length} أعمدة. البيانات الكاملة متاحة في تصدير Excel." : $"Slides show {shown} records and {cols.Length} columns. Use the Excel export for the complete data.") : null;
                slides.Add(TableSlide($"{(ar ? "البيانات" : "Records")} ({page + 1}/{pages})", cols.Select(i => data.Columns[i].Header).ToArray(), rows, cols.Select(i => data.Columns[i].Width > 0 ? data.Columns[i].Width : 14).ToArray(), ar, footnote));
            }
        }
        return Package(slides, meta.Title);
    }

    public static byte[] BuildText(ReportMeta meta, IReadOnlyList<(string Heading, string Body)> sections)
    {
        var slides = new List<string> { TitleSlide(meta) };
        foreach (var (heading, body) in sections)
        {
            var chunks = body.Replace("\r", "").Split("\n\n", StringSplitOptions.RemoveEmptyEntries).ToList();
            for (var i = 0; i < chunks.Count && slides.Count < 20; i += 4)
                slides.Add(TextSlide(heading, string.Join("\n", chunks.Skip(i).Take(4).Select(x => x.Length > 420 ? x[..420] + "…" : x)), meta.Arabic || AssistantIntentParser.IsArabic(body)));
        }
        return Package(slides, meta.Title);
    }

    private static string X(string s) => SecurityElement.Escape(new string(s.Where(c => c is '\t' or '\n' || (c >= ' ' && c != '￾' && c != '￿')).ToArray())) ?? "";

    private static string Para(string text, int size, string color, bool bold, bool rtl, string align = "") =>
        $"<a:p><a:pPr algn=\"{(align != "" ? align : rtl ? "r" : "l")}\"{(rtl ? " rtl=\"1\"" : "")}/><a:r><a:rPr lang=\"{(rtl ? "ar-JO" : "en-US")}\" sz=\"{size}\" b=\"{(bold ? 1 : 0)}\" dirty=\"0\"><a:solidFill><a:srgbClr val=\"{color}\"/></a:solidFill><a:latin typeface=\"Calibri\"/><a:cs typeface=\"Arial\"/></a:rPr><a:t>{X(text)}</a:t></a:r></a:p>";

    private static int shapeId;
    private static string Box(long x, long y, long cx, long cy, string? fill, string paragraphs, string geom = "rect", string anchor = "t", string? line = null) =>
        $"<p:sp><p:nvSpPr><p:cNvPr id=\"{Interlocked.Increment(ref shapeId) % 100000 + 2}\" name=\"Shape\"/><p:cNvSpPr/><p:nvPr/></p:nvSpPr><p:spPr><a:xfrm><a:off x=\"{x}\" y=\"{y}\"/><a:ext cx=\"{cx}\" cy=\"{cy}\"/></a:xfrm><a:prstGeom prst=\"{geom}\"><a:avLst/></a:prstGeom>" +
        (fill == null ? "<a:noFill/>" : $"<a:solidFill><a:srgbClr val=\"{fill}\"/></a:solidFill>") + (line == null ? "<a:ln><a:noFill/></a:ln>" : $"<a:ln w=\"9525\"><a:solidFill><a:srgbClr val=\"{line}\"/></a:solidFill></a:ln>") +
        $"</p:spPr><p:txBody><a:bodyPr wrap=\"square\" lIns=\"91440\" rIns=\"91440\" tIns=\"45720\" bIns=\"45720\" anchor=\"{anchor}\"><a:normAutofit/></a:bodyPr><a:lstStyle/>{(paragraphs.Length == 0 ? "<a:p><a:endParaRPr lang=\"en-US\"/></a:p>" : paragraphs)}</p:txBody></p:sp>";

    private static string Slide(string shapes, string background = "FFFFFF") =>
        $"<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><p:sld {Ns}><p:cSld><p:bg><p:bgPr><a:solidFill><a:srgbClr val=\"{background}\"/></a:solidFill><a:effectLst/></p:bgPr></p:bg><p:spTree><p:nvGrpSpPr><p:cNvPr id=\"1\" name=\"\"/><p:cNvGrpSpPr/><p:nvPr/></p:nvGrpSpPr><p:grpSpPr/>{shapes}</p:spTree></p:cSld><p:clrMapOvr><a:masterClrMapping/></p:clrMapOvr></p:sld>";

    private static string Header(string title, bool rtl) =>
        Box(0, 0, W, 90000, OrangeBrand.Orange, "") +
        Box(500000, 300000, W - 1000000, 700000, null, Para(title, 2800, OrangeBrand.Dark, true, rtl), anchor: "ctr") +
        Box(500000, H - 450000, W - 1000000, 300000, null, Para(rtl ? OrangeBrand.CompanyAr : OrangeBrand.Company, 1000, OrangeBrand.Grey, false, rtl));

    private static string TitleSlide(ReportMeta meta) => Slide(
        Box(0, 0, 420000, H, OrangeBrand.Orange, "") +
        Box(900000, 1500000, W - 1800000, 400000, null, Para(meta.Arabic ? OrangeBrand.CompanyAr : OrangeBrand.Company, 1400, OrangeBrand.Orange, true, meta.Arabic)) +
        Box(900000, 2000000, W - 1800000, 1600000, null, Para(meta.Title, 4000, "FFFFFF", true, meta.Arabic), anchor: "t") +
        Box(900000, 3900000, W - 1800000, 1300000, null,
            Para($"{meta.L("Generated", "تاريخ الإنشاء")}: {meta.Generated}", 1400, "D9D9D9", false, meta.Arabic) +
            Para($"{meta.L("By", "بواسطة")}: {meta.GeneratedBy} · {meta.Role}", 1400, "D9D9D9", false, meta.Arabic) +
            Para($"{meta.L("Scope", "النطاق")}: {meta.ScopeNote}", 1400, "D9D9D9", false, meta.Arabic)), "141414");

    private static string TilesSlide(string title, List<(string Label, string Value)> tiles, bool rtl)
    {
        var sb = new StringBuilder(Header(title, rtl));
        const int perRow = 3; long tileW = (W - 1000000 - 2 * 250000) / perRow, tileH = 1750000;
        for (var i = 0; i < tiles.Count; i++)
        {
            var col = rtl ? perRow - 1 - i % perRow : i % perRow;
            var x = 500000 + col * (tileW + 250000); var y = 1350000 + i / perRow * (tileH + 250000);
            var value = tiles[i].Value.Length > 40 ? tiles[i].Value[..39] + "…" : tiles[i].Value;
            sb.Append(Box(x, y, tileW, tileH, OrangeBrand.Light, Para(value, value.Length > 14 ? 2000 : 3600, OrangeBrand.Orange, true, rtl, "ctr") + Para(tiles[i].Label, 1300, OrangeBrand.Dark, false, rtl, "ctr"), "roundRect", "ctr"));
        }
        return Slide(sb.ToString());
    }

    private static string BarsSlide(string title, List<(string Label, double Value)> bars, bool rtl)
    {
        var sb = new StringBuilder(Header(title, rtl));
        var max = Math.Max(bars.Max(b => b.Value), 1); long top = 1300000, rowH = Math.Min(560000, (H - top - 700000) / Math.Max(bars.Count, 1));
        long labelW = 3400000, barMax = W - 1000000 - labelW - 1300000;
        for (var i = 0; i < bars.Count; i++)
        {
            var y = top + i * rowH; var len = Math.Max(60000, (long)(barMax * bars[i].Value / max));
            var labelX = rtl ? W - 500000 - labelW : 500000; var barX = rtl ? W - 500000 - labelW - 150000 - len : 500000 + labelW + 150000;
            var valueX = rtl ? barX - 1150000 : barX + len + 100000;
            var label = bars[i].Label.Length > 38 ? bars[i].Label[..37] + "…" : bars[i].Label;
            sb.Append(Box(labelX, y, labelW, rowH - 80000, null, Para(label, 1300, OrangeBrand.Dark, false, rtl), anchor: "ctr"));
            sb.Append(Box(barX, y + 70000, len, rowH - 220000, OrangeBrand.Orange, ""));
            sb.Append(Box(valueX, y, 1100000, rowH - 80000, null, Para(OrangeBrand.Cell(bars[i].Value, ColumnKind.Number), 1300, OrangeBrand.Grey, true, false, rtl ? "r" : "l"), anchor: "ctr"));
        }
        return Slide(sb.ToString());
    }

    private static string TextSlide(string title, string text, bool rtl) =>
        Slide(Header(title, rtl) + Box(500000, 1250000, W - 1000000, H - 1900000, null, string.Concat(text.Split('\n').Where(l => l.Trim().Length > 0).Take(14).Select(l => Para(l.Trim(), 1500, OrangeBrand.Dark, false, rtl)))));

    private static string TableSlide(string title, string[] headers, List<string[]> rows, double[] weights, bool rtl, string? footnote)
    {
        long width = W - 1000000, rowH = 480000; var total = weights.Sum();
        var order = Enumerable.Range(0, headers.Length).ToArray(); if (rtl) Array.Reverse(order);
        string Cell(string text, bool head, bool zebra) =>
            $"<a:tc><a:txBody><a:bodyPr/><a:lstStyle/>{Para(text.Length > 90 ? text[..89] + "…" : text, head ? 1200 : 1100, head ? "FFFFFF" : OrangeBrand.Dark, head, rtl)}</a:txBody><a:tcPr marL=\"68580\" marR=\"68580\" marT=\"34290\" marB=\"34290\" anchor=\"ctr\">" +
            $"<a:lnL w=\"6350\"><a:solidFill><a:srgbClr val=\"{OrangeBrand.Rule}\"/></a:solidFill></a:lnL><a:lnR w=\"6350\"><a:solidFill><a:srgbClr val=\"{OrangeBrand.Rule}\"/></a:solidFill></a:lnR><a:lnT w=\"6350\"><a:solidFill><a:srgbClr val=\"{OrangeBrand.Rule}\"/></a:solidFill></a:lnT><a:lnB w=\"6350\"><a:solidFill><a:srgbClr val=\"{OrangeBrand.Rule}\"/></a:solidFill></a:lnB>" +
            $"<a:solidFill><a:srgbClr val=\"{(head ? OrangeBrand.Dark : zebra ? "FAFAFA" : "FFFFFF")}\"/></a:solidFill></a:tcPr></a:tc>";
        var grid = string.Concat(order.Select(i => $"<a:gridCol w=\"{(long)(width * weights[i] / total)}\"/>"));
        var body = new StringBuilder($"<a:tr h=\"{rowH}\">{string.Concat(order.Select(i => Cell(headers[i], true, false)))}</a:tr>");
        for (var r = 0; r < rows.Count; r++) body.Append($"<a:tr h=\"{rowH}\">{string.Concat(order.Select(i => Cell(rows[r][i], false, r % 2 == 1)))}</a:tr>");
        var frame = $"<p:graphicFrame><p:nvGraphicFramePr><p:cNvPr id=\"{Interlocked.Increment(ref shapeId) % 100000 + 2}\" name=\"Table\"/><p:cNvGraphicFramePr><a:graphicFrameLocks noGrp=\"1\"/></p:cNvGraphicFramePr><p:nvPr/></p:nvGraphicFramePr>" +
            $"<p:xfrm><a:off x=\"500000\" y=\"1200000\"/><a:ext cx=\"{width}\" cy=\"{rowH * (rows.Count + 1)}\"/></p:xfrm><a:graphic><a:graphicData uri=\"http://schemas.openxmlformats.org/drawingml/2006/table\"><a:tbl><a:tblPr firstRow=\"1\"{(rtl ? " rtl=\"1\"" : "")}/><a:tblGrid>{grid}</a:tblGrid>{body}</a:tbl></a:graphicData></a:graphic></p:graphicFrame>";
        var note = footnote == null ? "" : Box(500000, H - 800000, W - 1000000, 320000, null, Para(footnote, 1000, OrangeBrand.Grey, false, rtl));
        return Slide(Header(title, rtl) + frame + note);
    }

    private static void Feed(OpenXmlPart part, string xml) { using var s = new MemoryStream(Encoding.UTF8.GetBytes(xml)); part.FeedData(s); }

    private static byte[] Package(List<string> slides, string title)
    {
        using var stream = new MemoryStream();
        using (var doc = PresentationDocument.Create(stream, PresentationDocumentType.Presentation))
        {
            doc.PackageProperties.Title = title; doc.PackageProperties.Created = DateTime.UtcNow;
            var presentation = doc.AddPresentationPart();
            var master = presentation.AddNewPart<SlideMasterPart>("rIdMaster");
            var layout = master.AddNewPart<SlideLayoutPart>("rIdLayout");
            layout.AddPart(master, "rIdMaster");
            var theme = master.AddNewPart<ThemePart>("rIdTheme");
            presentation.AddPart(theme, "rIdTheme");
            Feed(theme, Theme);
            Feed(layout, $"<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><p:sldLayout {Ns} type=\"blank\" preserve=\"1\"><p:cSld name=\"Blank\"><p:spTree><p:nvGrpSpPr><p:cNvPr id=\"1\" name=\"\"/><p:cNvGrpSpPr/><p:nvPr/></p:nvGrpSpPr><p:grpSpPr/></p:spTree></p:cSld><p:clrMapOvr><a:masterClrMapping/></p:clrMapOvr></p:sldLayout>");
            Feed(master, $"<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><p:sldMaster {Ns}><p:cSld><p:bg><p:bgRef idx=\"1001\"><a:schemeClr val=\"bg1\"/></p:bgRef></p:bg><p:spTree><p:nvGrpSpPr><p:cNvPr id=\"1\" name=\"\"/><p:cNvGrpSpPr/><p:nvPr/></p:nvGrpSpPr><p:grpSpPr/></p:spTree></p:cSld>" +
                "<p:clrMap bg1=\"lt1\" tx1=\"dk1\" bg2=\"lt2\" tx2=\"dk2\" accent1=\"accent1\" accent2=\"accent2\" accent3=\"accent3\" accent4=\"accent4\" accent5=\"accent5\" accent6=\"accent6\" hlink=\"hlink\" folHlink=\"folHlink\"/>" +
                "<p:sldLayoutIdLst><p:sldLayoutId id=\"2147483649\" r:id=\"rIdLayout\"/></p:sldLayoutIdLst></p:sldMaster>");
            var ids = new StringBuilder();
            for (var i = 0; i < slides.Count; i++)
            {
                var part = presentation.AddNewPart<SlidePart>("rIdSlide" + (i + 1));
                part.AddPart(layout, "rIdLayout");
                Feed(part, slides[i]);
                ids.Append($"<p:sldId id=\"{256 + i}\" r:id=\"rIdSlide{i + 1}\"/>");
            }
            Feed(presentation, $"<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><p:presentation {Ns} saveSubsetFonts=\"1\"><p:sldMasterIdLst><p:sldMasterId id=\"2147483648\" r:id=\"rIdMaster\"/></p:sldMasterIdLst>" +
                $"<p:sldIdLst>{ids}</p:sldIdLst><p:sldSz cx=\"{W}\" cy=\"{H}\"/><p:notesSz cx=\"6858000\" cy=\"9144000\"/></p:presentation>");
        }
        return stream.ToArray();
    }

    private const string Theme = """
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <a:theme xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main" name="Orange"><a:themeElements>
        <a:clrScheme name="Orange"><a:dk1><a:srgbClr val="1F1F1F"/></a:dk1><a:lt1><a:srgbClr val="FFFFFF"/></a:lt1><a:dk2><a:srgbClr val="333333"/></a:dk2><a:lt2><a:srgbClr val="F4F4F4"/></a:lt2>
        <a:accent1><a:srgbClr val="FF7900"/></a:accent1><a:accent2><a:srgbClr val="FFB322"/></a:accent2><a:accent3><a:srgbClr val="098FFF"/></a:accent3><a:accent4><a:srgbClr val="00BF81"/></a:accent4>
        <a:accent5><a:srgbClr val="6E6E6E"/></a:accent5><a:accent6><a:srgbClr val="C65200"/></a:accent6><a:hlink><a:srgbClr val="FF7900"/></a:hlink><a:folHlink><a:srgbClr val="C65200"/></a:folHlink></a:clrScheme>
        <a:fontScheme name="Orange"><a:majorFont><a:latin typeface="Calibri"/><a:ea typeface=""/><a:cs typeface="Arial"/></a:majorFont><a:minorFont><a:latin typeface="Calibri"/><a:ea typeface=""/><a:cs typeface="Arial"/></a:minorFont></a:fontScheme>
        <a:fmtScheme name="Orange"><a:fillStyleLst><a:solidFill><a:schemeClr val="phClr"/></a:solidFill><a:solidFill><a:schemeClr val="phClr"/></a:solidFill><a:solidFill><a:schemeClr val="phClr"/></a:solidFill></a:fillStyleLst>
        <a:lnStyleLst><a:ln w="6350"><a:solidFill><a:schemeClr val="phClr"/></a:solidFill></a:ln><a:ln w="12700"><a:solidFill><a:schemeClr val="phClr"/></a:solidFill></a:ln><a:ln w="19050"><a:solidFill><a:schemeClr val="phClr"/></a:solidFill></a:ln></a:lnStyleLst>
        <a:effectStyleLst><a:effectStyle><a:effectLst/></a:effectStyle><a:effectStyle><a:effectLst/></a:effectStyle><a:effectStyle><a:effectLst/></a:effectStyle></a:effectStyleLst>
        <a:bgFillStyleLst><a:solidFill><a:schemeClr val="phClr"/></a:solidFill><a:solidFill><a:schemeClr val="phClr"/></a:solidFill><a:solidFill><a:schemeClr val="phClr"/></a:solidFill></a:bgFillStyleLst></a:fmtScheme>
        </a:themeElements></a:theme>
        """;
}
