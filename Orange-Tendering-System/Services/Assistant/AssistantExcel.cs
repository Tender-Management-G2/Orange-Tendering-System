using ClosedXML.Excel;

namespace Orange_Tendering_System.Services.Assistant;

public sealed record ReportMeta(string Title, string GeneratedBy, string Role, DateTime GeneratedAtUtc, bool Arabic, string ScopeNote)
{
    public string L(string en, string ar) => Arabic ? ar : en;
    public string Generated => GeneratedAtUtc.ToJordan("yyyy-MM-dd HH:mm") + (Arabic ? " (بتوقيت الأردن)" : " (Jordan time)");
}

internal static class OrangeBrand
{
    public const string Orange = "FF7900", Dark = "1F1F1F", Grey = "6E6E6E", Light = "FFF1E5", Rule = "E1E1E1";
    public const string Company = "Orange · Sourcing & Tender Management";
    public const string CompanyAr = "أورنج · نظام إدارة التوريد والمناقصات";
    public static string Cell(object? v, ColumnKind kind) => v switch
    {
        null => "—",
        DateTime d => kind == ColumnKind.Date ? d.ToJordan("yyyy-MM-dd") : d.ToJordan("yyyy-MM-dd HH:mm"),
        decimal m => m.ToString("N2"),
        double x => x % 1 == 0 ? x.ToString("N0") : x.ToString("N1"),
        _ => v.ToString() ?? ""
    };
}

// Real .xlsx output through ClosedXML (MIT). Values are written as typed cells (text is never interpreted as a formula).
public static class AssistantExcel
{
    public static byte[] BuildText(ReportMeta meta, IReadOnlyList<(string Heading, string Body)> sections)
    {
        var data = new AssistantDataset { Topic = AssistantTopic.None, Title = meta.Title, Arabic = meta.Arabic, ScopeNote = meta.ScopeNote };
        data.Columns.Add(new(meta.L("Section", "القسم"), Width: 24));
        data.Columns.Add(new(meta.L("Analysis", "التحليل"), Width: 72));
        foreach (var (heading, body) in sections) data.Rows.Add([heading, body]);
        data.TotalCount = data.Rows.Count;
        return Build(data, meta);
    }

