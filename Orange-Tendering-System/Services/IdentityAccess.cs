using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Orange_Tendering_System.Data;
using Orange_Tendering_System.Models;
using Orange_Tendering_System.Models.Entities;

namespace Orange_Tendering_System.Services;

public static class IdentityAccess
{
    public static async Task ValidateCookieAsync(CookieValidatePrincipalContext context)
    {
        // Keep Identity's security-stamp validation, including role and password changes.
        await context.HttpContext.RequestServices.GetRequiredService<ISecurityStampValidator>().ValidateAsync(context);
        if (context.Principal?.Identity?.IsAuthenticated != true)
            return;

        var userId = context.Principal.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        var db = context.HttpContext.RequestServices.GetRequiredService<ApplicationDbContext>();
        var user = await db.Users.AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => new { u.IsActive, u.EmailConfirmed, u.SupplierId, SupplierStatus = u.Supplier == null ? null : (SupplierStatus?)u.Supplier.Status })
            .SingleOrDefaultAsync();

        if (user is { IsActive: true, EmailConfirmed: true } &&
            (context.Principal.IsInRole(SystemRoles.Supplier)
                ? user.SupplierId != null && (user.SupplierStatus is SupplierStatus.Pending or SupplierStatus.Approved)
                : user.SupplierId == null || user.SupplierStatus == SupplierStatus.Approved))
            return;

        context.RejectPrincipal();
        await context.HttpContext.SignOutAsync(IdentityConstants.ApplicationScheme);
    }
}

// Pending suppliers have an Identity cookie, but protected workspaces are for approved suppliers.
public sealed class SupplierApprovalGate(RequestDelegate next)
{
    // Registration step: a verified pending supplier sets up face / passkey sign-in from the Pending Approval page.
    // BiometricsController re-checks the password and account state on each of these.
    private static readonly string[] PendingEnrollmentPaths =
    [
        "/Biometrics/FaceEnrollChallenge", "/Biometrics/FaceEnroll",
        "/Biometrics/PasskeyRegisterOptions", "/Biometrics/PasskeyRegister"
    ];

    public async Task InvokeAsync(HttpContext context, ApplicationDbContext db)
    {
        var endpoint = context.GetEndpoint();
        var protectedEndpoint = endpoint?.Metadata.GetMetadata<IAuthorizeData>() != null &&
            endpoint.Metadata.GetMetadata<IAllowAnonymous>() == null;
        var path = context.Request.Path;
        if ((protectedEndpoint || path.StartsWithSegments("/Supplier", StringComparison.OrdinalIgnoreCase)) &&
            context.User.Identity?.IsAuthenticated == true &&
            context.User.IsInRole(SystemRoles.Supplier) &&
            !path.StartsWithSegments("/Identity/Account/PendingApproval", StringComparison.OrdinalIgnoreCase) &&
            !path.StartsWithSegments("/Identity/Account/Logout", StringComparison.OrdinalIgnoreCase) &&
            !PendingEnrollmentPaths.Any(p => path.Equals(p, StringComparison.OrdinalIgnoreCase)))
        {
            var userId = context.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            var approved = await db.Users.AsNoTracking().AnyAsync(u => u.Id == userId &&
                u.Supplier != null && u.Supplier.Status == SupplierStatus.Approved);
            if (!approved)
            {
                if (HttpMethods.IsGet(context.Request.Method) || HttpMethods.IsHead(context.Request.Method))
                    context.Response.Redirect(context.Request.PathBase + "/Identity/Account/PendingApproval");
                else
                {
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    await context.Response.WriteAsync("Supplier approval is required.");
                }
                return;
            }
        }

        await next(context);
    }
}

public sealed class FirstLoginGate(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, UserManager<ApplicationUser> users)
    {
        if (context.User.Identity?.IsAuthenticated == true)
        {
            var user = await users.GetUserAsync(context.User);
            if (user?.MustChangePassword == true && !Allowed(context.Request.Path))
            {
                context.Response.Redirect(context.Request.PathBase + "/Identity/Account/FirstLoginPasswordChange");
                return;
            }
        }

        await next(context);
    }

    private static bool Allowed(PathString path) =>
        path.StartsWithSegments("/Identity/Account/FirstLoginPasswordChange", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWithSegments("/Identity/Account/Logout", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWithSegments("/css", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWithSegments("/js", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWithSegments("/lib", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWithSegments("/assets", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWithSegments("/favicon.ico", StringComparison.OrdinalIgnoreCase);
}
