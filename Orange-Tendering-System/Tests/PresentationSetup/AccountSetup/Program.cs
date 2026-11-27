using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
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
using Orange_Tendering_System.ViewModels;

// One-off: creates the presentation accounts directly in the local OrangeTenderingTeamDev database (no code changes in the app).
const string connection = "Server=localhost;Database=OrangeTenderingTeamDev;Trusted_Connection=True;MultipleActiveResultSets=true;TrustServerCertificate=True";
const string password = "OrangeDemo2026!";
// bin/Debug/net10.0 -> AccountSetup folder; the web project is three levels above it (Tests/PresentationSetup/AccountSetup).
var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../.."));
Environment.SetEnvironmentVariable("ASPNETCORE_TEST_CONTENTROOT_ORANGE-TENDERING-SYSTEM", root);
Environment.SetEnvironmentVariable("ASPNETCORE_TEST_CONTENTROOT_ORANGE_TENDERING_SYSTEM", root);
Environment.SetEnvironmentVariable("ConnectionStrings__Default Connection", connection);
Environment.SetEnvironmentVariable("HostedDatabase__Enabled", "false");
Environment.SetEnvironmentVariable("BootstrapAdmin__Email", "");
Environment.SetEnvironmentVariable("BootstrapAdmin__Password", "");
using var app = new WebApplicationFactory<WebAppMarker>().WithWebHostBuilder(b =>
{
    b.UseContentRoot(root).UseEnvironment("Production");
    b.ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["ConnectionStrings:Default Connection"] = connection, ["HostedDatabase:Enabled"] = "false",
        ["BootstrapAdmin:Email"] = "", ["BootstrapAdmin:Password"] = "", ["Logging:LogLevel:Default"] = "Error"
    }));
});

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    var catalog = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(db.Database.GetConnectionString()).InitialCatalog;
    if (catalog != "OrangeTenderingTeamDev") throw new Exception("Wrong database: " + catalog);
    if (await db.Users.AnyAsync()) throw new Exception("Users already exist; expected an empty database.");
}

int departmentId;
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    departmentId = await db.Departments.Where(d => d.Name == "Information Technology").Select(d => d.Id).SingleAsync();
}

var internalAccounts = new (string Name, string Email, string Role, string? Seat, bool InDepartment)[]
{
    ("Haya", "haya@gmail.com", SystemRoles.Admin, null, false),
    ("Majed", "majed@gmail.com", SystemRoles.SourcingManager, "evaluation-manager", false),
    ("Tariq", "tariq@gmail.com", SystemRoles.DepartmentRepresentative, "evaluation-department", true),
    ("Saja", "saja@gmail.com", SystemRoles.Technical, "evaluation-technical-1", false),
    ("Amal", "amal@gmail.com", SystemRoles.Finance, "evaluation-financial", false),
    ("Anfal", "anfal@gmail.com", SystemRoles.ChiefExecutiveOfficer, "top-ceo", false),
    ("Mohammad", "mohammad@gmail.com", SystemRoles.ChiefFinancialOfficer, "top-cfo", false),
    ("Batool", "batool@gmail.com", SystemRoles.ChiefTechnologyOfficer, "top-cto", false),
    ("Hadeel", "hadeel@gmail.com", SystemRoles.ChiefOperationsOfficer, "top-coo", false),
    ("Sara", "sara@gmail.com", SystemRoles.SourcingOfficer, null, false),
    ("Sameh", "sameh@gmail.com", SystemRoles.RequestingDepartment, null, true),
};
var suppliers = new (string Name, string Email, string Company, string Registration, string Phone, string Address)[]
{
    ("Ali", "ali@gmail.com", "Ali Technology Supplies", "200145872", "+962 7 9512 4410", "Wasfi Al-Tal St., Amman"),
    ("Mosa", "mosa@gmail.com", "Mosa Computer Systems", "200238419", "+962 7 9634 1187", "Mecca St., Amman"),
    ("Omar", "omar@gmail.com", "Omar IT Solutions", "200311064", "+962 7 7845 2309", "University St., Irbid"),
};

