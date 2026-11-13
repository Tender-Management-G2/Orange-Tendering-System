using System.Collections.Concurrent;
using System.Security.Claims;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Hosting;
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
using Orange_Tendering_System.Models.Entities;
using Orange_Tendering_System.Services;

// Real SQL Server checks of the supplier-notification delivery layer (SMTP success / failure / retry cap / duplicate prevention / authority).
// Runs against the isolated UI fixture database with a scripted IEmailSender, because a live SMTP provider is not configured here.
// The browser-level relay (Manager acknowledgement, Officer "Notify Suppliers", bells) is covered by the Playwright suite.
public static class FinalDecisionChecks
{
    sealed class ScriptedMail : IEmailSender
    {
        public ConcurrentQueue<(string To, string Subject, string Body)> Sent { get; } = new();
        public HashSet<string> FailFor { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Task SendEmailAsync(string email, string subject, string htmlMessage)
        {
            if (FailFor.Contains(email)) throw new System.Net.Mail.SmtpException("Scripted SMTP failure");
            Sent.Enqueue((email, subject, htmlMessage)); return Task.CompletedTask;
        }
    }

    static int failures;
    static void Check(bool ok, string name) { Console.WriteLine((ok ? "PASS " : "FAIL ") + name); if (!ok) failures++; }

