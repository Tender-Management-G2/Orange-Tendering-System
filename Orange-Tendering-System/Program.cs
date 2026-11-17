using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.EntityFrameworkCore;
using Orange_Tendering_System.Data;
using Orange_Tendering_System.Models.Entities;
using Orange_Tendering_System.Services;
using Orange_Tendering_System.Services.Reports;

var builder = WebApplication.CreateBuilder(args);
// Development launch profiles select the isolated team database (Services/TeamDevDatabase.cs).
// The "https (hosted shared database)" profile switches to the team's hosted SQL Server instead (Services/HostedDatabase.cs).
var hostedDatabase = HostedDatabase.Apply(builder);
if (!hostedDatabase) TeamDevDatabase.Apply(builder);
// One development browser origin for every role, Identity and Executive Agent.
// Keep the historical HTTP listener only to redirect old links to HTTPS.
if (builder.Environment.IsDevelopment())
    builder.Services.AddHttpsRedirection(options => options.HttpsPort = 7182);

// Add services to the container.
var connectionString = builder.Configuration.GetConnectionString("Default Connection") ?? throw new InvalidOperationException("Connection string 'Default Connection' not found.");
builder.Services.AddSignalR();
builder.Services.AddSingleton<Orange_Tendering_System.Services.NotificationPushInterceptor>();
builder.Services.AddSingleton<NotificationTransactionInterceptor>();
builder.Services.AddDbContext<ApplicationDbContext>((services, options) =>
    options.UseSqlServer(connectionString)
        .AddInterceptors(services.GetRequiredService<Orange_Tendering_System.Services.NotificationPushInterceptor>(),
            services.GetRequiredService<NotificationTransactionInterceptor>()));
builder.Services.AddDatabaseDeveloperPageExceptionFilter();

// AddRoles puts the user's Identity roles into the sign-in cookie, which [Authorize(Roles = ...)] on the
// workspace areas relies on. Role names: Models/SystemRoles.cs.
builder.Services.AddDefaultIdentity<ApplicationUser>(options =>
    {
        options.SignIn.RequireConfirmedAccount = true;
        options.User.RequireUniqueEmail = true;
        options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
        options.Lockout.MaxFailedAccessAttempts = 5;
        options.Lockout.AllowedForNewUsers = true;
        options.Stores.SchemaVersion = IdentitySchemaVersions.Version3;
    })
    .AddRoles<IdentityRole>()
    .AddEntityFrameworkStores<ApplicationDbContext>();
builder.Services.Configure<SecurityStampValidatorOptions>(options => options.ValidationInterval = TimeSpan.Zero);
builder.Services.Configure<DataProtectionTokenProviderOptions>(options => options.TokenLifespan = TimeSpan.FromHours(1));
builder.Services.ConfigureApplicationCookie(options =>
{
    options.Cookie.HttpOnly = true;
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.ExpireTimeSpan = TimeSpan.FromHours(8);
    options.SlidingExpiration = true;
    options.Events.OnValidatePrincipal = IdentityAccess.ValidateCookieAsync;
});
builder.Services.AddTransient<SmtpEmailSender>();
builder.Services.AddTransient<IEmailSender>(services => services.GetRequiredService<SmtpEmailSender>());
builder.Services.AddScoped<EmployeeProvisioning>();
builder.Services.AddSingleton<AccountLinkGenerator>();

builder.Services.AddControllersWithViews(options => options.Filters.Add<OrangeMotionResultFilter>());
builder.Services.AddBiometrics(builder.Configuration);
// A tunnel such as ngrok ends HTTPS and forwards plain HTTP to Kestrel on this machine. Its X-Forwarded-For /
// X-Forwarded-Proto are trusted only from the loopback proxy (the framework's default known proxies), so passkeys
// see the real https:// origin and throttling sees the real client address. Host names: AllowedHosts.
builder.Services.Configure<Microsoft.AspNetCore.Builder.ForwardedHeadersOptions>(options =>
    options.ForwardedHeaders = Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedFor |
        Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedProto);
