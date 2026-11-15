using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Orange_Tendering_System.Data;
using Orange_Tendering_System.Models;
using Orange_Tendering_System.Models.Entities;

namespace Orange_Tendering_System.Services;

/// <summary>
/// Shared team login accounts for local development (Development environment only, switch TeamAccounts:Enabled,
/// default true). Every developer who fetches WorkV2 and runs the app gets the same seven accounts, all with the
/// password <see cref="Password"/>:
///
///   sourcing.officer@orange-demo.local   Sourcing Officer
///   sourcing.manager@orange-demo.local   Sourcing Manager
///   department@orange-demo.local         Requesting Department ("Team Demo Department")
///   top.ceo / top.cfo / top.cto / top.coo@orange-demo.local   Top Committee (CEO, CFO, CTO, COO job roles and seats)
///
/// Idempotent: existing accounts keep their password; roles, seat, department and activation are only added, never
/// removed. Synthetic data only. The workflow allows exactly one active Sourcing Officer, so the team officer is made
/// active only when no other active officer exists (otherwise it is left inactive and a warning is logged).
/// </summary>
public static class TeamAccountSeeder
{
    public const string Password = "OrangeDemo2026!";
    public const string DepartmentName = "Team Demo Department";

    private sealed record Account(string Email, string Name, string Role, string? Seat = null, bool Department = false);

    private static readonly Account[] Accounts =
    [
        new("sourcing.officer@orange-demo.local", "Team Sourcing Officer", SystemRoles.SourcingOfficer),
        new("sourcing.manager@orange-demo.local", "Team Sourcing Manager", SystemRoles.SourcingManager),
        new("department@orange-demo.local", "Team Requesting Department", SystemRoles.RequestingDepartment, Department: true),
        new("top.ceo@orange-demo.local", "Team Top Committee CEO", SystemRoles.ChiefExecutiveOfficer, "top-ceo"),
        new("top.cfo@orange-demo.local", "Team Top Committee CFO", SystemRoles.ChiefFinancialOfficer, "top-cfo"),
        new("top.cto@orange-demo.local", "Team Top Committee CTO", SystemRoles.ChiefTechnologyOfficer, "top-cto"),
        new("top.coo@orange-demo.local", "Team Top Committee COO", SystemRoles.ChiefOperationsOfficer, "top-coo")
    ];

    public static async Task SeedAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger(nameof(TeamAccountSeeder));

        var department = await db.Departments.SingleOrDefaultAsync(d => d.Name == DepartmentName);
        if (department == null)
        {
            department = new Department { Name = DepartmentName, Description = "Synthetic department for the shared team login.", IsActive = true };
            db.Departments.Add(department);
            await db.SaveChangesAsync();
        }

        foreach (var account in Accounts)
        {
            var user = await users.FindByEmailAsync(account.Email);
            if (user == null)
            {
                user = new ApplicationUser
                {
                    UserName = account.Email, Email = account.Email, EmailConfirmed = true, FullName = account.Name,
                    DepartmentId = account.Department ? department.Id : null,
                    IsActive = account.Role != SystemRoles.SourcingOfficer || !await OtherActiveOfficerAsync(users, account.Email),
                    MustChangePassword = false
                };
                var created = await users.CreateAsync(user, Password);
                if (!created.Succeeded)
                    throw new InvalidOperationException($"Team account creation failed for {account.Email}: {string.Join(", ", created.Errors.Select(e => e.Code))}");
                logger.LogInformation("Created team account {Email} ({Role}).", account.Email, account.Role);
            }
            else if (user.SupplierId != null || !user.FullName.StartsWith("Team ", StringComparison.Ordinal))
            {
                // Never take over a real or differently owned account that happens to use this address.
                logger.LogWarning("Skipped team account {Email}: the address belongs to another account.", account.Email);
                continue;
            }

            if (account.Department && user.DepartmentId == null)
            {
                user.DepartmentId = department.Id;
                await users.UpdateAsync(user);
            }

            foreach (var role in SystemRoles.AccountRoles(account.Role))
            {
                if (await users.IsInRoleAsync(user, role)) continue;
                var assigned = await users.AddToRoleAsync(user, role);
                if (!assigned.Succeeded) throw new InvalidOperationException($"Could not assign {role} to {account.Email}.");
            }

            if (account.Seat != null &&
                !(await users.GetClaimsAsync(user)).Any(c => c.Type == CommitteeAccountSeats.ClaimType && c.Value == account.Seat))
            {
                var seated = await users.AddClaimAsync(user, new Claim(CommitteeAccountSeats.ClaimType, account.Seat));
                if (!seated.Succeeded) throw new InvalidOperationException($"Could not assign seat {account.Seat} to {account.Email}.");
            }

            if (account.Role == SystemRoles.SourcingOfficer && !user.IsActive)
                logger.LogWarning("Team account {Email} is inactive because another Sourcing Officer is active (only one is allowed). Switch officers in Admin > Users.", account.Email);
        }
    }

    private static async Task<bool> OtherActiveOfficerAsync(UserManager<ApplicationUser> users, string email) =>
        (await users.GetUsersInRoleAsync(SystemRoles.SourcingOfficer)).Any(u => u.IsActive && !string.Equals(u.Email, email, StringComparison.OrdinalIgnoreCase));
}
