using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Orange_Tendering_System.Data;
using Orange_Tendering_System.Models;
using Orange_Tendering_System.Models.Entities;
using Orange_Tendering_System.Services.Biometric;

namespace Orange_Tendering_System.Areas.Identity.Pages.Account;

/// <summary>
/// Optional identity step for internal users (owner decision 2026-11-17). ActivateEmployee opens it right after the
/// password is set from the admin's emailed link: the employee chooses Face ID or Fingerprint, or skips. Enrollment
/// uses the existing endpoints (/Biometrics/FaceEnroll*, /Biometrics/PasskeyRegister*, /Biometrics/Phone/Enroll*),
/// which re-check the password. The method then works on the Login page under "Other User".
/// </summary>
[Authorize]
public class EmployeeIdentityModel(
    UserManager<ApplicationUser> users,
    ApplicationDbContext db,
    BiometricSignInService biometric) : PageModel
{
    public int FaceTemplateCount { get; private set; }
    public int PasskeyCount { get; private set; }
    public bool JustActivated { get; private set; }
    public string WorkspaceUrl { get; private set; } = "/Workspace";

    public async Task<IActionResult> OnGetAsync(bool activated = false)
    {
        var user = await users.GetUserAsync(User);
        if (user == null) return Challenge();
        // Suppliers have their own identity step in the registration flow.
        if (user.SupplierId != null || await users.IsInRoleAsync(user, SystemRoles.Supplier))
            return RedirectToPage("./PendingApproval");

        FaceTemplateCount = await db.FaceTemplates.CountAsync(t => t.UserId == user.Id);
        PasskeyCount = (await users.GetPasskeysAsync(user)).Count;
        JustActivated = activated;
        WorkspaceUrl = await biometric.DestinationAsync(user, null, Url.IsLocalUrl);
        return Page();
    }
}
