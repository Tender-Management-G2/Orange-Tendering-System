using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Orange_Tendering_System.Data;
using Orange_Tendering_System.Models;
using Orange_Tendering_System.Models.Biometric;
using Orange_Tendering_System.Models.Entities;
using Orange_Tendering_System.Services.Biometric;

namespace Orange_Tendering_System.Services;

public static class IdentityAccess
{
    /// <summary>
    /// Runs on every request that carries the sign-in cookie (Program.cs: Events.OnValidatePrincipal).
    /// 1. Identity's security-stamp check: a password or role change ends old sessions.
    /// 2. The account must still be allowed: active, email confirmed, and a supplier company that is Pending or Approved
    ///    (or Approved for a non-supplier user linked to a company). Otherwise the cookie is rejected and the user is signed out.
    /// </summary>
    public static async Task ValidateCookieAsync(CookieValidatePrincipalContext context)
    {
        // Remember the sign-in method from the already signed cookie (never from a header or the address).
        string? signInMethod = null;
        if (context.Principal != null)
        {
            signInMethod = context.Principal.FindFirstValue(BiometricSignInService.AuthenticationMethodClaim);
        }

        // Identity's own check. It may replace the principal with a fresh one built from the database.
        ISecurityStampValidator stampValidator = context.HttpContext.RequestServices.GetRequiredService<ISecurityStampValidator>();
        await stampValidator.ValidateAsync(context);

        if (context.Principal == null || context.Principal.Identity == null || !context.Principal.Identity.IsAuthenticated)
        {
            return;
        }

        // A fresh principal does not contain the method claim: copy it back so the session keeps its method.
        bool claimMissing = context.Principal.FindFirst(BiometricSignInService.AuthenticationMethodClaim) == null;
        ClaimsIdentity? identity = context.Principal.Identity as ClaimsIdentity;
        if (BiometricSignInService.IsKnownMethod(signInMethod) && claimMissing && identity != null)
        {
            identity.AddClaim(new Claim(BiometricSignInService.AuthenticationMethodClaim, signInMethod!));
            context.ReplacePrincipal(context.Principal);
            context.ShouldRenew = true;
        }

        // Read the account state again from the database.
        string? userId = context.Principal.FindFirstValue(ClaimTypes.NameIdentifier);
        ApplicationDbContext db = context.HttpContext.RequestServices.GetRequiredService<ApplicationDbContext>();
        ApplicationUser? user = await db.Users.AsNoTracking()
            .Include(u => u.Supplier)
            .SingleOrDefaultAsync(u => u.Id == userId);

        bool allowed = false;
        if (user != null && user.IsActive && user.EmailConfirmed)
        {
            bool companyPendingOrApproved = user.Supplier != null &&
                (user.Supplier.Status == SupplierStatus.Pending || user.Supplier.Status == SupplierStatus.Approved);
            bool companyApproved = user.Supplier != null && user.Supplier.Status == SupplierStatus.Approved;

            if (context.Principal.IsInRole(SystemRoles.Supplier))
            {
                allowed = user.SupplierId != null && companyPendingOrApproved;
            }
            else
            {
                allowed = user.SupplierId == null || companyApproved;
            }
        }

        if (!allowed)
        {
            context.RejectPrincipal();
            await context.HttpContext.SignOutAsync(IdentityConstants.ApplicationScheme);
        }
    }
}

/// <summary>
/// Middleware (Program.cs: app.UseMiddleware). A signed-in supplier may open protected pages and /Supplier only when
///   1. the company is Approved, and
///   2. the session came from Face ID, Fingerprint, or (while Biometric:SupplierPasswordOnly is true) Email &amp; Password.
/// Otherwise a page request goes to Pending Approval and any other request gets 403 Forbidden.
/// </summary>
public sealed class SupplierApprovalGate
{
    // Pages a pending supplier still needs: setting up face / passkey during registration.
    // BiometricsController and PhonePasskeyController re-check the password and the account on each of them.
    private static readonly string[] PendingEnrollmentPaths =
    {
        "/Biometrics/FaceEnrollChallenge", "/Biometrics/FaceEnroll",
        "/Biometrics/FaceSetupChallenge", "/Biometrics/FaceSetupLogin",
        "/Biometrics/PasskeyRegisterOptions", "/Biometrics/PasskeyRegister",
        "/Biometrics/Phone/EnrollStart", "/Biometrics/Phone/EnrollEmail"
    };

