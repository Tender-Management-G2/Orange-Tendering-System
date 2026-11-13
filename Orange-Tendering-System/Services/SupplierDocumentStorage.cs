namespace Orange_Tendering_System.Services;

public static class SupplierDocumentStorage
{
    private const long MaxBytes = 10 * 1024 * 1024;

    public static bool IsAllowed(IFormFile file)
    {
        if (file.Length is <= 0 or > MaxBytes || Path.GetFileName(file.FileName).Length > 260)
            return false;
        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (ext is not (".pdf" or ".png" or ".jpg" or ".jpeg")) return false;
        using var stream = file.OpenReadStream();
        Span<byte> header = stackalloc byte[8];
        try { stream.ReadExactly(header); }
        catch (EndOfStreamException) { return false; }
        return ContentType(header, ext) != null;
    }

    public static async Task<(string Path, string ContentType)> SaveAsync(IFormFile file, string contentRoot)
    {
        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        await using var input = file.OpenReadStream();
        var header = new byte[8];
        await input.ReadExactlyAsync(header);
        var type = ContentType(header, ext) ?? throw new InvalidOperationException("Unsupported document format.");
        var directory = Path.Combine(contentRoot, "App_Data", "SupplierDocuments");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, Guid.NewGuid().ToString("N") + ext);
        try
        {
            await using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            await output.WriteAsync(header);
            await input.CopyToAsync(output);
        }
        catch
        {
            File.Delete(path);
            throw;
        }
        return (path, type);
    }

    private static string? ContentType(ReadOnlySpan<byte> header, string ext)
    {
        if (ext == ".pdf" && header[..5].SequenceEqual("%PDF-"u8)) return "application/pdf";
        if (ext == ".png" && header.SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 })) return "image/png";
        if (ext is ".jpg" or ".jpeg" && header[..3].SequenceEqual(new byte[] { 255, 216, 255 })) return "image/jpeg";
        return null;
    }
}
