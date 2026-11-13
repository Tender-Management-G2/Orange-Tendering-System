using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Orange_Tendering_System.Areas.Identity.Pages.Account;

// The supplier registration flow uses VerifyEmail OTP only.
public class ConfirmEmailModel : PageModel
{
    public IActionResult OnGet() => NotFound();
}
