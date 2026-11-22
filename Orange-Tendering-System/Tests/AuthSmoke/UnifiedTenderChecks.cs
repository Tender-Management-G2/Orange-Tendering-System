using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Orange_Tendering_System;
using Orange_Tendering_System.Data;
using Orange_Tendering_System.Models;
using Orange_Tendering_System.Models.Entities;
using Orange_Tendering_System.Services;
using Orange_Tendering_System.Services.Biometric;
using Orange_Tendering_System.ViewModels;

// Real SQL Server + Identity requests. Isolated test data never enters the user's working database.
public static class UnifiedTenderChecks
{
    public static async Task Run()
    {
        var root = Environment.GetEnvironmentVariable("AUTH_PROJECT_ROOT") ?? throw new InvalidOperationException("AUTH_PROJECT_ROOT required");
        var suffix = Convert.ToHexString(RandomNumberGenerator.GetBytes(4));
        var server = Environment.GetEnvironmentVariable("AUTH_TEST_SERVER") ?? "localhost";
        var connection = $"Server={server};Database=OrangeAuthCheck_Unified_{suffix};Trusted_Connection=True;TrustServerCertificate=True";
        Environment.SetEnvironmentVariable("ConnectionStrings__Default Connection", connection);
        Environment.SetEnvironmentVariable("DemoData__Enabled", "false");
        var password = "Ut!9" + suffix + suffix;
        using var factory = new WebApplicationFactory<WebAppMarker>().WithWebHostBuilder(b =>
        {
            b.UseContentRoot(root).UseEnvironment("Development");
            b.ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Default Connection"] = connection, ["BootstrapAdmin:Email"] = "", ["BootstrapAdmin:Password"] = "",
                ["DemoData:Enabled"] = "false", ["Logging:LogLevel:Default"] = "Error",
                ["TenderApprovalPolicy:Version"] = "UNIFIED-CHECK", ["TenderApprovalPolicy:WeightApproverRoles:0"] = SystemRoles.SourcingManager
            }));
            b.ConfigureTestServices(s =>
            {
                s.RemoveAll<IHostedService>(); // Never dispatch queued test invitations to SMTP.
                // This tender-only fixture marks its synthetic supplier cookies as biometric.
                // AuthSmoke separately exercises real face and passkey enrollment and sign-in.
                s.PostConfigure<CookieAuthenticationOptions>(IdentityConstants.ApplicationScheme, options =>
                {
                    var validate = options.Events.OnValidatePrincipal;
                    options.Events.OnValidatePrincipal = async context =>
                    {
                        if (validate != null) await validate(context);
                        if (context.Principal?.IsInRole(SystemRoles.Supplier) == true &&
                            context.Principal.Identity is ClaimsIdentity identity &&
                            !identity.HasClaim(c => c.Type == BiometricSignInService.AuthenticationMethodClaim))
                            identity.AddClaim(new Claim(BiometricSignInService.AuthenticationMethodClaim,
                                BiometricSignInService.FaceMethod));
                    };
                });
            });
        });
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var flow = scope.ServiceProvider.GetRequiredService<TenderWorkflowService>();
        var checks = new List<string>();
        void Check(bool ok, string message) { if (!ok) throw new InvalidOperationException(message); checks.Add("PASS " + message); }
        var department = new Department { Name = "Unified checks" }; db.Add(department); await db.SaveChangesAsync();
        async Task<ApplicationUser> User(string name, string role, int? supplierId = null)
        {
            var u = new ApplicationUser { UserName = name + "@unified.test", Email = name + "@unified.test", FullName = name,
                EmailConfirmed = true, IsActive = true, DepartmentId = supplierId.HasValue ? null : department.Id, SupplierId = supplierId };
            Check((await users.CreateAsync(u, password)).Succeeded, "create " + name);
            Check((await users.AddToRolesAsync(u, SystemRoles.AccountRoles(role))).Succeeded, "role " + name);
            return u;
        }
        ClaimsPrincipal Principal(ApplicationUser u) => new(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, u.Id)], "Check"));
        var officer = await User("officer", SystemRoles.SourcingOfficer);
        var admin = await User("admin", SystemRoles.Admin);
        var requester = await User("requester", SystemRoles.RequestingDepartment);
        var manager = await User("manager", SystemRoles.SourcingManager);
        var evaluator = await User("department", SystemRoles.DepartmentRepresentative);
        var financial = await User("financial", SystemRoles.Finance);
        var technical = await User("technical", SystemRoles.Technical);
        var ceo = await User("ceo", SystemRoles.ChiefExecutiveOfficer);
        var cfo = await User("cfo", SystemRoles.ChiefFinancialOfficer);
        var cto = await User("cto", SystemRoles.ChiefTechnologyOfficer);
        var coo = await User("coo", SystemRoles.ChiefOperationsOfficer);
        var evaluationRoster = await flow.StandingCommittee("Evaluation", Principal(admin));
        await flow.SaveStandingCommittee(new StandingCommitteeInput
        {
            Committee = "Evaluation", Version = evaluationRoster.Version, MemberOneId = manager.Id,
            MemberTwoId = technical.Id, MemberThreeId = financial.Id, MemberFourId = evaluator.Id
        }, Principal(admin));
        var topRoster = await flow.StandingCommittee("Top", Principal(admin));
        await flow.SaveStandingCommittee(new StandingCommitteeInput
        {
            Committee = "Top", Version = topRoster.Version, MemberOneId = ceo.Id,
            MemberTwoId = cfo.Id, MemberThreeId = cto.Id, MemberFourId = coo.Id
        }, Principal(admin));
        var supplier = new Supplier { CompanyName = "Private supplier", RegistrationNumber = suffix, Email = "supplier@unified.test", Status = SupplierStatus.Approved };
        var competitor = new Supplier { CompanyName = "COMPETITOR_SECRET_NAME", RegistrationNumber = suffix + "X", Email = "competitor@unified.test", Status = SupplierStatus.Approved };
        db.AddRange(supplier, competitor); await db.SaveChangesAsync();
        var supplierUser = await User("supplier", SystemRoles.Supplier, supplier.Id);
        var outsider = await User("outsider", SystemRoles.Supplier, competitor.Id);
        var requestCategory = await db.ProcurementCategories.SingleAsync(c => c.Name == SupplierCategories.InformationTechnology);
        var request = new PurchaseRequest { DepartmentId = department.Id, CreatedByUserId = requester.Id, Title = "Unified purchase", Justification = "Test",
            Status = PurchaseRequestStatus.Submitted, CreatedAt = DateTime.UtcNow, SubmittedAt = DateTime.UtcNow,
            Items = { new PurchaseRequestItem { Name = "Laptop", CategoryId = requestCategory.Id, Quantity = 2, Unit = "unit", Specifications = "16 GB RAM" } } };
        db.Add(request); await db.SaveChangesAsync();

        async Task<string> Token(HttpClient client, string path)
        {
            var html = await client.GetStringAsync(path);
            var match = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");
            if (!match.Success) throw new InvalidOperationException("Antiforgery token missing for " + path);
            return WebUtility.HtmlDecode(match.Groups[1].Value);
        }
        async Task<HttpClient> Login(ApplicationUser u)
        {
            var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
            var token = await Token(client, "/Identity/Account/Login");
            var response = await client.PostAsync("/Identity/Account/Login", new FormUrlEncodedContent(new Dictionary<string, string>
            { ["Input.Email"] = u.Email!, ["Input.Password"] = password, ["__RequestVerificationToken"] = token }));
            Check(response.StatusCode == HttpStatusCode.Redirect, "Identity login " + u.FullName);
            return client;
        }
        async Task<string> Ajax(HttpClient client, string path)
        {
            using var message = new HttpRequestMessage(HttpMethod.Get, path); message.Headers.Add("X-Requested-With", "XMLHttpRequest");
            var response = await client.SendAsync(message); Check(response.StatusCode == HttpStatusCode.OK, "authorized response " + path);
            return await response.Content.ReadAsStringAsync();
        }
        using var officerClient = await Login(officer);
        var inbox = await officerClient.GetStringAsync("/SourcingOfficer/PurchaseRequests");
        Check(inbox.Contains("Details") && !inbox.Contains(">Create tender<"), "request inbox has details and no tender creation");
        var reviewToken = await Token(officerClient, "/SourcingOfficer/PurchaseRequests/Review/" + request.Id);
        var review = await officerClient.PostAsync("/SourcingOfficer/PurchaseRequests/Review/" + request.Id,
            new FormUrlEncodedContent(new Dictionary<string, string> { ["decision"] = "Accepted", ["comment"] = "Accepted for test", ["__RequestVerificationToken"] = reviewToken }));
        Check(review.StatusCode == HttpStatusCode.Redirect && review.Headers.Location?.ToString() == "/SourcingOfficer/PurchaseRequests/Prepare/" + request.Id, "accepting leads straight to tender preparation");
        var prepare = await officerClient.GetAsync("/SourcingOfficer/PurchaseRequests/Prepare/" + request.Id);
        Check(prepare.Headers.Location?.ToString() == "/SourcingOfficer/Workflow?request=" + request.Id, "preparation opens the tender form for the accepted request");
        var repeatAccept = await officerClient.PostAsync("/SourcingOfficer/PurchaseRequests/Review/" + request.Id,
            new FormUrlEncodedContent(new Dictionary<string, string> { ["decision"] = "Accepted", ["comment"] = "Accepted for test", ["__RequestVerificationToken"] = reviewToken }));
        Check(repeatAccept.StatusCode == HttpStatusCode.Redirect && await db.PurchaseRequestReviews.CountAsync(r => r.PurchaseRequestId == request.Id && r.Decision == ReviewDecision.Accepted) == 1,
            "a repeated acceptance records no second decision and resumes preparation");
        Check(await db.Notifications.CountAsync(n => n.UserId == officer.Id && n.Title == "Prepare the tender" && n.Link == "/SourcingOfficer/PurchaseRequests/Prepare/" + request.Id) == 1,
            "the Officer gets one actionable task linking to the preparation");
        var legacy = await officerClient.GetAsync("/SourcingOfficer/Tenders");
        Check(legacy.Headers.Location?.ToString() == "/SourcingOfficer/Workflow", "legacy tender list redirects to canonical page");
        var workflowHtml = await officerClient.GetStringAsync("/SourcingOfficer/Workflow?request=" + request.Id);
        bool OffersSeat(string field, string userId)
        {
            var select = Regex.Match(workflowHtml, "<select[^>]*id=\"" + field + "\"[^>]*>(.*?)</select>", RegexOptions.Singleline);
            return select.Success && select.Groups[1].Value.Contains("value=\"" + userId + "\"", StringComparison.Ordinal);
        }
        Check(OffersSeat("SourcingManagerId", manager.Id) && OffersSeat("DepartmentRepresentativeId", evaluator.Id) &&
              OffersSeat("FinancialRepresentativeId", financial.Id) && OffersSeat("TechnicalRepresentativeId", technical.Id),
            "tender form offers active users for all four evaluation seats");
        var createToken = await Token(officerClient, "/SourcingOfficer/Workflow");
        var legacyWrite = await officerClient.PostAsync("/SourcingOfficer/Tenders/Create", new FormUrlEncodedContent(new Dictionary<string, string>
        { ["__RequestVerificationToken"] = createToken }));
        Check(legacyWrite.StatusCode == HttpStatusCode.Conflict && await db.Tenders.CountAsync() == 0, "legacy creation cannot bypass canonical process");
        var legacyForward = await officerClient.PostAsync("/SourcingOfficer/Bids/ForwardToCommittee", new FormUrlEncodedContent(new Dictionary<string, string>
        { ["__RequestVerificationToken"] = createToken, ["RoundId"] = "1" }));
        Check(legacyForward.StatusCode == HttpStatusCode.Conflict, "legacy forwarding cannot create a parallel committee cycle");
        db.ChangeTracker.Clear();
        var input = new RequirementsInput { PurchaseRequestId = request.Id, Title = "Unified tender", Description = "Public description",
            TechnicalRequirements = "Laptop specifications", CommercialConditions = "JOD", DeliveryExpectations = "30 days", MandatoryCompliance = "Warranty",
            SourcingManagerId = manager.Id, DepartmentRepresentativeId = evaluator.Id, FinancialRepresentativeId = financial.Id, TechnicalRepresentativeId = technical.Id,
            EstimatedValue = 1000, Currency = "JOD", RequiredDocuments = ["OfficialQuotation"] };
        var id = await flow.Create(input, Principal(officer));
        var assignedEvaluation = await db.CommitteeMembers.Where(m => m.TenderId == id && m.CommitteeType == CommitteeType.Evaluation)
            .Select(m => m.UserId).ToListAsync();
        Check(assignedEvaluation.Count == 4 && assignedEvaluation.ToHashSet().SetEquals(new[] { manager.Id, evaluator.Id, financial.Id, technical.Id }) &&
              !await db.CommitteeMembers.AnyAsync(m => m.TenderId == id && m.CommitteeType == CommitteeType.Top),
            "canonical creation saves the selected evaluation members and defers Top Committee assignment");
        var duplicateRejected = false;
        try { await flow.Create(input, Principal(officer)); } catch (WorkflowException e) { duplicateRejected = e.Message.Contains("already has a tender"); }
        Check(duplicateRejected && await db.Tenders.CountAsync() == 1, "duplicate tender rejected on server");
        await flow.RequestWeights(id, Principal(officer));
        foreach (var member in new[] { manager, evaluator, financial, technical }) await flow.Propose(id, 40, 60, Principal(member));
        var managerApprovalRejected = false;
        try { await flow.ApproveWeights(id, Principal(manager)); } catch (WorkflowException) { managerApprovalRejected = true; }
        Check(managerApprovalRejected, "the manager cannot approve weights assigned to the Sourcing Officer");
        await flow.ApproveWeights(id, Principal(officer));
        await flow.OpenBidding(id, DateTimeOffset.UtcNow.AddDays(3), Principal(officer));
        Check(await db.Tenders.AnyAsync(t => t.Id == id && t.Status == TenderStatus.Bidding)
            && !await db.PreTenderReviews.AnyAsync(r => r.TenderId == id), "approved weights allow bidding without a Top Committee pre-tender review");
        var pickerPopup = await Ajax(officerClient, "/SourcingOfficer/Workflow/Details/" + id);
        var categoryFilter = Regex.Match(pickerPopup, "<select data-supplier-category>(.*?)</select>", RegexOptions.Singleline).Groups[1].Value;
        Check(categoryFilter.StartsWith("<option value=\"\">All categories", StringComparison.Ordinal)
              && WebUtility.HtmlDecode(categoryFilter).Contains("· in this tender") && !categoryFilter.Contains(" selected"),
            "a tender's item category is highlighted without hiding suppliers in other categories");
        using var inviteSelected = new HttpRequestMessage(HttpMethod.Post, "/SourcingOfficer/Workflow/InviteAll/" + id);
        inviteSelected.Headers.Add("X-Requested-With", "XMLHttpRequest");
        inviteSelected.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = createToken,
            ["supplierIds"] = supplier.Id.ToString()
        });
        using var inviteResponse = await officerClient.SendAsync(inviteSelected);
        Check(inviteResponse.StatusCode == HttpStatusCode.OK
              && (await db.TenderInvitations.Where(i => i.TenderRound.TenderId == id).Select(i => i.SupplierId).ToListAsync()).SequenceEqual(new[] { supplier.Id }),
            "the invitation form sends only the selected eligible supplier from another category");
        await flow.Invite(id, competitor.Id, Principal(officer));
        var dormant = new Supplier { CompanyName = "INACTIVE_SUPPLIER", RegistrationNumber = suffix + "D", Email = "inactive@unified.test", Status = SupplierStatus.Approved };
        db.Add(dormant); await db.SaveChangesAsync();
        var inactive = await User("inactive", SystemRoles.Supplier, dormant.Id); inactive.IsActive = false; await users.UpdateAsync(inactive);
        var inactiveRejected = false;
        try { await flow.Invite(id, dormant.Id, Principal(officer)); } catch (WorkflowException) { inactiveRejected = true; }
        Check(inactiveRejected, "inactive approved company cannot be invited through canonical endpoint");
        var invitation = await db.TenderInvitations.SingleAsync(i => i.SupplierId == supplier.Id);
        await flow.Respond(id, invitation.Id, true, Principal(supplierUser));
        Check(await db.Notifications.Where(n => n.UserId == supplierUser.Id).AllAsync(n => !n.Message.Contains("weights")), "new supplier notifications omit weights");
        using var supplierClient = await Login(supplierUser);
        var popup = await Ajax(supplierClient, "/Supplier/Workflow/Details/" + id);
        Check(popup.Contains("Prepare my offer") && !popup.Contains("COMPETITOR_SECRET_NAME") && !popup.Contains("Weights") && !popup.Contains("Committee")
            && !popup.Contains("40%") && !popup.Contains("60%"), "supplier popup has no committee, weights or competitor data");
        var officerPopup = await Ajax(officerClient, "/SourcingOfficer/Workflow/Details/" + id);
        Check(officerPopup.Contains("data-supplier-search") && officerPopup.Contains("select data-supplier-category")
            && !officerPopup.Contains("INACTIVE_SUPPLIER"), "canonical supplier picker preserves search, categories and eligibility");
        // Tender Passport: stickers come only from recorded events, suppliers never see it, a visit is recorded once per window.
        Check(officerPopup.Contains("data-tp ") && officerPopup.Contains("Tender Passport") && officerPopup.Contains("tp-k-approved") && officerPopup.Contains("Weights approved")
            && !officerPopup.Contains("tp-k-awarded") && !officerPopup.Contains("tp-k-rejected"), "officer popup shows the Tender Passport with only recorded stickers");
        Check(!popup.Contains("data-tp") && !popup.Contains("Tender Passport"), "suppliers never receive the Tender Passport");
        await Ajax(officerClient, "/SourcingOfficer/Workflow/Details/" + id);
        Check(await db.AuditLogs.CountAsync(a => a.EntityName == "Tender" && a.EntityId == id.ToString() && a.Action == AuditLogService.TenderViewed && a.UserId == officer.Id) == 1,
            "repeated popup refreshes record a single tender view");
        var opened = await db.AuditLogs.AsNoTracking().SingleAsync(a => a.EntityName == "Tender" && a.EntityId == id.ToString() && a.Action == "BiddingOpened");
        Check(opened.OldValues!.Contains("Draft") && opened.NewValues!.Contains("Bidding"), "workflow audit keeps the previous and new tender status");
        var trail = await Ajax(officerClient, "/SourcingOfficer/Workflow/Passport/" + id);
        Check(trail.Contains("tp-journey") && trail.Contains("Bidding opened") && trail.Contains("Weight proposal submitted") && trail.Contains("Draft"), "officer opens the full activity trail");
        using (var evaluatorClient = await Login(evaluator))
        {
            var memberTrail = await Ajax(evaluatorClient, "/EvaluationCommittee/Workflow/Passport/" + id);
            Check(memberTrail.Contains("Weights approved") && !memberTrail.Contains("Supplier invited") && !memberTrail.Contains("Invitation answered"),
                "before evaluation access a committee member's trail shows setup events only");
        }
        using (var supplierTrail = new HttpRequestMessage(HttpMethod.Get, "/Supplier/Workflow/Passport/" + id))
        {
            supplierTrail.Headers.Add("X-Requested-With", "XMLHttpRequest");
            Check((await supplierClient.SendAsync(supplierTrail)).StatusCode != HttpStatusCode.OK, "a supplier cannot open the activity trail");
        }
        var summary = await Ajax(supplierClient, "/Supplier/Workflow/TenderDetails/" + id);
        Check(!summary.Contains("Weights") && !summary.Contains("COMPETITOR_SECRET_NAME"), "supplier tender details are sanitized");
        var supplierRows = await flow.List(Principal(supplierUser));
        Check(supplierRows.Single().Status == "Open", "supplier list exposes availability only");
        var create = await supplierClient.GetAsync("/Supplier/Offers/Create/" + invitation.Id);
        var createHtml = await create.Content.ReadAsStringAsync();
        Check(create.StatusCode == HttpStatusCode.OK && createHtml.Contains("Submit Offer") && !createHtml.Contains("Save draft") && createHtml.Contains("Official quotation")
            && createHtml.Contains("One tax rate"), "offer page has one Submit Offer action, required documents and an explicit tax method");
        var offerService = scope.ServiceProvider.GetRequiredService<SupplierOfferService>();
        var draft = await offerService.GetCreateAsync(supplierUser.Id, invitation.Id);
        draft!.Input.TechnicalProposal = "Test proposal"; draft.Input.DeliveryDays = 12;
        foreach (var item in draft.Input.Items) item.UnitPrice = 400;
        Check(await offerService.CreateDraftAsync(supplierUser.Id, invitation.Id, draft.Input), "supplier draft saved through existing service");
        var bid = await db.Bids.SingleAsync(b => b.TenderInvitationId == invitation.Id);
        // A screening flag must not act as a predicted-outcome gate on a supplier's draft submission.
        bid.ComplianceStatus = ComplianceStatus.NonCompliant; await db.SaveChangesAsync();
        Check(await offerService.SubmitAsync(supplierUser.Id, bid.Id) is string noFile && noFile.Contains("attachment"), "offer cannot be submitted without an attachment");
        db.Add(new Document { FileName = "offer.pdf", StoragePath = "test/offer.pdf", ContentType = "application/pdf", SizeBytes = 10, UploadedByUserId = supplierUser.Id, UploadedAt = DateTime.UtcNow, BidId = bid.Id, DocumentType = "OfferAttachment" }); await db.SaveChangesAsync();
        Check(await offerService.SubmitAsync(supplierUser.Id, bid.Id) == null, "submission is not blocked by internal screening or predicted outcome"); db.RemoveRange(db.Documents.Where(d => d.BidId == bid.Id && d.StoragePath == "test/offer.pdf")); await db.SaveChangesAsync(); // compliance later checks the real upload path
        var privateOffer = await offerService.GetOfferAsync(supplierUser.Id, bid.Id);
        Check(privateOffer!.ComplianceStatus == "" && privateOffer.Outcome == null, "internal screening and unannounced outcome not exposed");
        var waitingPopup = await Ajax(officerClient, "/SourcingOfficer/Workflow/Details/" + id);
        Check(waitingPopup.Contains("data-close-bidding") && waitingPopup.Contains("Waiting for 1 invited supplier(s)")
            && Regex.IsMatch(waitingPopup, "<button[^>]*disabled[^>]*>Close bidding"), "close bidding stays visible and explains the pending supplier");
        var earlyCloseRejected = false;
        try { await flow.CloseBidding(id, Principal(officer)); } catch (WorkflowException) { earlyCloseRejected = true; }
        Check(earlyCloseRejected, "server still rejects early closure with an unanswered invitation");
        var additional = new List<(Supplier Company, ApplicationUser User)>();
        for (var number = 1; number <= 3; number++)
        {
            var company = new Supplier { CompanyName = "Additional " + number, RegistrationNumber = suffix + "-" + number,
                Email = $"additional{number}@unified.test", Status = SupplierStatus.Approved };
            db.Add(company); await db.SaveChangesAsync();
            var account = await User("additional" + number, SystemRoles.Supplier, company.Id);
            additional.Add((company, account));
            await flow.Invite(id, company.Id, Principal(officer));
        }
        async Task SubmitAdditional(int index)
        {
            var account = additional[index];
            var own = await db.TenderInvitations.SingleAsync(i => i.SupplierId == account.Company.Id);
            await flow.Respond(id, own.Id, true, Principal(account.User));
            await flow.SubmitBid(id, new BidInput { InvitationId = own.Id, TechnicalProposal = "Additional offer", DeliveryDays = 15, PaymentTerms = "30 days",
                Lines = await db.TenderItems.Where(i => i.TenderId == id).Select(i => new BidLineInput { TenderItemId = i.Id, UnitPrice = 450 }).ToListAsync() }, Principal(account.User));
        }
        await SubmitAdditional(0);
        var twoRejected = false;
        try { await flow.CloseBidding(id, Principal(officer)); } catch (WorkflowException) { twoRejected = true; }
        Check(twoRejected, "two submitted suppliers do not meet early-close threshold while others are pending");
        await SubmitAdditional(1);
        var readyPopup = await Ajax(officerClient, "/SourcingOfficer/Workflow/Details/" + id);
        Check(readyPopup.Contains("data-close-bidding") && readyPopup.Contains("without waiting for other invitations")
            && Regex.IsMatch(readyPopup, "<button(?![^>]*disabled)[^>]*>Close bidding"), "three submitted suppliers enable closing despite two pending invitations");
        await flow.CloseBidding(id, Principal(officer));
        Check(await db.TenderRounds.AnyAsync(r => r.TenderId == id && r.Status == TenderRoundStatus.Closed && r.ClosedAt != null), "authorized closure records the closed round");
        var beforeAttachmentRejected = false;
        try { await flow.Compliance(id, bid.Id, ComplianceStatus.Compliant, "Reviewed", "", Principal(officer)); }
        catch (WorkflowException e) { beforeAttachmentRejected = e.Message.Contains("Missing documents"); }
        Check(beforeAttachmentRejected, "compliant still requires an attached document");
        bid.MissingRequirements = "Supporting document";
        var demoPath = Path.Combine(root, "App_Data", "TenderDocuments", "compliance-" + suffix + ".txt");
        Directory.CreateDirectory(Path.GetDirectoryName(demoPath)!);
        await File.WriteAllTextAsync(demoPath, "Test attachment for compliance form regression.");
        db.Documents.Add(new Document { BidId = bid.Id, FileName = "TEST-compliance.txt", StoragePath = demoPath,
            ContentType = "text/plain", SizeBytes = new FileInfo(demoPath).Length, UploadedByUserId = officer.Id, UploadedAt = DateTime.UtcNow });
        await db.SaveChangesAsync();
        var realMissingRejected = false;
        try { await flow.Compliance(id, bid.Id, ComplianceStatus.Compliant, "Reviewed", "Still missing warranty", Principal(officer)); }
        catch (WorkflowException e) { realMissingRejected = e.Message.Contains("Missing documents"); }
        Check(realMissingRejected, "explicit unresolved requirements still block compliant");
        var complianceHtml = await officerClient.GetStringAsync("/SourcingOfficer/Bids/Compliance/" + bid.Id);
        Check(complianceHtml.Contains("data-offer-compliance") && complianceHtml.Contains("offer-compliance.js"), "compliance initializer loaded for full page and shared modal");
        var compliantResponse = await officerClient.PostAsync("/SourcingOfficer/Bids/Compliance/" + bid.Id,
            new FormUrlEncodedContent(new Dictionary<string, string> { ["status"] = "Compliant", ["__RequestVerificationToken"] = createToken }));
        await db.Entry(bid).ReloadAsync();
        Check(compliantResponse.StatusCode == HttpStatusCode.Redirect && bid.ComplianceStatus == ComplianceStatus.Compliant
            && string.IsNullOrWhiteSpace(bid.MissingRequirements), "officer confirming compliant clears stale hidden missing requirement after attachment review");
        var currentTender = await db.Tenders.SingleAsync(t => t.Id == id); currentTender.Status = TenderStatus.Evaluation; await db.SaveChangesAsync();
        var closedPopup = await Ajax(supplierClient, "/Supplier/Workflow/Details/" + id);
        Check(!closedPopup.Contains("Committee") && !closedPopup.Contains("Evaluation") && !closedPopup.Contains("Weights")
            && !closedPopup.Contains("Rank"), "internal stage remains confidential after bidding closes");
        var noToken = await supplierClient.PostAsync("/Supplier/Workflow/Respond/" + id, new FormUrlEncodedContent(new Dictionary<string, string>()));
        // Existing status-code middleware re-executes an empty 400 POST against a GET-only error page (405).
        Check((noToken.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.MethodNotAllowed)
            && await db.Bids.CountAsync() == 3 && invitation.Status == InvitationStatus.Accepted,
            "missing antiforgery rejected without mutation (HTTP " + (int)noToken.StatusCode + ")");
        using var outsiderClient = await Login(outsider);
        Check((await outsiderClient.GetAsync("/Supplier/Offers/Details/" + bid.Id)).StatusCode == HttpStatusCode.NotFound, "other supplier cannot read offer");
        Check((await supplierClient.GetAsync("/SourcingOfficer/Workflow")).StatusCode != HttpStatusCode.OK, "supplier cannot access officer workflow");
        // A known, uninvited tender must also be hidden through the new endpoints.
        var hiddenRequest = new PurchaseRequest { DepartmentId = department.Id, CreatedByUserId = requester.Id, Title = "Hidden", Justification = "Test", Status = PurchaseRequestStatus.Accepted };
        db.Add(hiddenRequest); await db.SaveChangesAsync();
        var hidden = new Tender { PurchaseRequestId = hiddenRequest.Id, Title = "HIDDEN_INTERNAL_TENDER", Description = "Private", CreatedByUserId = officer.Id };
        db.Add(hidden); await db.SaveChangesAsync();
        Check((await supplierClient.GetAsync("/Supplier/Workflow/TenderDetails/" + hidden.Id)).StatusCode == HttpStatusCode.NotFound, "uninvited tender hidden");
        // Complete scoring fixture, then exercise each member's real HTTP signing endpoint.
        // In particular, signing by Technical must not prevent the remaining three members.
        var round = await db.TenderRounds.SingleAsync(x => x.TenderId == id);
        var weightRound = await db.Set<WeightRound>().SingleAsync(x => x.TenderId == id);
        var seats = await db.CommitteeMembers.Where(x => x.TenderId == id && x.CommitteeType == CommitteeType.Evaluation).ToListAsync();
        var committeeRound = new CommitteeRound { TenderRoundId = round.Id, CommitteeType = CommitteeType.Evaluation, RoundNumber = 1,
            StartedAt = DateTime.UtcNow.AddHours(-2), FinancialWeight = weightRound.ApprovedFinancial, TechnicalWeight = weightRound.ApprovedTechnical };
        committeeRound.Criteria.Add(new EvaluationCriterion { Name = "Financial", Type = CriterionType.Financial, MaxScore = 100, Weight = weightRound.ApprovedFinancial });
        committeeRound.Criteria.Add(new EvaluationCriterion { Name = "Technical", Type = CriterionType.Technical, MaxScore = 100, Weight = weightRound.ApprovedTechnical });
        var ai = new AiAnalysis { TenderRoundId = round.Id, Status = "Completed", Model = "TEST", EvidenceJson = "{}", Findings = "Test evidence", ReviewedAt = DateTime.UtcNow, ReviewedByUserId = officer.Id };
        var session = new EvaluationSession { TenderRoundId = round.Id, WeightRoundId = weightRound.Id, CommitteeRound = committeeRound, AiAnalysis = ai,
            State = SessionState.Scheduled, StartsAt = DateTime.UtcNow.AddHours(-2), EndsAt = DateTime.UtcNow.AddHours(-1),
            EligibleBids = [new SessionBid { BidId = bid.Id }],
            Invitations = seats.Select(x => new EvaluationParticipation { CommitteeMemberId = x.Id, SentAt = DateTime.UtcNow.AddHours(-2) }).ToList() };
        db.Add(session); await db.SaveChangesAsync();
        await flow.CloseDueSessions(CancellationToken.None);
        Check(session.State == SessionState.Scheduled && session.ReportId == null, "meeting end does not close an incomplete evaluation");
        using (var lateClient = await Login(financial))
        {
            var afterMeeting = await Ajax(lateClient, "/EvaluationCommittee/Workflow/Details/" + id);
            Check(afterMeeting.Contains("Open the evaluation workspace") && afterMeeting.Contains("AI analysis for this meeting"), "the popup leads the member to the evaluation workspace");
            var workspace = await lateClient.GetStringAsync("/EvaluationCommittee/Workflow/Meeting/" + id);
            Check(workspace.Contains("data-evaluation-form") && workspace.Contains("step=\"0.1\"") && workspace.Contains("Approved evaluation model"), "financial and technical score inputs (one decimal) remain available after the meeting ends");
            Check(workspace.Contains("Sign and submit my evaluation") && workspace.Contains("data-report-signature") && !workspace.Contains("recommendation.RecommendedBidId"), "one submission carries scores and signature");
            Check(workspace.Contains("Download the AI analysis (PDF)") && workspace.Contains("advisory"), "AI analysis travels with the meeting and is marked advisory");
            var aiPdf = await lateClient.GetAsync("/EvaluationCommittee/Workflow/AiAnalysisPdf/" + id);
            var aiBytes = await aiPdf.Content.ReadAsByteArrayAsync();
            Check(aiPdf.StatusCode == HttpStatusCode.OK && aiPdf.Content.Headers.ContentType?.MediaType == "application/pdf" && aiBytes.Length > 500 && aiBytes[0] == (byte)'%' && aiBytes[1] == (byte)'P', "committee member downloads the AI analysis as a PDF");
            Check(await db.AuditLogs.AnyAsync(a => a.Action == AuditLogService.AiAnalysisPdfDownloaded && a.UserId == financial.Id && a.NewValues!.Contains("\"Watermarked\":true")),
                "the AI analysis PDF is a personalized watermarked copy and the download is audited");
        }
        var officerMeeting = await Ajax(officerClient, "/SourcingOfficer/Workflow/Details/" + id);
        Check(officerMeeting.Contains("Change the meeting time") && officerMeeting.Contains("RescheduleEvaluation"), "Officer popup offers to change the meeting time");
        Check((await officerClient.GetAsync("/SourcingOfficer/Workflow/AiAnalysisPdf/" + id)).StatusCode == HttpStatusCode.OK, "Officer can download the meeting's AI analysis");
        Check((await supplierClient.GetAsync("/Supplier/Workflow/AiAnalysisPdf/" + id)).StatusCode != HttpStatusCode.OK, "a supplier cannot download the AI analysis");
        var scores = new List<ScoreInput> { new() { BidId = bid.Id, Financial = 80, Technical = 80, Justification = "Test justification" } };
        var beforeStart = new EvaluationSession { StartsAt = DateTime.UtcNow.AddHours(1), State = SessionState.Scheduled };
        Check(!TenderWorkflowService.EvaluationOpen(beforeStart, DateTime.UtcNow), "evaluation stays closed before meeting start");
        // Officer moves the meeting while nobody has submitted; only the Officer may; never into the past.
        var originalStart = session.StartsAt; var originalEnd = session.EndsAt;
        var pastRefused = false; try { await flow.RescheduleEvaluation(id, DateTimeOffset.UtcNow.AddHours(-1), DateTimeOffset.UtcNow.AddHours(1), "test", Principal(officer)); } catch (WorkflowException) { pastRefused = true; }
        Check(pastRefused, "meeting cannot be moved into the past");
        var memberRefused = false; try { await flow.RescheduleEvaluation(id, DateTimeOffset.UtcNow.AddHours(1), DateTimeOffset.UtcNow.AddHours(2), "test", Principal(manager)); } catch (WorkflowException) { memberRefused = true; }
        Check(memberRefused, "only the Sourcing Officer can change the meeting time");
        var newStart = DateTimeOffset.UtcNow.AddHours(3); var newEnd = newStart.AddHours(1);
        await flow.RescheduleEvaluation(id, newStart, newEnd, "A member is not available at the original time.", Principal(officer));
        await db.Entry(session).ReloadAsync();
        Check(Math.Abs((session.StartsAt - newStart.UtcDateTime).TotalSeconds) < 1 && Math.Abs((session.EndsAt - newEnd.UtcDateTime).TotalSeconds) < 1 && session.OpenedAt == null, "Officer changes the meeting time before any submission");
        Check(await db.Notifications.AnyAsync(n => n.UserId == technical.Id && n.Title == "Evaluation meeting rescheduled" && n.Message.Contains("Jordan time")), "committee is notified of the new time in Jordan time");
        var earlyRefused = false; try { await flow.SubmitEvaluation(id, [new() { BidId = bid.Id, Financial = 80, Technical = 80, Justification = "early" }], Principal(technical)); } catch (WorkflowException) { earlyRefused = true; }
        Check(earlyRefused, "scoring stays closed before the new start time");
        session.StartsAt = originalStart; session.EndsAt = originalEnd; await db.SaveChangesAsync(); // test fixture: back to the elapsed window
        var meetingPng = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+a9WQAAAAASUVORK5CYII=").Concat(new byte[250]).ToArray();
        foreach (var scorer in new[] { technical, financial, evaluator, manager })
        {
            if (scorer.Id == financial.Id)
            {
                // Recover only sessions automatically closed by the old meeting-deadline rule.
                session.State = SessionState.Incomplete; session.ClosedAt = DateTime.UtcNow; session.ClosureReason = "Scheduled deadline reached";
                await db.SaveChangesAsync();
            }
            await flow.SubmitEvaluation(id, scores, Principal(scorer), meetingPng);
            Check(session.Invitations.Any(x => x.CommitteeMemberId == seats.Single(m => m.UserId == scorer.Id).Id && x.SubmittedAt != null), scorer.FullName + " submits after meeting end");
            Check(session.Invitations.Any(x => x.CommitteeMemberId == seats.Single(m => m.UserId == scorer.Id).Id && x.SignatureImage != null && x.SignedAt != null), scorer.FullName + " signature saved with the scores");
            if (scorer.Id == technical.Id)
            {
                var movedAfterSubmit = false; try { await flow.RescheduleEvaluation(id, DateTimeOffset.UtcNow.AddHours(1), DateTimeOffset.UtcNow.AddHours(2), "late", Principal(officer)); } catch (WorkflowException) { movedAfterSubmit = true; }
                Check(movedAfterSubmit, "meeting time is locked after the first final submission");
            }
            var scoreRepeatRejected = false;
            try { await flow.SubmitEvaluation(id, scores, Principal(scorer)); } catch (WorkflowException) { scoreRepeatRejected = true; }
            Check(scoreRepeatRejected, scorer.FullName + " cannot overwrite submitted scores");
        }
        await flow.CloseDueSessions(CancellationToken.None);
        Check(session.State == SessionState.Finalized && session.ReportId != null, "four signed submissions automatically finalize the report");
        var reportId = session.ReportId!.Value;
        var hash = session.Report!.PdfSha256!;
        Check(!(await flow.ReviewReport(reportId, Principal(technical))).CanSignCommittee && session.ReportSentAt != null,
            "the generated report requires no second committee signature");
        var officerDraftPopup = await Ajax(officerClient, "/SourcingOfficer/Workflow/Details/" + id);
        Check(!officerDraftPopup.Contains("/SendReport/") && !officerDraftPopup.Contains("RegenerateReport"), "the Officer has no send-for-signing or regenerate action");
        Check(await db.Set<ReportSignature>().CountAsync(s => s.TenderReportId == reportId && s.Decision == "SubmittedEvaluation" && s.SignatureImage != null) == 4,
            "all four signature images are associated with the final report");
        var finalReport = await db.TenderReports.AsNoTracking().SingleAsync(x => x.Id == reportId);
        Check(finalReport.Status == ReportStatus.Submitted && finalReport.PdfSha256 == hash, "four signatures finalize the report without changing canonical PDF hash");
        Check(await db.Notifications.CountAsync(n => n.UserId == officer.Id && n.Title == "Signed evaluation report received") == 1, "the completed, signed report reaches the Officer");
        var officerReview = await officerClient.GetStringAsync($"/SourcingOfficer/Reports/Review/{reportId}");
        Check(officerReview.Contains("technical") && officerReview.Contains("financial") && officerReview.Contains("department") && officerReview.Contains("manager"), "Officer review displays all four signer identities");
        Check((await officerClient.GetAsync($"/SourcingOfficer/Reports/Pdf/{reportId}")).StatusCode == HttpStatusCode.OK, "Officer can download the PDF with all persisted signatures");
        var reportPath = Path.Combine(root, "App_Data", "SajaDemo", "unified-tender-checks.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(reportPath)!);
        await File.WriteAllLinesAsync(reportPath, checks);
        Console.WriteLine($"Unified tender checks: {checks.Count} passed. Test database: OrangeAuthCheck_Unified_{suffix}. Working database unchanged.");
    }
}
