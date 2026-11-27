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

/// <summary>
/// Supplier registration (the four-step wizard is only on the page: all fields arrive here in one POST).
/// Creates a Pending Supplier, an inactive ApplicationUser with the Supplier role and the uploaded documents in one
/// database transaction, then emails a six-digit code and redirects to VerifyEmail.
/// </summary>
public class RegisterModel : PageModel
{
    private readonly ApplicationDbContext db;
    private readonly UserManager<ApplicationUser> users;
    private readonly IEmailSender emailSender;
    private readonly IConfiguration configuration;
    private readonly IWebHostEnvironment environment;
    private readonly ILogger<RegisterModel> logger;

    public RegisterModel(
        ApplicationDbContext db,
        UserManager<ApplicationUser> users,
        IEmailSender emailSender,
        IConfiguration configuration,
        IWebHostEnvironment environment,
        ILogger<RegisterModel> logger)
    {
        this.db = db;
        this.users = users;
        this.emailSender = emailSender;
        this.configuration = configuration;
        this.environment = environment;
        this.logger = logger;
    }

    // Model Binding fills these from the form ("Input.FullName", ... and the three file inputs).
    [BindProperty]
    public InputModel Input { get; set; } = new InputModel();

    [BindProperty]
    public IFormFile? CommercialRegistrationCertificate { get; set; }

    [BindProperty]
    public IFormFile? TaxCertificate { get; set; }

    [BindProperty]
    public List<IFormFile> LegalDocuments { get; set; } = new List<IFormFile>();

    public IReadOnlyList<string> Categories
    {
        get { return SupplierCategories.All; }
    }

    public class InputModel
    {
        [Required, StringLength(150)]
        public string FullName { get; set; } = string.Empty;

        [Required, EmailAddress, StringLength(256)]
        public string Email { get; set; } = string.Empty;

        [Required, StringLength(100, MinimumLength = 8), DataType(DataType.Password)]
        public string Password { get; set; } = string.Empty;

        [Required, Compare(nameof(Password)), DataType(DataType.Password)]
        public string ConfirmPassword { get; set; } = string.Empty;

        [Required, StringLength(200)]
        public string CompanyName { get; set; } = string.Empty;

        [Required, StringLength(80)]
        public string Category { get; set; } = string.Empty;

        [Required, StringLength(80)]
        public string RegistrationNumber { get; set; } = string.Empty;

        [StringLength(80)]
        public string? TaxNumber { get; set; }

        [Required, EmailAddress, StringLength(256)]
        public string CompanyEmail { get; set; } = string.Empty;

        [Required(ErrorMessage = "Company telephone is required."), RegularExpression(@"^[0-9]{10}$", ErrorMessage = "Telephone must be exactly 10 digits, for example 0791234567.")]
        public string CompanyPhone { get; set; } = string.Empty;

        [Required, StringLength(500)]
        public string CompanyAddress { get; set; } = string.Empty;
    }

