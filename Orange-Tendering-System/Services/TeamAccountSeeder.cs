using System.Text.Json;
using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Orange_Tendering_System.Data;
using Orange_Tendering_System.Models;
using Orange_Tendering_System.Models.Entities;

namespace Orange_Tendering_System.Services;

/// <summary>
/// The single source of truth for the shared internal development accounts (password <see cref="Password"/>).
///
/// Runs only in Development on a local team database (<see cref="TeamDevDatabase"/>), after migrations and
/// <see cref="IdentityBootstrap"/> (roles, administrator). On every start it creates missing accounts and repairs managed
/// ones: password (checked with CheckPasswordAsync, reset through a reset token only when wrong), email confirmation,
/// activation, MustChangePassword, lockout, roles (SystemRoles.AccountRoles), committee seat claims
/// (CommitteeAccountSeats) and the synthetic department. Account ids and workflow history are kept.
///
/// Ownership: managed accounts carry the claim <see cref="MarkerClaimType"/>. Accounts made by the first version of this
/// seeder (exact email, exact former display name, no supplier, only the expected roles and seat) are adopted once.
/// Any other account using a reserved email is never taken over: startup stops with an actionable error.
///
/// Only one Sourcing Officer may be active. Other active officers that are clearly synthetic (@orange-demo.local and a
/// "Demo "/"Team " name) are deactivated; a real active officer is never touched and startup stops with an error instead.
/// <see cref="VerifyAsync"/> then proves every account can really sign in before the app is reported ready.
/// </summary>
public static class TeamAccountSeeder
{
    public const string Password = "OrangeDemo2026!";
    public const string DepartmentName = "Team Demo Department";
    public const string MarkerClaimType = "orange:team-account";
    public const string MarkerValue = "managed";

    public sealed record TeamAccount(string Email, string Name, string Role, string? Seat, bool InDepartment, string? FormerName = null)
    {
        public string[] Roles => SystemRoles.AccountRoles(Role);
    }

    /// <summary>The ten canonical accounts. The Sourcing Manager is the fourth Evaluation Committee member.</summary>
    public static readonly IReadOnlyList<TeamAccount> Accounts =
    [
        new("admin@orange-demo.local", "Admin", SystemRoles.Admin, null, true, "Admin"),
        new("department@orange-demo.local", "Team Requesting Department", SystemRoles.RequestingDepartment, null, true, "Team Requesting Department"),
        new("sourcing.officer@orange-demo.local", "Team Sourcing Officer", SystemRoles.SourcingOfficer, null, false, "Team Sourcing Officer"),
        new("sourcing.manager@orange-demo.local", "Team Sourcing Manager", SystemRoles.SourcingManager, "evaluation-manager", false, "Team Sourcing Manager"),
        new("evaluation.technical@orange-demo.local", "Team Technical Evaluator", SystemRoles.Technical, "evaluation-technical-1", false),
        new("evaluation.financial@orange-demo.local", "Team Financial Evaluator", SystemRoles.Finance, "evaluation-financial", false),
        new("evaluation.department@orange-demo.local", "Team Department Representative", SystemRoles.DepartmentRepresentative, "evaluation-department", true),
        new("top.ceo@orange-demo.local", "Team Top Committee CEO", SystemRoles.ChiefExecutiveOfficer, "top-ceo", false, "Team Top Committee CEO"),
        new("top.cfo@orange-demo.local", "Team Top Committee CFO", SystemRoles.ChiefFinancialOfficer, "top-cfo", false, "Team Top Committee CFO"),
        new("top.cto@orange-demo.local", "Team Top Committee CTO", SystemRoles.ChiefTechnologyOfficer, "top-cto", false, "Team Top Committee CTO"),
        new("top.coo@orange-demo.local", "Team Top Committee COO", SystemRoles.ChiefOperationsOfficer, "top-coo", false, "Team Top Committee COO")
    ];

    public static TeamAccount Find(string role) => Accounts.Single(a => a.Role == role);

