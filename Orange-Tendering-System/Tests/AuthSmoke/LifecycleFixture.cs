using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Orange_Tendering_System;
using Orange_Tendering_System.Data;
using Orange_Tendering_System.Models;
using Orange_Tendering_System.Models.Entities;

// Accounts only (no tenders, requests or notifications): the full lifecycle is then driven through the real browser UI by
// docs/lifecycle-e2e/lifecycle_e2e.py. Isolated OrangeAuthCheck_UiE2E_ database; passwords are test values written to the private state file.
public static class LifecycleFixture
{
    public static async Task Run()
    {
        var connection = Environment.GetEnvironmentVariable("UI_FIXTURE_CONNECTION") ?? throw new InvalidOperationException("UI_FIXTURE_CONNECTION required.");
        if (!new SqlConnectionStringBuilder(connection).InitialCatalog.StartsWith("OrangeAuthCheck_UiE2E_", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Isolated OrangeAuthCheck_UiE2E_ database required.");
        var statePath = Environment.GetEnvironmentVariable("UI_FIXTURE_STATE") ?? throw new InvalidOperationException("UI_FIXTURE_STATE required.");
        var root = Environment.GetEnvironmentVariable("AUTH_PROJECT_ROOT") ?? throw new InvalidOperationException("AUTH_PROJECT_ROOT required.");
        var suffix = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(3)).ToLowerInvariant();
        string Pw() => "Lc!9" + Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(12));
        var adminEmail = $"admin-{suffix}@example.test"; var adminPw = Pw();
        Environment.SetEnvironmentVariable("DemoData__Enabled", "false");
        Environment.SetEnvironmentVariable("ConnectionStrings__Default Connection", connection);
        Environment.SetEnvironmentVariable("BootstrapAdmin__Email", adminEmail); Environment.SetEnvironmentVariable("BootstrapAdmin__Password", adminPw);
        using var factory = new WebApplicationFactory<WebAppMarker>().WithWebHostBuilder(b =>
        {
            b.UseContentRoot(root).UseEnvironment("Development");
            b.ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:Default Connection"] = connection, ["BootstrapAdmin:Email"] = adminEmail, ["BootstrapAdmin:Password"] = adminPw }));
        });
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var accounts = new JsonArray();
        var admin = (await users.FindByEmailAsync(adminEmail))!; admin.MustChangePassword = false; admin.FullName = "Layla Admin"; await users.UpdateAsync(admin);
        accounts.Add(new JsonObject { ["name"] = "admin", ["role"] = SystemRoles.Admin, ["email"] = adminEmail, ["password"] = adminPw, ["id"] = admin.Id });
        var dept = new Department { Name = "Information Technology", Description = "IT department" }; db.Departments.Add(dept); await db.SaveChangesAsync();
        async Task<ApplicationUser> Make(string name, string role, string full, int? department = null, int? supplierId = null)
        {
            var email = $"{name}-{suffix}@example.test"; var pw = Pw();
            var u = new ApplicationUser { UserName = email, Email = email, EmailConfirmed = true, IsActive = true, FullName = full, DepartmentId = department, SupplierId = supplierId };
            if (!(await users.CreateAsync(u, pw)).Succeeded || !(await users.AddToRoleAsync(u, role)).Succeeded) throw new InvalidOperationException("Could not create " + name);
            var entry = new JsonObject { ["name"] = name, ["role"] = role, ["email"] = email, ["password"] = pw, ["id"] = u.Id };
            if (supplierId != null) entry["supplierId"] = supplierId;
            accounts.Add(entry); return u;
        }
        await Make("department", SystemRoles.RequestingDepartment, "Omar Requester", dept.Id);
        await Make("officer", SystemRoles.SourcingOfficer, "Sara Officer");
        await Make("manager", SystemRoles.SourcingManager, "Nour Manager");
        await Make("department-evaluator", SystemRoles.EvaluationCommittee, "Khaled Department Rep", dept.Id);
        await Make("financial", SystemRoles.EvaluationCommittee, "Rana Financial", dept.Id);
        await Make("technical", SystemRoles.EvaluationCommittee, "Samer Technical", dept.Id);
        for (var i = 1; i <= 4; i++) await Make("top" + i, SystemRoles.TopCommittee, "Top Member " + i);
        for (var i = 1; i <= 3; i++)
        {
            var s = new Supplier { CompanyName = "Supplier Co " + i, RegistrationNumber = $"CR-{suffix}{i}", Email = $"company{i}-{suffix}@example.test", Status = SupplierStatus.Approved, CreatedAt = DateTime.UtcNow.AddDays(-30), ReviewedAt = DateTime.UtcNow.AddDays(-29), ReviewedByUserId = admin.Id };
            db.Suppliers.Add(s); await db.SaveChangesAsync();
            await Make("supplier" + i, SystemRoles.Supplier, "Supplier Contact " + i, supplierId: s.Id);
        }
        await File.WriteAllTextAsync(statePath, new JsonObject { ["connection"] = connection, ["accounts"] = accounts }.ToJsonString());
        Console.WriteLine("Lifecycle fixture ready: " + accounts.Count + " accounts.");
    }
}
