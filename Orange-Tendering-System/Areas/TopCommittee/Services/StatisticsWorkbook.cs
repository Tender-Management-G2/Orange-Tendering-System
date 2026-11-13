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
        using var output = new MemoryStream();
        using (var archive = new ZipArchive(output, ZipArchiveMode.Create, true))
        {
            Write(archive, "[Content_Types].xml", writer =>
            {
                writer.WriteStartElement("Types", "http://schemas.openxmlformats.org/package/2006/content-types");
                Empty(writer, "Default", ("Extension", "rels"), ("ContentType", "application/vnd.openxmlformats-package.relationships+xml"));
                Empty(writer, "Default", ("Extension", "xml"), ("ContentType", "application/xml"));
                Empty(writer, "Override", ("PartName", "/xl/workbook.xml"), ("ContentType", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"));
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
                writer.WriteEndElement();
            });
            for (var i = 0; i < sheets.Count; i++)
            {
                var rows = sheets[i].Rows;
                Write(archive, $"xl/worksheets/sheet{i + 1}.xml", writer =>
                {
                    writer.WriteStartElement("worksheet", "http://schemas.openxmlformats.org/spreadsheetml/2006/main"); writer.WriteStartElement("sheetData");
                    foreach (var row in rows)
                    {
                        writer.WriteStartElement("row");
                        foreach (var cell in row)
                        {
                            writer.WriteStartElement("c");
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
