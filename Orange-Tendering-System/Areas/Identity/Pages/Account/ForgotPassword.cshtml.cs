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

/// <summary>
/// Step 1 of password recovery: email a one-hour reset link (Program.cs: TokenLifespan = 1 hour) to an active,
/// confirmed account. The answer is the same for every email, so the page does not reveal which accounts exist.
/// </summary>
public class ForgotPasswordModel : PageModel
{
    private readonly UserManager<ApplicationUser> users;
    private readonly IEmailSender emailSender;
    private readonly AccountLinkGenerator accountLinks;
    private readonly IConfiguration configuration;
    private readonly ILogger<ForgotPasswordModel> logger;

    public ForgotPasswordModel(
        UserManager<ApplicationUser> users,
        IEmailSender emailSender,
        AccountLinkGenerator accountLinks,
        IConfiguration configuration,
        ILogger<ForgotPasswordModel> logger)
    {
        this.users = users;
        this.emailSender = emailSender;
        this.accountLinks = accountLinks;
        this.configuration = configuration;
        this.logger = logger;
    }

    [BindProperty]
    public InputModel Input { get; set; } = new InputModel();

    public class InputModel
    {
        [Required, EmailAddress]
        public string Email { get; set; } = string.Empty;
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid)
        {
            return Page();
        }
        if (!SmtpEmailSender.IsConfigured(configuration))
        {
            logger.LogError("Password recovery email delivery is not configured.");
            ModelState.AddModelError(string.Empty, "Email delivery is unavailable. Please contact the administrator.");
            return Page();
        }

        ApplicationUser? user = await users.FindByEmailAsync(Input.Email.Trim());
        if (user == null || !await users.IsEmailConfirmedAsync(user) || !user.IsActive)
        {
            return RedirectToPage("./ForgotPasswordConfirmation");
        }

        // Identity makes the reset code; it is Base64url-encoded so it can travel inside a link.
        string code = await users.GeneratePasswordResetTokenAsync(user);
        string encodedCode = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(code));
        string url = accountLinks.GetUriByPage(HttpContext, "/Account/ResetPassword", new { area = "Identity", code = encodedCode });

        try
        {
            await emailSender.SendEmailAsync(user.Email!, "Reset your password",
                "<p>Reset your password using <a href=\"" + HtmlEncoder.Default.Encode(url) + "\">this one-hour link</a>.</p><p>If you did not request this, ignore the message.</p>");
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
