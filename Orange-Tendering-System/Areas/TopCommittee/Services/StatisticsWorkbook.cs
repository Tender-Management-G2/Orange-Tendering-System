using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Xml;
using Orange_Tendering_System.Areas.TopCommittee.ViewModels;

namespace Orange_Tendering_System.Areas.TopCommittee.Services;

// Minimal genuine Open XML workbook. Text cells are inline strings, never executable formulas.
public static class StatisticsWorkbook
{
    public static byte[] Create(ManagementStatistics report)
    {
        var sheets = new List<(string Name, List<object?[]> Rows)>
        {
            ("Overview", [ ["Metric", "Value"], ["Generated (UTC)", CommitteeDisplay.Date(report.GeneratedAt)],
                ["Total suppliers", report.Suppliers], ["Approved suppliers", report.ApprovedSuppliers],
                ["Total tenders", report.Tenders], ["Active tenders", report.ActiveTenders],
                ["Awarded tenders", report.AwardedTenders], ["Completed requests (awarded tender)", report.CompletedRequests] ]),
            ("Suppliers", [["Supplier", "Status", "Invitations", "Submitted offers (distinct invitations)", "Times awarded"]]),
            ("Award values", [["Supplier", "Currency", "Awarded value"]]),
            ("Currency totals", [["Currency", "Awarded value"]]),
            ("Tender statuses", [["Status", "Count"]])
        };
        foreach (var supplier in report.SupplierRows)
        {
            sheets[1].Rows.Add([supplier.Name, supplier.Status.ToString(), supplier.Invitations, supplier.SubmittedOffers, supplier.Awards]);
            foreach (var value in supplier.Values) sheets[2].Rows.Add([supplier.Name, value.Currency, value.Amount]);
        }
        foreach (var value in report.Values) sheets[3].Rows.Add([value.Currency, value.Amount]);
        foreach (var status in report.Distribution) sheets[4].Rows.Add([status.Key.ToString(), status.Value]);
        return CreateSheets(sheets);
    }

