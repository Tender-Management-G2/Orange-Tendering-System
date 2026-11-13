using System.Diagnostics;
using System.Text;

namespace Orange_Tendering_System.Areas.TopCommittee.Services;

public static class BrowserReportPdf
{
    public static async Task<byte[]> Create(string html, string contentRoot, CancellationToken cancellationToken)
    {
        var browser = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Microsoft", "Edge", "Application", "msedge.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Microsoft", "Edge", "Application", "msedge.exe")
        }.FirstOrDefault(File.Exists) ?? throw new InvalidOperationException("PDF export requires the installed Microsoft Edge browser on this Windows server.");
        var root = Path.GetFullPath(Path.Combine(contentRoot, "Areas", "TopCommittee", ".exports"));
        var job = Path.GetFullPath(Path.Combine(root, Guid.NewGuid().ToString("N")));
        if (!job.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Invalid report workspace.");
        Directory.CreateDirectory(job);
        try
        {
            var source = Path.Combine(job, "report.html");
            var pdf = Path.Combine(job, "report.pdf");
            await File.WriteAllTextAsync(source, html, new UTF8Encoding(false), cancellationToken);
            var start = new ProcessStartInfo(browser) { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden, RedirectStandardError = true, RedirectStandardOutput = true };
            foreach (var argument in new[] { "--headless=new", "--disable-gpu", "--disable-extensions", "--disable-background-networking", "--no-first-run", "--no-default-browser-check", "--no-pdf-header-footer", "--user-data-dir=" + Path.Combine(job, "profile"), "--print-to-pdf=" + pdf, new Uri(source).AbsoluteUri }) start.ArgumentList.Add(argument);
            using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start the PDF renderer.");
            var stderr = process.StandardError.ReadToEndAsync(cancellationToken);
            var stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(60));
            try { await process.WaitForExitAsync(timeout.Token); await Task.WhenAll(stderr, stdout); }
            catch (OperationCanceledException)
            {
                if (!process.HasExited) process.Kill(entireProcessTree: true);
                throw new InvalidOperationException("The PDF renderer timed out. Please try again.");
            }
            if (process.ExitCode != 0 || !File.Exists(pdf)) throw new InvalidOperationException("PDF export could not be completed by the local renderer.");
            var bytes = await File.ReadAllBytesAsync(pdf, cancellationToken);
            if (bytes.Length < 5 || !bytes.AsSpan(0, 5).SequenceEqual("%PDF-"u8)) throw new InvalidOperationException("The renderer did not return a valid PDF.");
            return bytes;
        }
        finally
        {
            for (var attempt = 0; attempt < 3; attempt++)
            {
                try { if (Directory.Exists(job)) Directory.Delete(job, true); break; }
                catch (IOException) { await Task.Delay(100); }
                catch (UnauthorizedAccessException) { await Task.Delay(100); }
            }
        }
    }
}