    // POST /Identity/Account/Register
    public async Task<IActionResult> OnPostAsync()
    {
        // 1. Someone who already registered but did not verify the email yet: continue to the code page
        //    (the documents are already stored, so they are not uploaded again).
        if (!string.IsNullOrWhiteSpace(Input.Email) && !string.IsNullOrWhiteSpace(Input.Password))
        {
            ApplicationUser? pending = await users.FindByEmailAsync(Input.Email.Trim());
            if (pending != null && pending.SupplierId != null && !pending.EmailConfirmed &&
                await users.CheckPasswordAsync(pending, Input.Password))
            {
                return RedirectToPage("./VerifyEmail", new { email = Input.Email.Trim() });
            }
        }

        // 2. Checks that the [Required] attributes cannot do.
        if (!SupplierCategories.All.Contains(Input.Category))
        {
            ModelState.AddModelError("Input.Category", "Choose one of the listed supplier categories.");
        }
        if (CommercialRegistrationCertificate == null)
        {
            ModelState.AddModelError(nameof(CommercialRegistrationCertificate), "Commercial registration certificate is required.");
        }
        if (!string.IsNullOrWhiteSpace(Input.TaxNumber) && TaxCertificate == null)
        {
            ModelState.AddModelError(nameof(TaxCertificate), "Tax certificate is required when a tax number is provided.");
        }
        if (LegalDocuments.Count == 0)
        {
            ModelState.AddModelError(nameof(LegalDocuments), "At least one business license or legal document is required.");
        }

        List<IFormFile> allFiles = AllUploadedFiles();
        if (allFiles.Count > 7)
        {
            ModelState.AddModelError(nameof(LegalDocuments), "Upload no more than seven documents.");
        }
        foreach (IFormFile file in allFiles)
        {
            // Checks the extension, the real file signature and the 10 MB limit.
            if (!SupplierDocumentStorage.IsAllowed(file))
            {
                ModelState.AddModelError(nameof(LegalDocuments), "Documents must be PDF, PNG, or JPEG files of at most 10 MB each.");
            }
        }

        if (!ModelState.IsValid)
        {
            return Page();
        }

        // 3. No duplicate email and no duplicate commercial registration number.
        string email = Input.Email.Trim();
        string registrationNumber = Input.RegistrationNumber.Trim();
        ApplicationUser? existingUser = await users.FindByEmailAsync(email);
        bool companyExists = await db.Suppliers.AnyAsync(s => s.RegistrationNumber == registrationNumber);
        if (existingUser != null || companyExists)
        {
            ModelState.AddModelError(string.Empty,
                "An account or company with these details already exists. If you started an application, use Verify Email with your original contact address to request a new code.");
            return Page();
        }

        // 4. Save everything in ONE transaction: if any step fails, nothing is kept in the database.
        List<string> savedPaths = new List<string>();
        bool committed = false;
        await using var transaction = await db.Database.BeginTransactionAsync();
        try
        {
            // "Models.Entities.Supplier" is written in full because, inside the Areas folder, the short name
            // "Supplier" means the Areas.Supplier namespace.
            Models.Entities.Supplier supplier = new Models.Entities.Supplier();
            supplier.CompanyName = Input.CompanyName.Trim();
            supplier.RegistrationNumber = registrationNumber;
            supplier.Category = Input.Category;
            supplier.TaxNumber = Input.TaxNumber?.Trim();
            supplier.Email = Input.CompanyEmail.Trim();
            supplier.Phone = Input.CompanyPhone.Trim();
            supplier.Address = Input.CompanyAddress.Trim();
            supplier.Status = SupplierStatus.Pending;
            supplier.CreatedAt = DateTime.UtcNow;
            db.Suppliers.Add(supplier);
            await db.SaveChangesAsync();   // INSERT Suppliers (gives supplier.Id)

            // The contact account stays inactive until the email code is verified.
            ApplicationUser user = new ApplicationUser();
            user.UserName = email;
            user.Email = email;
            user.FullName = Input.FullName.Trim();
            user.SupplierId = supplier.Id;
            user.IsActive = false;
            user.EmailOtpSentAt = DateTime.UtcNow;

            // CreateAsync checks the password rules and stores only a hash of the password (INSERT AspNetUsers).
            IdentityResult created = await users.CreateAsync(user, Input.Password);
            if (!created.Succeeded)
            {
                foreach (IdentityError error in created.Errors)
                {
                    ModelState.AddModelError(string.Empty, error.Description);
                }
                await transaction.RollbackAsync();
                return Page();
            }

            IdentityResult assigned = await users.AddToRoleAsync(user, SystemRoles.Supplier);   // INSERT AspNetUserRoles
            if (!assigned.Succeeded)
            {
                throw new InvalidOperationException("Could not assign Supplier role.");
            }

            // The files go to the private upload folder; their details go to the Documents table.
            await SaveDocumentAsync(CommercialRegistrationCertificate!, "CommercialRegistration", supplier, user, savedPaths);
            if (TaxCertificate != null)
            {
                await SaveDocumentAsync(TaxCertificate, "TaxCertificate", supplier, user, savedPaths);
            }
            foreach (IFormFile legalDocument in LegalDocuments)
            {
                await SaveDocumentAsync(legalDocument, "BusinessLicense", supplier, user, savedPaths);
            }

            AuditLog audit = new AuditLog();
            audit.UserId = user.Id;
            audit.Action = "SupplierRegistrationStarted";
            audit.EntityName = "Supplier";
            audit.EntityId = supplier.Id.ToString();
            audit.CreatedAt = DateTime.UtcNow;
            db.AuditLogs.Add(audit);
            await db.SaveChangesAsync();   // INSERT Documents + AuditLogs

            // A six-digit code made by Identity (valid with the account's current security stamp).
            string code = await users.GenerateUserTokenAsync(user, TokenOptions.DefaultEmailProvider, "SupplierEmailOtp");
            await emailSender.SendEmailAsync(email, "Verify your supplier email",
                "<p>Your Orange Tendering verification code is <strong>" + code + "</strong>.</p><p>It expires in 10 minutes. Your company will still require administrator approval.</p>");

            // Only now, after the email was sent, is everything saved for real.
            await transaction.CommitAsync();
            committed = true;
            logger.LogInformation("Pending supplier registration created for user {UserId}.", user.Id);
            return RedirectToPage("./VerifyEmail", new { email = email });
        }
        catch (Exception exception)
        {
            // Unexpected errors are not handled here (the error page shows them).
            if (!IsExpectedRegistrationError(exception))
            {
                throw;
            }

            await transaction.RollbackAsync();
            if (exception is System.Net.Mail.SmtpException)
            {
                logger.LogWarning("Supplier email delivery failed with SMTP status {Status}.", ((System.Net.Mail.SmtpException)exception).StatusCode);
            }
            else
            {
                logger.LogError(exception, "Supplier registration failed.");
            }

            ModelState.AddModelError(string.Empty, RegistrationErrorMessage(exception));
            return Page();
        }
        finally
        {
            // After a rollback the uploaded files must not stay on disk either.
            if (!committed)
            {
                foreach (string path in savedPaths)
                {
                    System.IO.File.Delete(path);
                }
            }
        }
    }

