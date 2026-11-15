using System.Net.Sockets;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.AspNetCore.Identity.UI.Services;
using MimeKit;

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

        using var message = new MimeMessage();
        message.From.Add(MailboxAddress.Parse(configuration["Smtp:From"]!));
        message.To.Add(MailboxAddress.Parse(email));
        message.Subject = subject;
        message.Body = new TextPart("html") { Text = htmlMessage };

        var host = configuration["Smtp:Host"]!;
        var port = int.Parse(configuration["Smtp:Port"]!);
        var username = configuration["Smtp:Username"];
        // Plaintext is allowed only when explicitly selecting the unauthenticated loopback inbox.
        // Port 465 uses implicit TLS (System.Net.Mail cannot); other ports require STARTTLS.
        var security = IsLocalInbox(configuration) ? SecureSocketOptions.None
            : port == 465 ? SecureSocketOptions.SslOnConnect
            : SecureSocketOptions.StartTls;

        // Callers run this inside a database transaction, so fail fast instead of the 100 s default.
        using var client = new SmtpClient { Timeout = 30_000 };
        try
        {
            await client.ConnectAsync(host, port, security);
            if (!string.IsNullOrWhiteSpace(username))
                await client.AuthenticateAsync(username, configuration["Smtp:Password"]!);
            await client.SendAsync(new MailKit.SendRequest(message));
            await client.DisconnectAsync(true);
        }
        catch (Exception exception) when (exception is MailKit.ProtocolException or MailKit.CommandException
            or MailKit.Security.AuthenticationException or System.Security.Authentication.AuthenticationException
            or IOException or SocketException or TimeoutException or OperationCanceledException)
        {
            // Callers handle System.Net.Mail.SmtpException; keep that contract.
            var status = exception is SmtpCommandException command
                ? (System.Net.Mail.SmtpStatusCode)(int)command.StatusCode
                : System.Net.Mail.SmtpStatusCode.GeneralFailure;
            throw new System.Net.Mail.SmtpException(status, exception.Message);
        }
    }
}
