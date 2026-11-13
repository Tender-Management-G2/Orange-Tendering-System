using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Orange_Tendering_System.Data;
using Orange_Tendering_System.Models;
using Orange_Tendering_System.Models.Entities;

namespace Orange_Tendering_System.Areas.Identity.Pages.Account;

[Authorize(Roles = SystemRoles.Supplier)]
public class PendingApprovalModel(UserManager<ApplicationUser> users, ApplicationDbContext db) : PageModel
{
    // Both enrollment steps are required; company approval remains a separate decision.
    public int FaceTemplateCount { get; private set; }
    public int PasskeyCount { get; private set; }
    public bool JustRegistered { get; private set; }
    public bool CompanyApproved { get; private set; }

    public async Task<IActionResult> OnGetAsync(bool faceRegistered = false)
    {
        var user = await users.GetUserAsync(User);
        if (user?.SupplierId == null || !user.EmailConfirmed) return Forbid();
        FaceTemplateCount = await db.FaceTemplates.CountAsync(t => t.UserId == user.Id);
        PasskeyCount = (await users.GetPasskeysAsync(user)).Count;
        JustRegistered = faceRegistered && FaceTemplateCount > 0;
        var status = await db.Suppliers.AsNoTracking()
            .Where(s => s.Id == user.SupplierId)
            .Select(s => (SupplierStatus?)s.Status)
            .SingleOrDefaultAsync();
        CompanyApproved = status == SupplierStatus.Approved;
        return status switch
        {
            SupplierStatus.Pending => Page(),
            SupplierStatus.Approved => Page(),
            _ => Forbid()
        };
    }
}
