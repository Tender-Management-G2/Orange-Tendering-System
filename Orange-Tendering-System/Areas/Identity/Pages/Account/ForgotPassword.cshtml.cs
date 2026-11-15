using System.ComponentModel.DataAnnotations;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.WebUtilities;
using Orange_Tendering_System.Models.Entities;
using Orange_Tendering_System.Services;

namespace Orange_Tendering_System.Areas.Identity.Pages.Account;

public class ForgotPasswordModel(
    UserManager<ApplicationUser> users,
    IEmailSender emailSender,
    AccountLinkGenerator accountLinks,
    IConfiguration configuration,
    ILogger<ForgotPasswordModel> logger) : PageModel
{
    [BindProperty] public InputModel Input { get; set; } = new();
    public class InputModel
    {
        [Required, EmailAddress] public string Email { get; set; } = string.Empty;
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid) return Page();
        if (!SmtpEmailSender.IsConfigured(configuration))
        {
            logger.LogError("Password recovery email delivery is not configured.");
            ModelState.AddModelError(string.Empty, "Email delivery is unavailable. Please contact the administrator.");
            return Page();
        }

        var user = await users.FindByEmailAsync(Input.Email.Trim());
        if (user == null || !await users.IsEmailConfirmedAsync(user) || !user.IsActive)
            return RedirectToPage("./ForgotPasswordConfirmation");

        var code = await users.GeneratePasswordResetTokenAsync(user);
        var encoded = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(code));
        var url = accountLinks.GetUriByPage(HttpContext, "/Account/ResetPassword",
            new { area = "Identity", code = encoded });
        try
        {
            await emailSender.SendEmailAsync(user.Email!, "Reset your password",
                $"<p>Reset your password using <a href=\"{HtmlEncoder.Default.Encode(url)}\">this one-hour link</a>.</p><p>If you did not request this, ignore the message.</p>");
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Password recovery email delivery failed for user {UserId}.", user.Id);
            ModelState.AddModelError(string.Empty, "Email delivery is unavailable. Please contact the administrator.");
            return Page();
        }
        return RedirectToPage("./ForgotPasswordConfirmation");
    }
}
