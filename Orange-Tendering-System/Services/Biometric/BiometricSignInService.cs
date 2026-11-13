using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Orange_Tendering_System.Data;
using Orange_Tendering_System.Models;
using Orange_Tendering_System.Models.Entities;
using Orange_Tendering_System.ViewModels;

namespace Orange_Tendering_System.Services.Biometric;

/// <summary>
/// What happens after a biometric check. It applies the same account rules as the Identity password sign-in
/// (Areas/Identity/Pages/Account/Login.cshtml.cs): an active account, a confirmed account, lockout, and supplier
/// accounts only once their company is Approved. It then signs in through the one Identity cookie (no second
/// authentication system) and writes an audit row for every attempt. No biometric data is ever audited.
/// </summary>
public class BiometricSignInService(
    UserManager<ApplicationUser> users,
    SignInManager<ApplicationUser> signInManager,
    ApplicationDbContext db)
{
    public const string FaceMethod = "biometric-face";
    public const string PasskeyMethod = "biometric-passkey";

    public async Task<bool> SignInAsync(ApplicationUser user, string method, string? ipAddress)
    {
        if (method is not (PasskeyMethod or FaceMethod) || !await AccountAllowedAsync(user))
        {
            await AuditAsync(user.Id, "BiometricSignInDenied", method, ipAddress, "account not allowed to sign in");
            return false;
        }

        await users.ResetAccessFailedCountAsync(user);
        await signInManager.SignInAsync(user, isPersistent: false, authenticationMethod: method);
        await AuditAsync(user.Id, "BiometricSignIn", method, ipAddress, null);
        return true;
    }

    /// <summary>The same checks the password sign-in makes after the credential is accepted.</summary>
    public async Task<bool> AccountAllowedAsync(ApplicationUser user)
    {
        // Do not silently bypass an account's configured additional MFA policy.
        // Such accounts must use the existing Identity flow until passkey/MFA integration is defined.
        if (!user.IsActive || user.TwoFactorEnabled || await users.IsLockedOutAsync(user) || !await signInManager.CanSignInAsync(user))
            return false;

        var isSupplier = await users.IsInRoleAsync(user, SystemRoles.Supplier);
        return (!isSupplier && user.SupplierId == null) ||
            (user.SupplierId != null && await db.Suppliers.AsNoTracking()
                .AnyAsync(s => s.Id == user.SupplierId && s.Status == SupplierStatus.Approved));
    }

    /// <summary>
    /// Who may register a face or passkey: an active, email-confirmed account. Suppliers may enroll while their
    /// company is Pending (registration step) or Approved; signing in with it still requires Approved.
    /// </summary>
    public async Task<bool> EnrollmentAllowedAsync(ApplicationUser user)
    {
        if (!user.IsActive || !user.EmailConfirmed || await users.IsLockedOutAsync(user))
            return false;

        var isSupplier = await users.IsInRoleAsync(user, SystemRoles.Supplier);
        return (!isSupplier && user.SupplierId == null) ||
            (isSupplier && user.SupplierId != null && await db.Suppliers.AsNoTracking()
                .AnyAsync(s => s.Id == user.SupplierId && (s.Status == SupplierStatus.Pending || s.Status == SupplierStatus.Approved)));
    }

    /// <summary>Verifies the password with Identity's lockout accounting (re-checked before biometric enrollment).</summary>
    public async Task<bool> CheckPasswordAsync(ApplicationUser user, string? password)
    {
        if (string.IsNullOrEmpty(password)) return false;
        var result = await signInManager.CheckPasswordSignInAsync(user, password, lockoutOnFailure: true);
        return result.Succeeded;
    }

    /// <summary>A failed biometric check counts toward the same lockout as wrong passwords.</summary>
    public async Task RegisterFailureAsync(ApplicationUser? user, string method, string reason, string? ipAddress, bool countLockout = true)
    {
        if (countLockout && user is { LockoutEnabled: true })
            await users.AccessFailedAsync(user);

        await AuditAsync(user?.Id, "BiometricSignInFailed", method, ipAddress, reason);
    }

    public async Task<bool> IsLockedOutAsync(ApplicationUser? user) => user is not null && await users.IsLockedOutAsync(user);

    /// <summary>Where the person lands: first-login password change, the local page they asked for, or their workspace.</summary>
    public async Task<string> DestinationAsync(ApplicationUser user, string? returnUrl, Func<string?, bool> isLocalUrl)
    {
        if (user.MustChangePassword) return "/Identity/Account/FirstLoginPasswordChange";
        if (!string.IsNullOrEmpty(returnUrl) && returnUrl != "/" && isLocalUrl(returnUrl)) return returnUrl;

        var roles = await users.GetRolesAsync(user);
        var workspace = WorkspaceNavigation.Workspaces
            .Where(w => roles.Contains(w.Role))
            .OrderBy(w => w.Role == SystemRoles.SourcingManager ? -1 : 0)
            .FirstOrDefault();
        return workspace == null ? "/Workspace" : "/" + workspace.Area;
    }

    /// <summary>Writes one audit row with a direct INSERT so it is independent of the request's change tracker.</summary>
    public async Task AuditAsync(string? userId, string action, string method, string? ipAddress, string? detail)
    {
        var newValues = JsonSerializer.Serialize(new { method, detail, ip = ipAddress });
        var entityId = userId ?? "unknown";
        var now = DateTime.UtcNow;
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO [AuditLogs] ([UserId], [Action], [EntityName], [EntityId], [NewValues], [CreatedAt]) VALUES ({userId}, {action}, {nameof(ApplicationUser)}, {entityId}, {newValues}, {now})");
    }
}
