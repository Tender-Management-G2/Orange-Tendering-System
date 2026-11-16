using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Orange_Tendering_System.Data;
using Orange_Tendering_System.Models;
using Orange_Tendering_System.Models.Entities;

namespace Orange_Tendering_System.Services;

/// <summary>Six repeatable supplier logins for the local team development database.</summary>
public static class TeamSupplierSeeder
{
    public const int Count = 6;

    private static readonly string[] Categories =
    [
        SupplierCategories.InformationTechnology,
        SupplierCategories.OfficeSupplies,
        SupplierCategories.Furniture,
        SupplierCategories.ProfessionalServices,
        SupplierCategories.Construction,
        SupplierCategories.Transport
    ];

    private static string Email(int i) => $"supplier{i}@orange-demo.local";
    private static string Registration(int i) => $"DEMO-REG-{i:000}";
    private static string Name(int i) => $"DEMO Supplier {i}";

    public static async Task EnsureAsync(IServiceProvider services, ILogger logger)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        await using var transaction = await db.Database.BeginTransactionAsync();

        for (var i = 1; i <= Count; i++)
        {
            var email = Email(i);
            var registration = Registration(i);
            var supplier = await db.Suppliers.SingleOrDefaultAsync(s => s.RegistrationNumber == registration);
            if (supplier == null)
            {
                if (await db.Suppliers.AnyAsync(s => s.Email == email))
                    throw new TeamAccountException($"Supplier email {email} already belongs to another company.");
                supplier = new Supplier
                {
                    CompanyName = Name(i), RegistrationNumber = registration, Email = email,
                    Category = Categories[i - 1], Status = SupplierStatus.Approved,
                    CreatedAt = DateTime.UtcNow
                };
                db.Suppliers.Add(supplier);
                await db.SaveChangesAsync();
            }
            else if (!string.Equals(supplier.Email, email, StringComparison.OrdinalIgnoreCase) ||
                     supplier.CompanyName != Name(i))
            {
                throw new TeamAccountException($"Registration {registration} belongs to another supplier.");
            }

            var user = await users.FindByEmailAsync(email);
            if (user == null)
            {
                user = new ApplicationUser
                {
                    UserName = email, Email = email, FullName = Name(i), SupplierId = supplier.Id,
                    EmailConfirmed = true, IsActive = true, MustChangePassword = false
                };
                Check(await users.CreateAsync(user, TeamAccountSeeder.Password), $"create {email}");
            }
            else if (user.SupplierId != supplier.Id)
            {
                throw new TeamAccountException($"Account {email} is linked to another supplier.");
            }

            var claims = await users.GetClaimsAsync(user);
            if (!claims.Any(c => c.Type == TeamAccountSeeder.MarkerClaimType && c.Value == TeamAccountSeeder.MarkerValue))
                Check(await users.AddClaimAsync(user, new Claim(TeamAccountSeeder.MarkerClaimType, TeamAccountSeeder.MarkerValue)),
                    $"mark {email}");

            supplier.Status = SupplierStatus.Approved;
            supplier.Category = Categories[i - 1];
            user.IsActive = true;
            user.EmailConfirmed = true;
            user.MustChangePassword = false;
            user.FullName = Name(i);
            user.AccessFailedCount = 0;
            user.LockoutEnd = null;
            Check(await users.UpdateAsync(user), $"update {email}");
            if (!await users.CheckPasswordAsync(user, TeamAccountSeeder.Password))
            {
                var token = await users.GeneratePasswordResetTokenAsync(user);
                Check(await users.ResetPasswordAsync(user, token, TeamAccountSeeder.Password), $"reset password of {email}");
            }
            if (!await users.IsInRoleAsync(user, SystemRoles.Supplier))
                Check(await users.AddToRoleAsync(user, SystemRoles.Supplier), $"assign supplier role to {email}");
            logger.LogInformation("Team supplier {Email} ready.", email);
        }

        await db.SaveChangesAsync();
        await transaction.CommitAsync();
    }

    public static async Task VerifyAsync(IServiceProvider services, ILogger logger, string database)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var signIn = scope.ServiceProvider.GetRequiredService<SignInManager<ApplicationUser>>();
        var problems = new List<string>();
        for (var i = 1; i <= Count; i++)
        {
            var email = Email(i);
            var user = await users.FindByEmailAsync(email);
            if (user == null) { problems.Add($"{email}: missing"); continue; }
            if (user.SupplierId == null ||
                !await db.Suppliers.AnyAsync(s => s.Id == user.SupplierId && s.RegistrationNumber == Registration(i) &&
                                                 s.Status == SupplierStatus.Approved))
                problems.Add($"{email}: supplier link or approval");
            if (!user.IsActive || !user.EmailConfirmed || user.MustChangePassword)
                problems.Add($"{email}: account state");
            if (!await users.IsInRoleAsync(user, SystemRoles.Supplier))
                problems.Add($"{email}: role");
            if (!await users.CheckPasswordAsync(user, TeamAccountSeeder.Password))
                problems.Add($"{email}: password");
            if (!(await signIn.CheckPasswordSignInAsync(user, TeamAccountSeeder.Password, false)).Succeeded)
                problems.Add($"{email}: sign-in");
        }
        if (problems.Count != 0)
            throw new TeamAccountException($"Team suppliers are NOT ready in {database}: {string.Join("; ", problems)}.");
        logger.LogInformation("Team suppliers ready in {Database}: {Count} accounts verified.", database, Count);
    }

    private static void Check(IdentityResult result, string action)
    {
        if (!result.Succeeded)
            throw new TeamAccountException($"Could not {action}: {string.Join(", ", result.Errors.Select(e => e.Code))}.");
    }
}