    private readonly RequestDelegate next;

    public SupplierApprovalGate(RequestDelegate next)
    {
        this.next = next;
    }

    public async Task InvokeAsync(HttpContext context, ApplicationDbContext db)
    {
        if (!NeedsApprovedSupplier(context))
        {
            await next(context);
            return;
        }

        string? userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
        bool approved = await db.Users.AsNoTracking()
            .AnyAsync(u => u.Id == userId && u.Supplier != null && u.Supplier.Status == SupplierStatus.Approved);

        string? method = context.User.FindFirstValue(BiometricSignInService.AuthenticationMethodClaim);
        bool methodAccepted = false;
        if (method == BiometricSignInService.FaceMethod || method == BiometricSignInService.PasskeyMethod)
        {
            methodAccepted = true;
        }
        if (method == BiometricSignInService.PasswordStepMethod)
        {
            BiometricOptions options = context.RequestServices.GetRequiredService<IOptions<BiometricOptions>>().Value;
            methodAccepted = options.SupplierPasswordOnly;
        }

        if (approved && methodAccepted)
        {
            await next(context);
            return;
        }

        if (HttpMethods.IsGet(context.Request.Method) || HttpMethods.IsHead(context.Request.Method))
        {
            context.Response.Redirect(context.Request.PathBase + "/Identity/Account/PendingApproval");
            return;
        }

        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        if (approved)
        {
            await context.Response.WriteAsync("Supplier biometric sign-in is required.");
        }
        else
        {
            await context.Response.WriteAsync("Supplier approval is required.");
        }
    }

    // True for a signed-in supplier who opens a protected page (or anything under /Supplier),
    // except Pending Approval, Logout and the enrollment endpoints above.
    private static bool NeedsApprovedSupplier(HttpContext context)
    {
        if (context.User.Identity == null || !context.User.Identity.IsAuthenticated)
        {
            return false;
        }
        if (!context.User.IsInRole(SystemRoles.Supplier))
        {
            return false;
        }

        PathString path = context.Request.Path;
        Endpoint? endpoint = context.GetEndpoint();
        bool protectedEndpoint = endpoint != null &&
            endpoint.Metadata.GetMetadata<IAuthorizeData>() != null &&
            endpoint.Metadata.GetMetadata<IAllowAnonymous>() == null;
        bool supplierArea = path.StartsWithSegments("/Supplier", StringComparison.OrdinalIgnoreCase);
        if (!protectedEndpoint && !supplierArea)
        {
            return false;
        }

        if (path.StartsWithSegments("/Identity/Account/PendingApproval", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }
        if (path.StartsWithSegments("/Identity/Account/Logout", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }
        foreach (string allowedPath in PendingEnrollmentPaths)
        {
            if (path.Equals(allowedPath, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }
        return true;
    }
}

/// <summary>
/// Middleware (Program.cs). A user whose account says MustChangePassword (a new employee or the first Admin) is sent to
/// the First Login Password Change page until the password is changed. Logout and static files stay reachable.
/// </summary>
public sealed class FirstLoginGate
{
    private static readonly string[] AllowedPaths =
    {
        "/Identity/Account/FirstLoginPasswordChange", "/Identity/Account/Logout",
        "/css", "/js", "/lib", "/assets", "/favicon.ico"
    };

    private readonly RequestDelegate next;

    public FirstLoginGate(RequestDelegate next)
    {
        this.next = next;
    }

    public async Task InvokeAsync(HttpContext context, UserManager<ApplicationUser> users)
    {
        if (context.User.Identity != null && context.User.Identity.IsAuthenticated)
        {
            ApplicationUser? user = await users.GetUserAsync(context.User);
            if (user != null && user.MustChangePassword && !IsAllowed(context.Request.Path))
            {
                context.Response.Redirect(context.Request.PathBase + "/Identity/Account/FirstLoginPasswordChange");
                return;
            }
        }

        await next(context);
    }

    private static bool IsAllowed(PathString path)
    {
        foreach (string allowedPath in AllowedPaths)
        {
            if (path.StartsWithSegments(allowedPath, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }
}
