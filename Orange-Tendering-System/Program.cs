using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.EntityFrameworkCore;
using Orange_Tendering_System.Data;
using Orange_Tendering_System.Models.Entities;
using Orange_Tendering_System.Services;
using Orange_Tendering_System.Services.Reports;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
var connectionString = builder.Configuration.GetConnectionString("Default Connection") ?? throw new InvalidOperationException("Connection string 'Default Connection' not found.");
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(connectionString));
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
builder.Services.AddControllersWithViews();
builder.Services.AddScoped<Orange_Tendering_System.Services.SupplierProfileService>();
builder.Services.AddScoped<Orange_Tendering_System.Services.SupplierInvitationService>();
builder.Services.AddScoped<Orange_Tendering_System.Services.SupplierOfferService>();
builder.Services.AddScoped<Orange_Tendering_System.Services.SupplierCommunicationService>();
builder.Services.AddScoped<Orange_Tendering_System.Services.SupplierDocumentService>();
builder.Services.AddScoped<Orange_Tendering_System.Services.SupplierDashboardService>();

builder.Services.Configure<TenderApprovalPolicy>(builder.Configuration.GetSection("TenderApprovalPolicy"));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<TenderWorkflowService>();
builder.Services.Configure<ReportSigningOptions>(builder.Configuration.GetSection("ReportSigning"));
builder.Services.Configure<ReportRevisionPolicy>(builder.Configuration.GetSection("ReportRevisionPolicy"));
builder.Services.Configure<ReportPdfOptions>(builder.Configuration.GetSection("ReportPdf"));
builder.Services.AddSingleton<OrangeReportRenderer>();
builder.Services.AddScoped<IReportCertificateProvider, StoreReportCertificateProvider>();
builder.Services.AddScoped<ReportCryptography>();
builder.Services.AddScoped<ReportDocumentService>();
builder.Services.Configure<OllamaSettings>(builder.Configuration.GetSection("Ollama"));
builder.Services.AddHttpClient<OllamaBidAnalysisService>();
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
app.UseAuthorization();

app.MapStaticAssets();

// Role workspaces: /Admin, /Department, /SourcingOfficer, /Supplier, /EvaluationCommittee,
// /SourcingManager, /TopCommittee (see docs/ROUTE_MAP.md).
app.MapControllerRoute(
    name: "areas",
    pattern: "{area:exists}/{controller=Dashboard}/{action=Index}/{id?}")
    .WithStaticAssets();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();

app.MapRazorPages()
   .WithStaticAssets();

if (app.Environment.IsDevelopment())
{
    using var migrationScope = app.Services.CreateScope();
    var db = migrationScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    await db.Database.MigrateAsync();
}

await IdentityBootstrap.SeedAsync(app.Services, app.Configuration);
if (app.Environment.IsDevelopment() && app.Configuration.GetValue<bool>("DemoData:Enabled"))
    await DemoDataSeeder.SeedAsync(app.Services);

app.Run();

namespace Orange_Tendering_System { public sealed class WebAppMarker { } }
