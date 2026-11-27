using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
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
using Orange_Tendering_System.Services.Assistant;
using Orange_Tendering_System.Services.Biometric;
using Orange_Tendering_System.ViewModels;

// Workflow redesign rules on real SQL Server, services and Identity HTTP: request categories and generated title, required
// offer documents, money precision and tax calculation, one-step offer submission, meeting page access before the start,
// one-decimal scores, member recommendations, automatic report generation and the Manager's executive summary.
public static class WorkflowRedesignChecks
{
    private sealed class DocumentModelFixture : HttpMessageHandler
    {
        public int Requests { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var body = await request.Content!.ReadAsStringAsync(ct);
            if (body.Contains("Write a short internal tender award report", StringComparison.Ordinal))
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new { message = new { content = "Top Committee approved the financial tender and recorded Beta Systems as the winning supplier." } }) };
            if (!body.Contains("QA marker", StringComparison.Ordinal))
                return new HttpResponseMessage(HttpStatusCode.BadRequest);
            Requests++;
            const string analysis = """
                {"items":[
                  {"source":1,"explanation":"المبلغ المطلوب هو 1250 JOD.","quote":"QA marker: total is 1250 JOD."},
                  {"source":1,"explanation":"المبلغ 9999.","quote":"QA marker: total is 1250 JOD."},
                  {"source":1,"explanation":"تاريخ تسليم غير موثق.","quote":"Delivery is Thursday."}
                ]}
                """;
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new { message = new { content = analysis } }) };
        }
    }

    private sealed class ActivationInbox : Microsoft.AspNetCore.Identity.UI.Services.IEmailSender
    {
        public List<(string Email, string Subject)> Messages { get; } = [];
        public Task SendEmailAsync(string email, string subject, string htmlMessage)
        {
            Messages.Add((email, subject));
            return Task.CompletedTask;
        }
    }

    public static async Task Run()
    {
        var root = Environment.GetEnvironmentVariable("AUTH_PROJECT_ROOT") ?? throw new InvalidOperationException("AUTH_PROJECT_ROOT required");
        var suffix = Convert.ToHexString(RandomNumberGenerator.GetBytes(4));
        var server = Environment.GetEnvironmentVariable("AUTH_TEST_SERVER") ?? "localhost";
        var connection = $"Server={server};Database=OrangeAuthCheck_Redesign_{suffix};Trusted_Connection=True;TrustServerCertificate=True";
        Environment.SetEnvironmentVariable("ConnectionStrings__Default Connection", connection);
        Environment.SetEnvironmentVariable("DemoData__Enabled", "false");
        var password = "Rd!9" + suffix + suffix;
        var inbox = new ActivationInbox();
        var documentModel = new DocumentModelFixture();
        using var factory = new WebApplicationFactory<WebAppMarker>().WithWebHostBuilder(b =>
        {
            b.UseContentRoot(root).UseEnvironment("Development");
            b.ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Default Connection"] = connection, ["BootstrapAdmin:Email"] = "", ["BootstrapAdmin:Password"] = "",
                ["DemoData:Enabled"] = "false", ["Logging:LogLevel:Default"] = "Error",
                ["Smtp:Mode"] = "LocalInbox", ["Smtp:Host"] = "localhost", ["Smtp:Port"] = "1025",
                ["Smtp:AllowInsecureLoopback"] = "true", ["Smtp:From"] = "sender@example.test",
                ["Smtp:Username"] = "", ["Smtp:Password"] = "",
                ["AccountLinks:PublicBaseUrl"] = "https://localhost",
                ["Ollama:BaseUrl"] = "http://document-fixture.invalid/", ["Ollama:Model"] = "document-fixture"
            }));
            b.ConfigureTestServices(s =>
            {
                s.RemoveAll<IHostedService>();
                s.RemoveAll<Microsoft.AspNetCore.Identity.UI.Services.IEmailSender>();
                s.AddSingleton<Microsoft.AspNetCore.Identity.UI.Services.IEmailSender>(inbox);
                s.AddHttpClient<OllamaChatClient>().ConfigurePrimaryHttpMessageHandler(() => documentModel);
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
        ClaimsPrincipal ChatPrincipal(ApplicationUser actor, string role) => new(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, actor.Id), new Claim(ClaimTypes.Role, role)], "Check"));
        async Task<ChatReply> Chat(ApplicationUser actor, string role, string command, int? conversation = null)
        {
            using var scope = factory.Services.CreateScope();
            return await scope.ServiceProvider.GetRequiredService<AiChatService>()
                .SendAsync(ChatPrincipal(actor, role), conversation, command, CancellationToken.None);
        }
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
        Check((await users.AddToRoleAsync(manager, SystemRoles.EvaluationCommittee)).Succeeded, "the manager fixture has both the Sourcing Manager and Evaluation Committee roles");
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
        var categoryName = "AI category " + suffix;
        var categoryTools = AgentTools.Detect("بدي اعمل كاتاغوري جديده")!;
        var categoryEngine = Scoped<AgentEngine>(setup);
        var adminActor = new AssistantUser(admin.Id, admin.FullName, SystemRoles.Admin, [SystemRoles.Admin], department.Id, department.Name, null);
        var requestActor = new AssistantUser(requester.Id, requester.FullName, SystemRoles.RequestingDepartment, [SystemRoles.RequestingDepartment], department.Id, department.Name, null);
        var deniedCategory = await categoryEngine.StartAsync(requestActor, categoryTools, "بدي اعمل كاتاغوري جديده", true, default);
        Check(deniedCategory.Payload == null && deniedCategory.Text.Contains("مدير النظام"), "AI denies category creation for a non-Admin");
        var categoryStart = await categoryEngine.StartAsync(adminActor, categoryTools, "بدي اعمل كاتاغوري جديده", true, default);
        var collecting = AgentEngine.Read(categoryStart.Payload)!;
        Check(collecting.Asking == "name", "AI asks for the new category name");
        var categoryPreview = await categoryEngine.ContinueAsync(collecting, adminActor, P(admin), categoryName, null, default);
        var confirming = AgentEngine.Read(categoryPreview.Payload)!;
        Check(confirming.State == "Confirming" && !await db.ProcurementCategories.AnyAsync(c => c.Name == categoryName), "AI requires confirmation before writing a category");
        var categoryResult = await categoryEngine.ContinueAsync(confirming, adminActor, P(admin), "أكّد", confirming.ActionId, default);
        var savedCategory = await db.ProcurementCategories.AsNoTracking().SingleAsync(c => c.Name == categoryName);
        Check(AgentEngine.Read(categoryResult.Payload)?.State == "Done" && savedCategory.IsActive &&
            await db.AuditLogs.AnyAsync(a => a.Action == "CategoryCreated" && a.EntityId == savedCategory.Id.ToString()),
            "AI creates, verifies and audits the confirmed category");

        var followUpName = "Redai " + suffix;
        var previousChat = new AiChatConversation { UserId = admin.Id, Title = "Category follow-up", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
        db.AiChatConversations.Add(previousChat);
        foreach (var (role, content) in new[] { ("user", "بدي اعمل كاتاغوري جديده"), ("assistant", "What should I name it?"),
            ("user", followUpName), ("assistant", "Open the Categories page and add a category manually.") })
            db.AiChatMessages.Add(new AiChatMessage { Conversation = previousChat, Role = role, Content = content, CreatedAt = DateTime.UtcNow.AddMinutes(-2) });
        await db.SaveChangesAsync();
        var chatPrincipal = new ClaimsPrincipal(new ClaimsIdentity([
            new Claim(ClaimTypes.NameIdentifier, admin.Id), new Claim(ClaimTypes.Role, SystemRoles.Admin)
        ], "Check"));
        var chat = Scoped<AiChatService>(setup);
        var followUp = await chat.SendAsync(chatPrincipal, previousChat.Id, "انت بدك تعملها انشاء", default);
        var followUpPlan = AgentEngine.Read(followUp.Payload);
        Check(followUp.Kind == "agent-confirm" && followUp.Reply.Contains(followUpName) && followUpPlan != null &&
            !await db.ProcurementCategories.AnyAsync(c => c.Name == followUpName), "chat recovers the user's category and name from an older conversation");
        var followUpResult = await chat.SendAsync(chatPrincipal, previousChat.Id, "أكّد", default, actionId: followUpPlan!.ActionId);
        Check(followUpResult.Kind == "agent-done" && await db.ProcurementCategories.AnyAsync(c => c.Name == followUpName && c.IsActive),
            "chat confirmation creates the category without opening the Admin form");

        using (var adminChat = await Login(admin))
        {
            var page = await adminChat.GetStringAsync("/Admin");
            var csrf = WebUtility.HtmlDecode(Regex.Match(page, "data-csrf=\"([^\"]+)\"").Groups[1].Value);
            Check(!string.IsNullOrEmpty(csrf), "Admin chat includes its antiforgery token");
            async Task<ChatReply> Say(string message, int? conversationId = null, Guid? actionId = null)
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, "/AiChat/Send")
                {
                    Content = JsonContent.Create(new { message, conversationId, actionId, page = "/Admin" })
                };
                request.Headers.Add("RequestVerificationToken", csrf);
                using var response = await adminChat.SendAsync(request);
                response.EnsureSuccessStatusCode();
                return (await response.Content.ReadFromJsonAsync<ChatReply>())!;
            }
            var email = "chat.user." + suffix.ToLowerInvariant() + "@example.invalid";
            var chooseCreation = await Say("بدي تعملو انشاء");
            Check(chooseCreation.Kind == "create-kind", "chat asks what to create when the record type is missing");
            var onboarding = await Say("يوزر", chooseCreation.ConversationId);
            Check(onboarding.Kind == "agent-plan" && onboarding.Reply.Contains("الاسم"), "new employee chat asks for missing information");
            var review = await Say($"الاسم: Lina Saleh، البريد: {email}، الدور: Finance، القسم: IT Department", onboarding.ConversationId);
            var reviewPlan = AgentEngine.Read(review.Payload);
            Check(review.Kind == "agent-confirm" && reviewPlan != null && review.Reply.Contains(email) &&
                !await db.Users.AnyAsync(u => u.Email == email), "chat accepts the employee details together and waits for confirmation");
            var negative = await Say("do not confirm", onboarding.ConversationId, reviewPlan!.ActionId);
            Check(negative.Kind == "agent-confirm" && !await db.Users.AnyAsync(u => u.Email == email),
                "a negative phrase with an action ID cannot create an account");
            var saved = await Say("أكّد", onboarding.ConversationId, reviewPlan.ActionId);
            var account = await users.FindByEmailAsync(email);
            Check(saved.Kind == "agent-done" && account is { PasswordHash: null, MustChangePassword: true } &&
                account.DepartmentId == department.Id && await users.IsInRoleAsync(account, SystemRoles.Finance) &&
                inbox.Messages.Count(m => m.Email == email && m.Subject.Contains("Activate")) == 1,
                "chat creates the authorized employee and sends one activation message without setting a password");
        }
        Check(DepartmentRequestService.GenerateTitle(["Laptop", "Network switch"], "IT Department") == "Laptop + 1 more item — IT Department", "request title is generated from items and department");
        Check(DepartmentRequestService.GenerateTitle([new string('x', 400)], "IT Department").Length <= 200, "generated title respects the 200-character limit");
        bool ValidQuantity(decimal quantity)
        {
            var item = new RequestItemForm { Name = "Laptop", CategoryId = 1, Specifications = "Office laptop", Quantity = quantity, Unit = "Piece" };
            return Validator.TryValidateObject(item, new ValidationContext(item), [], validateAllProperties: true);
        }
        Check(!ValidQuantity(1.006m) && ValidQuantity(1m) && ValidQuantity(10m), "department quantity accepts whole numbers and rejects decimal fractions");
        var it = await db.ProcurementCategories.SingleAsync(c => c.Name == "IT & Electronics"); var tel = await db.ProcurementCategories.SingleAsync(c => c.Name == "Telecommunications");
        var form = new RequestForm { EvaluationMethod = TenderEvaluationMethod.Weighted, Justification = "The IT team needs new laptops and a switch for the new office.", NeededBy = DateTime.UtcNow.Date.AddDays(60),
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
        var documentCsrf = WebUtility.HtmlDecode(Regex.Match(createPage, "data-csrf=\"([^\"]+)\"").Groups[1].Value);
        var documentUpload = new MultipartFormDataContent();
        documentUpload.Add(new StringContent("QA marker: total is 1250 JOD. Valid unit information is included."), "file", "analysis-check.txt");
        var uploaded = await DocumentRequest("/AiChat/Upload", documentUpload);
        var analysisReply = await DocumentRequest("/AiChat/Send", JsonContent.Create(new { conversationId = uploaded.ConversationId, message = "بدي تحليل للملف" }));
        Check(analysisReply.Kind == "document-analysis" && analysisReply.Reply.Contains("QA marker: total is 1250 JOD.") &&
            !analysisReply.Reply.Contains("9999") && !analysisReply.Reply.Contains("Thursday") &&
            new[] { "PDF", "Excel", "Word" }.All(format => analysisReply.Reply.Contains(format)),
            "document analysis shows supported findings before asking for a format and omits unsupported claims");
        foreach (var (choice, extension) in new[] { ("Excel", "xlsx"), ("Word", "docx"), ("PDF", "pdf") })
        {
            var exported = await DocumentRequest("/AiChat/Send", JsonContent.Create(new { conversationId = uploaded.ConversationId, message = choice }));
            using var file = JsonDocument.Parse(exported.Payload!);
            var card = file.RootElement.GetProperty("file");
            var bytes = await requesterClient.GetByteArrayAsync(card.GetProperty("downloadUrl").GetString());
            using var stream = new MemoryStream(bytes);
            var preservesFooting = extension switch
            {
                "xlsx" => SheetContainsEvidence(stream),
                "docx" => WordContainsEvidence(stream),
                _ => bytes.AsSpan().StartsWith("%PDF-"u8)
            };
            Check(exported.Kind == "file" && card.GetProperty("format").GetString() == extension && preservesFooting,
                $"{choice} downloads the same saved analysis");
        }
        Check(documentModel.Requests == 1, "format selection does not rerun document analysis");
        Check(await Scoped<AiChatService>(setup).DeleteAsync(ChatPrincipal(requester, SystemRoles.RequestingDepartment), uploaded.ConversationId), "test analysis conversation cleaned up");
        await Scoped<AiChatFileStore>(setup).CleanupAsync(default);
        async Task<ChatReply> DocumentRequest(string url, HttpContent data)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = data };
            request.Headers.Add("RequestVerificationToken", documentCsrf);
            using var response = await requesterClient.SendAsync(request);
            if (!response.IsSuccessStatusCode) throw new InvalidOperationException($"{url}: {response.StatusCode} {await response.Content.ReadAsStringAsync()}");
            return (await response.Content.ReadFromJsonAsync<ChatReply>())!;
        }
        static bool SheetContainsEvidence(Stream stream)
        {
            using var book = new ClosedXML.Excel.XLWorkbook(stream);
            return book.Worksheet(1).CellsUsed().Any(c => c.GetString().Contains("QA marker: total is 1250 JOD."));
        }
        static bool WordContainsEvidence(Stream stream)
        {
            using var word = DocumentFormat.OpenXml.Packaging.WordprocessingDocument.Open(stream, false);
            return word.MainDocumentPart?.Document.InnerText.Contains("QA marker: total is 1250 JOD.") == true;
        }
        pr.Status = PurchaseRequestStatus.Accepted; await db.SaveChangesAsync();

        // ---- Tender preparation, weight revision and direct bidding ----
        int id;
        using (var creator = await Login(officer))
        {
            var page = await creator.GetStringAsync($"/SourcingOfficer/Workflow?request={prId}");
            var managerPicker = System.Text.RegularExpressions.Regex.Match(page, """<select\b[^>]*\bid="SourcingManagerId"[^>]*>[\s\S]*?</select>""").Value;
            Check(managerPicker.Contains($"""value="{manager.Id}" """), "the Officer wizard lists the dual-role manager in the Sourcing Manager seat");
            Check(page.Contains("Select Purchase Request") && page.Contains("Tender Requirements") && page.Contains("Evaluation Committee")
                && page.Contains("Review &amp; Create") && !page.Contains("Additional technical requirements"), "the Officer list renders the four-step tender wizard");
            Check(new[] { "CommercialConditions", "DeliveryExpectations", "MandatoryCompliance" }
                .All(field => !page.Contains($"name=\"{field}\"")), "the Officer wizard omits the three free-text requirements");
            using var arabicRequest = new HttpRequestMessage(HttpMethod.Get, $"/SourcingOfficer/Workflow?request={prId}");
            arabicRequest.Headers.Add("Cookie", "orange-lang=ar");
            using var arabic = await creator.SendAsync(arabicRequest);
            var arabicPage = WebUtility.HtmlDecode(await arabic.Content.ReadAsStringAsync());
            Check(arabic.IsSuccessStatusCode && arabicPage.Contains("dir=\"rtl\" lang=\"ar\"") && arabicPage.Contains("اختيار طلب الشراء"),
                "the Officer wizard renders its Arabic labels in RTL");
            var token = WebUtility.HtmlDecode(Regex.Match(page, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value);
            var values = new List<KeyValuePair<string, string>>
            {
                new("PurchaseRequestId", prId.ToString()), new("Title", "Client override"), new("Description", "Client override"),
                new("TechnicalRequirements", "Client override"),
                new("SourcingManagerId", manager.Id), new("DepartmentRepresentativeId", depRep.Id),
                new("FinancialRepresentativeId", fin.Id), new("TechnicalRepresentativeId", tech.Id),
                new("Currency", "JOD"), new("RequiredDocuments", "OfficialQuotation"), new("RequiredDocuments", "ProductDatasheet")
            };
            async Task<HttpResponseMessage> SubmitTender(string value)
            {
                using var post = new HttpRequestMessage(HttpMethod.Post, "/SourcingOfficer/Workflow/Create")
                {
                    Content = new FormUrlEncodedContent(values.Append(new("EstimatedValue", value)))
                };
                post.Headers.Add("RequestVerificationToken", token);
                post.Headers.Add("X-Requested-With", "XMLHttpRequest");
                return await creator.SendAsync(post);
            }
            using var invalid = await SubmitTender("0");
            Check(invalid.StatusCode == HttpStatusCode.BadRequest && (await invalid.Content.ReadAsStringAsync()).Contains("EstimatedValue"),
                "invalid tender value returns an inline validation key");
            using var created = await SubmitTender("2000");
            var result = await created.Content.ReadAsStringAsync();
            if (!created.IsSuccessStatusCode) throw new InvalidOperationException(result);
            using var tenderResponse = JsonDocument.Parse(result);
            id = tenderResponse.RootElement.GetProperty("id").GetInt32();
            using var duplicate = await SubmitTender("2000");
            Check(duplicate.StatusCode == HttpStatusCode.BadRequest && (await duplicate.Content.ReadAsStringAsync()).Contains("PurchaseRequestId"),
                "the same request cannot create a second tender");
        }
        Check(await Read(d => d.Tenders.AnyAsync(t => t.Id == id && t.Title == pr.Title && t.Description == pr.Justification
            && t.Requirements == null)), "the server derives inherited fields and creates the tender without free-text requirements");
        Check(await Read(d => d.TenderItems.Where(i => i.TenderId == id).AllAsync(i => i.CategoryId != null)) && await Read(d => d.Tenders.AnyAsync(t => t.Id == id && t.RequiredDocuments == "OfficialQuotation,ProductDatasheet")),
            "tender inherits item categories and stores the required offer documents");
        async Task<string> WeightScreen(ApplicationUser person, string area, bool arabic = false)
        {
            using var client = await Login(person);
            using var request = new HttpRequestMessage(HttpMethod.Get, $"/{area}/Workflow/Details/{id}");
            request.Headers.Add("X-Requested-With", "XMLHttpRequest");
            if (arabic) request.Headers.Add("Cookie", "orange-lang=ar");
            using var response = await client.SendAsync(request);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsStringAsync();
        }
        var sendScreen = await WeightScreen(officer, "SourcingOfficer");
        Check(sendScreen.Contains("Send to Committee") && sendScreen.Contains("data-wz-panel=\"2\"") && sendScreen.Contains("/RequestWeights")
            && new[] { manager, depRep, fin, tech }.All(member => sendScreen.Contains(WebUtility.HtmlEncode(member.FullName))),
            "send-to-committee shows the assigned roster and existing action");
        await Flow(w => w.RequestWeights(id, P(officer)));
        var voteScreen = await WeightScreen(fin, "EvaluationCommittee");
        Check(voteScreen.Contains("Set Evaluation Weights") && voteScreen.Contains("data-weight-sliders") &&
            voteScreen.Contains("id=\"ew-technical\" name=\"technical\" type=\"range\"") &&
            voteScreen.Contains("id=\"ew-financial\" name=\"financial\" type=\"range\""), "assigned evaluators see both weight sliders");
        var arabicVote = WebUtility.HtmlDecode(await WeightScreen(fin, "EvaluationCommittee", arabic: true));
        Check(arabicVote.Contains("wf-weight-setup\" dir=\"rtl\"") && arabicVote.Contains("تحديد أوزان التقييم"),
            "the evaluation weights form renders Arabic labels and RTL direction");
        await Refuse(w => w.Propose(id, 40.5m, 59.5m, P(manager)), "weight proposals reject fractional percentages", "whole-number");
        await Flow(w => w.Propose(id, 40, 60, P(fin)));
        var progressScreen = await WeightScreen(officer, "SourcingOfficer");
        Check(progressScreen.Contains("Voting Progress") && progressScreen.Contains("1 of 4 voted")
            && progressScreen.Contains("Voted</span>") && progressScreen.Contains("Pending</span>"), "voting progress reflects the recorded proposal");
        foreach (var m in new[] { manager, depRep, tech }) await Flow(w => w.Propose(id, 40, 60, P(m)));
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
        using (var officerClient = await Login(officer))
        using (var detail = new HttpRequestMessage(HttpMethod.Get, "/SourcingOfficer/Workflow/Details/" + id))
        {
            detail.Headers.Add("X-Requested-With", "XMLHttpRequest");
            var response = await officerClient.SendAsync(detail);
            var html = await response.Content.ReadAsStringAsync();
            Check(response.IsSuccessStatusCode && html.Contains("data-technical-total") && html.Contains("criteria[3].Weight"),
                "the Officer's approval form renders the interactive technical criteria table");
            Check(html.Contains("All proposals are in. Review the average") && html.Contains(">41.5%</span>") && html.Contains(">58.5%</span>"),
                "result displays the actual average of revised proposals");
            Check(html.Contains("value=\"16 GB RAM\"") && html.Contains("value=\"512 GB SSD\"") && html.Contains("value=\"Processor (CPU)\""),
                "technical weights offer tender specification details and laptop component suggestions");
        }
        var technicalCriteria = new[] {
            new TechnicalCriterionInput { Name = "Specifications", Weight = 40 },
            new TechnicalCriterionInput { Name = "Experience", Weight = 25 },
            new TechnicalCriterionInput { Name = "Support", Weight = 20 },
            new TechnicalCriterionInput { Name = "Delivery", Weight = 17 }
        };
        await Refuse(w => w.ApproveWeights(id, P(officer), technicalCriteria), "102% cannot be approved", "Reduce the technical criteria by 2%");
        technicalCriteria[3].Weight = 13;
        await Refuse(w => w.ApproveWeights(id, P(officer), technicalCriteria), "98% cannot be approved", "Add 2%");
        technicalCriteria[3].Weight = 15;
        await Flow(w => w.ApproveWeights(id, P(officer), technicalCriteria));
        var lockedScreen = await WeightScreen(officer, "SourcingOfficer");
        Check(lockedScreen.Contains("The weights are approved and locked.") && lockedScreen.Contains("/OpenBidding"),
            "approved weights expose the existing open-bidding action");
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
            Check(Regex.Matches(offerPage, @"data-offer-panel(?:\s|=)").Count == 5 &&
                offerPage.Contains("data-offer-review") && offerPage.Contains("data-offer-next"), "supplier offer renders all five wizard steps in one form");
            using var arabicRequest = new HttpRequestMessage(HttpMethod.Get, "/Supplier/Offers/Create/" + firstInvitationId);
            arabicRequest.Headers.Add("Cookie", "orange-lang=ar");
            using var arabicOffer = await supplierClient.SendAsync(arabicRequest);
            var arabicHtml = WebUtility.HtmlDecode(await arabicOffer.Content.ReadAsStringAsync());
            Check(arabicOffer.IsSuccessStatusCode && arabicHtml.Contains("lang=\"ar\" dir=\"rtl\"") && arabicHtml.Contains("المراجعة والإرسال"),
                "supplier offer supports Arabic RTL wizard navigation");
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
            return await SubmitForTender(id, index, edit, docs);
        }
        async Task<(int? Bid, List<string> Errors)> SubmitForTender(int tenderId, int index, Action<SupplierDraftOfferInputModel> edit, params string[] docs)
        {
            using var sc = factory.Services.CreateScope(); var service = Scoped<SupplierOfferService>(sc);
            var inv = await Scoped<ApplicationDbContext>(sc).TenderInvitations.SingleAsync(i => i.SupplierId == suppliers[index].Company.Id && i.TenderRound.TenderId == tenderId);
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
        (int? Bid, List<string> Errors) a;
        using (var supplierClient = await Login(suppliers[0].User))
        {
            var page = await supplierClient.GetStringAsync("/Supplier/Offers/Create/" + firstInvitationId);
            var token = WebUtility.HtmlDecode(Regex.Match(page, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value);
            var items = await Read(d => d.TenderItems.Where(item => item.TenderId == id).OrderBy(item => item.Id).ToListAsync());
            using var fields = new MultipartFormDataContent();
            void Field(string name, string value) => fields.Add(new StringContent(value), name);
            Field("__RequestVerificationToken", token);
            Field("Input.CurrencyCode", "JOD");
            Field("Input.ValidUntil", DateTime.UtcNow.Date.AddDays(90).ToString("yyyy-MM-dd"));
            Field("Input.DeliveryDays", "20");
            Field("Input.PaymentTerms", "Net 30 days after delivery");
            Field("Input.IncludeShipping", "true");
            Field("Input.ShippingCost", "50");
            Field("Input.InstallationCost", "999");
            Field("Input.TaxMode", "Rate");
            Field("Input.TaxRate", "16");
            for (var index = 0; index < items.Count; index++)
            {
                Field($"Input.Items[{index}].TenderItemId", items[index].Id.ToString());
                Field($"Input.Items[{index}].Quantity", items[index].Quantity.ToString(System.Globalization.CultureInfo.InvariantCulture));
                Field($"Input.Items[{index}].OfferedProduct", "Product");
                Field($"Input.Items[{index}].OfferedSpecifications", "As requested");
                Field($"Input.Items[{index}].Notes", "12 months");
                Field($"Input.Items[{index}].UnitPrice", index == 0 ? "600" : "500");
            }
            fields.Add(new ByteArrayContent(pdf), "doc_OfficialQuotation", "quote.pdf");
            fields.Add(new ByteArrayContent(pdf), "doc_ProductDatasheet", "datasheet.pdf");
            using var submitted = await supplierClient.PostAsync("/Supplier/Offers/Create/" + firstInvitationId, fields);
            Check(submitted.StatusCode == HttpStatusCode.Redirect && submitted.Headers.Location?.ToString().Contains("/Details/") == true,
                "the supplier wizard multipart post submits the draft through the existing endpoint");
            a = (await Read(d => d.Bids.Where(bid => bid.TenderInvitationId == firstInvitationId && bid.Status == BidStatus.Submitted)
                .Select(bid => (int?)bid.Id).SingleAsync()), []);
        }
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
        var schedule = await Chat(officer, SystemRoles.SourcingOfficer, $"Schedule evaluation meeting T-{id}");
        schedule = await Chat(officer, SystemRoles.SourcingOfficer, DateTime.UtcNow.AddHours(2).ToJordan("yyyy-MM-dd HH:mm"), schedule.ConversationId);
        schedule = await Chat(officer, SystemRoles.SourcingOfficer, "60", schedule.ConversationId);
        Check(schedule.Kind == "agent-confirm", "officer chat previews the evaluation meeting date and duration");
        var scheduled = await Chat(officer, SystemRoles.SourcingOfficer, "confirm", schedule.ConversationId);
        Check(scheduled.Reply.Contains("Meeting scheduled"), "confirmed officer chat schedules the meeting");

        // ---- Committee notification deep links and the Officer's reschedule form ----
        var popupLink = "/EvaluationCommittee/Workflow/Details/" + id;
        var invitations = await Read(d => d.Notifications.Where(n => n.Title == "Evaluation invitation" && n.Link == popupLink).ToListAsync());
        Check(invitations.Select(n => n.UserId).ToHashSet().SetEquals(new[] { fin.Id, manager.Id, depRep.Id, tech.Id }),
            "all four evaluation invitations link to the tender popup");
        var legacyNotificationId = await Read(async d =>
        {
            var legacy = new Notification { UserId = fin.Id, Title = "Evaluation invitation", Message = "Previously sent invitation", Link = "/Meetings/Join/" + id, CreatedAt = DateTime.UtcNow };
            d.Notifications.Add(legacy);
            await d.SaveChangesAsync();
            return legacy.Id;
        });
        var newStart = DateTimeOffset.UtcNow.AddHours(3);
        var newEnd = newStart.AddHours(1);
        using (var officerClient = await Login(officer))
        {
            using var popupRequest = new HttpRequestMessage(HttpMethod.Get, "/SourcingOfficer/Workflow/Details/" + id);
            popupRequest.Headers.Add("X-Requested-With", "XMLHttpRequest");
            var popup = await (await officerClient.SendAsync(popupRequest)).Content.ReadAsStringAsync();
            var rescheduleForm = Regex.Match(popup, "<form[^>]*RescheduleEvaluation.*?</form>", RegexOptions.Singleline).Value;
            var token = WebUtility.HtmlDecode(Regex.Match(rescheduleForm, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value);
            Check(popup.Contains("data-wz-panel=\"6\"") && popup.Contains("Change the meeting time") && token.Length > 0,
                "the Officer's Committee Evaluation wizard exposes the reschedule form after scheduling");
            using var request = new HttpRequestMessage(HttpMethod.Post, "/SourcingOfficer/Workflow/RescheduleEvaluation/" + id)
            {
                Content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["__RequestVerificationToken"] = token, ["startsAt"] = newStart.ToString("O"), ["endsAt"] = newEnd.ToString("O"),
                    ["reason"] = "A committee member is unavailable at the original time."
                })
            };
            request.Headers.Add("X-Requested-With", "XMLHttpRequest");
            using var response = await officerClient.SendAsync(request);
            Check(response.StatusCode == HttpStatusCode.OK, "the Officer reschedules through the wizard's HTTP endpoint");
        }
        var movedSession = await Read(d => d.Set<EvaluationSession>().SingleAsync(s => s.TenderRoundId == roundId));
        Check(movedSession.StartsAt == newStart.UtcDateTime && movedSession.EndsAt == newEnd.UtcDateTime,
            "both updated meeting times are persisted");
        Check(await Read(d => d.Notifications.CountAsync(n => n.Title == "Evaluation meeting rescheduled" && n.Link == popupLink)) == 4,
            "rescheduling sends four new notifications that link to the tender popup");

        // ---- Before the meeting: page access yes, scoring no ----
        using (var member = await Login(fin))
        {
            using var feed = JsonDocument.Parse(await member.GetStringAsync("/Notifications/Feed"));
            Check(feed.RootElement.GetProperty("items").EnumerateArray().Single(n => n.GetProperty("id").GetInt32() == legacyNotificationId)
                .GetProperty("link").GetString() == popupLink, "the notification bell updates a previously sent invitation's destination");
            var opened = await member.GetAsync("/Notifications/Open/" + legacyNotificationId);
            Check(opened.StatusCode == HttpStatusCode.Redirect && opened.Headers.Location?.OriginalString == popupLink,
                "the notifications page opens a previously sent invitation at the same destination");
            var deepLink = await member.GetAsync(popupLink);
            var popupIndex = deepLink.Headers.Location?.OriginalString;
            Check(deepLink.StatusCode == HttpStatusCode.Redirect && popupIndex?.EndsWith("?open=" + id) == true,
                "the notification opens the workflow list with the tender popup selected");
            var list = await member.GetStringAsync(popupIndex);
            Check(list.Contains("id=\"wf-dialog\"") && list.Contains($"data-id=\"{id}\" data-url=\"{popupLink}\""),
                "the linked list renders the selected tender's dialog entry point");
            var lobby = await member.GetAsync("/Meetings/Join/" + id);
            var lobbyHtml = await lobby.Content.ReadAsStringAsync();
            Check(lobby.StatusCode == HttpStatusCode.OK && lobbyHtml.Contains("Open the evaluation workspace") && !lobbyHtml.Contains("Please come back then"),
                "the meeting room remains available before the start time");
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
            new() { BidId = bidA.Id, Financial = fa, TechnicalScores = [ta, ta - 10, ta, ta], Justification = "Alpha" },
            new() { BidId = bidB.Id, Financial = 70, TechnicalScores = [70, 70, 70, 70], Justification = "Beta" },
            new() { BidId = bidC.Id, Financial = 60, TechnicalScores = [60, 60, 60, 60], Justification = "Gamma" }
        ];
        await Refuse(w => w.SubmitEvaluation(id, Scores(90, 90), P(fin), null), "scoring is refused before the meeting start", "Scoring opens");
        await Read(async d => { var s = await d.Set<EvaluationSession>().SingleAsync(x => x.TenderRoundId == roundId); s.StartsAt = DateTime.UtcNow.AddMinutes(-5); await d.SaveChangesAsync(); return 0; });

        // ---- During the meeting: precision, recommendation rules, automatic report ----
        using (var member = await Login(fin))
        using (var popupRequest = new HttpRequestMessage(HttpMethod.Get, "/EvaluationCommittee/Workflow/Details/" + id))
        {
            popupRequest.Headers.Add("X-Requested-With", "XMLHttpRequest");
            var popupResponse = await member.SendAsync(popupRequest);
            var popup = await popupResponse.Content.ReadAsStringAsync();
            Check(popupResponse.StatusCode == HttpStatusCode.OK && popup.Contains("class=\"wp-form wp-scores\"")
                && !popup.Contains("name=\"recommendation.") && popup.Contains("data-pad")
                && popup.Contains("maxlength=\"2\"") && popup.Contains("data-score-digits") && popup.Contains("data-weighted-preview"),
                "meeting popup keeps two-digit scores, weighted preview and mandatory signature");
        }
        await Refuse(w => w.SubmitEvaluation(id, Scores(89.95m, 90), P(fin), null), "a decimal score is refused", "whole-number");
        await Refuse(w => w.SubmitEvaluation(id, Scores(100, 90), P(fin), null), "a three-digit financial score is refused", "whole-number");
        await Refuse(w => w.SubmitEvaluation(id, Scores(89, 100), P(fin), null), "a three-digit technical score is refused", "whole-number");
        var evaluationSignature = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+a9WQAAAAASUVORK5CYII=");
        await Refuse(w => w.Forward(id, P(officer)), "the Officer cannot forward before four signed submissions");
        await Flow(w => w.SubmitEvaluation(id, Scores(89m, 90), P(fin), evaluationSignature));
        await Refuse(w => w.SubmitEvaluation(id, Scores(80, 80), P(fin), evaluationSignature), "a member cannot submit twice", "immutable");
        foreach (var m in new[] { manager, depRep, tech }) await Flow(w => w.SubmitEvaluation(id, Scores(88, 92), P(m), evaluationSignature));
        var session = await Read(d => d.Set<EvaluationSession>().Include(x => x.Report).Include(x => x.Invitations).SingleAsync(x => x.TenderRoundId == roundId));
        Check(session.State == SessionState.Finalized && session.Report != null && session.ReportSentAt != null && session.Report.RecommendedBidId == bidA.Id,
            "the fourth evaluation generates the report automatically and finalizes its recorded signatures");
        Check(!session.Report!.Content.Contains("MemberRecommendations") && session.Invitations.All(x => x.RecommendedBidId == null),
            "the report ranks offers from scores alone and stores no individual supplier recommendation");
        var stored = await Read(d => d.EvaluationScores.Where(x => x.BidId == bidA.Id).Select(x => x.Score).ToListAsync());
        Check(stored.Contains(89m) && stored.Contains(87.5m), "financial scores and weighted technical totals are stored");
        Check(await Read(d => d.Tenders.AnyAsync(t => t.Id == id && t.Status == TenderStatus.OfficerReview)), "four signed score submissions send the completed report directly to the Officer");
        Check(await Read(d => d.Set<ReportSignature>().CountAsync(r => r.TenderReportId == session.ReportId && r.Decision == "SubmittedEvaluation")) == 4,
            "the generated report records the four submitted evaluation signatures");
        var officerResult = await WeightScreen(officer, "SourcingOfficer");
        Check(officerResult.Contains("data-evaluation-ranking") && officerResult.Contains("data-calculated-recommendation")
            && officerResult.Contains("Recommended winner · Alpha Tech") && officerResult.Contains("Send to Manager")
            && !officerResult.Contains("name=\"recommendation."), "the Officer reviews the calculated winner and forwards without selecting a supplier");
        var handoff = await Chat(officer, SystemRoles.SourcingOfficer, $"Send the report to the Manager for T-{id}");
        Check(handoff.Kind == "agent-confirm", "officer chat previews forwarding the signed report");
        var forwarded = await Chat(officer, SystemRoles.SourcingOfficer, "confirm", handoff.ConversationId);
        Check(forwarded.Reply.Contains("Report sent to the Sourcing Manager"), "officer chat forwards the signed report on confirmation");
        var managerReview = await WeightScreen(manager, "SourcingManager");
        Check(managerReview.Contains("Manager Review") && managerReview.Contains("Review and Send to Top Management")
            && managerReview.Contains("Alpha has the longest warranty"), "the Manager sees the recommendation and the reviewed AI comparison");

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
        var topReview = WebUtility.HtmlDecode(await WeightScreen(chiefs[0], "TopCommittee"));
        Check(topReview.Contains("Top Management Review") && topReview.Contains("Accept & Award") &&
            topReview.Contains("value=\"Reject\"") && topReview.Contains("value=\"Retender\"") && topReview.Contains("Signature required"),
            "Top Management sees the three existing decisions with a signature");

        // Financial-only routing uses real submitted offers and the same signed Manager-to-Top approval path.
        form.EvaluationMethod = TenderEvaluationMethod.LowestPrice;
        int financialRequest;
        using (var sc = factory.Services.CreateScope())
        {
            financialRequest = await new DepartmentRequestService(Scoped<ApplicationDbContext>(sc), Scoped<UserManager<ApplicationUser>>(sc))
                .Save(requester, form, submit: true, Path.Combine(Path.GetTempPath(), "financial-" + suffix));
            var pendingRequest = await Chat(officer, SystemRoles.SourcingOfficer, $"Accept purchase request PR-{financialRequest}");
            Check(pendingRequest.Kind == "agent-confirm" && pendingRequest.Reply.Contains("PR-"), "chat prepares a specific request for review");
            var accepted = await Chat(officer, SystemRoles.SourcingOfficer, "confirm", pendingRequest.ConversationId);
            Check(accepted.Kind == "agent-done" && accepted.Reply.Contains("Purchase request accepted"), "chat accepts the request only after confirmation");
        }
        int financialTender;
        using (var sc = factory.Services.CreateScope())
        {
            financialTender = await Scoped<TenderWorkflowService>(sc).Create(new RequirementsInput
            {
                PurchaseRequestId = financialRequest, Title = "Financial price comparison", Description = "Comparable equipment",
                TechnicalRequirements = "Meet the specifications", CommercialConditions = "Price in JOD",
                DeliveryExpectations = "20 days", MandatoryCompliance = "Quote required", SourcingManagerId = manager.Id,
                EstimatedValue = 3000, Currency = "JOD", RequiredDocuments = ["OfficialQuotation"]
            }, P(officer));
        }
        await Refuse(w => w.RequestWeights(financialTender, P(officer)), "financial-only tenders cannot request committee weights");
        Check(!await Read(d => d.CommitteeMembers.AnyAsync(m => m.TenderId == financialTender && m.CommitteeType == CommitteeType.Evaluation)),
            "financial-only tenders have no Evaluation Committee assignments");
        await Flow(w => w.OpenBidding(financialTender, DateTimeOffset.UtcNow.AddDays(5), P(officer)));
        var supplierRefs = string.Join(", ", suppliers.Select(s => $"S-{s.Company.Id}"));
        var invitationsPlan = await Chat(officer, SystemRoles.SourcingOfficer, $"Invite suppliers {supplierRefs} to T-{financialTender}");
        Check(invitationsPlan.Kind == "agent-confirm" && invitationsPlan.Reply.Contains("Beta Systems"), "chat previews the exact selected invitation recipients");
        var invitationsSent = await Chat(officer, SystemRoles.SourcingOfficer, "confirm", invitationsPlan.ConversationId);
        Check(invitationsSent.Reply.Contains("Sent 3 invitations"), "chat sends the selected invitations");
        var financialBids = new List<int>();
        for (var index = 0; index < suppliers.Count; index++)
        {
            var supplier = suppliers[index];

            var invitation = await Read(d => d.TenderInvitations.SingleAsync(i => i.SupplierId == supplier.Company.Id && i.TenderRound.TenderId == financialTender));
            if (index == 0)
            {
                var prepared = await Chat(supplier.User, SystemRoles.Supplier, $"Accept my invitation INV-{invitation.Id}");
                Check(prepared.Kind == "agent-confirm", "supplier chat only prepares acceptance of their own invitation");
                var accepted = await Chat(supplier.User, SystemRoles.Supplier, "confirm", prepared.ConversationId);
                Check(accepted.Reply.Contains("Invitation accepted"), "supplier chat accepts their invitation on confirmation");
            }
            else await Flow(w => w.Respond(financialTender, invitation.Id, true, P(supplier.User)));
            if (index == 0)
            {
                var offerDraft = await Chat(supplier.User, SystemRoles.Supplier, $"Prepare my offer INV-{invitation.Id}");
                foreach (var answer in new[] { "JOD", "Laptop model | 10 | Core i7 and 16 GB RAM\nSwitch model | 10 | 24 managed ports",
                    "20", "Net 30 days after delivery", DateTime.UtcNow.AddDays(90).ToString("yyyy-MM-dd"), "0" })
                    offerDraft = await Chat(supplier.User, SystemRoles.Supplier, answer, offerDraft.ConversationId);
                Check(offerDraft.Kind == "agent-confirm" && offerDraft.Reply.Contains("Laptop model"), "supplier chat collects real quote fields and preview");
                var saved = await Chat(supplier.User, SystemRoles.Supplier, "confirm", offerDraft.ConversationId);
                Check(saved.Reply.Contains("Draft saved"), "supplier chat saves a draft on confirmation");
                using (var sc = factory.Services.CreateScope())
                {
                    var pdfBytes = session.Report!.PdfContent!;
                    using var stream = new MemoryStream(pdfBytes);
                    var file = new Microsoft.AspNetCore.Http.FormFile(stream, 0, stream.Length, "file", "test-quotation.pdf");
                    await Scoped<AiChatService>(sc).UploadAsync(ChatPrincipal(supplier.User, SystemRoles.Supplier), offerDraft.ConversationId, file, false, CancellationToken.None);
                }
                var attachment = await Chat(supplier.User, SystemRoles.Supplier, $"Attach the file to my offer INV-{invitation.Id}", offerDraft.ConversationId);
                attachment = await Chat(supplier.User, SystemRoles.Supplier, "1", attachment.ConversationId);
                attachment = await Chat(supplier.User, SystemRoles.Supplier, "OfficialQuotation", attachment.ConversationId);
                Check(attachment.Kind == "agent-confirm" && attachment.Reply.Contains("test-quotation.pdf"), "supplier chat previews the selected uploaded quotation");
                var attached = await Chat(supplier.User, SystemRoles.Supplier, "confirm", attachment.ConversationId);
                Check(attached.Reply.Contains("Document attached"), "confirmed chat attachment is saved with the draft");
                var submission = await Chat(supplier.User, SystemRoles.Supplier, $"Submit my offer INV-{invitation.Id}", attachment.ConversationId);
                Check(submission.Kind == "agent-confirm" && submission.Reply.Contains("Grand total"), "supplier chat previews totals before submission");
                var sent = await Chat(supplier.User, SystemRoles.Supplier, "confirm", submission.ConversationId);
                Check(sent.Reply.Contains("Offer submitted"), "supplier chat sends the offer after confirmation and document checks");
                financialBids.Add(await Read(d => d.Bids.Where(b => b.TenderInvitationId == invitation.Id && b.Status == BidStatus.Submitted).Select(b => b.Id).SingleAsync()));
            }
            else
            {
                var offer = await SubmitForTender(financialTender, index, input =>
                {
                    foreach (var line in input.Items) line.UnitPrice = 20;
                }, "OfficialQuotation");
                Check(offer.Bid.HasValue, "financial-only offer submitted: " + string.Join(", ", offer.Errors));
                financialBids.Add(offer.Bid!.Value);
            }
        }
        await Flow(w => w.CloseBidding(financialTender, P(officer)));
        using (var officerClient = await Login(officer))
        using (var detail = new HttpRequestMessage(HttpMethod.Get, "/SourcingOfficer/Workflow/Details/" + financialTender))
        {
            detail.Headers.Add("X-Requested-With", "XMLHttpRequest");
            var response = await officerClient.SendAsync(detail);
            var html = await response.Content.ReadAsStringAsync();
            Check(response.IsSuccessStatusCode && html.Contains("FinalizeFinancialReview") && !html.Contains("Schedule and invite the committee"),
                "the financial-only workflow shows screening and skips committee scheduling");
        }
        await Refuse(w => w.FinalizeFinancialReview(financialTender, P(officer)), "financial review waits for screening", "Complete compliance");
        foreach (var bid in financialBids)
            await Flow(w => w.Compliance(financialTender, bid, ComplianceStatus.Compliant, "Meets the requirements.", "", P(officer)));
        // The cheapest offer is withdrawn from consideration through a recorded screening result.
        await Flow(w => w.Compliance(financialTender, financialBids[0], ComplianceStatus.NonCompliant,
            "One specification is missing.", "Minimum specification", P(officer)));
        await Refuse(w => w.FinalizeFinancialReview(financialTender, P(officer)), "equal lowest totals block the recommendation", "tied");
        await Flow(w => w.Compliance(financialTender, financialBids[2], ComplianceStatus.NonCompliant,
            "Required evidence is missing.", "Required evidence", P(officer)));
        await Flow(w => w.FinalizeFinancialReview(financialTender, P(officer)));
        var priceReport = await Read(d => d.TenderReports.SingleAsync(r => r.TenderRound.TenderId == financialTender));
        Check(priceReport.RecommendedBidId == financialBids[1]
            && await Read(d => d.Tenders.AnyAsync(t => t.Id == financialTender && t.Status == TenderStatus.ManagerReview)),
            "the lowest compliant offer goes directly to Manager Review");
        using (var sc = factory.Services.CreateScope())
        {
            var w = Scoped<TenderWorkflowService>(sc);
            await w.ReportPdf(priceReport.Id, P(manager));
            await w.SubmitExecutiveSummary(financialTender, summary, P(manager), priceReport.PdfSha256!, true, null);
        }
        Check(await Read(d => d.Tenders.AnyAsync(t => t.Id == financialTender && t.Status == TenderStatus.TopCommitteeReview)),
            "financial-only recommendations continue to Top Committee after the Manager signs");
        var topCard = await Chat(chiefs[0], SystemRoles.TopCommittee, $"Approve and sign T-{financialTender}");
        Check(topCard.Kind == "agent-action" && topCard.Payload?.Contains("top_vote") == true
            && await Read(d => d.Tenders.AnyAsync(t => t.Id == financialTender && t.Status == TenderStatus.TopCommitteeReview)),
            "Top chat prepares an approval card without voting before verified consent");
        foreach (var chief in chiefs)
        {
            using var sc = factory.Services.CreateScope();
            var w = Scoped<TenderWorkflowService>(sc);
            await w.ReportPdf(priceReport.Id, P(chief));
            await w.TopVote(financialTender, VoteDecision.Approve, "", P(chief), priceReport.PdfSha256, true);
        }
        Check(await Read(d => d.Tenders.AnyAsync(t => t.Id == financialTender && t.Status == TenderStatus.Awarded
            && t.AwardedBidId == financialBids[1])), "four Top approvals award the financial-only tender to the lowest compliant supplier");

        var fileReply = await Chat(suppliers[0].User, SystemRoles.Supplier, "Export this text to Word: Quotation delivered successfully");
        Check(fileReply.Kind == "file" && fileReply.Payload?.Contains(".docx") == true, "chat generates a Word export from supplied text");
        var accessCheck = await Chat(suppliers[0].User, SystemRoles.Supplier, $"Accept purchase request PR-{financialRequest}");
        Check(accessCheck.Kind != "agent-confirm", "the supplier workspace cannot prepare an Officer approval");
        var mailBeforeAward = inbox.Messages.Count;
        using (var sc = factory.Services.CreateScope())
            await Scoped<InternalAwardReportDispatcher>(sc).DispatchPending(CancellationToken.None);
        var awardReport = await Read(d => d.InternalAwardReports.Include(r => r.OfficerNotification).Include(r => r.ManagerNotification)
            .SingleAsync(r => r.TenderId == financialTender));
        Check(awardReport.AiWritten && awardReport.OfficerNotification.UserId == officer.Id && awardReport.ManagerNotification.UserId == manager.Id
            && awardReport.ManagerNotification.Message.Contains("Beta Systems") && awardReport.OfficerEmailSentAt != null
            && awardReport.ManagerEmailSentAt != null && inbox.Messages.Count == mailBeforeAward + 2, "both award recipients receive the AI report in-app and by email");
        using (var sc = factory.Services.CreateScope())
            await Scoped<InternalAwardReportDispatcher>(sc).DispatchPending(CancellationToken.None);
        Check(inbox.Messages.Count == mailBeforeAward + 2, "reprocessing a delivered award report does not send duplicate email");

        for (var index = 0; index < chiefs.Length; index++)
        {
            using var sc = factory.Services.CreateScope();
            var flow = Scoped<TenderWorkflowService>(sc);
            var chief = P(chiefs[index]);
            await flow.ReportPdf(session.ReportId!.Value, chief);
            await flow.TopVote(id, VoteDecision.Approve, "", chief, session.Report.PdfSha256, true, evaluationSignature);
            var state = await Read(d => d.Tenders.Where(tender => tender.Id == id).Select(tender => tender.Status).SingleAsync());
            Check(state == (index == chiefs.Length - 1 ? TenderStatus.Awarded : TenderStatus.TopCommitteeReview),
                $"weighted tender status after signed approval {index + 1}");
        }
        var awardScreen = await WeightScreen(officer, "SourcingOfficer");
        Check(awardScreen.Contains("data-award-result") && awardScreen.Contains("Alpha Tech") && awardScreen.Contains("Award Notifications")
            && await Read(d => d.Tenders.AnyAsync(tender => tender.Id == id && tender.AwardedBidId == bidA.Id)),
            "the award screen shows the calculated winner and recorded notifications after unanimous signed approval");

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
