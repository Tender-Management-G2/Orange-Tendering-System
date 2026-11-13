using Microsoft.AspNetCore.Identity;
using Orange_Tendering_System.Models;
using Orange_Tendering_System.Models.Entities;
using Orange_Tendering_System.Data;

namespace Orange_Tendering_System.Services;

public static class IdentityBootstrap
{
    public static async Task SeedAsync(IServiceProvider services, IConfiguration configuration)
    {
        using var scope = services.CreateScope();
        var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        foreach (var name in SystemRoles.All)
        {
            if (await roles.RoleExistsAsync(name)) continue;
            var result = await roles.CreateAsync(new IdentityRole(name));
            if (!result.Succeeded)
                throw new InvalidOperationException($"Could not create Identity role {name}: {string.Join(", ", result.Errors.Select(e => e.Code))}");
        }

        var email = configuration["BootstrapAdmin:Email"];
        var password = configuration["BootstrapAdmin:Password"];
        if (string.IsNullOrWhiteSpace(email) && string.IsNullOrWhiteSpace(password)) return;
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
            throw new InvalidOperationException("BootstrapAdmin requires both Email and Password.");

        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var existing = await users.FindByEmailAsync(email);
        if (existing != null)
        {
            if (!await users.IsInRoleAsync(existing, SystemRoles.Admin))
                throw new InvalidOperationException("Bootstrap Admin email belongs to a non-Admin account. No privilege was granted.");
            return;
        }

        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await using var transaction = await db.Database.BeginTransactionAsync();
        var user = new ApplicationUser
        {
            UserName = email.Trim(), Email = email.Trim(), EmailConfirmed = true,
            FullName = configuration["BootstrapAdmin:FullName"]?.Trim() ?? "Initial Administrator",
            IsActive = true, MustChangePassword = true
        };
        var created = await users.CreateAsync(user, password);
        if (!created.Succeeded)
            throw new InvalidOperationException($"Could not create initial Admin: {string.Join(", ", created.Errors.Select(e => e.Code))}");
        var assigned = await users.AddToRoleAsync(user, SystemRoles.Admin);
        if (!assigned.Succeeded)
            throw new InvalidOperationException($"Could not assign initial Admin role: {string.Join(", ", assigned.Errors.Select(e => e.Code))}");
        await transaction.CommitAsync();
    }
}
