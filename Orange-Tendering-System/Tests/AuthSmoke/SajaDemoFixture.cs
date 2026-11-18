using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Orange_Tendering_System;
using Orange_Tendering_System.Data;
using Orange_Tendering_System.Models;
using Orange_Tendering_System.Models.Entities;

// Local walkthrough data, added to the original local database at Saja's request.
public static class SajaDemoFixture
{
    private const string Connection = "Server=localhost;Database=OrangeTenderingSystem;Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=true";
    private const string Marker = "SajaWalkthroughV1";
    private sealed record Account(string Email, string Role, string Name, string State);

    public static async Task Run(bool verifyExisting = false)
    {
        var root = Path.GetFullPath(Environment.GetEnvironmentVariable("AUTH_PROJECT_ROOT")
            ?? throw new InvalidOperationException("Set AUTH_PROJECT_ROOT to the web project directory."));
        var output = Path.Combine(root, "App_Data", "SajaDemo");
        Directory.CreateDirectory(output);
        // Minimal-host startup reads configuration before the test-host callback runs.
        // Set these in this short-lived process as well, so migrations also target the demo database.
        Environment.SetEnvironmentVariable("ConnectionStrings__Default Connection", Connection);
        Environment.SetEnvironmentVariable("DemoData__Enabled", "false");
        Environment.SetEnvironmentVariable("BootstrapAdmin__Email", "");
        Environment.SetEnvironmentVariable("BootstrapAdmin__Password", "");
        using var factory = new WebApplicationFactory<WebAppMarker>().WithWebHostBuilder(b =>
        {
            b.UseContentRoot(root).UseEnvironment("Development");
            b.ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Default Connection"] = Connection,
                ["DemoData:Enabled"] = "false", ["BootstrapAdmin:Email"] = "", ["BootstrapAdmin:Password"] = "",
                ["Logging:LogLevel:Default"] = "Warning",
                ["TenderApprovalPolicy:WeightApproverRoles:0"] = SystemRoles.SourcingManager
            }));
        });
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var actual = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(db.Database.GetConnectionString());
        if (actual.InitialCatalog != "OrangeTenderingSystem" || actual.DataSource != "localhost")
            throw new InvalidOperationException("Refusing to access a database outside the local walkthrough target.");
        Console.WriteLine("Target: localhost / OrangeTenderingSystem (additive local walkthrough data)");
        if (await db.AuditLogs.AnyAsync(a => a.Action == Marker))
        {
            if (verifyExisting) await Verify();
            Console.WriteLine("Existing walkthrough kept unchanged. Credentials: " + Path.Combine(output, "accounts.md"));
            return;
        }
        if (verifyExisting) throw new InvalidOperationException("Seed the walkthrough before verifying it.");
        if (await db.Users.AnyAsync(u => u.Email != null && u.Email.EndsWith("@saja-demo.test"))
            || await db.Departments.AnyAsync(d => d.Name.StartsWith("SAJA DEMO -"))
            || await db.Suppliers.AnyAsync(s => s.RegistrationNumber.StartsWith("SAJA-DEMO-")))
            throw new InvalidOperationException("Existing walkthrough names found without the seed marker. No existing records were changed.");
        var activeOfficers = (await users.GetUsersInRoleAsync(SystemRoles.SourcingOfficer)).Where(u => u.IsActive).ToList();
        if (activeOfficers.Count > 1)
            throw new InvalidOperationException("More than one active officer exists. No account was disabled or changed.");

        // Keep the generated password outside Git, and retain it if a failed run is retried.
        var passwordFile = Path.Combine(output, "password.txt");
        var password = File.Exists(passwordFile) ? (await File.ReadAllTextAsync(passwordFile)).Trim()
            : "Saja!9" + Convert.ToHexString(RandomNumberGenerator.GetBytes(10));
        await File.WriteAllTextAsync(passwordFile, password);
        var accounts = new List<Account>();
        var now = DateTime.UtcNow;
        await using var tx = await db.Database.BeginTransactionAsync();
        var it = new Department { Name = "SAJA DEMO - IT", Description = "Walkthrough IT department" };
        var facilities = new Department { Name = "SAJA DEMO - Facilities", Description = "Second department for ownership tests" };
        db.Departments.AddRange(it, facilities);
        await db.SaveChangesAsync();
        async Task<ApplicationUser> User(string name, string role, int? departmentId = null, int? supplierId = null,
            bool active = true, bool confirmed = true, bool firstLogin = false, string state = "Ready")
        {
            var email = name + "@saja-demo.test";
            var u = new ApplicationUser { Email = email, UserName = email, FullName = "SAJA DEMO - " + name,
                DepartmentId = departmentId, SupplierId = supplierId, IsActive = active, EmailConfirmed = confirmed,
                MustChangePassword = firstLogin };
            var created = await users.CreateAsync(u, password);
            if (!created.Succeeded) throw new InvalidOperationException(string.Join("; ", created.Errors.Select(e => e.Description)));
            var assigned = await users.AddToRoleAsync(u, role);
            if (!assigned.Succeeded) throw new InvalidOperationException("Role assignment failed: " + email);
            accounts.Add(new(email, role, u.FullName, state));
            return u;
        }
        var admin = await User("admin", SystemRoles.Admin);
        var requester = await User("requester-it", SystemRoles.RequestingDepartment, it.Id);
        await User("requester-facilities", SystemRoles.RequestingDepartment, facilities.Id);
        var officer = activeOfficers.SingleOrDefault();
        if (officer is null)
            officer = await User("officer", SystemRoles.SourcingOfficer, it.Id);
        else
            accounts.Add(new(officer.Email!, SystemRoles.SourcingOfficer, officer.FullName, "EXISTING ACCOUNT: use its original password; password and roles unchanged"));
        var manager = await User("manager", SystemRoles.SourcingManager);
        var departmentRep = await User("evaluator-department", SystemRoles.EvaluationCommittee, it.Id);
        var financial = await User("evaluator-financial", SystemRoles.EvaluationCommittee);
        var technical = await User("evaluator-technical", SystemRoles.EvaluationCommittee);
        await User("evaluator-unassigned", SystemRoles.EvaluationCommittee, state: "Not assigned: must not score others' tenders");
        for (var i = 1; i <= 4; i++) await User("top" + i, SystemRoles.TopCommittee);
        await User("employee-first-login", SystemRoles.RequestingDepartment, it.Id, firstLogin: true, state: "Must change password");
        await User("employee-inactive", SystemRoles.RequestingDepartment, it.Id, active: false, state: "Inactive: login blocked");

        var companies = new List<(Supplier Company, ApplicationUser User)>();
        async Task Company(string name, string category, SupplierStatus status = SupplierStatus.Approved, bool active = true, bool confirmed = true)
        {
            var company = new Supplier { CompanyName = "SAJA DEMO - " + name, Category = category,
                RegistrationNumber = "SAJA-DEMO-" + name, TaxNumber = "DEMO-TAX-" + name, Email = name + "@saja-demo.test",
                Phone = "+962790000000", Address = "Amman - demonstration only", Status = status, CreatedAt = now.AddDays(-5),
                ReviewedByUserId = status == SupplierStatus.Pending ? null : admin.Id,
                ReviewedAt = status == SupplierStatus.Pending ? null : now.AddDays(-4),
                ReviewNotes = status == SupplierStatus.Pending ? null : "Seeded walkthrough state, not a real verification." };
            db.Suppliers.Add(company); await db.SaveChangesAsync();
            var u = await User(name, SystemRoles.Supplier, supplierId: company.Id, active: active, confirmed: confirmed,
                state: $"{category}; {status}; active={active}; emailConfirmed={confirmed}");
            companies.Add((company, u));
            company.Documents.Add(await SampleDocument(u, "CommercialRegistration", supplier: company,
                verification: status == SupplierStatus.Approved ? DocumentVerificationStatus.Verified : DocumentVerificationStatus.Pending));
        }
        for (var i = 1; i <= 4; i++) await Company("supplier-it" + i, SupplierCategories.InformationTechnology);
        // At least one approved, active supplier in every category, plus four competing IT suppliers.
        foreach (var category in SupplierCategories.All.Where(c => c != SupplierCategories.InformationTechnology))
            await Company("supplier-category" + (SupplierCategories.All.ToList().IndexOf(category) + 1), category);
        await Company("supplier-pending", SupplierCategories.InformationTechnology, SupplierStatus.Pending);
        await Company("supplier-rejected", SupplierCategories.InformationTechnology, SupplierStatus.Rejected);
        await Company("supplier-suspended", SupplierCategories.InformationTechnology, SupplierStatus.Suspended);
        await Company("supplier-inactive", SupplierCategories.InformationTechnology, active: false);
        await Company("supplier-unverified", SupplierCategories.InformationTechnology, SupplierStatus.Pending, confirmed: false);

        PurchaseRequest Request(string title, PurchaseRequestStatus status)
        {
            var r = new PurchaseRequest { Title = "SAJA DEMO - " + title, DepartmentId = it.Id, CreatedByUserId = requester.Id,
                Justification = "Walkthrough sample: purchase laptops for a new team.", Status = status, Priority = RequestPriority.High,
                CreatedAt = now.AddDays(-3), SubmittedAt = status == PurchaseRequestStatus.Draft ? null : now.AddDays(-2), NeededBy = now.AddMonths(2),
                Items = { new PurchaseRequestItem { Name = "Business laptop", Quantity = 10, Unit = "unit", Specifications = "16 GB RAM, 512 GB SSD, 3-year warranty" },
                    new PurchaseRequestItem { Name = "Docking station", Quantity = 10, Unit = "unit", Specifications = "USB-C, HDMI, Ethernet" } } };
            db.PurchaseRequests.Add(r); return r;
        }
        foreach (var status in new[] { PurchaseRequestStatus.Draft, PurchaseRequestStatus.Submitted, PurchaseRequestStatus.Returned, PurchaseRequestStatus.Rejected, PurchaseRequestStatus.Accepted })
        {
            var r = Request("Request " + status, status);
            if (status != PurchaseRequestStatus.Draft)
                r.Reviews.Add(new PurchaseRequestReview { SubmissionNumber = 1, SubmittedAt = r.SubmittedAt!.Value,
                    SubmittedSnapshotJson = JsonSerializer.Serialize(new { r.Title, r.Justification, Items = r.Items.Select(i => new { i.Name, i.Quantity, i.Unit, i.Specifications }) }),
                    Decision = status switch { PurchaseRequestStatus.Returned => ReviewDecision.Returned, PurchaseRequestStatus.Rejected => ReviewDecision.Rejected,
                        PurchaseRequestStatus.Accepted => ReviewDecision.Accepted, _ => ReviewDecision.Pending },
                    Comment = status == PurchaseRequestStatus.Returned ? "Please clarify the docking station specifications." : "Walkthrough review sample.",
                    ReviewedByUserId = status == PurchaseRequestStatus.Submitted ? null : officer.Id,
                    ReviewedAt = status == PurchaseRequestStatus.Submitted ? null : now.AddDays(-1) });
        }
        var seats = new[] { (manager, CommitteeResponsibility.SourcingManager), (departmentRep, CommitteeResponsibility.DepartmentRepresentative),
            (financial, CommitteeResponsibility.FinancialRepresentative), (technical, CommitteeResponsibility.TechnicalRepresentative) };
        var scenarios = new (string Name, int Offers, bool Closed, bool Weights, bool Expired)[]
        {
            ("01 Start here - requirements and weights", 0, false, false, false),
            ("02 Invitations and editable drafts", 0, false, true, false),
            ("03 Two offers - minimum count", 2, true, true, false),
            ("04 Three offers - ready for compliance", 3, true, true, false),
            ("05 Four offers - all compliance states", 4, true, true, false),
            ("06 Expired deadline - submission blocked", 0, false, true, true)
        };
        foreach (var scenario in scenarios)
        {
            var request = Request(scenario.Name, PurchaseRequestStatus.Accepted);
            var tender = new Tender { PurchaseRequest = request, Title = "SAJA DEMO - " + scenario.Name, Description = request.Justification,
                Requirements = JsonSerializer.Serialize(new { TechnicalRequirements = "Laptop and docking station specifications", CommercialConditions = "JOD; taxes specified",
                    DeliveryExpectations = "Delivery within 30 days", MandatoryCompliance = "Commercial registration and warranty evidence" }),
                CreatedByUserId = officer.Id, CreatedAt = now.AddDays(-2), Status = scenario.Weights ? TenderStatus.Bidding : TenderStatus.Draft,
                Items = request.Items.Select(i => new TenderItem { Name = i.Name, Specifications = i.Specifications, Quantity = i.Quantity, Unit = i.Unit }).ToList() };
            var round = new TenderRound { RoundNumber = 1, CreatedAt = now.AddDays(-2), CreatedByUserId = officer.Id,
                Status = !scenario.Weights ? TenderRoundStatus.Draft : scenario.Closed ? TenderRoundStatus.Closed : TenderRoundStatus.Open,
                OpensAt = scenario.Weights ? now.AddDays(-2) : null, Deadline = !scenario.Weights ? null : scenario.Expired || scenario.Closed ? now.AddDays(-1) : now.AddDays(30),
                ClosedAt = scenario.Closed ? now.AddHours(-12) : null };
            tender.Rounds.Add(round);
            foreach (var (u, seat) in seats) tender.CommitteeMembers.Add(new CommitteeMember { UserId = u.Id, CommitteeType = CommitteeType.Evaluation, Responsibility = seat, AssignedAt = now.AddDays(-2) });
            db.Tenders.Add(tender); await db.SaveChangesAsync();
            if (!scenario.Weights) continue;
            var weight = new WeightRound { TenderId = tender.Id, Version = 1, State = WeightState.Approved, CreatedAt = now.AddDays(-2),
                RequirementsJson = JsonSerializer.Serialize(new { tender.Title, tender.Description, tender.Requirements, Items = tender.Items.Select(i => new { i.Name, i.Quantity, i.Unit, i.Specifications }) }),
                FinancialAverage = 50, TechnicalAverage = 50, ApprovedFinancial = 50, ApprovedTechnical = 50, ApprovedByUserId = manager.Id, ApprovedAt = now.AddDays(-2),
                ApprovalPolicyJson = JsonSerializer.Serialize(new { Version = "SAJA-DEMO", WeightApproverRoles = new[] { SystemRoles.SourcingManager } }) };
            foreach (var member in tender.CommitteeMembers) weight.Proposals.Add(new WeightProposal { CommitteeMemberId = member.Id, MemberUserId = member.UserId, Financial = 50, Technical = 50, SubmittedAt = now.AddDays(-2) });
            db.Add(weight);
            for (var i = 0; i < 4; i++)
            {
                var (company, u) = companies[i];
                var status = scenario.Name.StartsWith("02") ? i switch { 0 => InvitationStatus.Accepted, 1 => InvitationStatus.Accepted, 2 => InvitationStatus.Pending, _ => InvitationStatus.Declined }
                    : i < scenario.Offers ? InvitationStatus.Accepted : InvitationStatus.Pending;
                var invitation = new TenderInvitation { TenderRoundId = round.Id, SupplierId = company.Id, Status = status, InvitedByUserId = officer.Id,
                    InvitedAt = now.AddDays(-2), RespondedAt = status == InvitationStatus.Pending ? null : now.AddDays(-2).AddHours(1) };
                db.TenderInvitations.Add(invitation);
                db.Notifications.Add(new Notification { UserId = u.Id, Title = "Walkthrough tender invitation", Message = tender.Title,
                    Link = "/Supplier/Invitations", CreatedAt = now.AddDays(-2), IsRead = i % 2 == 0 });
                if (i >= scenario.Offers && !(scenario.Name.StartsWith("02") && i == 0) && !(scenario.Expired && i == 0)) continue;
                if (scenario.Expired) { invitation.Status = InvitationStatus.Accepted; invitation.RespondedAt = now.AddDays(-2).AddHours(1); }
                var draft = scenario.Offers == 0;
                var compliance = scenario.Name.StartsWith("05") ? new[] { ComplianceStatus.Compliant, ComplianceStatus.NonCompliant, ComplianceStatus.PendingClarification, ComplianceStatus.NotChecked }[i] : ComplianceStatus.NotChecked;
                var bid = new Bid { TenderInvitation = invitation, Status = draft ? BidStatus.Draft : BidStatus.Submitted, RevisionNumber = 1, CurrencyCode = "JOD",
                    TechnicalProposal = "Demonstration offer: 16 GB RAM, 512 GB SSD and docking stations.", DeliveryDays = 10 + i * 5, PaymentTerms = "30 days after delivery",
                    CreatedAt = now.AddDays(-2).AddHours(2), SubmittedAt = draft ? null : now.AddDays(-2).AddHours(3), ValidUntil = now.AddDays(60),
                    ComplianceStatus = compliance, ComplianceNotes = compliance == ComplianceStatus.NotChecked ? null : "Seeded compliance example for testing.",
                    MissingRequirements = compliance is ComplianceStatus.NonCompliant or ComplianceStatus.PendingClarification ? "Warranty evidence" : null,
                    ComplianceCheckedByUserId = compliance == ComplianceStatus.NotChecked ? null : officer.Id, ComplianceCheckedAt = compliance == ComplianceStatus.NotChecked ? null : now.AddHours(-6),
                    Items = tender.Items.Select(item => new BidItem { TenderItemId = item.Id, Quantity = item.Quantity, UnitPrice = item.Name == "Business laptop" ? 500 + i * 40 : 60 + i * 5,
                        OfferedSpecifications = item.Specifications }).ToList() };
                db.Bids.Add(bid);
                bid.Documents.Add(await SampleDocument(u, "TechnicalProposal", bid: bid));
                if (scenario.Name.StartsWith("05") && i < 3)
                {
                    var state = new[] { ClarificationStatus.Open, ClarificationStatus.Answered, ClarificationStatus.Closed }[i];
                    var thread = new Clarification { Bid = bid, FromUserId = officer.Id, ToUserId = u.Id, Subject = "SAJA DEMO - Warranty " + state,
                        Status = state, CreatedAt = now.AddHours(-5), ClosedAt = state == ClarificationStatus.Closed ? now.AddHours(-1) : null,
                        Messages = { new ClarificationMessage { SenderUserId = officer.Id, Message = "Please provide the warranty terms for this offer.", SentAt = now.AddHours(-5) } } };
                    if (state != ClarificationStatus.Open) thread.Messages.Add(new ClarificationMessage { SenderUserId = u.Id, Message = "The offered warranty is three years. Please confirm if further evidence is required.", SentAt = now.AddHours(-2) });
                    db.Clarifications.Add(thread);
                    db.Notifications.Add(new Notification { UserId = u.Id, Title = "Walkthrough clarification", Message = thread.Subject, Link = "/Supplier/Clarifications", CreatedAt = now.AddHours(-5) });
                }
            }
        }
        db.AuditLogs.Add(new AuditLog { Action = Marker, EntityName = "DemoDataset", EntityId = "1", CreatedAt = now,
            NewValues = "Local walkthrough fixtures. Seeded states are not evidence of real approvals or tests." });
        await db.SaveChangesAsync();
        // Write credentials before commit, so a file-write failure cannot leave accounts without their password.
        await File.WriteAllTextAsync(Path.Combine(output, "accounts.json"), JsonSerializer.Serialize(new { Password = password, Accounts = accounts }, new JsonSerializerOptions { WriteIndented = true }));
        var guide = new StringBuilder("# Saja local walkthrough accounts\n\nDatabase: OrangeTenderingSystem on localhost (original database).\n\nCommon initial password for NEW demo accounts: `" + password + "`\n\nEXISTING ACCOUNT rows keep their original password, not the demo password. Existing users were not modified.\n\n| Email | Role | State |\n|---|---|---|\n");
        foreach (var a in accounts) guide.AppendLine($"| {a.Email} | {a.Role} | {a.State} |");
        await File.WriteAllTextAsync(Path.Combine(output, "accounts.md"), guide.ToString());
        await tx.CommitAsync();
        await Verify();
        Console.WriteLine("Credentials (not committed): " + Path.Combine(output, "accounts.md"));

        async Task<Document> SampleDocument(ApplicationUser u, string type, Supplier? supplier = null, Bid? bid = null, DocumentVerificationStatus? verification = null)
        {
            var relative = "App_Data/SupplierUploads/SajaDemo/" + Guid.NewGuid().ToString("N") + ".png";
            var path = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            // A real tiny PNG for download / upload checks, explicitly labelled as demo evidence.
            var bytes = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+aFOsAAAAASUVORK5CYII=");
            await File.WriteAllBytesAsync(path, bytes);
            return new Document { FileName = "DEMO-placeholder-" + type + ".png", StoragePath = relative, ContentType = "image/png", SizeBytes = bytes.Length,
                UploadedByUserId = u.Id, UploadedAt = now.AddDays(-2), DocumentType = type, Supplier = supplier, Bid = bid, VerificationStatus = verification };
        }

        async Task Verify()
        {
            var officers = await users.GetUsersInRoleAsync(SystemRoles.SourcingOfficer);
            if (officers.Count(u => u.IsActive) != 1) throw new InvalidOperationException("Expected exactly one active officer.");
            if (await db.Suppliers.CountAsync(s => s.Category == SupplierCategories.InformationTechnology && s.Status == SupplierStatus.Approved
                && db.Users.Any(u => u.SupplierId == s.Id && u.IsActive)) < 4) throw new InvalidOperationException("Missing competing IT suppliers.");
            if ((await db.Suppliers.Select(s => s.Category).Distinct().CountAsync()) != SupplierCategories.All.Count) throw new InvalidOperationException("Missing categories.");
            var credentials = JsonSerializer.Deserialize<JsonElement>(await File.ReadAllTextAsync(Path.Combine(output, "accounts.json")));
            var checks = new List<string>();
            foreach (var a in credentials.GetProperty("Accounts").EnumerateArray())
            {
                var user = await users.FindByEmailAsync(a.GetProperty("Email").GetString()!);
                if (a.GetProperty("State").GetString()!.StartsWith("EXISTING ACCOUNT"))
                {
                    if (user is null || !user.IsActive || !await users.IsInRoleAsync(user, SystemRoles.SourcingOfficer))
                        throw new InvalidOperationException("Existing officer no longer available.");
                    checks.Add("PASS existing officer retained; original password not changed or used: " + user.Email);
                    continue;
                }
                if (user is null || !await users.CheckPasswordAsync(user, credentials.GetProperty("Password").GetString()!)
                    || !await users.IsInRoleAsync(user, a.GetProperty("Role").GetString()!)) throw new InvalidOperationException("Account verification failed.");
                using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
                var html = await client.GetStringAsync("/Identity/Account/Login");
                var token = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");
                if (!token.Success) throw new InvalidOperationException("Login antiforgery token missing.");
                using var login = await client.PostAsync("/Identity/Account/Login", new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["Input.Email"] = user.Email!, ["Input.Password"] = credentials.GetProperty("Password").GetString()!,
                    ["__RequestVerificationToken"] = WebUtility.HtmlDecode(token.Groups[1].Value)
                }));
                var supplierState = user.SupplierId == null ? null : await db.Suppliers.Where(s => s.Id == user.SupplierId).Select(s => (SupplierStatus?)s.Status).SingleAsync();
                var blocked = !user.IsActive || !user.EmailConfirmed || supplierState is SupplierStatus.Rejected or SupplierStatus.Suspended;
                if (login.StatusCode != (blocked ? HttpStatusCode.OK : HttpStatusCode.Redirect))
                    throw new InvalidOperationException("Unexpected login status for " + user.Email + ": " + login.StatusCode);
                if (blocked && (await client.GetAsync("/Workspace")).StatusCode != HttpStatusCode.Redirect)
                    throw new InvalidOperationException("Blocked account obtained workspace access: " + user.Email);
                checks.Add("PASS login state: " + user.Email);
                if (!blocked && !user.MustChangePassword && supplierState != SupplierStatus.Pending)
                {
                    var route = a.GetProperty("Role").GetString() switch
                    {
                        SystemRoles.Admin => "/Admin", SystemRoles.RequestingDepartment => "/Department",
                        SystemRoles.SourcingOfficer => "/SourcingOfficer", SystemRoles.SourcingManager => "/SourcingManager",
                        SystemRoles.EvaluationCommittee => "/EvaluationCommittee", SystemRoles.TopCommittee => "/TopCommittee", _ => "/Supplier"
                    };
                    using var page = await client.GetAsync(route);
                    if (page.StatusCode != HttpStatusCode.OK) throw new InvalidOperationException("Dashboard failed for " + user.Email + ": " + page.StatusCode);
                    checks.Add("PASS dashboard: " + user.Email);
                }
                if (user.Email == "supplier-it1@saja-demo.test")
                {
                    foreach (var route in new[] { "/Supplier/Profile", "/Supplier/Invitations", "/Supplier/Offers", "/Supplier/Clarifications", "/Supplier/Documents", "/Notifications" })
                    {
                        if ((await client.GetAsync(route)).StatusCode != HttpStatusCode.OK) throw new InvalidOperationException("Supplier page failed: " + route);
                        checks.Add("PASS page: " + route);
                    }
                    if ((await client.GetAsync("/Admin")).StatusCode == HttpStatusCode.OK) throw new InvalidOperationException("Supplier accessed Admin.");
                    var foreign = await db.Bids.FirstAsync(b => b.TenderInvitation.SupplierId != user.SupplierId);
                    if ((await client.GetAsync("/Supplier/Offers/Details/" + foreign.Id)).StatusCode != HttpStatusCode.NotFound)
                        throw new InvalidOperationException("Other supplier's offer was not hidden.");
                    var foreignDoc = await db.Documents.FirstAsync(d => d.SupplierId != null && d.SupplierId != user.SupplierId);
                    if ((await client.GetAsync("/Supplier/Documents/Download/" + foreignDoc.Id)).StatusCode != HttpStatusCode.NotFound)
                        throw new InvalidOperationException("Other supplier's document was not hidden.");
                    var draft = await db.Bids.FirstAsync(b => b.TenderInvitation.SupplierId == user.SupplierId && b.Status == BidStatus.Draft && b.TenderInvitation.TenderRound.Deadline > DateTime.UtcNow);
                    if ((await client.GetAsync("/Supplier/Offers/Edit/" + draft.Id)).StatusCode != HttpStatusCode.OK) throw new InvalidOperationException("Draft edit unavailable.");
                    var submitted = await db.Bids.FirstAsync(b => b.TenderInvitation.SupplierId == user.SupplierId && b.Status == BidStatus.Submitted);
                    if ((await client.GetAsync("/Supplier/Offers/Edit/" + submitted.Id)).StatusCode != HttpStatusCode.NotFound) throw new InvalidOperationException("Submitted offer editable.");
                    checks.Add("PASS supplier role, cross-company offer/document isolation, draft edit and submitted-offer immutability");
                }
            }
            await File.WriteAllLinesAsync(Path.Combine(output, "verification.txt"), checks);
            Console.WriteLine($"Verified: {await db.Users.CountAsync()} accounts, {await db.Suppliers.CountAsync()} suppliers, {await db.Tenders.CountAsync()} tenders, {await db.Bids.CountAsync()} offers. Existing states were not reset.");
            Console.WriteLine($"HTTP checks passed: {checks.Count} (real Identity cookies and local SQL Server; in-process host, not a browser).");
        }
    }
}
