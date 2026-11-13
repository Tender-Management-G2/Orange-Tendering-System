using System.Text;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Orange_Tendering_System;
using Orange_Tendering_System.Data;
using Orange_Tendering_System.Models.Entities;

// Explicit isolated test-fixture provisioning, never loaded by the production application.
// Tokens are issued by the real Identity services. No email sender or application service is substituted.
public static class BrowserProvisioning
{
    public static async Task Run()
    {
        var path = Environment.GetEnvironmentVariable("ORANGE_E2E_PRIVATE_STATE") ?? throw new InvalidOperationException("Private state path required.");
        var state = JsonNode.Parse(await File.ReadAllTextAsync(path))!;
        var connection = state["connection"]!.GetValue<string>();
        if (!new SqlConnectionStringBuilder(connection).InitialCatalog.StartsWith("OrangeAuthCheck_BrowserE2E_")) throw new InvalidOperationException("Isolated browser SQL database required.");
        var root = state["root"]!.GetValue<string>();
        Environment.SetEnvironmentVariable("ConnectionStrings__Default Connection", connection);
        Environment.SetEnvironmentVariable("BootstrapAdmin__Email", state["admin"]!["email"]!.GetValue<string>());
        Environment.SetEnvironmentVariable("BootstrapAdmin__Password", state["admin"]!["password"]!.GetValue<string>());
        using var factory = new WebApplicationFactory<WebAppMarker>().WithWebHostBuilder(b =>
        {
            b.UseContentRoot(root).UseEnvironment("Development");
            b.ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(new Dictionary<string,string?> {
                ["ConnectionStrings:Default Connection"] = connection,
                ["BootstrapAdmin:Email"] = state["admin"]!["email"]!.GetValue<string>(),
                ["BootstrapAdmin:Password"] = state["admin"]!["password"]!.GetValue<string>() }));
        });
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var guardAction = Environment.GetEnvironmentVariable("ORANGE_E2E_GUARD_ACTION");
        if (!string.IsNullOrWhiteSpace(guardAction))
        {
            var admin = (await users.FindByEmailAsync(state["admin"]!["email"]!.GetValue<string>()))!;
            IdentityResult result = IdentityResult.Success;
            if (guardAction == "duplicate-on") result = await users.AddToRoleAsync(admin,"Sourcing Officer");
            else if (guardAction == "duplicate-off") result = await users.RemoveFromRoleAsync(admin,"Sourcing Officer");
            else if (guardAction == "reset-token")
            {
                var supplier = state["accounts"]!.AsArray().Single(x => x!["name"]!.GetValue<string>() == "supplier1")!;
                var user = (await users.FindByIdAsync(supplier["id"]!.GetValue<string>()))!;
                supplier["resetCode"] = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(await users.GeneratePasswordResetTokenAsync(user)));
                await File.WriteAllTextAsync(path,state.ToJsonString());
            }
            else throw new InvalidOperationException("Unknown isolated fixture operation.");
            if (!result.Succeeded) throw new InvalidOperationException("Fixture operation rejected by Identity.");
            Console.WriteLine("Isolated fixture operation completed through Identity; no email delivery claimed."); return;
        }
        if (await db.Users.CountAsync() != 1) throw new InvalidOperationException("Provision only a fresh isolated database with its bootstrap administrator.");
        var department = new Department { Name = "Browser E2E Department" }; db.Departments.Add(department); await db.SaveChangesAsync();
        state["departmentId"] = department.Id;
        foreach (var item in state["accounts"]!.AsArray())
        {
            var a = item!; var email = a["email"]!.GetValue<string>(); var role = a["role"]!.GetValue<string>();
            var user = new ApplicationUser { Email=email, UserName=email, FullName="E2E " + a["name"]!.GetValue<string>(), DepartmentId=department.Id,
                IsActive=true, MustChangePassword=role!="Supplier", EmailConfirmed=false };
            if (role == "Supplier")
            {
                var supplier = new Supplier { CompanyName="E2E " + a["name"]!.GetValue<string>(), Email=email, RegistrationNumber=Guid.NewGuid().ToString("N"), Status=SupplierStatus.Pending };
                db.Suppliers.Add(supplier); await db.SaveChangesAsync(); user.SupplierId=supplier.Id; user.DepartmentId=null;
            }
            var created = role=="Supplier" ? await users.CreateAsync(user,a["password"]!.GetValue<string>()) : await users.CreateAsync(user);
            if (!created.Succeeded) throw new InvalidOperationException(string.Join(";", created.Errors.Select(e=>e.Description)));
            if (!(await users.AddToRoleAsync(user,role)).Succeeded) throw new InvalidOperationException("Role assignment failed.");
            a["id"]=user.Id;
            if (role=="Supplier")
            {
                user.EmailOtpSentAt=DateTime.UtcNow; await users.UpdateAsync(user);
                a["otp"]=await users.GenerateUserTokenAsync(user,TokenOptions.DefaultEmailProvider,"SupplierEmailOtp");
                a["supplierId"]=user.SupplierId;
                var directory=Path.Combine(root,"App_Data","SupplierDocuments"); Directory.CreateDirectory(directory);
                var file=Path.Combine(directory,Guid.NewGuid().ToString("N")+".pdf");
                await File.WriteAllTextAsync(file,"%PDF-1.7\nSynthetic isolated E2E commercial registration fixture\n%%EOF");
                db.Documents.Add(new Document { SupplierId=user.SupplierId, FileName="e2e-registration.pdf", StoragePath=file,
                    DocumentType="CommercialRegistration", UploadedByUserId=user.Id, SizeBytes=new FileInfo(file).Length, UploadedAt=DateTime.UtcNow });
            }
            else a["activationCode"]=WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(await users.GeneratePasswordResetTokenAsync(user)));
        }
        await db.SaveChangesAsync(); await File.WriteAllTextAsync(path,state.ToJsonString());
        Console.WriteLine("Provisioned 14 isolated Identity accounts; staff activation and supplier OTP/admin approval still required. No email delivery claimed.");
    }
}
