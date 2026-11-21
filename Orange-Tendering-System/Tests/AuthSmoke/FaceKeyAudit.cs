using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.SqlClient;

/// <summary>
/// Read-only check: which Data Protection scope can open the stored face templates of a database.
/// Prints counts per scope only (no ids, no template data). Usage: AUTH_AUDIT_CONNECTION=... AuthSmoke --face-key-audit
/// </summary>
static class FaceKeyAudit
{
    public static async Task Run()
    {
        var connection = Environment.GetEnvironmentVariable("AUTH_AUDIT_CONNECTION")
            ?? throw new InvalidOperationException("Set AUTH_AUDIT_CONNECTION.");
        var keys = new DirectoryInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ASP.NET", "DataProtection-Keys"));
        var repositories = Environment.GetEnvironmentVariable("AUTH_AUDIT_REPOS") ?? "";
        var scopes = new List<string> { "Orange-Tendering-System" };
        foreach (var repo in Directory.GetDirectories(repositories))
        {
            var root = Path.Combine(repo, "Orange-Tendering-System");
            if (Directory.Exists(root)) scopes.AddRange([root, root + Path.DirectorySeparatorChar]);
        }
        var protectors = scopes.Select(s => (Scope: s, P: DataProtectionProvider.Create(keys, b => b.SetApplicationName(s)).CreateProtector("Orange.Biometric.FaceTemplate.v1"))).ToList();

        var blobs = new List<byte[]>();
        await using (var sql = new SqlConnection(connection))
        {
            await sql.OpenAsync();
            await using var command = new SqlCommand("SELECT ProtectedTemplate FROM FaceTemplates", sql);
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync()) blobs.Add((byte[])reader[0]);
        }
        Console.WriteLine($"templates: {blobs.Count}");
        var unreadable = 0;
        foreach (var blob in blobs)
        {
            var hit = protectors.FirstOrDefault(p => { try { p.P.Unprotect(blob); return true; } catch { return false; } });
            if (hit.P is null) unreadable++; else Console.WriteLine("readable with scope: " + hit.Scope);
        }
        Console.WriteLine($"unreadable under every scope: {unreadable}");
    }
}