var ids = new Dictionary<string, string>();
foreach (var a in internalAccounts)
{
    using var scope = app.Services.CreateScope();
    var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
    var user = new ApplicationUser { UserName = a.Email, Email = a.Email, EmailConfirmed = true, FullName = a.Name, IsActive = true,
        MustChangePassword = false, DepartmentId = a.InDepartment ? departmentId : null };
    Check(await users.CreateAsync(user, password), "create " + a.Email);
    Check(await users.AddToRolesAsync(user, SystemRoles.AccountRoles(a.Role)), "roles " + a.Email);
    if (a.Seat != null) Check(await users.AddClaimAsync(user, new Claim(CommitteeAccountSeats.ClaimType, a.Seat)), "seat " + a.Email);
    ids[a.Email] = user.Id;
    Console.WriteLine($"created {a.Name} <{a.Email}> {a.Role}{(a.Seat != null ? " / " + a.Seat : "")}");
}

foreach (var s in suppliers)
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
    var company = new Supplier { CompanyName = s.Company, RegistrationNumber = s.Registration, Email = s.Email, Phone = s.Phone, Address = s.Address,
        Category = SupplierCategories.InformationTechnology, Status = SupplierStatus.Approved, ReviewedByUserId = ids["haya@gmail.com"],
        ReviewedAt = DateTime.UtcNow, ReviewNotes = "Registration reviewed and approved.", CreatedAt = DateTime.UtcNow };
    db.Suppliers.Add(company);
    await db.SaveChangesAsync();
    var user = new ApplicationUser { UserName = s.Email, Email = s.Email, EmailConfirmed = true, FullName = s.Name, IsActive = true,
        MustChangePassword = false, SupplierId = company.Id };
    Check(await users.CreateAsync(user, password), "create " + s.Email);
    Check(await users.AddToRoleAsync(user, SystemRoles.Supplier), "role " + s.Email);
    ids[s.Email] = user.Id;
    Console.WriteLine($"created supplier {s.Name} <{s.Email}> / {s.Company}");
}

// Standing committees, saved by the Admin exactly as the Admin > Committees page does.
var admin = new ClaimsPrincipal(new ClaimsIdentity([new(ClaimTypes.NameIdentifier, ids["haya@gmail.com"])], "setup"));
var emptyVersion = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("")));
using (var scope = app.Services.CreateScope())
{
    var flow = scope.ServiceProvider.GetRequiredService<TenderWorkflowService>();
    await flow.SaveStandingCommittee(new StandingCommitteeInput { Committee = "Evaluation", Version = emptyVersion,
        MemberOneId = ids["majed@gmail.com"], MemberTwoId = ids["saja@gmail.com"], MemberThreeId = ids["amal@gmail.com"], MemberFourId = ids["tariq@gmail.com"] }, admin);
}
using (var scope = app.Services.CreateScope())
{
    var flow = scope.ServiceProvider.GetRequiredService<TenderWorkflowService>();
    await flow.SaveStandingCommittee(new StandingCommitteeInput { Committee = "Top", Version = emptyVersion,
        MemberOneId = ids["anfal@gmail.com"], MemberTwoId = ids["mohammad@gmail.com"], MemberThreeId = ids["batool@gmail.com"], MemberFourId = ids["hadeel@gmail.com"] }, admin);
    Console.WriteLine("standing Evaluation and Top committees saved; Top roster ready: " + await flow.TopRosterReady());
}

// Verify every account can sign in.
using (var scope = app.Services.CreateScope())
{
    var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
    var signIn = scope.ServiceProvider.GetRequiredService<SignInManager<ApplicationUser>>();
    foreach (var email in ids.Keys)
    {
        var u = (await users.FindByEmailAsync(email))!;
        if (!await users.CheckPasswordAsync(u, password) || !await signIn.CanSignInAsync(u)) throw new Exception("Cannot sign in: " + email);
    }
    Console.WriteLine($"PASS {ids.Count} accounts verified (password, active, confirmed).");
}

static void Check(IdentityResult r, string what)
{
    if (!r.Succeeded) throw new Exception(what + ": " + string.Join(", ", r.Errors.Select(e => e.Description)));
}
