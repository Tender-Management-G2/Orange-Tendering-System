using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Orange_Tendering_System.Data;
using Orange_Tendering_System.Models;
using Orange_Tendering_System.Models.Entities;
using Orange_Tendering_System.Services;

namespace Orange_Tendering_System.Areas.Identity.Pages.Account;

public class RegisterModel(
    ApplicationDbContext db,
    UserManager<ApplicationUser> users,
    IEmailSender emailSender,
    IConfiguration configuration,
    IWebHostEnvironment environment,
    ILogger<RegisterModel> logger) : PageModel
{
    [BindProperty] public InputModel Input { get; set; } = new();
    [BindProperty] public IFormFile? CommercialRegistrationCertificate { get; set; }
    [BindProperty] public IFormFile? TaxCertificate { get; set; }
    [BindProperty] public List<IFormFile> LegalDocuments { get; set; } = [];
    public IReadOnlyList<string> Categories => SupplierCategories.All;

    public class InputModel
    {
        [Required, StringLength(150)] public string FullName { get; set; } = string.Empty;
        [Required, EmailAddress, StringLength(256)] public string Email { get; set; } = string.Empty;
        [Required, StringLength(100, MinimumLength = 8), DataType(DataType.Password)] public string Password { get; set; } = string.Empty;
        [Required, Compare(nameof(Password)), DataType(DataType.Password)] public string ConfirmPassword { get; set; } = string.Empty;
        [Required, StringLength(200)] public string CompanyName { get; set; } = string.Empty;
        [Required, StringLength(80)] public string Category { get; set; } = string.Empty;
        [Required, StringLength(80)] public string RegistrationNumber { get; set; } = string.Empty;
        [StringLength(80)] public string? TaxNumber { get; set; }
        [Required, EmailAddress, StringLength(256)] public string CompanyEmail { get; set; } = string.Empty;
        [Required, Phone, StringLength(40)] public string CompanyPhone { get; set; } = string.Empty;
        [Required, StringLength(500)] public string CompanyAddress { get; set; } = string.Empty;
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!SupplierCategories.All.Contains(Input.Category))
            ModelState.AddModelError("Input.Category", "Choose one of the listed supplier categories.");
        if (CommercialRegistrationCertificate == null)
            ModelState.AddModelError(nameof(CommercialRegistrationCertificate), "Commercial registration certificate is required.");
        if (!string.IsNullOrWhiteSpace(Input.TaxNumber) && TaxCertificate == null)
            ModelState.AddModelError(nameof(TaxCertificate), "Tax certificate is required when a tax number is provided.");
        if (LegalDocuments.Count == 0)
            ModelState.AddModelError(nameof(LegalDocuments), "At least one business license or legal document is required.");

        var files = new List<(IFormFile File, string Type)>();
        if (CommercialRegistrationCertificate != null) files.Add((CommercialRegistrationCertificate, "CommercialRegistration"));
        if (TaxCertificate != null) files.Add((TaxCertificate, "TaxCertificate"));
        files.AddRange(LegalDocuments.Select(f => (f, "BusinessLicense")));
        if (files.Count > 7)
            ModelState.AddModelError(nameof(LegalDocuments), "Upload no more than seven documents.");
        foreach (var (file, _) in files)
            if (!SupplierDocumentStorage.IsAllowed(file))
                ModelState.AddModelError(nameof(LegalDocuments), "Documents must be PDF, PNG, or JPEG files of at most 10 MB each.");
        if (!ModelState.IsValid) return Page();

        var email = Input.Email.Trim();
        var registration = Input.RegistrationNumber.Trim();
        if (await users.FindByEmailAsync(email) != null ||
            await db.Suppliers.AnyAsync(s => s.RegistrationNumber == registration))
        {
            ModelState.AddModelError(string.Empty, "This account or commercial registration is already registered.");
            return Page();
        }

        var savedPaths = new List<string>();
        var committed = false;
        await using var transaction = await db.Database.BeginTransactionAsync();
        try
        {
            var supplier = new Orange_Tendering_System.Models.Entities.Supplier
            {
                CompanyName = Input.CompanyName.Trim(), RegistrationNumber = registration,
                Category = SupplierCategories.All.Contains(Input.Category) ? Input.Category : SupplierCategories.Other,
                TaxNumber = Input.TaxNumber?.Trim(), Email = Input.CompanyEmail.Trim(),
                Phone = Input.CompanyPhone.Trim(), Address = Input.CompanyAddress.Trim(),
                Status = SupplierStatus.Pending, CreatedAt = DateTime.UtcNow
            };
            db.Suppliers.Add(supplier);
            await db.SaveChangesAsync();

            var user = new ApplicationUser
            {
                UserName = email, Email = email, FullName = Input.FullName.Trim(),
                SupplierId = supplier.Id, IsActive = true, EmailOtpSentAt = DateTime.UtcNow
            };
            var created = await users.CreateAsync(user, Input.Password);
            if (!created.Succeeded)
            {
                foreach (var error in created.Errors)
                    ModelState.AddModelError(string.Empty, error.Description);
                await transaction.RollbackAsync();
                return Page();
            }
            var assigned = await users.AddToRoleAsync(user, SystemRoles.Supplier);
            if (!assigned.Succeeded)
                throw new InvalidOperationException("Could not assign Supplier role.");

            foreach (var (file, type) in files)
            {
                var stored = await SupplierDocumentStorage.SaveAsync(file, environment.ContentRootPath);
                savedPaths.Add(stored.Path);
                db.Documents.Add(new Document
                {
                    SupplierId = supplier.Id, UploadedByUserId = user.Id,
                    FileName = Path.GetFileName(file.FileName), StoragePath = stored.Path,
                    ContentType = stored.ContentType, SizeBytes = file.Length,
                    DocumentType = type, VerificationStatus = DocumentVerificationStatus.Pending,
                    UploadedAt = DateTime.UtcNow
                });
            }

            var admins = await users.GetUsersInRoleAsync(SystemRoles.Admin);
            foreach (var admin in admins)
                db.Notifications.Add(new Notification
                {
                    UserId = admin.Id, Title = "Supplier awaiting review",
                    Message = $"{supplier.CompanyName} submitted a supplier registration.",
                    Link = "/Admin/Suppliers/Review/" + supplier.Id,
                    CreatedAt = DateTime.UtcNow
                });
            db.AuditLogs.Add(new AuditLog
            {
                UserId = user.Id, Action = "SupplierRegistered", EntityName = "Supplier",
                EntityId = supplier.Id.ToString(), CreatedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();

            var code = await users.GenerateUserTokenAsync(user, TokenOptions.DefaultEmailProvider, "SupplierEmailOtp");
            await emailSender.SendEmailAsync(email, "Verify your supplier email",
                $"<p>Your Orange Tendering verification code is <strong>{code}</strong>.</p><p>It expires in 10 minutes. Your company will still require administrator approval.</p>");
            await transaction.CommitAsync();
            committed = true;
            logger.LogInformation("Pending supplier registration created for user {UserId}.", user.Id);
            return RedirectToPage("./VerifyEmail", new { email });
        }
        catch (Exception exception) when (exception is DbUpdateException or IOException or InvalidOperationException or FormatException or System.Net.Mail.SmtpException)
        {
            await transaction.RollbackAsync();
            if (exception is System.Net.Mail.SmtpException smtpException)
                logger.LogWarning("Supplier email delivery failed with SMTP status {Status}.", smtpException.StatusCode);
            else
                logger.LogError(exception, "Supplier registration failed.");
            var localMail = environment.IsDevelopment() && SmtpEmailSender.IsLocalInbox(configuration);
            var deliveryFailed = exception is System.Net.Mail.SmtpException ||
                (exception is InvalidOperationException && !SmtpEmailSender.IsConfigured(configuration));
            var message = deliveryFailed && localMail
                ? "The local development mail inbox is not running. Start it with 'node Tools/DevMail/server.js', then try again."
                : deliveryFailed && !SmtpEmailSender.IsConfigured(configuration)
                    ? "Email delivery is not configured. Configure SMTP before registering."
                    : deliveryFailed
                        ? "Email delivery failed. Check the SMTP service settings and try again."
                    : "Registration could not be completed. Check the application log and try again.";
            ModelState.AddModelError(string.Empty, message);
            return Page();
        }
        finally
        {
            if (!committed)
                foreach (var path in savedPaths)
                    System.IO.File.Delete(path);
        }
    }
}
