using Microsoft.EntityFrameworkCore;
using Orange_Tendering_System.Data;
using Orange_Tendering_System.Services.Reports;
using PdfSharp.Drawing;
using PdfSharp.Pdf.IO;

namespace Orange_Tendering_System.Services;

/// <summary>What a personalized watermark says: classification, who, when, which tender and which version.</summary>
public sealed record WatermarkStamp(string Identity, DateTime AtUtc, string TenderReference, string? Version, string Purpose, string Classification = DocumentWatermarkService.Classification)
{
    public string Line1 => Classification.ToUpperInvariant();
    public string Line2 => Identity;
    public string Line3 => string.Join(" · ", new[] { TenderReference, Version, Purpose + " " + AtUtc.ToJordan("yyyy-MM-dd HH:mm") + " " + JordanTime.Label }.Where(x => !string.IsNullOrWhiteSpace(x)));
}

public sealed record WatermarkResult(byte[] Bytes, bool Applied);

/// <summary>
/// Server-side personalized watermark for PDF copies served to a user. It always works on an in-memory copy:
/// stored files and the canonical (hashed, signed) report bytes are never modified. This is a visual marking only and
/// is not a digital signature. Non-PDF files and PDFs that cannot be opened (for example encrypted ones) are returned
/// unchanged with Applied = false so the caller can record that fact in the audit trail.
/// </summary>
public sealed class DocumentWatermarkService(ApplicationDbContext db, OrangeReportRenderer fonts, ILogger<DocumentWatermarkService> log)
{
    public const string Classification = "Orange Restricted";
    // The renderer registers the PDF font resolver when it is constructed; keep the dependency so watermarking never runs before it.
    private readonly OrangeReportRenderer _fonts = fonts;

    public static bool IsPdf(ReadOnlySpan<byte> bytes) => bytes.Length > 5 && bytes[..5].SequenceEqual("%PDF-"u8);

    /// <summary>"Full Name &lt;login&gt;" of the authenticated user, as stored in Identity.</summary>
    public async Task<string> IdentityAsync(string userId)
    {
        var user = await db.Users.AsNoTracking().Where(x => x.Id == userId).Select(x => new { x.FullName, x.Email, x.UserName }).SingleOrDefaultAsync();
        if (user == null) return "Unknown user";
        var login = user.Email ?? user.UserName ?? "";
        var name = string.IsNullOrWhiteSpace(user.FullName) ? login : user.FullName.Trim();
        var text = string.IsNullOrEmpty(login) || name == login ? name : $"{name} <{login}>";
        return text.Length > 70 ? text[..70] + "…" : text;
    }

    /// <param name="underlay">True draws beneath the page content (used for generated reports, so the stamp never covers figures or signatures);
    /// false draws a translucent overlay (used for uploaded documents, which may be full-page scans).</param>
    public WatermarkResult Apply(byte[] pdf, WatermarkStamp stamp, bool underlay = false)
    {
        if (!IsPdf(pdf)) return new(pdf, false);
        try
        {
            using var input = new MemoryStream(pdf);
            using var document = PdfReader.Open(input, PdfDocumentOpenMode.Modify);
            document.Info.Keywords = $"{stamp.Line1}; {stamp.Line2}; {stamp.Line3}";
            var orange = XColor.FromArgb(underlay ? 46 : 38, 255, 121, 0);
            var ink = XColor.FromArgb(underlay ? 70 : 60, 90, 40, 0);
            foreach (var page in document.Pages)
            {
                using var g = XGraphics.FromPdfPage(page, underlay ? XGraphicsPdfPageOptions.Prepend : XGraphicsPdfPageOptions.Append);
                double w = page.Width.Point, h = page.Height.Point;
                var size = Math.Clamp(Math.Min(w, h) / 9.5, 26, 64);
                g.Save();
                g.TranslateTransform(w / 2, h / 2);
                g.RotateTransform(-Math.Atan2(h, w) * 180 / Math.PI);
                var big = new XFont("Orange Sans", size, XFontStyleEx.Bold);
                var small = new XFont("Orange Sans", size * .3);
                var boxW = Math.Max(g.MeasureString(stamp.Line1, big).Width, Math.Max(g.MeasureString(stamp.Line2, small).Width, g.MeasureString(stamp.Line3, small).Width)) + size * .8;
                var boxH = size * 2.25;
                g.DrawRoundedRectangle(new XPen(orange, Math.Max(1.5, size / 16)), -boxW / 2, -boxH / 2, boxW, boxH, size / 3, size / 3);
                g.DrawString(stamp.Line1, big, new XSolidBrush(orange), new XRect(-boxW / 2, -boxH / 2 + size * .12, boxW, size * 1.1), XStringFormats.Center);
                g.DrawString(stamp.Line2, small, new XSolidBrush(ink), new XRect(-boxW / 2, size * .28, boxW, size * .36), XStringFormats.Center);
                g.DrawString(stamp.Line3, small, new XSolidBrush(ink), new XRect(-boxW / 2, size * .66, boxW, size * .36), XStringFormats.Center);
                g.Restore();
                // A small, always-legible footer line so a cropped or printed page still names its viewer.
                // Generated reports already carry their own personalized margin and footer stamp (OrangeReportRenderer.AccessCopy).
                if (!underlay) g.DrawString($"{stamp.Line1} · {stamp.Line2} · {stamp.Line3}", new XFont("Orange Sans", 6), new XSolidBrush(XColor.FromArgb(170, 110, 110, 110)), new XRect(24, h - 14, w - 48, 10), XStringFormats.Center);
            }
            using var output = new MemoryStream();
            document.Save(output, false);
            return new(output.ToArray(), true);
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            log.LogWarning(error, "A PDF could not be watermarked ({Reference}); the unmodified copy was served and the audit records Watermarked = false.", stamp.TenderReference);
            return new(pdf, false);
        }
    }
}
