using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Orange_Tendering_System;
using Orange_Tendering_System.Data;
using Orange_Tendering_System.Models;
using Orange_Tendering_System.Models.Entities;
using Orange_Tendering_System.Services;

/// <summary>
/// Team account integration checks (real SQL Server, provisioning ENABLED, the real Program startup).
/// Every scenario uses its own new database named OrangeTenderingTeamDev_Check_* (and two non-team names for the
/// isolation checks) on the server of TEAM_TEST_SERVER (default "localhost"); the databases it created are dropped at
/// the end. Run: dotnet Tests/AuthSmoke/bin/Release/net10.0/AuthSmoke.dll --team-account-checks  (from the web project)
/// </summary>
static class TeamAccountChecks
{
    private static readonly string Server = Environment.GetEnvironmentVariable("TEAM_TEST_SERVER") ?? "localhost";
    private static readonly string Suffix = Convert.ToHexString(RandomNumberGenerator.GetBytes(4)).ToLowerInvariant();
    private static readonly List<string> Created = [];
    private static int passed;

    private static readonly Dictionary<string, string> Workspace = new()
    {
        ["department@orange-demo.local"] = "/Department",
        ["sourcing.officer@orange-demo.local"] = "/SourcingOfficer",
        ["sourcing.manager@orange-demo.local"] = "/SourcingManager",
        ["evaluation.technical@orange-demo.local"] = "/EvaluationCommittee",
        ["evaluation.financial@orange-demo.local"] = "/EvaluationCommittee",
        ["evaluation.department@orange-demo.local"] = "/EvaluationCommittee",
        ["top.ceo@orange-demo.local"] = "/TopCommittee",
        ["top.cfo@orange-demo.local"] = "/TopCommittee",
        ["top.cto@orange-demo.local"] = "/TopCommittee",
        ["top.coo@orange-demo.local"] = "/TopCommittee"
    };

    public static async Task Run()
    {
        var root = Environment.GetEnvironmentVariable("AUTH_PROJECT_ROOT") ?? Directory.GetCurrentDirectory();
        try
        {
            await Scenarios(root);
            Console.WriteLine($"Team account checks passed ({passed}).");
        }
        finally
        {
            foreach (var name in Created) await Drop(name);
        }
    }

