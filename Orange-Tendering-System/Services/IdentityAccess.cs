using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
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
                ? user.SupplierId != null && user.SupplierStatus == SupplierStatus.Approved
                : user.SupplierId == null || user.SupplierStatus == SupplierStatus.Approved))
            return;

        context.RejectPrincipal();
        await context.HttpContext.SignOutAsync(IdentityConstants.ApplicationScheme);
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
