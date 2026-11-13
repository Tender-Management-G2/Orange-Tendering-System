using System.Net;
using System.Net.Mail;
using Microsoft.AspNetCore.Identity.UI.Services;

namespace Orange_Tendering_System.Services;

public sealed class SmtpEmailSender(IConfiguration configuration) : IEmailSender
{
    public static bool IsLocalInbox(IConfiguration configuration) =>
        string.Equals(configuration["Smtp:Mode"], "LocalInbox", StringComparison.OrdinalIgnoreCase);

    private static bool IsLoopback(string host) =>
        host.Equals("localhost", StringComparison.OrdinalIgnoreCase) || host is "127.0.0.1" or "::1";

    public static bool IsConfigured(IConfiguration configuration)
    {
        var mode = configuration["Smtp:Mode"];
        var host = configuration["Smtp:Host"];
        var username = configuration["Smtp:Username"];
        var password = configuration["Smtp:Password"];
        if (string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(configuration["Smtp:From"]) ||
            !int.TryParse(configuration["Smtp:Port"], out var port) || port is < 1 or > 65535 ||
            (string.IsNullOrWhiteSpace(username) != string.IsNullOrWhiteSpace(password)))
            return false;

        if (IsLocalInbox(configuration))
            return IsLoopback(host) && string.IsNullOrWhiteSpace(username) &&
                configuration.GetValue<bool>("Smtp:AllowInsecureLoopback");

        // Explicit SMTP mode requires credentials. An omitted mode retains legacy relay settings.
        if (mode?.Equals("Smtp", StringComparison.OrdinalIgnoreCase) == true)
            return !IsLoopback(host) && !string.IsNullOrWhiteSpace(username);
        return string.IsNullOrWhiteSpace(mode) && !IsLoopback(host);
    }

    public async Task SendEmailAsync(string email, string subject, string htmlMessage)
    {
        if (!IsConfigured(configuration))
            throw new InvalidOperationException("SMTP email delivery is not configured.");

        using var message = new MailMessage(configuration["Smtp:From"]!, email, subject, htmlMessage)
        {
            IsBodyHtml = true
        };
        var host = configuration["Smtp:Host"]!;
        var username = configuration["Smtp:Username"];
        // Plaintext is allowed only when explicitly selecting the unauthenticated loopback inbox.
        var localMailCatcher = IsLocalInbox(configuration);
        using var client = new SmtpClient(host, int.Parse(configuration["Smtp:Port"]!))
        {
            EnableSsl = !localMailCatcher,
            DeliveryMethod = SmtpDeliveryMethod.Network
        };
        if (!string.IsNullOrWhiteSpace(username))
            client.Credentials = new NetworkCredential(username, configuration["Smtp:Password"]);
        await client.SendMailAsync(message);
    }
}
