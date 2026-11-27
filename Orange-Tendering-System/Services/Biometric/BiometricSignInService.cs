using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Orange_Tendering_System.Data;
using Orange_Tendering_System.Models;
using Orange_Tendering_System.Models.Entities;
using Orange_Tendering_System.ViewModels;

namespace Orange_Tendering_System.Services.Biometric;

/// <summary>
/// The account rules used by every sign-in method, in one place:
///   - who may sign in (active, confirmed, not locked, supplier company Approved),
///   - who may enroll a face or passkey (active, confirmed, supplier company Pending or Approved),
///   - the sign-in itself (the one ASP.NET Core Identity cookie, plus a claim that names the method),
///   - where the person goes next, and the audit row of every attempt. No biometric data is ever audited.
/// </summary>
public class BiometricSignInService
{
    public const string FaceMethod = "biometric-face";
    public const string PasskeyMethod = "biometric-passkey";
    /// <summary>An approved supplier who signed in with Email &amp; Password (allowed while Biometric:SupplierPasswordOnly is true).</summary>
    public const string PasswordStepMethod = "supplier-password-step";
    /// <summary>The claim inside the sign-in cookie that remembers which of the three methods was used.</summary>
    public const string AuthenticationMethodClaim = "orange:biometric-auth-method";

    private readonly UserManager<ApplicationUser> users;
    private readonly SignInManager<ApplicationUser> signInManager;
    private readonly ApplicationDbContext db;

    public BiometricSignInService(UserManager<ApplicationUser> users, SignInManager<ApplicationUser> signInManager, ApplicationDbContext db)
    {
        this.users = users;
        this.signInManager = signInManager;
        this.db = db;
    }

    /// <summary>True for the three method names this service writes into the cookie.</summary>
    public static bool IsKnownMethod(string? method)
    {
        if (method == FaceMethod || method == PasskeyMethod || method == PasswordStepMethod)
        {
            return true;
        }
        return false;
    }

    /// <summary>Checks the account rules again and, when they pass, signs the user in with the method claim.</summary>
    public async Task<bool> SignInAsync(ApplicationUser user, string method, string? ipAddress)
    {
        if (!IsKnownMethod(method) || !await AccountAllowedAsync(user))
        {
            await AuditAsync(user.Id, "BiometricSignInDenied", method, ipAddress, "account not allowed to sign in");
            return false;
        }

        await users.ResetAccessFailedCountAsync(user);

        // The claim is stored in the signed cookie. SupplierApprovalGate reads it to know how the supplier signed in.
        List<Claim> extraClaims = new List<Claim>();
        extraClaims.Add(new Claim(AuthenticationMethodClaim, method));
        await signInManager.SignInWithClaimsAsync(user, false, extraClaims);

        await AuditAsync(user.Id, "BiometricSignIn", method, ipAddress, null);
        return true;
    }

    /// <summary>May this account sign in now? The same checks the password sign-in makes after a correct password.</summary>
    public async Task<bool> AccountAllowedAsync(ApplicationUser user)
    {
        if (!user.IsActive)
        {
            return false;
        }
        // An account with an extra two-factor policy must use the normal Identity flow; it is never bypassed here.
        if (user.TwoFactorEnabled)
        {
            return false;
        }
        if (await users.IsLockedOutAsync(user))
        {
            return false;
        }
        if (!await signInManager.CanSignInAsync(user))
        {
            return false;
        }

        bool isSupplier = await users.IsInRoleAsync(user, SystemRoles.Supplier);
        if (user.SupplierId == null)
        {
            // An internal user (no company). A Supplier role without a company is not allowed.
            return !isSupplier;
        }

        // A supplier contact: the company must be Approved.
        bool approved = await db.Suppliers.AsNoTracking()
            .AnyAsync(s => s.Id == user.SupplierId && s.Status == SupplierStatus.Approved);
        return approved;
    }