    /// <summary>Create or repair every account and the department, in one transaction.</summary>
    public static async Task EnsureAsync(IServiceProvider services, ILogger logger)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        await using var transaction = await db.Database.BeginTransactionAsync();
        var department = await db.Departments.SingleOrDefaultAsync(d => d.Name == DepartmentName);
        if (department == null)
        {
            department = new Department { Name = DepartmentName, Description = "Synthetic department for the shared team accounts.", IsActive = true };
            db.Departments.Add(department);
            await db.SaveChangesAsync();
        }
        else if (!department.IsActive)
        {
            department.IsActive = true;
            await db.SaveChangesAsync();
        }

        var results = new List<string>();
        ApplicationUser? officer = null;
        foreach (var account in Accounts)
        {
            var (user, changes) = await ReconcileAsync(users, account, department.Id);
            if (account.Role == SystemRoles.SourcingOfficer) officer = user;
            results.Add(changes.Count == 0 ? $"{account.Email}: ok" : $"{account.Email}: {string.Join(", ", changes)}");
        }

        // One active Sourcing Officer: the canonical one.
        foreach (var other in (await users.GetUsersInRoleAsync(SystemRoles.SourcingOfficer)).Where(u => u.IsActive && u.Id != officer!.Id))
        {
            if (!IsSyntheticDemoAccount(other))
                throw new TeamAccountException(
                    $"Another Sourcing Officer is active in this team database: {other.Email}. Only one may be active, and it is not a " +
                    "synthetic demo account, so it was not changed. Deactivate it in Admin > Users (or use a fresh team database), then restart.");
            other.IsActive = false;
            Check(await users.UpdateAsync(other), $"deactivate synthetic officer {other.Email}");
            results.Add($"{other.Email}: synthetic Sourcing Officer deactivated (only one may be active)");
        }

        // Team database only: when the Admin has never saved a Top Committee, save the four seeded Chiefs as the standing
        // Top Committee so a Manager approval can seat it. An existing roster (the Admin's choice) is never changed.
        var topRole = await db.Roles.SingleOrDefaultAsync(r => r.Name == SystemRoles.TopCommittee);
        if (topRole != null && !await db.RoleClaims.AnyAsync(c => c.RoleId == topRole.Id && c.ClaimType == TenderWorkflowService.StandingClaim))
        {
            var chiefs = new Dictionary<string, string>();
            foreach (var seat in CommitteeAccountSeats.All.Where(x => x.Committee == "Top"))
            {
                var chief = await users.FindByEmailAsync(Accounts.Single(a => a.Seat == seat.Key).Email);
                if (chief is { IsActive: true }) chiefs[seat.Key] = chief.Id;
            }
            if (chiefs.Count == 4)
            {
                db.RoleClaims.Add(new IdentityRoleClaim<string> { RoleId = topRole.Id, ClaimType = TenderWorkflowService.StandingClaim, ClaimValue = JsonSerializer.Serialize(chiefs) });
                await db.SaveChangesAsync();
                results.Add("standing Top Committee saved with the four team Chiefs (it was empty)");
            }
        }