    private static async Task Scenarios(string root)
    {
        // 1. Fresh database, real HTTP login for all ten accounts (form and JS handler), workspace access.
        var main = Db("main");
        string[] ids;
        Dictionary<string, (string Hash, string Stamp)> before;
        using (var host = Host(root, main))
        {
            await LoginAll(host, "fresh database");
            (ids, before) = await Snapshot(host);
        }

        // 10. Repeated startup: same ids, no password reset, still able to sign in.
        using (var host = Host(root, main))
        {
            var (again, after) = await Snapshot(host);
            Check(ids.SequenceEqual(again) && before.All(kv => after[kv.Key] == kv.Value),
                "repeated startup keeps account ids, password hashes and security stamps");
            await LoginAll(host, "repeated startup");
        }

        // 3-8, 11. Damage the accounts, add a synthetic competing officer and unrelated data, restart.
        string unrelatedHash;
        using (var host = Host(root, main))
        {
            await WithUsers(host, async (users, db) =>
            {
                var officer = (await users.FindByEmailAsync("sourcing.officer@orange-demo.local"))!;
                var token = await users.GeneratePasswordResetTokenAsync(officer);
                Ok(await users.ResetPasswordAsync(officer, token, "Changed!2026x"));                       // 3 modified password
                var manager = (await users.FindByEmailAsync("sourcing.manager@orange-demo.local"))!;
                manager.IsActive = false; Ok(await users.UpdateAsync(manager));                               // 4 disabled
                var department = (await users.FindByEmailAsync("department@orange-demo.local"))!;
                department.EmailConfirmed = false; Ok(await users.UpdateAsync(department));                  // 5 unconfirmed
                var ceo = (await users.FindByEmailAsync("top.ceo@orange-demo.local"))!;
                Ok(await users.SetLockoutEndDateAsync(ceo, DateTimeOffset.UtcNow.AddDays(1)));               // 6 locked
                ceo.AccessFailedCount = 4; Ok(await users.UpdateAsync(ceo));
                var technical = (await users.FindByEmailAsync("evaluation.technical@orange-demo.local"))!;
                Ok(await users.RemoveFromRoleAsync(technical, SystemRoles.Technical));                       // 7 missing role
                var financial = (await users.FindByEmailAsync("evaluation.financial@orange-demo.local"))!;
                Ok(await users.RemoveClaimAsync(financial, new Claim(CommitteeAccountSeats.ClaimType, "evaluation-financial")));
                Ok(await users.AddClaimAsync(financial, new Claim(CommitteeAccountSeats.ClaimType, "top-ceo")));   // 7 wrong seat
                var synthetic = new ApplicationUser { UserName = "officer@orange-demo.local", Email = "officer@orange-demo.local", EmailConfirmed = true, FullName = "Demo Sourcing Officer", IsActive = true };
                Ok(await users.CreateAsync(synthetic, "Synthetic!2026x"));                                   // 8 competing synthetic officer
                Ok(await users.AddToRoleAsync(synthetic, SystemRoles.SourcingOfficer));
                var unrelated = new ApplicationUser { UserName = "unrelated@example.test", Email = "unrelated@example.test", EmailConfirmed = true, FullName = "Unrelated Person", IsActive = true };
                Ok(await users.CreateAsync(unrelated, "Unrelated!2026x"));                                   // 11 unrelated account
                Ok(await users.AddToRoleAsync(unrelated, SystemRoles.SourcingManager));
                db.AuditLogs.Add(new AuditLog { UserId = unrelated.Id, Action = "HistoricalCheck", EntityName = "ApplicationUser", EntityId = unrelated.Id, CreatedAt = DateTime.UtcNow.AddDays(-3) });
                await db.SaveChangesAsync();
            });
            unrelatedHash = await Hash(host, "unrelated@example.test");
        }
        using (var host = Host(root, main))
        {
            await WithUsers(host, async (users, db) =>
            {
                var officer = (await users.FindByEmailAsync("sourcing.officer@orange-demo.local"))!;
                Check(await users.CheckPasswordAsync(officer, TeamAccountSeeder.Password), "a modified team password is restored");
                Check((await users.FindByEmailAsync("sourcing.manager@orange-demo.local"))!.IsActive, "a disabled team account is reactivated");
                Check((await users.FindByEmailAsync("department@orange-demo.local"))!.EmailConfirmed, "an unconfirmed team account is confirmed");
                var ceo = (await users.FindByEmailAsync("top.ceo@orange-demo.local"))!;
                Check(!await users.IsLockedOutAsync(ceo) && ceo.AccessFailedCount == 0, "a locked team account is unlocked and its failures cleared");
                Check(await users.IsInRoleAsync((await users.FindByEmailAsync("evaluation.technical@orange-demo.local"))!, SystemRoles.Technical), "a missing team role is restored");
                var seats = (await users.GetClaimsAsync((await users.FindByEmailAsync("evaluation.financial@orange-demo.local"))!)).Where(c => c.Type == CommitteeAccountSeats.ClaimType).Select(c => c.Value).ToList();
                Check(seats.SequenceEqual(["evaluation-financial"]), "a wrong committee seat is replaced by the correct one");
                Check(!(await users.FindByEmailAsync("officer@orange-demo.local"))!.IsActive &&
                      (await users.GetUsersInRoleAsync(SystemRoles.SourcingOfficer)).Count(u => u.IsActive) == 1, "a competing synthetic officer is deactivated; exactly one officer is active");
                var unrelated = (await users.FindByEmailAsync("unrelated@example.test"))!;
                Check(unrelated.IsActive && unrelated.FullName == "Unrelated Person" && await users.IsInRoleAsync(unrelated, SystemRoles.SourcingManager) &&
                      await db.AuditLogs.AnyAsync(a => a.Action == "HistoricalCheck"), "unrelated accounts and historical rows are preserved");
                var (now, _) = await Snapshot(host);
                Check(ids.SequenceEqual(now), "repairs keep the original account ids");
            });
            Check(await Hash(host, "unrelated@example.test") == unrelatedHash, "an unrelated account's password is untouched");
            await LoginAll(host, "after repair");
        }

        // 2. Accounts made by the first TeamAccountSeeder (no ownership marker) are adopted, not duplicated.
        var legacy = Db("legacy");
        using (var host = Host(root, legacy, teamAccounts: false)) await host.StartAsync();
        string legacyOfficerId = "";
        using (var host = Host(root, legacy, teamAccounts: false))
            await WithUsers(host, async (users, db) =>
            {
                var department = new Department { Name = TeamAccountSeeder.DepartmentName, IsActive = true };
                db.Departments.Add(department);
                await db.SaveChangesAsync();
                foreach (var (email, name, role, seat) in new[]
                {
                    ("sourcing.officer@orange-demo.local", "Team Sourcing Officer", SystemRoles.SourcingOfficer, (string?)null),
                    ("sourcing.manager@orange-demo.local", "Team Sourcing Manager", SystemRoles.SourcingManager, null),
                    ("department@orange-demo.local", "Team Requesting Department", SystemRoles.RequestingDepartment, null),
                    ("top.ceo@orange-demo.local", "Team Top Committee CEO", SystemRoles.ChiefExecutiveOfficer, "top-ceo")
                })
                {
                    var user = new ApplicationUser { UserName = email, Email = email, EmailConfirmed = true, FullName = name, IsActive = true, DepartmentId = role == SystemRoles.RequestingDepartment ? department.Id : null };
                    Ok(await users.CreateAsync(user, TeamAccountSeeder.Password));
                    Ok(await users.AddToRolesAsync(user, SystemRoles.AccountRoles(role)));
                    if (seat != null) Ok(await users.AddClaimAsync(user, new Claim(CommitteeAccountSeats.ClaimType, seat)));
                    if (role == SystemRoles.SourcingOfficer) legacyOfficerId = user.Id;
                }
            });
        using (var host = Host(root, legacy))
        {
            await WithUsers(host, async (users, _) =>
            {
                var officer = (await users.FindByEmailAsync("sourcing.officer@orange-demo.local"))!;
                Check(officer.Id == legacyOfficerId && (await users.GetClaimsAsync(officer)).Any(c => c.Type == TeamAccountSeeder.MarkerClaimType),
                    "accounts from the previous team seeder are adopted with their ids");
            });
            await LoginAll(host, "previous seeder accounts");
        }

        // 8b. A real (non-synthetic) active officer is never deactivated: startup stops with an actionable error.
        var realOfficer = Db("realofficer");
        using (var host = Host(root, realOfficer)) await host.StartAsync();
        using (var host = Host(root, realOfficer, teamAccounts: false))
            await WithUsers(host, async (users, _) =>
            {
                var real = new ApplicationUser { UserName = "real.officer@example.test", Email = "real.officer@example.test", EmailConfirmed = true, FullName = "Real Officer", IsActive = true };
                Ok(await users.CreateAsync(real, "RealOfficer!2026x"));
                Ok(await users.AddToRoleAsync(real, SystemRoles.SourcingOfficer));
            });
        var realError = await StartFailure(root, realOfficer);
        using (var host = Host(root, realOfficer, teamAccounts: false))
            await WithUsers(host, async (users, _) =>
                Check(realError?.Contains("Another Sourcing Officer is active") == true && (await users.FindByEmailAsync("real.officer@example.test"))!.IsActive,
                    "a real active officer is not disabled; startup reports what to do"));

        // 13. A reserved email used by an unrelated account is never taken over.
        var collision = Db("collision");
        using (var host = Host(root, collision, teamAccounts: false)) await host.StartAsync();
        string collisionHash = "";
        using (var host = Host(root, collision, teamAccounts: false))
            await WithUsers(host, async (users, _) =>
            {
                var person = new ApplicationUser { UserName = "department@orange-demo.local", Email = "department@orange-demo.local", EmailConfirmed = true, FullName = "Real Person", IsActive = true };
                Ok(await users.CreateAsync(person, "RealPerson!2026x"));
                Ok(await users.AddToRoleAsync(person, SystemRoles.Supplier));
                collisionHash = person.PasswordHash!;
            });
        var collisionError = await StartFailure(root, collision);
        using (var host = Host(root, collision, teamAccounts: false))
            await WithUsers(host, async (users, _) =>
            {
                var person = (await users.FindByEmailAsync("department@orange-demo.local"))!;
                Check(collisionError?.Contains("reserved team email") == true && person.FullName == "Real Person" && person.PasswordHash == collisionHash &&
                      await users.IsInRoleAsync(person, SystemRoles.Supplier) && !await users.IsInRoleAsync(person, SystemRoles.RequestingDepartment),
                    "a reserved-email collision stops startup and leaves the other account untouched");
            });

        // 9. Demo fixtures use the canonical team accounts and create no competing employees.
        var demo = Db("demo");
        for (var start = 0; start < 2; start++)
            using (var host = Host(root, demo, demoData: true))
                await WithUsers(host, async (users, db) =>
                {
                    var officer = (await users.FindByEmailAsync("sourcing.officer@orange-demo.local"))!;
                    var officers = (await users.GetUsersInRoleAsync(SystemRoles.SourcingOfficer)).Where(u => u.IsActive).ToList();
                    var demoTenders = await db.Tenders.Where(t => t.Title.StartsWith("DEMO Tender - ")).ToListAsync();
                    var stray = await db.Users.CountAsync(u => u.Email == "officer@orange-demo.local" || u.Email == "manager@orange-demo.local" ||
                        u.Email == "requester@orange-demo.local" || u.Email!.StartsWith("committee") || u.Email!.StartsWith("top") && !u.Email.StartsWith("top."));
                    Check(officers.Count == 1 && officers[0].Id == officer.Id && demoTenders.Count > 0 && demoTenders.All(t => t.CreatedByUserId == officer.Id) && stray == 0,
                        $"demo data uses the team accounts and adds no competing employees (start {start + 1})");
                });
        using (var host = Host(root, demo, demoData: true)) await LoginAll(host, "demo data enabled");

        // 12. Production: nothing is provisioned, even on a team-named database.
        var production = Db("production");
        // Migrated by the real app (same Identity model), with team accounts switched off for that start.
        using (var host = Host(root, production, teamAccounts: false)) await host.StartAsync();
        using (var host = Host(root, production, environment: "Production"))
            await WithUsers(host, async (users, db) =>
                Check(await users.FindByEmailAsync("sourcing.officer@orange-demo.local") == null && !await db.Departments.AnyAsync(d => d.Name == TeamAccountSeeder.DepartmentName),
                    "Production creates no team accounts"));

        // 14. Isolation: the launch-profile setting selects the team database on the same server; non-team databases are left alone.
        var baseName = $"OrangeTenderingSystem_NotUsed_{Suffix}";
        var selected = Db("selected");
        using (var host = Host(root, $"{baseName}", teamDevDatabase: selected))
            await WithUsers(host, async (users, db) =>
                Check(db.Database.GetDbConnection().Database == selected && await users.FindByEmailAsync("top.coo@orange-demo.local") != null && !await Exists(baseName),
                    "TeamDev:Database selects the team database on the configured server and leaves the configured database untouched"));
        var nonTeam = $"OrangeTenderingNonTeam_Check_{Suffix}";
        Created.Add(nonTeam);
        using (var host = Host(root, nonTeam))
            await WithUsers(host, async (users, _) =>
                Check(await users.FindByEmailAsync("sourcing.officer@orange-demo.local") == null, "a non-team Development database gets no fixed-password accounts"));
        Check(TeamDevDatabase.IsLocalTeamDatabase($"Server=localhost;Database={TeamDevDatabase.DefaultName}") &&
              !TeamDevDatabase.IsLocalTeamDatabase($"Server=db.example.com;Database={TeamDevDatabase.DefaultName}") &&
              !TeamDevDatabase.IsLocalTeamDatabase("Server=localhost;Database=OrangeTenderingSystem"),
            "the fixed-password boundary needs a team database name on a local server");
    }

