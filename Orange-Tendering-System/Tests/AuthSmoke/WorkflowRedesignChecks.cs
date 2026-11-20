using System.Net;
using System.ComponentModel.DataAnnotations;
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
using Orange_Tendering_System.Areas.Department.Services;
using Orange_Tendering_System.Areas.Department.ViewModels;
using Orange_Tendering_System.Data;
using Orange_Tendering_System.Models;
using Orange_Tendering_System.Models.Entities;
using Orange_Tendering_System.Services;
using Orange_Tendering_System.Services.Biometric;
using Orange_Tendering_System.ViewModels;

// Workflow redesign rules on real SQL Server, services and Identity HTTP: request categories and generated title, required
// offer documents, money precision and tax calculation, one-step offer submission, meeting page access before the start,
// one-decimal scores, member recommendations, automatic report generation and the Manager's executive summary.
public static class WorkflowRedesignChecks
{
    public static async Task Run()
    {
        var root = Environment.GetEnvironmentVariable("AUTH_PROJECT_ROOT") ?? throw new InvalidOperationException("AUTH_PROJECT_ROOT required");
        var suffix = Convert.ToHexString(RandomNumberGenerator.GetBytes(4));
        var server = Environment.GetEnvironmentVariable("AUTH_TEST_SERVER") ?? "localhost";
        var connection = $"Server={server};Database=OrangeAuthCheck_Redesign_{suffix};Trusted_Connection=True;TrustServerCertificate=True";
        Environment.SetEnvironmentVariable("ConnectionStrings__Default Connection", connection);
        Environment.SetEnvironmentVariable("DemoData__Enabled", "false");
        var password = "Rd!9" + suffix + suffix;
        using var factory = new WebApplicationFactory<WebAppMarker>().WithWebHostBuilder(b =>
        {
            b.UseContentRoot(root).UseEnvironment("Development");
            b.ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Default Connection"] = connection, ["BootstrapAdmin:Email"] = "", ["BootstrapAdmin:Password"] = "",
                ["DemoData:Enabled"] = "false", ["Logging:LogLevel:Default"] = "Error"
            }));
            b.ConfigureTestServices(s =>
            {
                s.RemoveAll<IHostedService>();
                s.PostConfigure<CookieAuthenticationOptions>(IdentityConstants.ApplicationScheme, options =>
                {
                    var validate = options.Events.OnValidatePrincipal;
                    options.Events.OnValidatePrincipal = async context =>
                    {
                        if (validate != null) await validate(context);
                        if (context.Principal?.IsInRole(SystemRoles.Supplier) == true && context.Principal.Identity is ClaimsIdentity identity &&
                            !identity.HasClaim(c => c.Type == BiometricSignInService.AuthenticationMethodClaim))
                            identity.AddClaim(new Claim(BiometricSignInService.AuthenticationMethodClaim, BiometricSignInService.FaceMethod));
                    };
                });
            });
        });
        var passes = 0;
        void Check(bool ok, string name) { if (!ok) throw new InvalidOperationException("FAIL " + name); passes++; Console.WriteLine("PASS " + name); }
        T Scoped<T>(IServiceScope sc) where T : notnull => sc.ServiceProvider.GetRequiredService<T>();
        async Task Flow(Func<TenderWorkflowService, Task> work) { using var sc = factory.Services.CreateScope(); await work(Scoped<TenderWorkflowService>(sc)); }
        async Task<T> Read<T>(Func<ApplicationDbContext, Task<T>> work) { using var sc = factory.Services.CreateScope(); return await work(Scoped<ApplicationDbContext>(sc)); }
        async Task Refuse(Func<TenderWorkflowService, Task> work, string label, string? contains = null)
        {
            try { await Flow(work); }
            catch (WorkflowException e) { Check(contains == null || e.Message.Contains(contains, StringComparison.OrdinalIgnoreCase), label + " (" + e.Message + ")"); return; }
            throw new InvalidOperationException("FAIL " + label + " (was allowed)");
        }
        ClaimsPrincipal P(ApplicationUser u) => new(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, u.Id)], "Check"));

        using var setup = factory.Services.CreateScope();
        var db = Scoped<ApplicationDbContext>(setup); var users = Scoped<UserManager<ApplicationUser>>(setup);
        var department = new Department { Name = "IT Department" }; db.Add(department); await db.SaveChangesAsync();
        async Task<ApplicationUser> User(string name, string role, int? supplierId = null)
        {
            var u = new ApplicationUser { UserName = name + "@redesign.test", Email = name + "@redesign.test", FullName = name, EmailConfirmed = true, IsActive = true,
                DepartmentId = supplierId.HasValue ? null : department.Id, SupplierId = supplierId };
            if (!(await users.CreateAsync(u, password)).Succeeded || !(await users.AddToRolesAsync(u, SystemRoles.AccountRoles(role))).Succeeded) throw new Exception("Identity " + name);
            return u;
        }
        var officer = await User("officer", SystemRoles.SourcingOfficer); var admin = await User("admin", SystemRoles.Admin);
        var requester = await User("requester", SystemRoles.RequestingDepartment); var manager = await User("manager", SystemRoles.SourcingManager);
        var depRep = await User("deprep", SystemRoles.DepartmentRepresentative); var fin = await User("financial", SystemRoles.Finance); var tech = await User("technical", SystemRoles.Technical);
        var outsider = await User("outsider", SystemRoles.Technical);
        var chiefs = new[] { await User("ceo", SystemRoles.ChiefExecutiveOfficer), await User("cfo", SystemRoles.ChiefFinancialOfficer), await User("cto", SystemRoles.ChiefTechnologyOfficer), await User("coo", SystemRoles.ChiefOperationsOfficer) };
        var flowSetup = Scoped<TenderWorkflowService>(setup);
        var top = await flowSetup.StandingCommittee("Top", P(admin));
        await flowSetup.SaveStandingCommittee(new StandingCommitteeInput { Committee = "Top", Version = top.Version, MemberOneId = chiefs[0].Id, MemberTwoId = chiefs[1].Id, MemberThreeId = chiefs[2].Id, MemberFourId = chiefs[3].Id }, P(admin));
        var suppliers = new List<(Supplier Company, ApplicationUser User)>();
        foreach (var n in new[] { "Alpha Tech", "Beta Systems", "Gamma Supply" })
        {
            var c = new Supplier { CompanyName = n, RegistrationNumber = suffix + n, Email = n.Replace(" ", "") + "@redesign.test", Status = SupplierStatus.Approved, Category = SupplierCategories.InformationTechnology };
            db.Add(c); await db.SaveChangesAsync(); suppliers.Add((c, await User(n.Replace(" ", "").ToLowerInvariant(), SystemRoles.Supplier, c.Id)));
        }

        // ---- Department: categories, generated title, required specifications ----
        Check(await db.ProcurementCategories.CountAsync(c => c.IsActive) >= 14 && await db.ProcurementCategories.AnyAsync(c => c.Name == "IT & Electronics"), "migration seeds manageable procurement categories");
        Check(DepartmentRequestService.GenerateTitle(["Laptop", "Network switch"], "IT Department") == "Laptop + 1 more item — IT Department", "request title is generated from items and department");
        Check(DepartmentRequestService.GenerateTitle([new string('x', 400)], "IT Department").Length <= 200, "generated title respects the 200-character limit");
        bool ValidQuantity(decimal quantity)
        {
            var item = new RequestItemForm { Name = "Laptop", CategoryId = 1, Specifications = "Office laptop", Quantity = quantity, Unit = "Piece" };
            return Validator.TryValidateObject(item, new ValidationContext(item), [], validateAllProperties: true);
        }
        Check(!ValidQuantity(1.006m) && ValidQuantity(1m) && ValidQuantity(10m), "department quantity accepts whole numbers and rejects decimal fractions");
        var it = await db.ProcurementCategories.SingleAsync(c => c.Name == "IT & Electronics"); var tel = await db.ProcurementCategories.SingleAsync(c => c.Name == "Telecommunications");
        var form = new RequestForm { Justification = "The IT team needs new laptops and a switch for the new office.", NeededBy = DateTime.UtcNow.Date.AddDays(60),
            Items = [new() { Name = "Laptop", CategoryId = it.Id, Specifications = "Core i7, 16 GB RAM, 512 GB SSD", Quantity = 2, Unit = "Piece" },
                     new() { Name = "Network switch", CategoryId = tel.Id, Specifications = "24-port gigabit managed", Quantity = 1, Unit = "Piece" }] };
        var prId = await new DepartmentRequestService(db, users).Save(requester, form, submit: true, Path.Combine(Path.GetTempPath(), "redesign-" + suffix));
        var pr = await db.PurchaseRequests.Include(r => r.Items).Include(r => r.Reviews).SingleAsync(r => r.Id == prId);
        Check(pr.Title == "Laptop + 1 more item — IT Department" && pr.Status == PurchaseRequestStatus.Submitted && pr.Items.All(i => i.CategoryId != null)
            && pr.Reviews.Single().SubmittedSnapshotJson.Contains("Telecommunications"), "request saved with categories, generated title and a submission snapshot");
        using var requesterClient = await Login(requester);
        var createPage = await requesterClient.GetStringAsync("/Department/PurchaseRequests/Create");
        Check(createPage.Contains("IT Department") && createPage.Contains("Items[0].CategoryId") && createPage.Contains("Telecommunications") && !createPage.Contains("name=\"Title\""),
            "request form shows department automatically, item categories and no title input");
        pr.Status = PurchaseRequestStatus.Accepted; await db.SaveChangesAsync();

        // ---- Tender preparation, weight revision and direct bidding ----
        int id = 0;
        await Flow(async w => id = await w.Create(new RequirementsInput { PurchaseRequestId = prId, Title = pr.Title, Description = "Laptops and switch", TechnicalRequirements = "Meet item specifications",
            CommercialConditions = "Prices in JOD", DeliveryExpectations = "30 days", MandatoryCompliance = "Signed quotation", SourcingManagerId = manager.Id, DepartmentRepresentativeId = depRep.Id,
            FinancialRepresentativeId = fin.Id, TechnicalRepresentativeId = tech.Id, EstimatedValue = 2000, Currency = "JOD", RequiredDocuments = ["OfficialQuotation", "ProductDatasheet"] }, P(officer)));
        Check(await Read(d => d.TenderItems.Where(i => i.TenderId == id).AllAsync(i => i.CategoryId != null)) && await Read(d => d.Tenders.AnyAsync(t => t.Id == id && t.RequiredDocuments == "OfficialQuotation,ProductDatasheet")),
            "tender inherits item categories and stores the required offer documents");
        await Flow(w => w.RequestWeights(id, P(officer)));
        await Refuse(w => w.Propose(id, 40.555m, 59.445m, P(manager)), "weight proposals accept at most two decimals", "2 decimal");
        foreach (var m in new[] { manager, depRep, fin, tech }) await Flow(w => w.Propose(id, 40, 60, P(m)));
        var departmentSeatId = await Read(d => d.CommitteeMembers.Where(c => c.TenderId == id && c.UserId == depRep.Id).Select(c => c.Id).SingleAsync());
        await Flow(w => w.ReturnWeights(id, [departmentSeatId], "Please revisit the technical weighting.", P(officer)));
        Check(await Read(d => d.Set<WeightRound>().Where(x => x.TenderId == id).Select(x => x.Proposals.Count).SingleAsync()) == 3,
            "only the selected member's weight proposal is reopened");
        await Flow(w => w.Propose(id, 41, 59, P(depRep)));
        var otherSeatIds = await Read(d => d.CommitteeMembers.Where(c => c.TenderId == id && (c.UserId == fin.Id || c.UserId == tech.Id)).Select(c => c.Id).ToArrayAsync());
        await Flow(w => w.ReturnWeights(id, otherSeatIds, "Please review the financial split and resubmit.", P(officer)));
        Check(await Read(d => d.Set<WeightRound>().Where(x => x.TenderId == id).Select(x => x.Proposals.Count).SingleAsync()) == 2
            && await Read(d => d.AuditLogs.AnyAsync(x => x.EntityName == "Tender" && x.EntityId == id.ToString() && x.Action == "WeightRevisionRequestedEvidence" && x.NewValues!.Contains("financial split"))),
            "two selected proposals reopen with the Officer's note recorded");
        await Flow(w => w.Propose(id, 42, 58, P(fin)));
        await Flow(w => w.Propose(id, 43, 57, P(tech)));
        await Flow(w => w.ApproveWeights(id, P(officer)));
        await Refuse(w => w.OpenBidding(id, DateTimeOffset.UtcNow.AddDays(90), P(officer)), "submission deadline cannot be after the department's needed-by date", "needs the items");
        await Flow(w => w.OpenBidding(id, DateTimeOffset.UtcNow.AddDays(5), P(officer)));
        foreach (var s in suppliers) await Flow(w => w.Invite(id, s.Company.Id, P(officer)));
        foreach (var s in suppliers)
        {
            var inv = await Read(d => d.TenderInvitations.SingleAsync(i => i.SupplierId == s.Company.Id));
            await Flow(w => w.Respond(id, inv.Id, true, P(s.User)));
        }

        // ---- Supplier offer: one-step submission, documents, precision, tax ----
        var firstInvitationId = await Read(d => d.TenderInvitations.Where(x => x.SupplierId == suppliers[0].Company.Id).Select(x => x.Id).SingleAsync());
        using (var supplierClient = await Login(suppliers[0].User))
        {
            var offerPage = await supplierClient.GetStringAsync("/Supplier/Offers/Create/" + firstInvitationId);
            Check(!offerPage.Contains("Tender Requirements (Read-Only)") && offerPage.Contains("data-line-image")
                && offerPage.Contains("data-sum=\"grand\"") && offerPage.Contains("Input.Items[0].Notes")
                && offerPage.Contains("data-save-draft") && offerPage.Contains("data-key=\"OfficialQuotation\"")
                && !offerPage.Contains("data-key=\"ReferenceImage\"") && !offerPage.Contains("name=\"Input.TechnicalProposal\" rows=\"2\" maxlength=\"10000\" required"),
                "supplier offer form is compact, shows only required attachments, and allows draft saving");
        }
        var pdf = "%PDF-1.4\n% redesign test document\n"u8.ToArray();
        SupplierOfferService.UploadedOfferFile File(string type) => SupplierOfferService.ReadUpload(type, type + ".pdf", pdf).File!;
        Check(SupplierOfferService.ReadUpload("OfficialQuotation", "fake.pdf", "not a pdf"u8.ToArray()).Error != null, "file content is validated by signature, not by name");
        Check(SupplierOfferService.ReadUpload("ReferenceImage", "photo.pdf", pdf).Error != null, "product images reject PDF files");
        async Task<(int? Bid, List<string> Errors)> Submit(int index, Action<SupplierDraftOfferInputModel> edit, params string[] docs)
        {
            using var sc = factory.Services.CreateScope(); var service = Scoped<SupplierOfferService>(sc);
            var inv = await Scoped<ApplicationDbContext>(sc).TenderInvitations.SingleAsync(i => i.SupplierId == suppliers[index].Company.Id);
            var page = await service.GetOfferFormAsync(suppliers[index].User.Id, inv.Id);
            if (page == null) return await service.SubmitOfferAsync(suppliers[index].User.Id, inv.Id, new SupplierDraftOfferInputModel(), docs.Select(File).ToList());
            var input = page.Input; input.CurrencyCode = "JOD"; input.DeliveryDays = 20; input.PaymentTerms = "Net 30 days after delivery"; input.TechnicalProposal = "Proposal";
            input.ValidUntil = DateTime.UtcNow.Date.AddDays(90); input.TaxMode = "Rate"; input.TaxRate = 16;
            foreach (var line in input.Items) { line.UnitPrice = 500; line.OfferedProduct = "Product"; line.OfferedSpecifications = "As requested"; }
            edit(input);
            return await service.SubmitOfferAsync(suppliers[index].User.Id, inv.Id, input, docs.Select(File).ToList());
        }
        var missing = await Submit(0, _ => { }, "OfficialQuotation");
        Check(missing.Bid == null && missing.Errors.Any(e => e.Contains("Product datasheets")), "an offer without a document the tender requires is refused");
        var fils = await Submit(0, i => i.Items[0].UnitPrice = 500.1234m, "OfficialQuotation", "ProductDatasheet");
        Check(fils.Bid == null && fils.Errors.Any(e => e.Contains("3 decimal")), "JOD amounts with more than three decimals are refused, not rounded");
        var usd = await Submit(0, i => { i.CurrencyCode = "USD"; i.Items[0].UnitPrice = 500.125m; }, "OfficialQuotation", "ProductDatasheet");
        Check(usd.Bid == null && usd.Errors.Any(e => e.Contains("2 decimal")), "USD amounts with more than two decimals are refused");
        var partial = await Submit(0, i => i.Items[0].Quantity = 1, "OfficialQuotation", "ProductDatasheet");
        Check(partial.Bid == null && partial.Errors.Any(e => e.Contains("Partial quantities")), "partial quantities are refused (no approved partial-offer rule)");
        var noDesc = await Submit(0, i => { i.IncludeOther = true; i.OtherCost = 10; }, "OfficialQuotation", "ProductDatasheet");
        Check(noDesc.Bid == null && noDesc.Errors.Any(e => e.Contains("Description of other costs")), "other costs require a description");
        Check(await Read(d => d.Bids.CountAsync()) == 0, "refused submissions leave no offer or draft behind");
        using (var supplierClient = await Login(suppliers[0].User))
        {
            var offerPage = await supplierClient.GetStringAsync("/Supplier/Offers/Create/" + firstInvitationId);
            var token = Regex.Match(offerPage, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value;
            var tenderItems = await Read(d => d.TenderItems.Where(x => x.TenderId == id).OrderBy(x => x.Id).Select(x => new { x.Id, x.Quantity }).ToListAsync());
            var draftFields = new List<KeyValuePair<string, string>>
            {
                new("__RequestVerificationToken", token), new("Input.CurrencyCode", "JOD"), new("Input.TaxMode", "Rate"), new("Input.TaxRate", "16")
            };
            for (var index = 0; index < tenderItems.Count; index++)
            {
                draftFields.Add(new($"Input.Items[{index}].TenderItemId", tenderItems[index].Id.ToString()));
                draftFields.Add(new($"Input.Items[{index}].Quantity", tenderItems[index].Quantity.ToString(System.Globalization.CultureInfo.InvariantCulture)));
                draftFields.Add(new($"Input.Items[{index}].UnitPrice", ""));
            }
            draftFields.Add(new("Input.Items[0].OfferedProduct", "Draft laptop"));
            var saved = await supplierClient.PostAsync("/Supplier/Offers/SaveDraft/" + firstInvitationId, new FormUrlEncodedContent(draftFields));
            var savedPage = saved.Headers.Location == null ? "" : await supplierClient.GetStringAsync(saved.Headers.Location);
            var draftExists = await Read(d => d.Bids.AnyAsync(x => x.TenderInvitationId == firstInvitationId && x.Status == BidStatus.Draft && x.Items.Any(i => i.OfferedProduct == "Draft laptop" && i.UnitPrice == 0)));
            Check(saved.StatusCode == HttpStatusCode.Redirect && savedPage.Contains("Draft saved") && draftExists,
                "incomplete supplier offer is saved as a draft without submission");
        }
        var secondInvitationId = await Read(d => d.TenderInvitations.Where(x => x.SupplierId == suppliers[1].Company.Id).Select(x => x.Id).SingleAsync());
        using (var scope = factory.Services.CreateScope())
        {
            var service = Scoped<SupplierOfferService>(scope);
            var page = (await service.GetOfferFormAsync(suppliers[1].User.Id, secondInvitationId))!;
            var png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+/rNsAAAAASUVORK5CYII=");
            var image = SupplierOfferService.ReadUpload("ReferenceImage", $"Item-{page.ItemContext[0].TenderItemId}-sample.png", png).File!;
            var imageDraft = await service.SaveDraftAsync(suppliers[1].User.Id, secondInvitationId, page.Input, [image]);
            Check(imageDraft.BidId != null, "product image is saved with an incomplete draft");
        }
        using (var supplierClient = await Login(suppliers[1].User))
        {
            var savedImagePage = await supplierClient.GetStringAsync("/Supplier/Offers/Create/" + secondInvitationId);
            Check(savedImagePage.Contains("data-remove-saved-image") && savedImagePage.Contains("/Supplier/Documents/Preview/"),
                "saved draft image appears in the product preview");
        }
        // Items: 2 × 500 + 1 × 500 = 1500. Shipping 50.
        var a = await Submit(0, i => { i.Items[0].UnitPrice = 600; i.IncludeShipping = true; i.ShippingCost = 50; i.IncludeInstallation = false; i.InstallationCost = 999; }, "OfficialQuotation", "ProductDatasheet");
        var b = await Submit(1, i => { i.IncludeShipping = true; i.ShippingCost = 50; i.TaxIncludesCharges = true; }, "OfficialQuotation", "ProductDatasheet");
        var c3 = await Submit(2, i => { i.TaxMode = "Amount"; i.TaxAmount = 100.5m; i.TaxRate = 99; i.TechnicalProposal = null; }, "OfficialQuotation", "ProductDatasheet");
        Check(a.Bid != null && b.Bid != null && c3.Bid != null, "three complete offers submitted in one step (" + string.Join("; ", a.Errors.Concat(b.Errors).Concat(c3.Errors)) + ")");
        var bids = await Read(d => d.Bids.Include(x => x.Items).Include(x => x.Documents).Where(x => x.Status == BidStatus.Submitted).ToDictionaryAsync(x => x.Id));
        var bidA = bids[a.Bid!.Value]; var bidB = bids[b.Bid!.Value]; var bidC = bids[c3.Bid!.Value];
        Check(bidA.TaxAmount == 272m && bidA.TaxRate == 16 && !bidA.TaxIncludesCharges && bidA.InstallationCost == 0 && OfferTotals.Of(bidA).GrandTotal == 2022m,
            "rate tax applies to items only by default; an unticked cost is never charged (1700 + 50 + 272 = 2022)");
        Check(bidB.TaxAmount == 248m && OfferTotals.Of(bidB).GrandTotal == 1798m, "rate tax includes additional costs only when the supplier says so ((1500 + 50) × 16%)");
        Check(bidC.TaxAmount == 100.5m && bidC.TaxRate == null && OfferTotals.Of(bidC).GrandTotal == 1600.5m, "a stated tax amount is used as entered and never doubled with a rate");
        Check(bidC.TechnicalProposal == null, "additional notes are optional when submitting an offer");
        Check(bidA.Documents.Select(x => x.DocumentType).OrderBy(x => x).SequenceEqual(["OfficialQuotation", "ProductDatasheet"]) && bidB.Documents.Count == 3 && bidA.SubmittedAt != null,
            "attachments are stored privately with their document types and the submission time");
        var savedImageId = bidB.Documents.Single(d => d.DocumentType == "ReferenceImage").Id;
        using (var supplierClient = await Login(suppliers[1].User))
        {
            var preview = await supplierClient.GetAsync("/Supplier/Documents/Preview/" + savedImageId);
            Check(preview.StatusCode == HttpStatusCode.OK && preview.Content.Headers.ContentType?.MediaType == "image/png",
                "saved product image is displayed inline for its supplier");
        }
        using (var otherSupplier = await Login(suppliers[0].User))
            Check((await otherSupplier.GetAsync("/Supplier/Documents/Preview/" + savedImageId)).StatusCode == HttpStatusCode.NotFound,
                "another supplier cannot view the product image");
        Check(bidA.Items.All(i => i.OfferedProduct == "Product"), "item-level offered product is stored");
        var again = await Submit(0, _ => { }, "OfficialQuotation", "ProductDatasheet");
        Check(again.Bid == null && again.Errors.Any(e => e.Contains("already submitted")), "a second submission of the same offer is refused");
        using (var sc = factory.Services.CreateScope())
            Check(await Scoped<SupplierOfferService>(sc).GetOfferAsync(suppliers[1].User.Id, bidA.Id) == null, "a supplier cannot read a competitor's offer");
        Check(await Read(d => d.Notifications.CountAsync(n => n.UserId == officer.Id && n.Title == "Supplier offer submitted")) == 3, "the Officer is notified of each submitted offer");

        // ---- Compliance, scheduling ----
        await Flow(w => w.CloseBidding(id, P(officer)));
        foreach (var bid in new[] { bidA, bidB, bidC }) await Flow(w => w.Compliance(id, bid.Id, ComplianceStatus.Compliant, "Documents complete", "", P(officer)));
        await Flow(w => w.Proceed(id, P(officer)));
        var roundId = await Read(d => d.TenderRounds.Where(r => r.TenderId == id).Select(r => r.Id).SingleAsync());
        var aiId = await Read(async d => { var ai = new AiAnalysis { TenderRoundId = roundId, Status = "Completed", Model = "fixture", EvidenceJson = "{}", Findings = "{\"Strengths\":\"Alpha has the longest warranty\"}" }; d.Add(ai); await d.SaveChangesAsync(); return ai.Id; });
        await Flow(w => w.ReviewAi(id, aiId, P(officer)));
        await Flow(w => w.Setup(id, new SessionInput { AiAnalysisId = aiId, StartsAt = DateTimeOffset.UtcNow.AddHours(2), EndsAt = DateTimeOffset.UtcNow.AddHours(3), MeetingUrl = "https://meet.jit.si/OrangeTender-redesign-" + suffix }, P(officer)));

        // ---- Before the meeting: page access yes, scoring no ----
        using (var member = await Login(fin))
        {
            var lobby = await member.GetAsync("/Meetings/Join/" + id);
            var lobbyHtml = await lobby.Content.ReadAsStringAsync();
            Check(lobby.StatusCode == HttpStatusCode.OK && lobbyHtml.Contains("Open the evaluation workspace") && !lobbyHtml.Contains("Please come back then"),
                "the meeting notification opens a useful page before the start time");
            var workspace = await member.GetStringAsync("/EvaluationCommittee/Workflow/Meeting/" + id);
            Check(workspace.Contains("Supplier comparison") && workspace.Contains("Alpha Tech"), "before the start the workspace compares the offers");
            Check(workspace.Contains("Approved evaluation model"), "before the start the workspace shows the locked evaluation model");
            Check(workspace.Contains("Alpha has the longest warranty"), "before the start the workspace shows the AI analysis");
            Check(!workspace.Contains("name=\"scores[0].Financial\""), "before the start there is no scoring form");
        }
        using (var stranger = await Login(outsider))
            Check((await stranger.GetAsync("/Meetings/Join/" + id)).StatusCode == HttpStatusCode.NotFound, "a user outside the committee cannot open the meeting page");
        List<ScoreInput> Scores(decimal fa, decimal ta) =>
        [
            new() { BidId = bidA.Id, Financial = fa, Technical = ta, Justification = "Alpha" },
            new() { BidId = bidB.Id, Financial = 70, Technical = 70, Justification = "Beta" },
            new() { BidId = bidC.Id, Financial = 60, Technical = 60, Justification = "Gamma" }
        ];
        MemberRecommendationInput Rec(int bidId, string? deviation = null) => new() { RecommendedBidId = bidId, Reason = "Best value for the department.", DeviationJustification = deviation };
        await Refuse(w => w.SubmitEvaluation(id, Scores(90, 90), P(fin), null, Rec(bidA.Id)), "scoring is refused before the meeting start", "Scoring opens");
        await Read(async d => { var s = await d.Set<EvaluationSession>().SingleAsync(x => x.TenderRoundId == roundId); s.StartsAt = DateTime.UtcNow.AddMinutes(-5); await d.SaveChangesAsync(); return 0; });

        // ---- During the meeting: precision, recommendation rules, automatic report ----
        using (var member = await Login(fin))
        using (var popupRequest = new HttpRequestMessage(HttpMethod.Get, "/EvaluationCommittee/Workflow/Details/" + id))
        {
            popupRequest.Headers.Add("X-Requested-With", "XMLHttpRequest");
            var popupResponse = await member.SendAsync(popupRequest);
            var popup = await popupResponse.Content.ReadAsStringAsync();
            Check(popupResponse.StatusCode == HttpStatusCode.OK && popup.Contains("class=\"wp-form wp-scores\"")
                && popup.Contains("name=\"recommendation.RecommendedBidId\"") && popup.Contains("name=\"recommendation.DeviationJustification\"")
                && popup.Contains("step=\"0.1\""), "Tariq5 popup scoring is available during the meeting with current recommendation and precision rules");
        }
        await Refuse(w => w.SubmitEvaluation(id, Scores(89.95m, 90), P(fin), null, Rec(bidA.Id)), "a score with two decimals (89.95) is refused, not rounded", "one decimal");
        await Refuse(w => w.SubmitEvaluation(id, Scores(89.5m, 90), P(fin), null, Rec(bidB.Id)), "recommending an offer below one's own top score needs a justification", "deviation");
        await Flow(w => w.SubmitEvaluation(id, Scores(89.5m, 90), P(fin), null, Rec(bidB.Id, "Beta offers local support that the department relies on.")));
        await Refuse(w => w.SubmitEvaluation(id, Scores(80, 80), P(fin), null, Rec(bidA.Id)), "a member cannot submit twice", "immutable");
        foreach (var m in new[] { manager, depRep, tech }) await Flow(w => w.SubmitEvaluation(id, Scores(88, 92), P(m), null, Rec(bidA.Id)));
        var session = await Read(d => d.Set<EvaluationSession>().Include(x => x.Report).Include(x => x.Invitations).SingleAsync(x => x.TenderRoundId == roundId));
        Check(session.State == SessionState.ReportDraft && session.Report != null && session.ReportSentAt != null && session.Report.RecommendedBidId == bidA.Id,
            "the fourth evaluation generates the report automatically and opens signing");
        Check(session.Report!.Content.Contains("MemberRecommendations") && session.Invitations.Count(x => x.RecommendedBidId == bidA.Id) == 3
            && session.Invitations.Single(x => x.RecommendedBidId == bidB.Id).RecommendationReason!.Contains("Deviation from own ranking"), "member recommendations and deviation reasons are recorded in the report");
        var stored = await Read(d => d.EvaluationScores.Where(x => x.BidId == bidA.Id).Select(x => x.Score).ToListAsync());
        Check(stored.Contains(89.5m) && stored.Contains(88m), "entered scores are stored exactly as typed");
        foreach (var m in new[] { manager, depRep, fin })
        { using var sc = factory.Services.CreateScope(); var w = Scoped<TenderWorkflowService>(sc); await w.ReportPdf(session.ReportId!.Value, P(m)); await w.FinalizeReport(id, "", P(m), session.Report.PdfSha256, true); }
        await Refuse(w => w.Forward(id, P(officer)), "the report cannot be forwarded with three of four signatures");
        { using var sc = factory.Services.CreateScope(); var w = Scoped<TenderWorkflowService>(sc); await w.ReportPdf(session.ReportId!.Value, P(tech)); await w.FinalizeReport(id, "", P(tech), session.Report.PdfSha256, true); }
        Check(await Read(d => d.Tenders.AnyAsync(t => t.Id == id && t.Status == TenderStatus.OfficerReview)), "four signatures send the completed report to the Officer");
        await Flow(w => w.Forward(id, P(officer)));

        // ---- Manager executive summary ----
        var summary = new ExecutiveSummaryInput { BusinessNeed = "The IT team needs laptops and a switch.", KeyFindings = "Three compliant offers; Alpha ranks first.",
            RecommendationReasons = "Alpha has the highest weighted score.", ManagerAssessment = "Short." };
        await Refuse(w => w.SaveExecutiveSummary(id, summary, P(manager)), "every summary section needs meaningful content", "20 to 4000");
        summary.ManagerAssessment = "I recommend approving the award to Alpha Tech.";
        await Flow(w => w.SaveExecutiveSummary(id, summary, P(manager)));
        summary.KeyFindings = "Three compliant offers were evaluated; Alpha Tech ranks first.";
        await Flow(w => w.SaveExecutiveSummary(id, summary, P(manager)));
        Check(await Read(d => d.TenderReports.CountAsync(r => r.ReportType == ReportType.ExecutiveSummary && r.Status == ReportStatus.Draft)) == 1
            && await Read(d => d.TenderReports.AnyAsync(r => r.ReportType == ReportType.ExecutiveSummary && r.Content.Contains("Alpha Tech ranks first"))), "the Manager edits one draft of the executive summary");
        await Refuse(w => w.SaveExecutiveSummary(id, summary, P(officer)), "only the assigned Manager writes the executive summary");
        { using var sc = factory.Services.CreateScope(); var w = Scoped<TenderWorkflowService>(sc); await w.ReportPdf(session.ReportId!.Value, P(manager)); await w.SubmitExecutiveSummary(id, summary, P(manager), session.Report.PdfSha256!, true, null); }
        Check(await Read(d => d.Tenders.AnyAsync(t => t.Id == id && t.Status == TenderStatus.TopCommitteeReview))
            && await Read(d => d.TenderReports.AnyAsync(r => r.ReportType == ReportType.ExecutiveSummary && r.Status == ReportStatus.Submitted)), "submitting the summary sends the report to the Top Committee");
        await Refuse(w => w.SaveExecutiveSummary(id, summary, P(manager)), "a submitted executive summary cannot be edited");
        Check(await Read(d => d.TenderReports.AnyAsync(r => r.Id == session.ReportId && r.PdfSha256 == session.Report.PdfSha256)), "the signed evaluation report itself was not changed");

        Console.WriteLine($"RESULT PASS={passes} FAIL=0 DATABASE=OrangeAuthCheck_Redesign_{suffix}. Real SQL Server; working database unchanged.");

        async Task<HttpClient> Login(ApplicationUser u)
        {
            var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
            var html = await client.GetStringAsync("/Identity/Account/Login");
            var token = WebUtility.HtmlDecode(Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value);
            var response = await client.PostAsync("/Identity/Account/Login", new FormUrlEncodedContent(new Dictionary<string, string>
            { ["Input.Email"] = u.Email!, ["Input.Password"] = password, ["__RequestVerificationToken"] = token }));
            if (response.StatusCode != HttpStatusCode.Redirect) throw new InvalidOperationException("Login failed for " + u.Email);
            return client;
        }
    }
}