        await transaction.CommitAsync();
        foreach (var line in results) logger.LogInformation("Team account {Result}", line);
    }

    private static async Task<(ApplicationUser User, List<string> Changes)> ReconcileAsync(UserManager<ApplicationUser> users, TeamAccount account, int departmentId)
    {
        var changes = new List<string>();
        var user = await users.FindByEmailAsync(account.Email);
        if (user == null)
        {
            if (await users.FindByNameAsync(account.Email) != null)
                throw new TeamAccountException($"The reserved team user name {account.Email} belongs to another account. Rename it in Admin > Users, then restart.");
            user = new ApplicationUser
            {
                UserName = account.Email,
                Email = account.Email,
                EmailConfirmed = true,
                FullName = account.Name,
                DepartmentId = account.InDepartment ? departmentId : null,
                IsActive = true,
                MustChangePassword = false
            };
            Check(await users.CreateAsync(user, Password), $"create {account.Email}");
            Check(await users.AddClaimAsync(user, new Claim(MarkerClaimType, MarkerValue)), $"mark {account.Email}");
            changes.Add("created");
        }
        else
        {
            var claims = await users.GetClaimsAsync(user);
            if (!claims.Any(c => c.Type == MarkerClaimType && c.Value == MarkerValue))
            {
                if (!await IsFormerTeamAccountAsync(users, user, account, claims))
                    throw new TeamAccountException(
                        $"The reserved team email {account.Email} belongs to another account ('{user.FullName}') that this seeder did not create. " +
                        "It was not changed. Give that account a different email in Admin > Users, or use a fresh team database, then restart.");
                Check(await users.AddClaimAsync(user, new Claim(MarkerClaimType, MarkerValue)), $"adopt {account.Email}");
                changes.Add("adopted from the previous team seeder");
            }
        }

        // Profile and state.
        var dirty = false;
        void Set<T>(T current, T wanted, Action apply, string what)
        {
            if (EqualityComparer<T>.Default.Equals(current, wanted)) return;
            apply();
            dirty = true;
            changes.Add(what);
        }
        Set(user.EmailConfirmed, true, () => user.EmailConfirmed = true, "email confirmed");
        Set(user.IsActive, true, () => user.IsActive = true, "activated");
        Set(user.MustChangePassword, false, () => user.MustChangePassword = false, "password change cleared");
        Set(user.FullName, account.Name, () => user.FullName = account.Name, "name");
        Set(user.UserName, account.Email, () => user.UserName = account.Email, "user name");
        int? wantedDepartment = account.InDepartment ? departmentId : null;
        Set(user.DepartmentId, wantedDepartment, () => user.DepartmentId = wantedDepartment, "department");
        if (dirty) Check(await users.UpdateAsync(user), $"update {account.Email}");

        if (await users.IsLockedOutAsync(user) || user.LockoutEnd != null)
        {
            Check(await users.SetLockoutEndDateAsync(user, null), $"unlock {account.Email}");
            changes.Add("unlocked");
        }
        if (user.AccessFailedCount > 0)
            Check(await users.ResetAccessFailedCountAsync(user), $"reset failed sign-ins of {account.Email}");

        if (!await users.CheckPasswordAsync(user, Password))
        {
            var token = await users.GeneratePasswordResetTokenAsync(user);
            Check(await users.ResetPasswordAsync(user, token, Password), $"reset password of {account.Email}");
            changes.Add("password reset");
        }

        // Roles exactly as AccountRoles defines them for the job role.
        var current = await users.GetRolesAsync(user);
        var missing = account.Roles.Except(current).ToArray();
        var extra = current.Except(account.Roles).ToArray();
        if (missing.Length > 0) { Check(await users.AddToRolesAsync(user, missing), $"add roles to {account.Email}"); changes.Add("roles added: " + string.Join("/", missing)); }
        if (extra.Length > 0) { Check(await users.RemoveFromRolesAsync(user, extra), $"remove roles from {account.Email}"); changes.Add("roles removed: " + string.Join("/", extra)); }

        // Exactly the account's committee seat.
        var seats = (await users.GetClaimsAsync(user)).Where(c => c.Type == CommitteeAccountSeats.ClaimType).ToList();
        foreach (var wrong in seats.Where(c => c.Value != account.Seat))
        {
            Check(await users.RemoveClaimAsync(user, wrong), $"remove seat from {account.Email}");
            changes.Add("seat removed: " + wrong.Value);
        }
        if (account.Seat != null && !seats.Any(c => c.Value == account.Seat))
        {
            Check(await users.AddClaimAsync(user, new Claim(CommitteeAccountSeats.ClaimType, account.Seat)), $"seat {account.Email}");
            changes.Add("seat " + account.Seat);
        }
        return (user, changes);
    }

    /// <summary>Strict recognition of accounts created by the first TeamAccountSeeder (before the ownership marker).</summary>
    private static async Task<bool> IsFormerTeamAccountAsync(UserManager<ApplicationUser> users, ApplicationUser user, TeamAccount account, IList<Claim> claims)
    {
        if (account.FormerName == null || user.FullName != account.FormerName || user.SupplierId != null) return false;
        if (!string.Equals(user.Email, account.Email, StringComparison.OrdinalIgnoreCase)) return false;
        var roles = await users.GetRolesAsync(user);
        if (roles.Except(account.Roles).Any()) return false;
        return claims.Where(c => c.Type == CommitteeAccountSeats.ClaimType).All(c => c.Value == account.Seat);
    }

    private static bool IsSyntheticDemoAccount(ApplicationUser user) =>
        user.SupplierId == null &&
        user.Email?.EndsWith("@orange-demo.local", StringComparison.OrdinalIgnoreCase) == true &&
        (user.FullName.StartsWith("Demo ", StringComparison.Ordinal) || user.FullName.StartsWith("Team ", StringComparison.Ordinal));

    /// <summary>
    /// Proves the accounts are usable: exists, documented password, active, confirmed, not locked out, exact roles,
    /// seat, department, SignInManager eligibility, and exactly one active Sourcing Officer. Throws with every problem.
    /// </summary>
    public static async Task VerifyAsync(IServiceProvider services, ILogger logger, string database)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var signIn = scope.ServiceProvider.GetRequiredService<SignInManager<ApplicationUser>>();
        var departmentId = await db.Departments.Where(d => d.Name == DepartmentName && d.IsActive).Select(d => (int?)d.Id).SingleOrDefaultAsync();
        var problems = new List<string>();
        foreach (var account in Accounts)
        {
            var user = await users.FindByEmailAsync(account.Email);
            if (user == null) { problems.Add($"{account.Email}: missing"); continue; }
            if (!await users.CheckPasswordAsync(user, Password)) problems.Add($"{account.Email}: password does not match");
            if (!user.IsActive) problems.Add($"{account.Email}: inactive");
            if (!user.EmailConfirmed) problems.Add($"{account.Email}: email not confirmed");
            if (user.MustChangePassword) problems.Add($"{account.Email}: must change password");
            if (await users.IsLockedOutAsync(user)) problems.Add($"{account.Email}: locked out");
            if (!await signIn.CanSignInAsync(user)) problems.Add($"{account.Email}: not allowed to sign in");
            var roles = await users.GetRolesAsync(user);
            if (roles.Count != account.Roles.Length || account.Roles.Except(roles).Any()) problems.Add($"{account.Email}: roles {string.Join("/", roles)}");
            var seats = (await users.GetClaimsAsync(user)).Where(c => c.Type == CommitteeAccountSeats.ClaimType).Select(c => c.Value).ToList();
            if (account.Seat == null ? seats.Count > 0 : seats.Count != 1 || seats[0] != account.Seat) problems.Add($"{account.Email}: seat {string.Join("/", seats)}");
            if (account.InDepartment ? user.DepartmentId == null || user.DepartmentId != departmentId : user.DepartmentId != null)
                problems.Add($"{account.Email}: department");
        }
        var activeOfficers = (await users.GetUsersInRoleAsync(SystemRoles.SourcingOfficer)).Count(u => u.IsActive);
        if (activeOfficers != 1) problems.Add($"{activeOfficers} active Sourcing Officers (exactly one is required)");

        if (problems.Count > 0)
            throw new TeamAccountException($"Team accounts are NOT ready in {database}: {string.Join("; ", problems)}.");
        logger.LogInformation("Team accounts ready in {Database}: {Count} accounts verified (sign-in, roles, seats, department). Password for all: see DEMO-DATA.md.",
            database, Accounts.Count);
    }

    private static void Check(IdentityResult result, string action)
    {
        if (!result.Succeeded)
            throw new TeamAccountException($"Team account provisioning failed to {action}: {string.Join(", ", result.Errors.Select(e => e.Code))}.");
    }
}

/// <summary>Team account provisioning could not complete; the message says what to do.</summary>
public sealed class TeamAccountException(string message) : InvalidOperationException(message);
