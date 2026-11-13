using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Orange_Tendering_System.Data;
using Orange_Tendering_System.Models.Entities;
using Orange_Tendering_System.Services;// sara rjoub
using Orange_Tendering_System.Models; // sara rjoub 

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
var connectionString = builder.Configuration.GetConnectionString("Default Connection") ?? throw new InvalidOperationException("Connection string 'Default Connection' not found.");
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(connectionString));
builder.Services.AddDatabaseDeveloperPageExceptionFilter();

// AddRoles puts the user's Identity roles into the sign-in cookie, which [Authorize(Roles = ...)] on the
// workspace areas relies on. Role names: Models/SystemRoles.cs.
builder.Services.AddDefaultIdentity<ApplicationUser>(options => options.SignIn.RequireConfirmedAccount = true)
    .AddRoles<IdentityRole>()
    .AddEntityFrameworkStores<ApplicationDbContext>();
builder.Services.AddControllersWithViews();
builder.Services.AddScoped<AdminService>(); // sara rjoub
builder.Services.AddScoped<SettingsService>(); // sara rjoub

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
    using var scope = app.Services.CreateScope();

    var roleManager = scope.ServiceProvider
        .GetRequiredService<RoleManager<IdentityRole>>();

    var userManager = scope.ServiceProvider
        .GetRequiredService<UserManager<ApplicationUser>>();

    // Create Admin role if missing
    //if (!await roleManager.RoleExistsAsync(SystemRoles.Admin))
    //  {
    //  var roleResult = await roleManager.CreateAsync(
    //  new IdentityRole(SystemRoles.Admin));

    //if (!roleResult.Succeeded)
    // {
    // throw new InvalidOperationException(
    //    string.Join("; ",
    //      roleResult.Errors.Select(e => e.Description)));
    //  }
    // }

    // Create all predefined system roles for local testing
    foreach (var roleName in SystemRoles.All)
    {
        if (!await roleManager.RoleExistsAsync(roleName))
        {
            var roleResult = await roleManager.CreateAsync(
                new IdentityRole(roleName));

            if (!roleResult.Succeeded)
            {
                throw new InvalidOperationException(
                    string.Join("; ",
                        roleResult.Errors.Select(e => e.Description)));
            }
        }
    }

    // Local-only test account
    // sara rjoub 
    var testEmail = "admin.test@example.com";

    var testPassword = builder.Configuration[
        "DevelopmentTestAdmin:Password"];

    if (string.IsNullOrWhiteSpace(testPassword))
    {
        throw new InvalidOperationException(
            "Set DevelopmentTestAdmin:Password in user-secrets first.");
    }

    var admin = await userManager.FindByEmailAsync(testEmail);

    if (admin == null)
    {
        admin = new ApplicationUser
        {
            UserName = testEmail,
            Email = testEmail,
            FullName = "Test Administrator",
            IsActive = true,
            EmailConfirmed = true
        };

        var createResult = await userManager.CreateAsync(
            admin, testPassword);

        if (!createResult.Succeeded)
        {
            throw new InvalidOperationException(
                string.Join("; ",
                    createResult.Errors.Select(e => e.Description)));
        }
    }

    if (!await userManager.IsInRoleAsync(admin, SystemRoles.Admin))
    {
        var assignResult = await userManager.AddToRoleAsync(
            admin, SystemRoles.Admin);

        if (!assignResult.Succeeded)
        {
            throw new InvalidOperationException(
                string.Join("; ",
                    assignResult.Errors.Select(e => e.Description)));
        }
    }
}


app.Run();