    // ---- HTTP sign-in through the real Login page (HTTPS, cookies, antiforgery) ----

    private static async Task LoginAll(WebApplicationFactory<WebAppMarker> host, string when)
    {
        foreach (var account in TeamAccountSeeder.Accounts)
        {
            var expected = Workspace[account.Email];
            // Regular form post.
            using (var client = Client(host))
            {
                var token = await Token(client);
                var response = await client.PostAsync("/Identity/Account/Login", new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["Input.Email"] = account.Email, ["Input.Password"] = TeamAccountSeeder.Password,
                    ["Input.AccountType"] = "other", ["__RequestVerificationToken"] = token
                }));
                var location = response.Headers.Location?.ToString() ?? "";
                Check(response.StatusCode == HttpStatusCode.Redirect && location.StartsWith(expected, StringComparison.OrdinalIgnoreCase) && await Opens(client, location),
                    $"{when}: {account.Email} signs in with the login form and opens {expected} (got {(int)response.StatusCode} {location})");
            }
            // The page script's credentials handler.
            using (var client = Client(host))
            {
                var token = await Token(client);
                var response = await client.PostAsync("/Identity/Account/Login?handler=Credentials", new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["Input.Email"] = account.Email, ["Input.Password"] = TeamAccountSeeder.Password,
                    ["Input.AccountType"] = "other", ["__RequestVerificationToken"] = token
                }));
                var body = response.StatusCode == HttpStatusCode.OK ? await response.Content.ReadFromJsonAsync<JsonElement>() : default;
                var redirect = body.ValueKind == JsonValueKind.Object && body.TryGetProperty("redirect", out var r) ? r.GetString() ?? "" : "";
                Check(body.ValueKind == JsonValueKind.Object && body.GetProperty("outcome").GetString() == "signedIn" &&
                      redirect.StartsWith(expected, StringComparison.OrdinalIgnoreCase) && await Opens(client, redirect),
                    $"{when}: {account.Email} signs in through the credentials handler and opens {expected}");
            }
        }
    }

    /// <summary>The workspace really opens for the signed-in cookie (following in-app redirects, never to the login page).</summary>
    private static async Task<bool> Opens(HttpClient client, string path)
    {
        for (var hop = 0; hop < 4; hop++)
        {
            var response = await client.GetAsync(path);
            if (response.StatusCode == HttpStatusCode.OK) return true;
            var next = response.Headers.Location?.ToString();
            if (response.StatusCode != HttpStatusCode.Redirect && response.StatusCode != HttpStatusCode.Found || next == null ||
                next.Contains("/Identity/Account/Login", StringComparison.OrdinalIgnoreCase) ||
                next.Contains("AccessDenied", StringComparison.OrdinalIgnoreCase)) return false;
            path = next;
        }
        return false;
    }

    private static async Task<string> Token(HttpClient client)
    {
        var html = await (await client.GetAsync("/Identity/Account/Login")).Content.ReadAsStringAsync();
        var match = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");
        if (!match.Success) throw new InvalidOperationException("FAILED: no antiforgery token on the login page");
        return WebUtility.HtmlDecode(match.Groups[1].Value);
    }

    private static HttpClient Client(WebApplicationFactory<WebAppMarker> host) =>
        host.CreateClient(new() { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false, HandleCookies = true });

    // ---- Hosts and databases ----

    private static string Db(string name)
    {
        var database = $"OrangeTenderingTeamDev_Check_{Suffix}_{name}";
        Created.Add(database);
        return database;
    }

    private static string Connection(string database) =>
        new SqlConnectionStringBuilder { DataSource = Server, InitialCatalog = database, IntegratedSecurity = true, TrustServerCertificate = true, MultipleActiveResultSets = true }.ConnectionString;

    private static WebApplicationFactory<WebAppMarker> Host(string root, string database, bool teamAccounts = true, bool demoData = false,
        string environment = "Development", string? teamDevDatabase = null)
    {
        // Program reads these while it builds the host, so they are set process-wide before each host.
        Environment.SetEnvironmentVariable("ConnectionStrings__Default Connection", Connection(database));
        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", environment);
        Environment.SetEnvironmentVariable("TeamDev__Database", teamDevDatabase);
        Environment.SetEnvironmentVariable("TeamAccounts__Enabled", teamAccounts ? null : "false");
        Environment.SetEnvironmentVariable("DemoData__Enabled", demoData ? "true" : null);
        Environment.SetEnvironmentVariable("BootstrapAdmin__Email", null);
        Environment.SetEnvironmentVariable("BootstrapAdmin__Password", null);
        var content = Path.Combine(Path.GetTempPath(), "OrangeTeamAccountCheck_" + Suffix);
        Directory.CreateDirectory(content);
        return new WebApplicationFactory<WebAppMarker>().WithWebHostBuilder(builder =>
        {
            builder.UseContentRoot(content).UseEnvironment(environment);
            builder.ConfigureAppConfiguration((_, config) => config
                .AddJsonFile(Path.Combine(root, "appsettings.json"), optional: false)
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Smtp:Mode"] = "Smtp", ["Smtp:Host"] = "test.local", ["Smtp:Port"] = "587", ["Smtp:From"] = "sender@example.test",
                    ["Smtp:Username"] = "unused", ["Smtp:Password"] = "unused",
                    ["AccountLinks:PublicBaseUrl"] = "https://tendering.example.test"
                }));
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IEmailSender>();
                services.AddSingleton<IEmailSender, NoEmail>();
            });
        });
    }

    private static Task StartAsync(this WebApplicationFactory<WebAppMarker> host)
    {
        _ = host.Services;
        return Task.CompletedTask;
    }

    private static async Task<string?> StartFailure(string root, string database)
    {
        try
        {
            using var host = Host(root, database);
            _ = host.Services;
            return null;
        }
        catch (Exception e)
        {
            for (var inner = e; inner != null; inner = inner.InnerException)
                if (inner is TeamAccountException) return inner.Message;
            return e.ToString();
        }
    }

    private static async Task WithUsers(WebApplicationFactory<WebAppMarker> host, Func<UserManager<ApplicationUser>, ApplicationDbContext, Task> action)
    {
        using var scope = host.Services.CreateScope();
        await action(scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>(), scope.ServiceProvider.GetRequiredService<ApplicationDbContext>());
    }

    private static async Task<(string[] Ids, Dictionary<string, (string, string)> Secrets)> Snapshot(WebApplicationFactory<WebAppMarker> host)
    {
        var ids = new List<string>();
        var secrets = new Dictionary<string, (string, string)>();
        await WithUsers(host, async (users, _) =>
        {
            foreach (var account in TeamAccountSeeder.Accounts)
            {
                var user = (await users.FindByEmailAsync(account.Email))!;
                ids.Add(user.Id);
                secrets[account.Email] = (user.PasswordHash!, user.SecurityStamp!);
            }
        });
        return (ids.ToArray(), secrets);
    }

    private static async Task<string> Hash(WebApplicationFactory<WebAppMarker> host, string email)
    {
        var hash = "";
        await WithUsers(host, async (users, _) => hash = (await users.FindByEmailAsync(email))!.PasswordHash!);
        return hash;
    }

    private static async Task<bool> Exists(string database)
    {
        await using var connection = new SqlConnection(Connection("master"));
        await connection.OpenAsync();
        await using var command = new SqlCommand("SELECT COUNT(*) FROM sys.databases WHERE name = @name", connection);
        command.Parameters.AddWithValue("@name", database);
        return (int)(await command.ExecuteScalarAsync())! > 0;
    }

    private static async Task Drop(string database)
    {
        try
        {
            if (!await Exists(database)) return;
            await using var connection = new SqlConnection(Connection("master"));
            await connection.OpenAsync();
            // Only databases this run created (unique suffix) are dropped.
            await using var command = new SqlCommand($"ALTER DATABASE [{database}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{database}];", connection);
            await command.ExecuteNonQueryAsync();
        }
        catch (Exception e)
        {
            Console.WriteLine($"Note: could not drop test database {database}: {e.Message}");
        }
    }

    private static void Ok(IdentityResult result)
    {
        if (!result.Succeeded) throw new InvalidOperationException("FAILED: test setup " + string.Join(", ", result.Errors.Select(e => e.Code)));
    }

    private static void Check(bool value, string name)
    {
        if (!value) throw new InvalidOperationException("FAILED: " + name);
        passed++;
        Console.WriteLine("PASS " + name);
    }

    private sealed class NoEmail : IEmailSender
    {
        public Task SendEmailAsync(string email, string subject, string htmlMessage) => Task.CompletedTask;
    }
}