    public static byte[] CreateSheets(IReadOnlyList<(string Name, List<object?[]> Rows)> sheets)
    {
        using var output = new MemoryStream();
        using (var archive = new ZipArchive(output, ZipArchiveMode.Create, true))
        {
            Write(archive, "[Content_Types].xml", writer =>
            {
                writer.WriteStartElement("Types", "http://schemas.openxmlformats.org/package/2006/content-types");
                Empty(writer, "Default", ("Extension", "rels"), ("ContentType", "application/vnd.openxmlformats-package.relationships+xml"));
                Empty(writer, "Default", ("Extension", "xml"), ("ContentType", "application/xml"));
                Empty(writer, "Override", ("PartName", "/xl/workbook.xml"), ("ContentType", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"));
                Empty(writer, "Override", ("PartName", "/xl/styles.xml"), ("ContentType", "application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml"));
                for (var i = 0; i < sheets.Count; i++) Empty(writer, "Override", ("PartName", $"/xl/worksheets/sheet{i + 1}.xml"), ("ContentType", "application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"));
                writer.WriteEndElement();
            });
            Write(archive, "_rels/.rels", writer =>
            {
                writer.WriteStartElement("Relationships", "http://schemas.openxmlformats.org/package/2006/relationships");
                Empty(writer, "Relationship", ("Id", "rId1"), ("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument"), ("Target", "xl/workbook.xml")); writer.WriteEndElement();
            });
            Write(archive, "xl/workbook.xml", writer =>
            {
                writer.WriteStartElement("workbook", "http://schemas.openxmlformats.org/spreadsheetml/2006/main");
                writer.WriteAttributeString("xmlns", "r", null, "http://schemas.openxmlformats.org/officeDocument/2006/relationships");
                writer.WriteStartElement("sheets");
                for (var i = 0; i < sheets.Count; i++)
                {
                    writer.WriteStartElement("sheet"); writer.WriteAttributeString("name", sheets[i].Name);
                    writer.WriteAttributeString("sheetId", (i + 1).ToString());
                    writer.WriteAttributeString("r", "id", "http://schemas.openxmlformats.org/officeDocument/2006/relationships", $"rId{i + 1}"); writer.WriteEndElement();
                }
                writer.WriteEndElement(); writer.WriteEndElement();
            });
            Write(archive, "xl/_rels/workbook.xml.rels", writer =>
            {
                writer.WriteStartElement("Relationships", "http://schemas.openxmlformats.org/package/2006/relationships");
                for (var i = 0; i < sheets.Count; i++) Empty(writer, "Relationship", ("Id", $"rId{i + 1}"), ("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet"), ("Target", $"worksheets/sheet{i + 1}.xml"));
                Empty(writer, "Relationship", ("Id", "rStyles"), ("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles"), ("Target", "styles.xml"));
                writer.WriteEndElement();
            });
            Write(archive, "xl/styles.xml", writer => System.Xml.Linq.XElement.Parse("<styleSheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><fonts count=\"2\"><font><sz val=\"11\"/><name val=\"Calibri\"/></font><font><b/><sz val=\"11\"/><color rgb=\"FFFFFFFF\"/><name val=\"Calibri\"/></font></fonts><fills count=\"3\"><fill><patternFill patternType=\"none\"/></fill><fill><patternFill patternType=\"gray125\"/></fill><fill><patternFill patternType=\"solid\"><fgColor rgb=\"FFFF7900\"/><bgColor indexed=\"64\"/></patternFill></fill></fills><borders count=\"1\"><border><left/><right/><top/><bottom style=\"thin\"><color rgb=\"FFE1E5ED\"/></bottom><diagonal/></border></borders><cellStyleXfs count=\"1\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\"/></cellStyleXfs><cellXfs count=\"3\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\" applyAlignment=\"1\"><alignment vertical=\"center\" wrapText=\"1\"/></xf><xf numFmtId=\"0\" fontId=\"1\" fillId=\"2\" borderId=\"0\" applyFont=\"1\" applyFill=\"1\" applyAlignment=\"1\"><alignment vertical=\"center\" wrapText=\"1\"/></xf><xf numFmtId=\"4\" fontId=\"0\" fillId=\"0\" borderId=\"0\" applyNumberFormat=\"1\"><alignment vertical=\"center\"/></xf></cellXfs><cellStyles count=\"1\"><cellStyle name=\"Normal\" xfId=\"0\" builtinId=\"0\"/></cellStyles></styleSheet>").WriteTo(writer));
            for (var i = 0; i < sheets.Count; i++)
            {
                var rows = sheets[i].Rows;
                Write(archive, $"xl/worksheets/sheet{i + 1}.xml", writer =>
                {
                    writer.WriteStartElement("worksheet", "http://schemas.openxmlformats.org/spreadsheetml/2006/main");
                    writer.WriteStartElement("sheetViews"); writer.WriteStartElement("sheetView"); writer.WriteAttributeString("workbookViewId", "0");
                    Empty(writer, "pane", ("ySplit", "1"), ("topLeftCell", "A2"), ("activePane", "bottomLeft"), ("state", "frozen")); writer.WriteEndElement(); writer.WriteEndElement();
                    writer.WriteStartElement("cols"); Empty(writer, "col", ("min", "1"), ("max", "20"), ("width", "28"), ("customWidth", "1")); writer.WriteEndElement();
                    writer.WriteStartElement("sheetData");
                    var rowNumber = 0;
                    foreach (var row in rows)
                    {
                        rowNumber++;
                        writer.WriteStartElement("row"); writer.WriteAttributeString("r", rowNumber.ToString());
                        writer.WriteAttributeString("ht", Math.Min(120, Math.Max(rowNumber == 1 ? 32 : 22, (int)Math.Ceiling(row.Select(v => v?.ToString()?.Length ?? 0).DefaultIfEmpty().Max() / 28d) * 15 + 8)).ToString()); writer.WriteAttributeString("customHeight", "1");
                        foreach (var cell in row)
                        {
                            writer.WriteStartElement("c");
                            writer.WriteAttributeString("s", rowNumber == 1 ? "1" : cell is decimal ? "2" : "0");
                            if (cell is int or decimal)
                                writer.WriteElementString("v", Convert.ToString(cell, CultureInfo.InvariantCulture));
                            else
                            {
                                writer.WriteAttributeString("t", "inlineStr"); writer.WriteStartElement("is");
                                writer.WriteElementString("t", Clean(cell?.ToString() ?? "")); writer.WriteEndElement();
                            }
                            writer.WriteEndElement();
                        }
                        writer.WriteEndElement();
                    }
                    writer.WriteEndElement(); writer.WriteEndElement();
                });
            }
        }
        return output.ToArray();
    }
    private static string Clean(string text) => new(text.Where(XmlConvert.IsXmlChar).ToArray());
    private static void Empty(XmlWriter writer, string name, params (string Name, string Value)[] attributes)
    { writer.WriteStartElement(name); foreach (var attribute in attributes) writer.WriteAttributeString(attribute.Name, attribute.Value); writer.WriteEndElement(); }
    private static void Write(ZipArchive archive, string name, Action<XmlWriter> body)
    {
        using var stream = archive.CreateEntry(name).Open();
        using var writer = XmlWriter.Create(stream, new XmlWriterSettings { Encoding = new UTF8Encoding(false), CloseOutput = false });
        writer.WriteStartDocument(); body(writer); writer.WriteEndDocument();
    }
}
