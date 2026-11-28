using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Orange_Tendering_System.Data;
using Orange_Tendering_System.Models;
using Orange_Tendering_System.Models.Entities;
using Orange_Tendering_System.Services;
using Orange_Tendering_System.Services.Biometric;

namespace Orange_Tendering_System.Areas.Identity.Pages.Account;

/// <summary>
/// The supplier's page after email verification: enroll a face or a phone / passkey, then wait for the administrator.
/// An approved supplier who signed in with a sign-in method goes straight to the supplier workspace.
/// </summary>
[Authorize(Roles = SystemRoles.Supplier)]
public class PendingApprovalModel : PageModel
{
    private readonly UserManager<ApplicationUser> users;
    private readonly ApplicationDbContext db;
    private readonly SignInManager<ApplicationUser> signInManager;

    public PendingApprovalModel(UserManager<ApplicationUser> users, ApplicationDbContext db, SignInManager<ApplicationUser> signInManager)
    {
        this.users = users;
        this.db = db;
        this.signInManager = signInManager;
    }

    public int FaceTemplateCount { get; set; }
    public int PasskeyCount { get; set; }
    public bool JustRegistered { get; set; }
    public bool CompanyApproved { get; set; }

    // GET /Identity/Account/PendingApproval (faceRegistered=true right after a face enrollment)
    public async Task<IActionResult> OnGetAsync(bool faceRegistered = false)
    {
        ApplicationUser? user = await users.GetUserAsync(User);
        if (user == null || user.SupplierId == null || !user.EmailConfirmed)
        {
            return Forbid();
        }

        FaceTemplateCount = await db.FaceTemplates.CountAsync(t => t.UserId == user.Id);
        PasskeyCount = (await users.GetPasskeysAsync(user)).Count;
        JustRegistered = faceRegistered && FaceTemplateCount > 0;

        SupplierStatus? status = await db.Suppliers.AsNoTracking()
            .Where(s => s.Id == user.SupplierId)
            .Select(s => (SupplierStatus?)s.Status)
            .SingleOrDefaultAsync();
        CompanyApproved = status == SupplierStatus.Approved;

        string? method = User.FindFirstValue(BiometricSignInService.AuthenticationMethodClaim);
        if (CompanyApproved && BiometricSignInService.IsKnownMethod(method))
        {
            return LocalRedirect("/Supplier");
        }

        // Rejected and Suspended companies cannot use this page.
        if (status == SupplierStatus.Pending || status == SupplierStatus.Approved)
        {
            return Page();
        }
        return Forbid();
    }

    // GET /Identity/Account/PendingApproval?handler=Enrolled — after a face or phone enrollment the supplier
    // signs out and starts again from the sign-in page.
    public async Task<IActionResult> OnGetEnrolledAsync()
    {
        await signInManager.SignOutAsync();
        TempData.Orange(OrangeFeedback.Popup("Identity enrolled", "Sign in with your email and password, then confirm with your face or fingerprint.", OrangeVariant.Approve));
        return RedirectToPage("./Login");
    }
}