    // The three file inputs as one list (for the count and type checks).
    private List<IFormFile> AllUploadedFiles()
    {
        List<IFormFile> files = new List<IFormFile>();
        if (CommercialRegistrationCertificate != null)
        {
            files.Add(CommercialRegistrationCertificate);
        }
        if (TaxCertificate != null)
        {
            files.Add(TaxCertificate);
        }
        files.AddRange(LegalDocuments);
        return files;
    }

    // Stores one file in the private upload folder and adds its Documents row (saved later by SaveChangesAsync).
    private async Task SaveDocumentAsync(IFormFile file, string documentType, Models.Entities.Supplier supplier, ApplicationUser user, List<string> savedPaths)
    {
        var stored = await SupplierDocumentStorage.SaveAsync(file, environment.ContentRootPath);
        savedPaths.Add(stored.Path);

        Document document = new Document();
        document.SupplierId = supplier.Id;
        document.UploadedByUserId = user.Id;
        document.FileName = Path.GetFileName(file.FileName);
        document.StoragePath = stored.Path;
        document.ContentType = stored.ContentType;
        document.SizeBytes = file.Length;
        document.DocumentType = documentType;
        document.VerificationStatus = DocumentVerificationStatus.Pending;
        document.UploadedAt = DateTime.UtcNow;
        db.Documents.Add(document);
    }

    private static bool IsExpectedRegistrationError(Exception exception)
    {
        return exception is DbUpdateException
            || exception is IOException
            || exception is InvalidOperationException
            || exception is FormatException
            || exception is System.Net.Mail.SmtpException;
    }

    private string RegistrationErrorMessage(Exception exception)
    {
        bool smtpConfigured = SmtpEmailSender.IsConfigured(configuration);
        bool deliveryFailed = exception is System.Net.Mail.SmtpException ||
            (exception is InvalidOperationException && !smtpConfigured);
        bool localMail = environment.IsDevelopment() && SmtpEmailSender.IsLocalInbox(configuration);

        if (deliveryFailed && localMail)
        {
            return "The local development mail inbox is not running. Start it with 'node Tools/DevMail/server.js', then try again.";
        }
        if (deliveryFailed && !smtpConfigured)
        {
            return "Email delivery is not configured. Configure SMTP before registering.";
        }
        if (deliveryFailed)
        {
            return "Email delivery failed. Check the SMTP service settings and try again.";
        }
        return "Registration could not be completed. Check the application log and try again.";
    }
}
