using System.Net;
using System.Net.Mail;
using Microsoft.AspNetCore.Identity.UI.Services;

namespace Orange_Tendering_System.Services;

public sealed class SmtpEmailSender(IConfiguration configuration) : IEmailSender
{
    public static bool IsConfigured(IConfiguration configuration) =>
        !string.IsNullOrWhiteSpace(configuration["Smtp:Host"]) &&
        !string.IsNullOrWhiteSpace(configuration["Smtp:From"]) &&
        int.TryParse(configuration["Smtp:Port"], out var port) && port is > 0 and <= 65535 &&
        (string.IsNullOrWhiteSpace(configuration["Smtp:Username"]) ==
         string.IsNullOrWhiteSpace(configuration["Smtp:Password"]));

    public async Task SendEmailAsync(string email, string subject, string htmlMessage)
    {
        if (!IsConfigured(configuration))
            throw new InvalidOperationException("SMTP email delivery is not configured.");

        using var message = new MailMessage(configuration["Smtp:From"]!, email, subject, htmlMessage)
        {
            IsBodyHtml = true
        };
        using var client = new SmtpClient(configuration["Smtp:Host"]!, int.Parse(configuration["Smtp:Port"]!))
        {
            EnableSsl = true,
            DeliveryMethod = SmtpDeliveryMethod.Network
        };
        var username = configuration["Smtp:Username"];
        if (!string.IsNullOrWhiteSpace(username))
            client.Credentials = new NetworkCredential(username, configuration["Smtp:Password"]);
        await client.SendMailAsync(message);
    }
}
