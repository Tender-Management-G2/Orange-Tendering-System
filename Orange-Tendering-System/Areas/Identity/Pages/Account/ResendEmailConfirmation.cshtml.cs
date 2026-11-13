using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Orange_Tendering_System.Areas.Identity.Pages.Account;

public class ResendEmailConfirmationModel : PageModel
{
    public IActionResult OnGet() => RedirectToPage("./VerifyEmail");
    public IActionResult OnPost() => RedirectToPage("./VerifyEmail");
}