builder.Services.AddScoped<Orange_Tendering_System.Services.SupplierProfileService>();
builder.Services.AddScoped<Orange_Tendering_System.Services.SupplierInvitationService>();
builder.Services.AddScoped<Orange_Tendering_System.Services.SupplierOfferService>();
builder.Services.AddScoped<Orange_Tendering_System.Services.EventSpotlightService>();
builder.Services.AddScoped<Orange_Tendering_System.Services.SupplierCommunicationService>();
builder.Services.AddScoped<Orange_Tendering_System.Services.SupplierDocumentService>();
builder.Services.AddScoped<Orange_Tendering_System.Services.SupplierDashboardService>();
builder.Services.AddScoped<SupplierTenderPortalService>();

builder.Services.Configure<TenderApprovalPolicy>(builder.Configuration.GetSection("TenderApprovalPolicy"));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<TenderWorkflowService>();
builder.Services.AddScoped<PurchaseRequestService>();
builder.Services.AddScoped<InternalDashboardService>();
builder.Services.AddScoped<FinalDecisionService>();
builder.Services.AddScoped<AiChatService>();
builder.Services.Configure<ExecutiveAgentOptions>(builder.Configuration.GetSection("ExecutiveAgent"));
builder.Services.AddHttpClient<ExecutiveAgentClient>();
builder.Services.AddScoped<ExecutiveAgentService>();
builder.Services.AddScoped<ExecutiveNavigationService>();
builder.Services.Configure<ExecutiveSpeechOptions>(builder.Configuration.GetSection("ExecutiveSpeech"));
builder.Services.AddScoped<ExecutiveSpeechService>();
builder.Services.AddScoped<ClarificationService>();
builder.Services.AddScoped<ReportingService>();
builder.Services.AddScoped<ReportExport>();
builder.Services.AddScoped<Orange_Tendering_System.Areas.TopCommittee.Services.TopCommitteeService>();
builder.Services.AddScoped<AdminService>();
builder.Services.AddScoped<SettingsService>();
builder.Services.Configure<ReportSigningOptions>(builder.Configuration.GetSection("ReportSigning"));
builder.Services.Configure<ReportRevisionPolicy>(builder.Configuration.GetSection("ReportRevisionPolicy"));
builder.Services.Configure<ReportPdfOptions>(builder.Configuration.GetSection("ReportPdf"));
builder.Services.AddSingleton<OrangeReportRenderer>();
builder.Services.AddScoped<IReportCertificateProvider, StoreReportCertificateProvider>();
builder.Services.AddScoped<ReportCryptography>();
builder.Services.AddScoped<ReportDocumentService>();
builder.Services.Configure<OllamaSettings>(builder.Configuration.GetSection("Ollama"));
builder.Services.AddHttpClient<OllamaChatClient>();
builder.Services.AddScoped<OllamaBidAnalysisService>();
builder.Services.AddScoped<ProtectedDocumentStore>();
builder.Services.Configure<ZoomOptions>(builder.Configuration.GetSection("Zoom"));
builder.Services.AddHttpClient<ZoomMeetingService>();
builder.Services.AddScoped<EvaluationSchedulingService>();
builder.Services.Configure<ExternalInvitationOptions>(builder.Configuration.GetSection("ExternalInvitations"));
builder.Services.AddSingleton<IExternalInvitationChannel, SmtpInvitationChannel>();
builder.Services.AddScoped<ExternalInvitationDispatcher>();
builder.Services.AddScoped<SupplierReviewService>();
builder.Services.AddHostedService<EvaluationDeadlineWorker>();


var app = builder.Build();

