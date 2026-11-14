using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
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
using Orange_Tendering_System.ViewModels;

// Real SQL Server + Identity requests. Isolated test data never enters the user's working database.
public static class UnifiedTenderChecks
{
    public static async Task Run()
    {
        var root = Environment.GetEnvironmentVariable("AUTH_PROJECT_ROOT") ?? throw new InvalidOperationException("AUTH_PROJECT_ROOT required");
        var suffix = Convert.ToHexString(RandomNumberGenerator.GetBytes(4));
        var connection = $"Server=localhost;Database=OrangeAuthCheck_Unified_{suffix};Trusted_Connection=True;TrustServerCertificate=True";
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
            b.ConfigureTestServices(s => s.RemoveAll<IHostedService>()); // Never dispatch queued test invitations to SMTP.
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
            Check((await users.AddToRoleAsync(u, role)).Succeeded, "role " + name);
            return u;
        }
        ClaimsPrincipal Principal(ApplicationUser u) => new(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, u.Id)], "Check"));
        var officer = await User("officer", SystemRoles.SourcingOfficer);
        var requester = await User("requester", SystemRoles.RequestingDepartment);
        var manager = await User("manager", SystemRoles.SourcingManager);
        var evaluator = await User("department", SystemRoles.EvaluationCommittee);
        var financial = await User("financial", SystemRoles.EvaluationCommittee);
        var technical = await User("technical", SystemRoles.EvaluationCommittee);
        var supplier = new Supplier { CompanyName = "Private supplier", RegistrationNumber = suffix, Email = "supplier@unified.test", Status = SupplierStatus.Approved };
        var competitor = new Supplier { CompanyName = "COMPETITOR_SECRET_NAME", RegistrationNumber = suffix + "X", Email = "competitor@unified.test", Status = SupplierStatus.Approved };
        db.AddRange(supplier, competitor); await db.SaveChangesAsync();
        var supplierUser = await User("supplier", SystemRoles.Supplier, supplier.Id);
        var outsider = await User("outsider", SystemRoles.Supplier, competitor.Id);
        var request = new PurchaseRequest { DepartmentId = department.Id, CreatedByUserId = requester.Id, Title = "Unified purchase", Justification = "Test",
            Status = PurchaseRequestStatus.Submitted, CreatedAt = DateTime.UtcNow, SubmittedAt = DateTime.UtcNow,
            Items = { new PurchaseRequestItem { Name = "Laptop", Quantity = 2, Unit = "unit", Specifications = "16 GB RAM" } } };
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
        using var officerClient = await Login(officer);
        var inbox = await officerClient.GetStringAsync("/SourcingOfficer/PurchaseRequests");
        Check(inbox.Contains("Details") && !inbox.Contains(">Create tender<"), "request inbox has details and no tender creation");
        var reviewToken = await Token(officerClient, "/SourcingOfficer/PurchaseRequests/Review/" + request.Id);
        var review = await officerClient.PostAsync("/SourcingOfficer/PurchaseRequests/Review/" + request.Id,
            new FormUrlEncodedContent(new Dictionary<string, string> { ["decision"] = "Accepted", ["comment"] = "Accepted for test", ["__RequestVerificationToken"] = reviewToken }));
        Check(review.StatusCode == HttpStatusCode.Redirect, "accept request from details");
        var legacy = await officerClient.GetAsync("/SourcingOfficer/Tenders");
        Check(legacy.Headers.Location?.ToString() == "/SourcingOfficer/Workflow", "legacy tender list redirects to canonical page");
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
            EstimatedValue = 1000, Currency = "JOD" };
        var id = await flow.Create(input, Principal(officer));
        Check(await db.CommitteeMembers.CountAsync(m => m.TenderId == id) == 4, "canonical creation assigns four distinct seats");
        var duplicateRejected = false;
        try { await flow.Create(input, Principal(officer)); } catch (WorkflowException e) { duplicateRejected = e.Message.Contains("already has a tender"); }
        Check(duplicateRejected && await db.Tenders.CountAsync() == 1, "duplicate tender rejected on server");
        await flow.RequestWeights(id, Principal(officer));
        foreach (var member in new[] { manager, evaluator, financial, technical }) await flow.Propose(id, 40, 60, Principal(member));
        await flow.ApproveWeights(id, Principal(manager));
        await flow.OpenBidding(id, DateTimeOffset.UtcNow.AddDays(3), Principal(officer));
        await flow.Invite(id, supplier.Id, Principal(officer));
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
        async Task<string> Ajax(HttpClient client, string path)
        {
            using var message = new HttpRequestMessage(HttpMethod.Get, path); message.Headers.Add("X-Requested-With", "XMLHttpRequest");
            var response = await client.SendAsync(message); Check(response.StatusCode == HttpStatusCode.OK, "authorized response " + path);
            return await response.Content.ReadAsStringAsync();
        }
        var popup = await Ajax(supplierClient, "/Supplier/Workflow/Details/" + id);
        Check(popup.Contains("Prepare my offer") && !popup.Contains("COMPETITOR_SECRET_NAME") && !popup.Contains("Weights") && !popup.Contains("Committee")
            && !popup.Contains("40%") && !popup.Contains("60%"), "supplier popup has no committee, weights or competitor data");
        var officerPopup = await Ajax(officerClient, "/SourcingOfficer/Workflow/Details/" + id);
        Check(officerPopup.Contains("data-supplier-search") && officerPopup.Contains("select data-supplier-category")
            && !officerPopup.Contains("INACTIVE_SUPPLIER"), "canonical supplier picker preserves search, categories and eligibility");
        var summary = await Ajax(supplierClient, "/Supplier/Workflow/TenderDetails/" + id);
        Check(!summary.Contains("Weights") && !summary.Contains("COMPETITOR_SECRET_NAME"), "supplier tender details are sanitized");
        var supplierRows = await flow.List(Principal(supplierUser));
        Check(supplierRows.Single().Status == "Open", "supplier list exposes availability only");
        var create = await supplierClient.GetAsync("/Supplier/Offers/Create/" + invitation.Id);
        Check(create.StatusCode == HttpStatusCode.OK && (await create.Content.ReadAsStringAsync()).Contains("Save draft"), "eligible supplier can prepare an offer");
        var offerService = scope.ServiceProvider.GetRequiredService<SupplierOfferService>();
        var draft = await offerService.GetCreateAsync(supplierUser.Id, invitation.Id);
        draft!.Input.TechnicalProposal = "Test proposal"; draft.Input.DeliveryDays = 12;
        foreach (var item in draft.Input.Items) item.UnitPrice = 400;
        Check(await offerService.CreateDraftAsync(supplierUser.Id, invitation.Id, draft.Input), "supplier draft saved through existing service");
        var bid = await db.Bids.SingleAsync(b => b.TenderInvitationId == invitation.Id);
        // A screening flag must not act as a predicted-outcome gate on a supplier's draft submission.
        bid.ComplianceStatus = ComplianceStatus.NonCompliant; await db.SaveChangesAsync();
        Check(await offerService.SubmitAsync(supplierUser.Id, bid.Id) == null, "submission is not blocked by internal screening or predicted outcome");
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
        var seats = await db.CommitteeMembers.Where(x => x.TenderId == id).ToListAsync();
        var committeeRound = new CommitteeRound { TenderRoundId = round.Id, CommitteeType = CommitteeType.Evaluation, RoundNumber = 1,
            StartedAt = DateTime.UtcNow.AddHours(-2), FinancialWeight = weightRound.ApprovedFinancial, TechnicalWeight = weightRound.ApprovedTechnical };
        committeeRound.Criteria.Add(new EvaluationCriterion { Name = "Financial", Type = CriterionType.Financial, MaxScore = 100, Weight = weightRound.ApprovedFinancial });
        committeeRound.Criteria.Add(new EvaluationCriterion { Name = "Technical", Type = CriterionType.Technical, MaxScore = 100, Weight = weightRound.ApprovedTechnical });
        var ai = new AiAnalysis { TenderRoundId = round.Id, Status = "Completed", Model = "TEST", EvidenceJson = "{}", Findings = "Test evidence", ReviewedAt = DateTime.UtcNow, ReviewedByUserId = officer.Id };
        var session = new EvaluationSession { TenderRoundId = round.Id, WeightRoundId = weightRound.Id, CommitteeRound = committeeRound, AiAnalysis = ai,
            State = SessionState.Closed, StartsAt = DateTime.UtcNow.AddHours(-2), EndsAt = DateTime.UtcNow.AddHours(-1),
            EligibleBids = [new SessionBid { BidId = bid.Id }],
            Invitations = seats.Select(x => new EvaluationParticipation { CommitteeMemberId = x.Id, SentAt = DateTime.UtcNow.AddHours(-2), SubmittedAt = DateTime.UtcNow.AddHours(-1) }).ToList() };
        db.Add(session); await db.SaveChangesAsync();
        foreach (var seat in seats)
            foreach (var criterion in committeeRound.Criteria)
                db.EvaluationScores.Add(new EvaluationScore { BidId = bid.Id, CommitteeMemberId = seat.Id, EvaluationCriterionId = criterion.Id, Score = 80, Comment = "Test justification", ScoredAt = DateTime.UtcNow.AddHours(-1) });
        await db.SaveChangesAsync();
        await flow.GenerateReport(id, "Test unified report", Principal(technical));
        var reportId = session.ReportId!.Value;
        var hash = session.Report!.PdfSha256!;
        var png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+a9WQAAAAASUVORK5CYII=").Concat(new byte[250]).ToArray();
        foreach (var signer in new[] { technical, financial, evaluator, manager })
        {
            using var client = await Login(signer);
            var area = signer.Id == manager.Id ? "SourcingManager" : "EvaluationCommittee";
            var reviewUrl = $"/{area}/Reports/Review/{reportId}";
            var reviewHtml = await client.GetStringAsync(reviewUrl);
            Check(reviewHtml.Contains("data-report-signature") && reviewHtml.Contains("report-signature.js"), signer.FullName + " has a signature pad and persistent initializer");
            Check(reviewHtml.Contains("I have opened and reviewed this report version"), signer.FullName + " can sign own committee section");
            var token = await Token(client, reviewUrl);
            var pdf = await client.GetAsync($"/{area}/Reports/Pdf/{reportId}");
            Check(pdf.StatusCode == HttpStatusCode.OK, signer.FullName + " can review the protected PDF");
            var fields = new Dictionary<string, string> { ["documentHash"] = hash, ["confirmed"] = "true", ["signatureImage"] = "data:image/png;base64," + Convert.ToBase64String(png), ["__RequestVerificationToken"] = token };
            var signed = await client.PostAsync($"/EvaluationCommittee/Reports/Sign/{reportId}", new FormUrlEncodedContent(fields));
            Check(signed.StatusCode == HttpStatusCode.Redirect, signer.FullName + " signature HTTP save: " + await signed.Content.ReadAsStringAsync());
            Check(await db.Set<ReportSignature>().AsNoTracking().AnyAsync(x => x.TenderReportId == reportId && x.UserId == signer.Id && x.SignatureImage != null), signer.FullName + " PNG persisted for own account");
            var repeat = await client.PostAsync($"/EvaluationCommittee/Reports/Sign/{reportId}", new FormUrlEncodedContent(fields));
            Check(repeat.StatusCode == HttpStatusCode.Redirect && await db.Set<ReportSignature>().CountAsync(x => x.TenderReportId == reportId && x.UserId == signer.Id) == 1, signer.FullName + " retry does not duplicate signature");
        }
        var finalReport = await db.TenderReports.AsNoTracking().SingleAsync(x => x.Id == reportId);
        Check(finalReport.Status == ReportStatus.Submitted && finalReport.PdfSha256 == hash, "four signatures finalize the report without changing canonical PDF hash");
        var officerReview = await officerClient.GetStringAsync($"/SourcingOfficer/Reports/Review/{reportId}");
        Check(officerReview.Contains("technical") && officerReview.Contains("financial") && officerReview.Contains("department") && officerReview.Contains("manager"), "Officer review displays all four signer identities");
        Check((await officerClient.GetAsync($"/SourcingOfficer/Reports/Pdf/{reportId}")).StatusCode == HttpStatusCode.OK, "Officer can download the PDF with all persisted signatures");
        var reportPath = Path.Combine(root, "App_Data", "SajaDemo", "unified-tender-checks.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(reportPath)!);
        await File.WriteAllLinesAsync(reportPath, checks);
        Console.WriteLine($"Unified tender checks: {checks.Count} passed. Test database: OrangeAuthCheck_Unified_{suffix}. Working database unchanged.");
    }
}
