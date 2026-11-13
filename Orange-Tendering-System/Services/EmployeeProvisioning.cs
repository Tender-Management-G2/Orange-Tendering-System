using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Orange_Tendering_System.Data;
using Orange_Tendering_System.Models;
using Orange_Tendering_System.Models.Entities;

namespace Orange_Tendering_System.Services;

// Called by Rjoub's Admin user-creation POST. No public employee creation endpoint exists.
public sealed class EmployeeProvisioning(
    ApplicationDbContext db,
    UserManager<ApplicationUser> users,
    IEmailSender emailSender,
    LinkGenerator links)
{
    public async Task<IdentityResult> CreateAsync(HttpContext context, string fullName, string email,
        int departmentId, IReadOnlyCollection<string> roles)
    {
        if (!context.User.IsInRole(SystemRoles.Admin))
            throw new UnauthorizedAccessException("Only Admin may provision employees.");
        if (string.IsNullOrWhiteSpace(fullName) || string.IsNullOrWhiteSpace(email) ||
            roles.Count == 0 || roles.Any(r => r == SystemRoles.Supplier || !SystemRoles.All.Contains(r)) ||
            !await db.Departments.AnyAsync(d => d.Id == departmentId))
            return IdentityResult.Failed(new IdentityError { Code = "InvalidEmployee", Description = "Employee details are invalid." });

        await using var transaction = await db.Database.BeginTransactionAsync();
        var user = new ApplicationUser
        {
            UserName = email.Trim(), Email = email.Trim(), FullName = fullName.Trim(),
            DepartmentId = departmentId, IsActive = true, MustChangePassword = true
        };
        var created = await users.CreateAsync(user);
        if (!created.Succeeded) return created;
        var assigned = await users.AddToRolesAsync(user, roles);
        if (!assigned.Succeeded) return assigned;

        var token = await users.GeneratePasswordResetTokenAsync(user);
        var code = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));
        var url = links.GetUriByPage(context, "/Account/ActivateEmployee", values: new
        {
            area = "Identity", email = user.Email, code
        });
        if (url == null) throw new InvalidOperationException("Could not generate employee activation URL.");

        await emailSender.SendEmailAsync(user.Email!, "Activate your Orange Tendering account",
            $"<p>An administrator created an employee account for you.</p><p><a href=\"{HtmlEncoder.Default.Encode(url)}\">Set your private password</a>. This link expires after one hour.</p>");
        db.AuditLogs.Add(new AuditLog
        {
            UserId = context.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value,
            Action = "EmployeeProvisioned", EntityName = "ApplicationUser",
            EntityId = user.Id, CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
        return IdentityResult.Success;
    }
}
