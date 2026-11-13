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
    public async Task<IActionResult> OnGetAsync()
    {
        var user = await users.GetUserAsync(User);
        if (user?.SupplierId == null || !user.EmailConfirmed) return Forbid();
        var status = await db.Suppliers.AsNoTracking()
            .Where(s => s.Id == user.SupplierId)
            .Select(s => (SupplierStatus?)s.Status)
            .SingleOrDefaultAsync();
        return status switch
        {
            SupplierStatus.Pending => Page(),
            SupplierStatus.Approved => Redirect("/Supplier"),
            _ => Forbid()
        };
    }
}
