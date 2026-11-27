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
/// Optional identity step for internal users. ActivateEmployee opens it right after the first password is set: the
/// employee sets up Face ID or Fingerprint, or skips. Enrollment uses the same endpoints as suppliers
/// (/Biometrics/FaceEnroll*, /Biometrics/PasskeyRegister*, /Biometrics/Phone/Enroll*), which re-check the password.
/// </summary>
[Authorize]
public class EmployeeIdentityModel : PageModel
{
    private readonly UserManager<ApplicationUser> users;
    private readonly ApplicationDbContext db;
    private readonly BiometricSignInService biometric;

    public EmployeeIdentityModel(UserManager<ApplicationUser> users, ApplicationDbContext db, BiometricSignInService biometric)
    {
        this.users = users;
        this.db = db;
        this.biometric = biometric;
    }

    public int FaceTemplateCount { get; set; }
    public int PasskeyCount { get; set; }
    public bool JustActivated { get; set; }
    public string WorkspaceUrl { get; set; } = "/Workspace";

    public async Task<IActionResult> OnGetAsync(bool activated = false)
    {
        ApplicationUser? user = await users.GetUserAsync(User);
        if (user == null)
        {
            return Challenge();
        }

        // Suppliers have their own identity step in the registration flow.
        if (user.SupplierId != null || await users.IsInRoleAsync(user, SystemRoles.Supplier))
        {
            return RedirectToPage("./PendingApproval");
        }

        FaceTemplateCount = await db.FaceTemplates.CountAsync(t => t.UserId == user.Id);
        PasskeyCount = (await users.GetPasskeysAsync(user)).Count;
        JustActivated = activated;
        WorkspaceUrl = await biometric.DestinationAsync(user, null, Url);
        return Page();
    }
}
