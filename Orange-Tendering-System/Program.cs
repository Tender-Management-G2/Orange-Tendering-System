
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.EntityFrameworkCore;
using Orange_Tendering_System.Data;
using Orange_Tendering_System.Models;
using Orange_Tendering_System.Models.Entities;
using Orange_Tendering_System.Services;
using Orange_Tendering_System.Services.Reports;

var builder = WebApplication.CreateBuilder(args);

// =====================================================
// DATABASE CONNECTION
// =====================================================

var connectionString = builder.Configuration
    .GetConnectionString("Default Connection")
    ?? throw new InvalidOperationException(
        "Connection string 'Default Connection' not found.");

builder.Services.AddSignalR();

builder.Services.AddSingleton<NotificationPushInterceptor>();

builder.Services.AddDbContext<ApplicationDbContext>((services, options) =>
    options.UseSqlServer(connectionString)
        .AddInterceptors(
            services.GetRequiredService<NotificationPushInterceptor>()));

builder.Services.AddDatabaseDeveloperPageExceptionFilter();


// =====================================================
// ASP.NET CORE IDENTITY
// =====================================================

builder.Services.AddDefaultIdentity<ApplicationUser>(options =>
{
    options.SignIn.RequireConfirmedAccount = true;

    options.User.RequireUniqueEmail = true;

    options.Lockout.DefaultLockoutTimeSpan =
        TimeSpan.FromMinutes(15);

    options.Lockout.MaxFailedAccessAttempts = 5;

    options.Lockout.AllowedForNewUsers = true;

    // Identity V3 supports passkeys
    options.Stores.SchemaVersion =
        IdentitySchemaVersions.Version3;
})
.AddRoles<IdentityRole>()
.AddEntityFrameworkStores<ApplicationDbContext>();

builder.Services.Configure<SecurityStampValidatorOptions>(
    options =>
        options.ValidationInterval = TimeSpan.Zero);

builder.Services.Configure<DataProtectionTokenProviderOptions>(
    options =>
        options.TokenLifespan = TimeSpan.FromHours(1));

builder.Services.ConfigureApplicationCookie(options =>
{
    options.Cookie.HttpOnly = true;

    options.Cookie.SecurePolicy =
        CookieSecurePolicy.Always;

    options.Cookie.SameSite = SameSiteMode.Lax;

    options.ExpireTimeSpan = TimeSpan.FromHours(8);

    options.SlidingExpiration = true;

    options.Events.OnValidatePrincipal =
        IdentityAccess.ValidateCookieAsync;
});


// =====================================================
// EMAIL AND EMPLOYEE SERVICES
// =====================================================

builder.Services.AddTransient<SmtpEmailSender>();

builder.Services.AddTransient<IEmailSender>(services =>
    services.GetRequiredService<SmtpEmailSender>());

builder.Services.AddScoped<EmployeeProvisioning>();

builder.Services.AddControllersWithViews();


// =====================================================
// BIOMETRIC AUTHENTICATION
// =====================================================

builder.Services.AddBiometrics(builder.Configuration);


// =====================================================
// SUPPLIER SERVICES
// =====================================================

builder.Services.AddScoped<SupplierProfileService>();

builder.Services.AddScoped<SupplierInvitationService>();

builder.Services.AddScoped<SupplierOfferService>();

builder.Services.AddScoped<SupplierCommunicationService>();

builder.Services.AddScoped<SupplierDocumentService>();

builder.Services.AddScoped<SupplierDashboardService>();


// =====================================================
// TENDER AND PURCHASE REQUEST SERVICES
// =====================================================

builder.Services.Configure<TenderApprovalPolicy>(
    builder.Configuration.GetSection("TenderApprovalPolicy"));

builder.Services.AddSingleton(TimeProvider.System);

builder.Services.AddScoped<TenderWorkflowService>();

builder.Services.AddScoped<PurchaseRequestService>();

builder.Services.AddScoped<InternalDashboardService>();

builder.Services.AddScoped<FinalDecisionService>();

builder.Services.AddScoped<AiChatService>();

builder.Services.AddScoped<ClarificationService>();


// =====================================================
// REPORTING SERVICES
// =====================================================

builder.Services.AddScoped<ReportingService>();

builder.Services.AddScoped<ReportExport>();

builder.Services.AddScoped<
    Orange_Tendering_System.Areas.TopCommittee.Services.TopCommitteeService>();

builder.Services.Configure<ReportSigningOptions>(
    builder.Configuration.GetSection("ReportSigning"));

builder.Services.Configure<ReportRevisionPolicy>(
    builder.Configuration.GetSection("ReportRevisionPolicy"));

