using Microsoft.Extensions.Configuration;
using Orange_Tendering_System.Services;

static class SmtpModeChecks
{
    public static void Run()
    {
        IConfiguration Settings(string mode, string host, string? username = null, string? password = null) =>
            new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Smtp:Mode"] = mode,
                ["Smtp:Host"] = host,
                ["Smtp:Port"] = "587",
                ["Smtp:From"] = "sender@example.test",
                ["Smtp:Username"] = username,
                ["Smtp:Password"] = password,
                ["Smtp:AllowInsecureLoopback"] = "true"
            }).Build();

        if (!SmtpEmailSender.IsConfigured(Settings("LocalInbox", "127.0.0.1")) ||
            SmtpEmailSender.IsConfigured(Settings("Smtp", "127.0.0.1")) ||
            SmtpEmailSender.IsConfigured(Settings("LocalInbox", "smtp.example.test", "sender@example.test", "unused")) ||
            SmtpEmailSender.IsConfigured(Settings("Smtp", "smtp.example.test")) ||
            !SmtpEmailSender.IsConfigured(Settings("Smtp", "smtp.example.test", "sender@example.test", "unused")) ||
            SmtpEmailSender.IsConfigured(Settings("Unknown", "smtp.example.test", "sender@example.test", "unused")))
            throw new InvalidOperationException("SMTP mode isolation failed.");

        Console.WriteLine("SMTP mode isolation checks passed; no email was sent.");
    }
}
