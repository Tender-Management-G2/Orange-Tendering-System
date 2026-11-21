using System.Net;
using Microsoft.AspNetCore.Builder;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Orange_Tendering_System;
using Orange_Tendering_System.Data;
using Orange_Tendering_System.Models;
using Orange_Tendering_System.Models.Entities;
using Orange_Tendering_System.Services;

var password = Environment.GetEnvironmentVariable("SUPPLIER_DEMO_PASSWORD") ?? throw new Exception("Set SUPPLIER_DEMO_PASSWORD.");
var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
// Use the normal Development configuration (including User Secrets) and launch-profile database selection.
var settings = WebApplication.CreateBuilder(new WebApplicationOptions { ContentRootPath = root, EnvironmentName = "Development", ApplicationName = typeof(WebAppMarker).Assembly.GetName().Name });
settings.Configuration["TeamDev:Database"] = TeamDevDatabase.DefaultName;
TeamDevDatabase.Apply(settings);
var connection = settings.Configuration.GetConnectionString(TeamDevDatabase.ConnectionName)!;
if (!TeamDevDatabase.IsLocalTeamDatabase(connection)) throw new Exception("Local team database required.");
Console.WriteLine("Database: " + TeamDevDatabase.Describe(connection));
// Suppress all unrelated startup provisioning and migrations in this one-off process.
Environment.SetEnvironmentVariable("ConnectionStrings__Default Connection", connection);
Environment.SetEnvironmentVariable("BootstrapAdmin__Email", "");
Environment.SetEnvironmentVariable("BootstrapAdmin__Password", "");
using var factory = new WebApplicationFactory<WebAppMarker>().WithWebHostBuilder(b => {
    b.UseContentRoot(root).UseEnvironment("Production");
    b.ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(new Dictionary<string,string?> {
        ["ConnectionStrings:Default Connection"] = connection,
        ["BootstrapAdmin:Email"] = "", ["BootstrapAdmin:Password"] = "",
        ["Logging:LogLevel:Default"] = "Error"
    }));
});
var emails = Enumerable.Range(30,7).Select(n => $"SupplierTariq{n}@Demo-orange.loacl").ToArray();
using (var scope = factory.Services.CreateScope()) {
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
    await using var tx = await db.Database.BeginTransactionAsync();
    foreach (var email in emails) {
        var existing = await users.FindByEmailAsync(email);
        if (existing != null) { Console.WriteLine(email + ": already exists; unchanged"); continue; }
        var number = Regex.Match(email, @"\d+").Value;
        var registration = "TARIQ-DEMO-" + number;
        if (await db.Suppliers.AnyAsync(s => s.RegistrationNumber == registration || s.Email == email)) throw new Exception("Supplier collision: " + email);
        var supplier = new Supplier { CompanyName = "Demo Supplier Tariq " + number, RegistrationNumber = registration, Email = email, Status = SupplierStatus.Approved, CreatedAt = DateTime.UtcNow };
        db.Suppliers.Add(supplier);
        await db.SaveChangesAsync();
        var user = new ApplicationUser { UserName = email, Email = email, FullName = "Supplier Tariq " + number, SupplierId = supplier.Id, EmailConfirmed = true, IsActive = true, MustChangePassword = false };
        Check(await users.CreateAsync(user, password));
        Check(await users.AddToRoleAsync(user, SystemRoles.Supplier));
    }
    await tx.CommitAsync();
}
// Fresh scopes read committed SQL Server data, then real HTTP form requests prove cookie sign-in and workspace access.
foreach (var email in emails) {
    using (var scope = factory.Services.CreateScope()) {
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await db.Users.AsNoTracking().SingleAsync(u => u.Email == email);
        var roles = await users.GetRolesAsync(user);
        if (user.Email != email || user.UserName != email || !user.IsActive || !user.EmailConfirmed || user.MustChangePassword || roles.Count != 1 || roles[0] != SystemRoles.Supplier || !await users.CheckPasswordAsync(user,password) || !await db.Suppliers.AnyAsync(s => s.Id == user.SupplierId && s.Email == email && s.Status == SupplierStatus.Approved)) throw new Exception("Persisted verification failed: " + email);
    }
    using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
    var html = await client.GetStringAsync("/Identity/Account/Login");
    var token = WebUtility.HtmlDecode(Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value);
    if (string.IsNullOrEmpty(token)) throw new Exception("Missing antiforgery token.");
    var response = await client.PostAsync("/Identity/Account/Login?handler=Credentials", new FormUrlEncodedContent(new Dictionary<string,string> { ["__RequestVerificationToken"] = token, ["Input.Email"] = email, ["Input.Password"] = password, ["Input.AccountType"] = "supplier" }));
    var body = await response.Content.ReadAsStringAsync();
    if (!response.IsSuccessStatusCode || !body.Contains("signedIn")) throw new Exception("HTTP login failed: " + email + " " + response.StatusCode);
    var workspace = await client.GetAsync("/Supplier");
    if (workspace.StatusCode != HttpStatusCode.OK) throw new Exception("Supplier workspace denied: " + email + " " + workspace.StatusCode);
    Console.WriteLine(email + ": SQL persistence, password login, Supplier-only role, workspace HTTP 200 PASS");
}
static void Check(IdentityResult result) { if (!result.Succeeded) throw new Exception(string.Join(", ", result.Errors.Select(e => e.Code))); }