    public static byte[] Build(AssistantDataset data, ReportMeta meta)
    {
        using var book = new XLWorkbook();
        book.Properties.Title = meta.Title; book.Properties.Author = meta.GeneratedBy; book.Properties.Company = "Orange";
        var sheet = book.Worksheets.Add(meta.L("Report", "التقرير"));
        sheet.RightToLeft = meta.Arabic;
        var columns = Math.Max(data.Columns.Count, 2);

        var title = sheet.Range(1, 1, 1, columns).Merge();
        title.Value = meta.Title;
        title.Style.Font.SetBold().Font.SetFontSize(16).Font.SetFontColor(XLColor.White).Fill.SetBackgroundColor(XLColor.FromHtml("#" + OrangeBrand.Orange));
        title.Style.Alignment.SetVertical(XLAlignmentVerticalValues.Center);
        sheet.Row(1).Height = 30;
        var sub = sheet.Range(2, 1, 2, columns).Merge();
        sub.Value = $"{meta.L(OrangeBrand.Company, OrangeBrand.CompanyAr)} · {meta.L("Generated", "تاريخ الإنشاء")}: {meta.Generated} · {meta.L("By", "بواسطة")}: {meta.GeneratedBy} ({meta.Role})";
        sub.Style.Font.SetFontColor(XLColor.FromHtml("#" + OrangeBrand.Grey)).Font.SetFontSize(9);
        var scope = sheet.Range(3, 1, 3, columns).Merge();
        scope.Value = $"{meta.L("Scope", "النطاق")}: {meta.ScopeNote} · {meta.L("Records", "عدد السجلات")}: {data.Rows.Count}{(data.Truncated ? meta.L($" (first {data.Rows.Count} of {data.TotalCount})", $" (أول {data.Rows.Count} من {data.TotalCount})") : "")}";
        scope.Style.Font.SetFontColor(XLColor.FromHtml("#" + OrangeBrand.Grey)).Font.SetFontSize(9);

        const int headerRow = 5;
        for (var c = 0; c < data.Columns.Count; c++)
        {
            var cell = sheet.Cell(headerRow, c + 1);
            cell.Value = data.Columns[c].Header;
            cell.Style.Font.SetBold().Font.SetFontColor(XLColor.White).Fill.SetBackgroundColor(XLColor.FromHtml("#" + OrangeBrand.Dark));
            cell.Style.Alignment.SetWrapText(true).Alignment.SetVertical(XLAlignmentVerticalValues.Center);
        }
        sheet.Row(headerRow).Height = 28;
        for (var r = 0; r < data.Rows.Count; r++)
            for (var c = 0; c < data.Columns.Count; c++)
                Write(sheet.Cell(headerRow + 1 + r, c + 1), data.Rows[r].ElementAtOrDefault(c), data.Columns[c].Kind);

        if (data.Rows.Count > 0)
        {
            var body = sheet.Range(headerRow, 1, headerRow + data.Rows.Count, data.Columns.Count);
            body.SetAutoFilter();
            body.Style.Border.SetInsideBorder(XLBorderStyleValues.Hair).Border.SetOutsideBorder(XLBorderStyleValues.Thin);
            body.Style.Border.SetInsideBorderColor(XLColor.FromHtml("#" + OrangeBrand.Rule)).Border.SetOutsideBorderColor(XLColor.FromHtml("#" + OrangeBrand.Rule));
            for (var r = headerRow + 2; r <= headerRow + data.Rows.Count; r += 2) sheet.Range(r, 1, r, data.Columns.Count).Style.Fill.SetBackgroundColor(XLColor.FromHtml("#FAFAFA"));
        }
        else sheet.Cell(headerRow + 1, 1).Value = data.Empty ?? meta.L("No records match this request in your authorized scope.", "لا توجد سجلات مطابقة ضمن نطاق صلاحياتك.");
        sheet.SheetView.FreezeRows(headerRow);
        for (var c = 0; c < data.Columns.Count; c++)
        {
            var column = sheet.Column(c + 1);
            column.AdjustToContents(headerRow, Math.Min(headerRow + data.Rows.Count, headerRow + 300));
            var preferred = data.Columns[c].Width > 0 ? data.Columns[c].Width : 14;
            column.Width = Math.Clamp(Math.Max(column.Width, preferred * 0.8), 8, 60);
            if (data.Columns[c].Kind == ColumnKind.Text) column.Style.Alignment.SetWrapText(column.Width >= 40);
        }
        sheet.PageSetup.SetPageOrientation(data.Columns.Count > 6 ? XLPageOrientation.Landscape : XLPageOrientation.Portrait);
        sheet.PageSetup.FitToPages(1, 0);
        sheet.PageSetup.SetRowsToRepeatAtTop(headerRow, headerRow);
        var footer = sheet.PageSetup.Footer.Center;
        footer.AddText(meta.L("Page ", "صفحة ")); footer.AddText(XLHFPredefinedText.PageNumber); footer.AddText(" / "); footer.AddText(XLHFPredefinedText.NumberOfPages);

        if (data.Summary.Count > 0 || data.Chart.Count > 0)
        {
            var s = book.Worksheets.Add(meta.L("Summary", "الملخص"));
            s.RightToLeft = meta.Arabic;
            s.Cell(1, 1).Value = meta.Title; s.Cell(1, 1).Style.Font.SetBold().Font.SetFontSize(14).Font.SetFontColor(XLColor.FromHtml("#" + OrangeBrand.Orange));
            s.Cell(2, 1).Value = meta.Generated; s.Cell(2, 1).Style.Font.SetFontColor(XLColor.FromHtml("#" + OrangeBrand.Grey));
            var row = 4;
            foreach (var (label, value) in data.Summary) { s.Cell(row, 1).Value = label; Write(s.Cell(row, 2), value, ColumnKind.Text); s.Cell(row, 1).Style.Font.SetBold(); row++; }
            if (data.Chart.Count > 0)
            {
                row++;
                s.Cell(row, 1).Value = data.ChartTitle ?? meta.L("Breakdown", "التوزيع");
                s.Cell(row, 2).Value = meta.L("Value", "القيمة");
                s.Range(row, 1, row, 2).Style.Font.SetBold().Font.SetFontColor(XLColor.White).Fill.SetBackgroundColor(XLColor.FromHtml("#" + OrangeBrand.Dark));
                foreach (var (label, value) in data.Chart) { row++; s.Cell(row, 1).Value = label; s.Cell(row, 2).Value = value; s.Cell(row, 2).Style.NumberFormat.Format = "#,##0.##"; }
            }
            s.Column(1).Width = 48; s.Column(2).Width = 24;
        }
        using var output = new MemoryStream();
        book.SaveAs(output);
        return output.ToArray();
    }

    private static void Write(IXLCell cell, object? value, ColumnKind kind)
    {
        switch (value)
        {
            case null: cell.Value = Blank.Value; break;
            case DateTime d:
                cell.Value = d.ToJordan();
                cell.Style.NumberFormat.Format = kind == ColumnKind.Date ? "yyyy-mm-dd" : "yyyy-mm-dd hh:mm";
                break;
            case decimal m: cell.Value = m; cell.Style.NumberFormat.Format = "#,##0.00"; break;
            case int i: cell.Value = i; cell.Style.NumberFormat.Format = "#,##0"; break;
            case long l: cell.Value = l; cell.Style.NumberFormat.Format = "#,##0"; break;
            case double x: cell.Value = x; cell.Style.NumberFormat.Format = kind == ColumnKind.Money ? "#,##0.00" : "#,##0.##"; break;
            default:
                var text = value.ToString() ?? "";
                if (text.Length > 32000) text = text[..32000];
                cell.Value = text;   // a string value is stored as text, never as a formula
                if (text.Length > 0 && text[0] is '=' or '+' or '-' or '@' or '\t' or '\r') cell.Style.IncludeQuotePrefix = true;
                break;
        }
    }
}