builder.Services.Configure<ReportPdfOptions>(
    builder.Configuration.GetSection("ReportPdf"));

builder.Services.AddSingleton<OrangeReportRenderer>();

builder.Services.AddScoped<
    IReportCertificateProvider,
    StoreReportCertificateProvider>();

builder.Services.AddScoped<ReportCryptography>();

builder.Services.AddScoped<ReportDocumentService>();


// =====================================================
// AI - OLLAMA
// =====================================================

builder.Services.Configure<OllamaSettings>(
    builder.Configuration.GetSection("Ollama"));

builder.Services.AddHttpClient<OllamaChatClient>();

builder.Services.AddScoped<OllamaBidAnalysisService>();


// =====================================================
// DOCUMENTS AND EVALUATION SERVICES
// =====================================================

builder.Services.AddScoped<ProtectedDocumentStore>();

builder.Services.Configure<ZoomOptions>(
    builder.Configuration.GetSection("Zoom"));

builder.Services.AddHttpClient<ZoomMeetingService>();

builder.Services.AddScoped<EvaluationSchedulingService>();

builder.Services.Configure<ExternalInvitationOptions>(
    builder.Configuration.GetSection("ExternalInvitations"));

builder.Services.AddSingleton<
    IExternalInvitationChannel,
    SmtpInvitationChannel>();

builder.Services.AddScoped<ExternalInvitationDispatcher>();

builder.Services.AddScoped<SupplierReviewService>();

builder.Services.AddHostedService<EvaluationDeadlineWorker>();


// =====================================================
// ADMIN SERVICES - SARA RJOUB
// =====================================================

builder.Services.AddScoped<AdminService>();

builder.Services.AddScoped<SettingsService>();


// =====================================================
// BUILD APPLICATION
// =====================================================

var app = builder.Build();


// =====================================================
// HTTP REQUEST PIPELINE
// =====================================================

if (app.Environment.IsDevelopment())
{
    app.UseMigrationsEndPoint();
}
else
{
    app.UseExceptionHandler("/Home/Error");

    app.UseHsts();
}


// =====================================================
// HIDE DEVELOPMENT DEMO ROUTES IN PRODUCTION
// =====================================================

app.Use(async (context, next) =>
{
    if (!app.Environment.IsDevelopment() &&
        (context.Request.Path.StartsWithSegments("/fingerprint-demo") ||
         context.Request.Path.StartsWithSegments("/face-demo")))
    {
        context.Response.StatusCode =
            StatusCodes.Status404NotFound;

        return;
    }

    await next(context);
});


// =====================================================
// ERROR PAGES
// =====================================================

app.UseStatusCodePagesWithReExecute("/status/{0}");


// =====================================================
// MIDDLEWARE
// =====================================================

app.UseHttpsRedirection();

app.UseRouting();

app.UseAuthentication();

app.UseMiddleware<FirstLoginGate>();

app.UseMiddleware<SupplierApprovalGate>();

app.UseAuthorization();

app.MapStaticAssets();


// =====================================================
// AREA ROUTING
// =====================================================

app.MapControllerRoute(
    name: "areas",
    pattern: "{area:exists}/{controller=Dashboard}/{action=Index}/{id?}")
    .WithStaticAssets();


// =====================================================
// DEFAULT ROUTING
// =====================================================

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();


// =====================================================
// SIGNALR NOTIFICATIONS
// =====================================================

app.MapHub<Orange_Tendering_System.Hubs.NotificationHub>(
    "/hubs/notifications");


// =====================================================
// IDENTITY RAZOR PAGES
// =====================================================

app.MapRazorPages()
   .WithStaticAssets();


// =====================================================
// DATABASE MIGRATIONS - DEVELOPMENT ONLY
// =====================================================

if (app.Environment.IsDevelopment())
{
    using var migrationScope = app.Services.CreateScope();

    var db = migrationScope.ServiceProvider
        .GetRequiredService<ApplicationDbContext>();

    await db.Database.MigrateAsync();
}


// =====================================================
// OFFICIAL IDENTITY BOOTSTRAP - MAIN BRANCH
// =====================================================

await IdentityBootstrap.SeedAsync(
    app.Services,
    app.Configuration);


// =====================================================
// OPTIONAL DEMO DATA
// =====================================================

if (app.Environment.IsDevelopment() &&
    app.Configuration.GetValue<bool>("DemoData:Enabled"))
{
    await DemoDataSeeder.SeedAsync(app.Services);
}


// =====================================================
// RUN APPLICATION
// =====================================================

app.Run();

namespace Orange_Tendering_System
{
    public sealed class WebAppMarker
    {
    }
}