app.UseForwardedHeaders();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseMigrationsEndPoint();
}
else
{
    app.UseExceptionHandler("/Home/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

// Friendly pages for 404 and other empty error responses (HomeController.HttpStatus).
app.UseStatusCodePagesWithReExecute("/status/{0}");

app.UseHttpsRedirection();
app.UseRouting();

app.UseAuthentication();
app.UseMiddleware<FirstLoginGate>();
app.UseMiddleware<SupplierApprovalGate>();
app.UseAuthorization();

app.MapStaticAssets();

// Role workspaces: /Admin, /Department, /SourcingOfficer, /Supplier, /EvaluationCommittee,
// /SourcingManager, /TopCommittee (see docs/ROUTE_MAP.md).
app.MapControllerRoute(
    name: "sourcing-manager-home",
    pattern: "SourcingManager",
    defaults: new { area = "SourcingManager", controller = "ManagerDashboard", action = "Index" })
    .WithStaticAssets();

app.MapControllerRoute(
    name: "areas",
    pattern: "{area:exists}/{controller=Dashboard}/{action=Index}/{id?}")
    .WithStaticAssets();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();

app.MapHub<Orange_Tendering_System.Hubs.NotificationHub>("/hubs/notifications");
app.MapRazorPages()
   .WithStaticAssets();

if (app.Environment.IsDevelopment())
{
    using var migrationScope = app.Services.CreateScope();
    var db = migrationScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    // The hosted database is shared by the team: never migrate it implicitly (HostedDatabase:AutoMigrate to opt in).
    if (!hostedDatabase || app.Configuration.GetValue<bool>("HostedDatabase:AutoMigrate"))
        await db.Database.MigrateAsync();
    else
    {
        var pending = (await db.Database.GetPendingMigrationsAsync()).ToList();
        var hostedLog = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("HostedDatabase");
        if (pending.Count == 0) hostedLog.LogInformation("Hosted database {Database} is up to date.", TeamDevDatabase.Describe(db.Database.GetConnectionString()));
        else hostedLog.LogWarning("Hosted database {Database} is missing {Count} migration(s): {Migrations}. They were NOT applied automatically.",
            TeamDevDatabase.Describe(db.Database.GetConnectionString()), pending.Count, string.Join(", ", pending));
    }
}

await IdentityBootstrap.SeedAsync(app.Services, app.Configuration);

// Shared team accounts and optional demo fixtures: Development AND a local team database only (fixed passwords).
if (app.Environment.IsDevelopment())
{
    var startup = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("TeamDevelopment");
    // The database the app really uses (the DbContext connection), not a configuration value read again later.
    string? effective;
    using (var probe = app.Services.CreateScope())
        effective = probe.ServiceProvider.GetRequiredService<ApplicationDbContext>().Database.GetConnectionString();
    var database = TeamDevDatabase.Describe(effective);
    if (TeamDevDatabase.IsLocalTeamDatabase(effective))
    {
        startup.LogInformation("Development database: {Database} (team database).", database);
        var teamAccounts = app.Configuration.GetValue("TeamAccounts:Enabled", true);
        if (teamAccounts)
        {
            await TeamAccountSeeder.EnsureAsync(app.Services, startup);
            await TeamSupplierSeeder.EnsureAsync(app.Services, startup);
        }
        else startup.LogWarning("Team accounts were NOT provisioned: TeamAccounts:Enabled is false in this run's configuration (environment variable, User Secrets or appsettings). Remove that setting for normal team development.");
        if (app.Configuration.GetValue<bool>("DemoData:Enabled")) await DemoDataSeeder.SeedAsync(app.Services);
        if (teamAccounts)
        {
            await TeamAccountSeeder.VerifyAsync(app.Services, startup, database);
            await TeamSupplierSeeder.VerifyAsync(app.Services, startup, database);
        }
    }
    else
    {
        startup.LogInformation("Development database: {Database}. Not a team database, so team accounts and demo data are not seeded here.", database);
        if (app.Configuration.GetValue<bool>("DemoData:Enabled"))
            startup.LogWarning("DemoData:Enabled is ignored: demo fixtures are only created in the OrangeTenderingTeamDev database.");
    }
}

app.Run();

namespace Orange_Tendering_System { public sealed class WebAppMarker { } }
