using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Orange_Tendering_System.Data;
using Orange_Tendering_System.Models;
using Orange_Tendering_System.ViewModels;
using Orange_Tendering_System.Services;

static class SmtpLiveChecks
{
    public static async Task Run()
    {
        var root = Environment.GetEnvironmentVariable("AUTH_PROJECT_ROOT")
            ?? throw new InvalidOperationException("Set AUTH_PROJECT_ROOT to the web project directory.");
        var address = Environment.GetEnvironmentVariable("SMTP_LIVE_TEST_TO")
            ?? throw new InvalidOperationException("Set SMTP_LIVE_TEST_TO to the sender's own address.");
        var configuration = new ConfigurationBuilder()
            .SetBasePath(root)
            .AddJsonFile("appsettings.json")
            .AddJsonFile("appsettings.Development.json", optional: true)
            .AddUserSecrets<Orange_Tendering_System.WebAppMarker>()
            .AddEnvironmentVariables()
            .Build();

        if (!address.Equals(configuration["Smtp:From"], StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Live SMTP test may only send to its configured sender address.");
        if (!SmtpEmailSender.IsConfigured(configuration))
            throw new InvalidOperationException("SMTP sender is not configured.");

        await new SmtpEmailSender(configuration).SendEmailAsync(address,
            "Orange Tendering email delivery test",
            "<p>This message confirms that Orange Tendering can hand mail to its configured Gmail SMTP sender.</p>");
        Console.WriteLine("SMTP accepted the single test message addressed to the configured sender.");
    }

    public static async Task RunCreate()
    {
        var root = Environment.GetEnvironmentVariable("AUTH_PROJECT_ROOT")
            ?? throw new InvalidOperationException("Set AUTH_PROJECT_ROOT.");
        var connection = Environment.GetEnvironmentVariable("AUTH_TEST_CONNECTION")
            ?? throw new InvalidOperationException("Set AUTH_TEST_CONNECTION.");
        if (!new SqlConnectionStringBuilder(connection).InitialCatalog.StartsWith(
            "OrangeAuthCheck_LiveEmail_", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Live email check requires an isolated database.");
        var address = Environment.GetEnvironmentVariable("SMTP_LIVE_TEST_TO")
            ?? throw new InvalidOperationException("Set SMTP_LIVE_TEST_TO.");
        var configuration = new ConfigurationBuilder().SetBasePath(root)
            .AddJsonFile("appsettings.json")
            .AddJsonFile("appsettings.Development.json")
            .AddUserSecrets<Orange_Tendering_System.WebAppMarker>()
            .Build();
        if (!address.Equals(configuration["Smtp:From"], StringComparison.OrdinalIgnoreCase) ||
            !SmtpEmailSender.IsConfigured(configuration))
            throw new InvalidOperationException("Live check may only create a user for its configured SMTP sender.");

        var adminEmail = "live-email-admin-" + Guid.NewGuid().ToString("N") + "@example.test";
        Environment.SetEnvironmentVariable("ConnectionStrings__Default Connection", connection);
        Environment.SetEnvironmentVariable("BootstrapAdmin__Email", adminEmail);
        Environment.SetEnvironmentVariable("BootstrapAdmin__Password", "Aa!9" + Guid.NewGuid().ToString("N"));
        Environment.SetEnvironmentVariable("DemoData__Enabled", "false");
        using var factory = new WebApplicationFactory<Orange_Tendering_System.WebAppMarker>()
            .WithWebHostBuilder(builder => builder.UseContentRoot(root).UseEnvironment("Development"));
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var adminId = await db.Users.Where(u => u.Email == adminEmail).Select(u => u.Id).SingleAsync();
        var context = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
        context.Request.Scheme = "https";
        context.Request.Host = new HostString("localhost");
        var result = await scope.ServiceProvider.GetRequiredService<AdminService>().CreateUserAsync(
            new CreateUserViewModel { FullName = "Orange Tendering SMTP Test",
                Email = address, Role = SystemRoles.Technical, IsActive = false,
                Password = "Tt!9" + Guid.NewGuid().ToString("N") },
            adminId, context);
        if (!result.Succeeded || !await db.Users.AnyAsync(u => u.Email == address))
            throw new InvalidOperationException("Admin user creation or welcome email delivery failed: " +
                string.Join("; ", result.Errors.Select(error => error.Description)));
        Console.WriteLine("Admin Create User created the isolated account and Gmail SMTP accepted its welcome message.");
    }
}