    /// <summary>
    /// May this account register a face or a passkey? An active, email-confirmed account. A supplier may enroll while
    /// the company is Pending (during registration) or Approved. Signing in with it still needs Approved.
    /// </summary>
    public async Task<bool> EnrollmentAllowedAsync(ApplicationUser user)
    {
        if (!user.IsActive || !user.EmailConfirmed)
        {
            return false;
        }
        if (await users.IsLockedOutAsync(user))
        {
            return false;
        }

        bool isSupplier = await users.IsInRoleAsync(user, SystemRoles.Supplier);
        if (!isSupplier && user.SupplierId == null)
        {
            return true;
        }
        if (isSupplier && user.SupplierId != null)
        {
            bool pendingOrApproved = await db.Suppliers.AsNoTracking()
                .AnyAsync(s => s.Id == user.SupplierId && (s.Status == SupplierStatus.Pending || s.Status == SupplierStatus.Approved));
            return pendingOrApproved;
        }
        return false;
    }

    /// <summary>Checks the password with Identity's lockout counting (used before every enrollment).</summary>
    public async Task<bool> CheckPasswordAsync(ApplicationUser user, string? password)
    {
        if (string.IsNullOrEmpty(password))
        {
            return false;
        }

        // true = count a wrong password toward the lockout.
        var result = await signInManager.CheckPasswordSignInAsync(user, password, true);
        return result.Succeeded;
    }

    /// <summary>A failed face or passkey check counts toward the same lockout as a wrong password, and is audited.</summary>
    public async Task RegisterFailureAsync(ApplicationUser? user, string method, string reason, string? ipAddress)
    {
        if (user != null && user.LockoutEnabled)
        {
            await users.AccessFailedAsync(user);
        }

        string? userId = null;
        if (user != null)
        {
            userId = user.Id;
        }
        await AuditAsync(userId, "BiometricSignInFailed", method, ipAddress, reason);
    }

    public async Task<bool> IsLockedOutAsync(ApplicationUser? user)
    {
        if (user == null)
        {
            return false;
        }
        return await users.IsLockedOutAsync(user);
    }

    /// <summary>Where the person goes after a sign-in: the first-login password change, the local page they asked for, or their workspace.</summary>
    public async Task<string> DestinationAsync(ApplicationUser user, string? returnUrl, IUrlHelper url)
    {
        if (user.MustChangePassword)
        {
            return "/Identity/Account/FirstLoginPasswordChange";
        }

        // Only a local address is followed, so a link cannot send the user to another website (open redirect).
        if (!string.IsNullOrEmpty(returnUrl) && returnUrl != "/" && url.IsLocalUrl(returnUrl))
        {
            return returnUrl;
        }

        return await WorkspaceUrlAsync(user);
    }

    /// <summary>The home page of the user's role, for example "/Admin". Sourcing Manager wins when a user has several roles.</summary>
    public async Task<string> WorkspaceUrlAsync(ApplicationUser user)
    {
        IList<string> roles = await users.GetRolesAsync(user);

        string? firstArea = null;
        foreach (var workspace in WorkspaceNavigation.Workspaces)
        {
            if (!roles.Contains(workspace.Role))
            {
                continue;
            }
            if (workspace.Role == SystemRoles.SourcingManager)
            {
                return "/" + workspace.Area;
            }
            if (firstArea == null)
            {
                firstArea = workspace.Area;
            }
        }

        if (firstArea == null)
        {
            return "/Workspace";
        }
        return "/" + firstArea;
    }

    /// <summary>
    /// Writes one audit row with a direct SQL INSERT. A direct INSERT saves only this row: SaveChanges would also save
    /// any other entity the request happens to be tracking at that moment.
    /// </summary>
    public async Task AuditAsync(string? userId, string action, string method, string? ipAddress, string? detail)
    {
        string newValues = JsonSerializer.Serialize(new { method = method, detail = detail, ip = ipAddress });
        string entityId = userId ?? "unknown";
        DateTime now = DateTime.UtcNow;
        string entityName = nameof(ApplicationUser);

        // ExecuteSqlInterpolatedAsync turns every {value} into a SQL parameter, so this is safe from SQL injection.
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO [AuditLogs] ([UserId], [Action], [EntityName], [EntityId], [NewValues], [CreatedAt]) VALUES ({userId}, {action}, {entityName}, {entityId}, {newValues}, {now})");
    }
}