    public static async Task Run()
    {
        var connection = Environment.GetEnvironmentVariable("UI_FIXTURE_CONNECTION")!;
        if (!new SqlConnectionStringBuilder(connection).InitialCatalog.StartsWith("OrangeAuthCheck_UiE2E_", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Isolated UI database required.");
        var state = JsonNode.Parse(await File.ReadAllTextAsync(Environment.GetEnvironmentVariable("UI_FIXTURE_STATE")!))!;
        var tenderId = state["emailTenderId"]!.GetValue<int>();
        var root = Environment.GetEnvironmentVariable("AUTH_PROJECT_ROOT")!;
        string Email(string area) => state["accounts"]!.AsArray().Single(x => x!["area"]!.GetValue<string>() == area)!["email"]!.GetValue<string>();
        var mail = new ScriptedMail();
        Environment.SetEnvironmentVariable("DemoData__Enabled", "false");
        Environment.SetEnvironmentVariable("ConnectionStrings__Default Connection", connection);
        Environment.SetEnvironmentVariable("BootstrapAdmin__Email", Email("Admin")); Environment.SetEnvironmentVariable("BootstrapAdmin__Password", state["accounts"]![0]!["password"]!.GetValue<string>());
        using var factory = new WebApplicationFactory<WebAppMarker>().WithWebHostBuilder(b =>
        {
            b.UseContentRoot(root).UseEnvironment("Development");
            b.ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(new Dictionary<string, string?>
            { ["Smtp:Host"] = "smtp.test.local", ["Smtp:Port"] = "587", ["Smtp:From"] = "noreply@example.test" }));
            b.ConfigureTestServices(s => { s.RemoveAll<IEmailSender>(); s.AddSingleton<IEmailSender>(mail); });
        });

        async Task<ClaimsPrincipal> Principal(string area)
        {
            using var scope = factory.Services.CreateScope();
            var address = Email(area);
            var id = await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Users.Where(u => u.Email == address).Select(u => u.Id).SingleAsync();
            return new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, id)], "test"));
        }
        var officer = await Principal("SourcingOfficer"); var manager = await Principal("SourcingManager"); var supplier = await Principal("Supplier");
        async Task<(bool Ok, string? Error)> Try(Func<IServiceProvider, Task> work)
        {
            using var scope = factory.Services.CreateScope();
            try { await work(scope.ServiceProvider); return (true, null); } catch (WorkflowException e) { return (false, e.Message); }
        }
        async Task<List<SupplierResultDelivery>> Deliveries()
        {
            using var scope = factory.Services.CreateScope();
            return await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Set<SupplierResultDelivery>().AsNoTracking().Where(d => d.TenderId == tenderId).OrderBy(d => d.SupplierId).ToListAsync();
        }

        // Authority: only the Officer may announce results.
        Check(!(await Try(s => s.GetRequiredService<FinalDecisionService>().NotifySuppliers(tenderId, manager))).Ok, "Sourcing Manager cannot notify suppliers");
        Check(!(await Try(s => s.GetRequiredService<FinalDecisionService>().NotifySuppliers(tenderId, supplier))).Ok, "Supplier cannot notify suppliers");
        Check((await Deliveries()).Count == 0 && mail.Sent.IsEmpty, "Unauthorized attempts created no notifications or e-mails");

        // Duplicate prevention under concurrency: two simultaneous Notify clicks produce exactly one announcement.
        var race = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => Try(s => s.GetRequiredService<FinalDecisionService>().NotifySuppliers(tenderId, officer))));
        Console.WriteLine("race errors: " + string.Join(" / ", race.Select(r => r.Error)));
        Check(race.Count(r => r.Ok) == 1, $"Concurrent double-click: exactly one succeeds ({race.Count(r => r.Ok)} ok, {race.Count(r => !r.Ok)} rejected)");
        var rows = await Deliveries();
        Check(rows.Count == 2 && rows.Select(r => r.RecipientUserId).Distinct().Count() == 2, "One delivery row per supplier user (no duplicates)");
        Check(rows.Count(r => r.Outcome == "Won") == 1 && rows.Count(r => r.Outcome == "Unsuccessful") == 1, "One winning and one unsuccessful result");
        Check(rows.All(r => r.EmailStatus == "Sent" && r.EmailAttempts == 1) && mail.Sent.Count == 2, "SMTP configured: each supplier e-mailed once, status Sent");
        Check(mail.Sent.All(m => !m.Body.Contains("Gulf", StringComparison.OrdinalIgnoreCase) && !m.Body.Contains("TechWorld", StringComparison.OrdinalIgnoreCase) && !System.Text.RegularExpressions.Regex.IsMatch(m.Body, @"\d{3,}")),
            "E-mails contain no competitor names or prices");
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            Check(await db.Notifications.CountAsync(n => n.Title == "Tender result" && rows.Select(r => r.RecipientUserId).Contains(n.UserId) && n.Link!.Contains("/Supplier/Offers/Details/" + rows[0].BidId) || n.Link!.Contains("/Supplier/Offers/Details/" + rows[1].BidId)) >= 2, "In-app Tender result notifications stored in SQL");
            Check(await db.AuditLogs.AnyAsync(a => a.Action == "SupplierResultsAnnounced" && a.EntityId == tenderId.ToString()), "Announcement audit log written");
            Check(await db.FinalDecisionNotices.AnyAsync(n => n.TenderId == tenderId && n.Status == FinalDecisionStatus.SuppliersNotified && n.SuppliersNotifiedAt != null), "Notice marked SuppliersNotified");
        }
        var again = await Try(s => s.GetRequiredService<FinalDecisionService>().NotifySuppliers(tenderId, officer));
        Check(!again.Ok && (await Deliveries()).Count == 2 && mail.Sent.Count == 2, "Second Notify Suppliers rejected: no new notifications or e-mails");

        // Retry: one delivery failed; only it is retried, and only up to the attempt cap.
        var target = rows[1];
        using (var scope = factory.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Set<SupplierResultDelivery>().Where(d => d.Id == target.Id).ExecuteUpdateAsync(s => s.SetProperty(d => d.EmailStatus, "Failed").SetProperty(d => d.LastError, "Scripted SMTP failure"));
        string targetEmail; using (var scope = factory.Services.CreateScope()) targetEmail = (await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Users.Where(u => u.Id == target.RecipientUserId).Select(u => u.Email).SingleAsync())!;
        mail.FailFor.Add(targetEmail);
        await Try(s => s.GetRequiredService<FinalDecisionService>().RetryEmails(tenderId, officer));
        var afterFail = (await Deliveries()).Single(d => d.Id == target.Id);
        Check(afterFail.EmailStatus == "Failed" && afterFail.EmailAttempts == 2 && mail.Sent.Count == 2, "Failed retry recorded (Failed, attempts 2); other supplier not re-sent");
        mail.FailFor.Clear();
        await Try(s => s.GetRequiredService<FinalDecisionService>().RetryEmails(tenderId, officer));
        var afterOk = (await Deliveries()).Single(d => d.Id == target.Id);
        Check(afterOk.EmailStatus == "Sent" && afterOk.EmailAttempts == 3 && mail.Sent.Count == 3, "Successful retry recorded (Sent, attempts 3)");
        using (var scope = factory.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Set<SupplierResultDelivery>().Where(d => d.Id == target.Id).ExecuteUpdateAsync(s => s.SetProperty(d => d.EmailStatus, "Failed"));
        await Try(s => s.GetRequiredService<FinalDecisionService>().RetryEmails(tenderId, officer));
        Check(mail.Sent.Count == 3 && (await Deliveries()).Single(d => d.Id == target.Id).EmailAttempts == 3, "Retry cap (3 attempts) enforced: no further sending");
        Console.WriteLine(failures == 0 ? "Final decision delivery checks passed." : $"{failures} final decision delivery check(s) FAILED.");
        if (failures > 0) Environment.ExitCode = 1;
    }
}
