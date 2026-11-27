using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Orange_Tendering_System;
using Orange_Tendering_System.Areas.Department.Services;
using Orange_Tendering_System.Areas.Department.ViewModels;
using Orange_Tendering_System.Data;
using Orange_Tendering_System.Models;
using Orange_Tendering_System.Models.Entities;
using Orange_Tendering_System.Services;
using Orange_Tendering_System.ViewModels;

// Builds the seven presentation tenders through the application's own services, each stopped one step before its live action.
// Usage: dotnet run -- <database>   (OrangeTenderingTeamDev or OrangeTenderingTeamDev_Rehearsal)
var database = args.Length > 0 ? args[0] : throw new Exception("Database name required.");
if (database is not ("OrangeTenderingTeamDev" or "OrangeTenderingTeamDev_Rehearsal")) throw new Exception("Refusing database " + database);
var connection = $"Server=localhost;Database={database};Trusted_Connection=True;MultipleActiveResultSets=true;TrustServerCertificate=True";
// bin/Debug/net10.0 -> TenderSetup folder; the web project is three levels above it (Tests/PresentationSetup/TenderSetup).
var setupDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../.."));
var root = Path.GetFullPath(Path.Combine(setupDir, "../../.."));
Environment.SetEnvironmentVariable("ASPNETCORE_TEST_CONTENTROOT_ORANGE-TENDERING-SYSTEM", root);
Environment.SetEnvironmentVariable("ASPNETCORE_TEST_CONTENTROOT_ORANGE_TENDERING_SYSTEM", root);
var assets = Path.Combine(setupDir, "assets");
var images = Path.Combine(setupDir, "images");
var data = JsonDocument.Parse(File.ReadAllText(Path.Combine(setupDir, "tenders.json"))).RootElement;

Environment.SetEnvironmentVariable("ConnectionStrings__Default Connection", connection);
Environment.SetEnvironmentVariable("HostedDatabase__Enabled", "false");
Environment.SetEnvironmentVariable("BootstrapAdmin__Email", "");
Environment.SetEnvironmentVariable("BootstrapAdmin__Password", "");
using var app = new WebApplicationFactory<WebAppMarker>().WithWebHostBuilder(b =>
{
    b.UseContentRoot(root).UseEnvironment("Production");
    b.ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["ConnectionStrings:Default Connection"] = connection, ["HostedDatabase:Enabled"] = "false",
        ["BootstrapAdmin:Email"] = "", ["BootstrapAdmin:Password"] = "", ["Logging:LogLevel:Default"] = "Error",
        ["Meetings:Provider"] = "Jitsi"
    }));
});

async Task Run(Func<IServiceProvider, Task> work) { using var s = app.Services.CreateScope(); await work(s.ServiceProvider); }
async Task<T> Get<T>(Func<IServiceProvider, Task<T>> work) { using var s = app.Services.CreateScope(); return await work(s.ServiceProvider); }
Task Flow(Func<TenderWorkflowService, Task> work) => Run(sp => work(sp.GetRequiredService<TenderWorkflowService>()));
ClaimsPrincipal P(string id) => new(new ClaimsIdentity([new(ClaimTypes.NameIdentifier, id)], "setup"));
void Log(string text) => Console.WriteLine($"{DateTime.Now:HH:mm:ss} {text}");

var catalog = await Get(async sp => new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(sp.GetRequiredService<ApplicationDbContext>().Database.GetConnectionString()).InitialCatalog);
if (catalog != database) throw new Exception("Connected to " + catalog);

var u = await Get(async sp => await sp.GetRequiredService<ApplicationDbContext>().Users.ToDictionaryAsync(x => x.Email!.Split('@')[0], x => x.Id));
var users = await Get(async sp => await sp.GetRequiredService<ApplicationDbContext>().Users.AsNoTracking().ToDictionaryAsync(x => x.Id));
var categories = await Get(async sp => await sp.GetRequiredService<ApplicationDbContext>().ProcurementCategories.ToDictionaryAsync(x => x.Name, x => x.Id));
var weights = data.GetProperty("weights");
var criteria = data.GetProperty("criteria").EnumerateArray().Select(x => new TechnicalCriterionInput { Name = x[0].GetString(), Weight = x[1].GetDecimal() }).ToList();
byte[] Signature(string key) => File.ReadAllBytes(Path.Combine(assets, $"signature-{key}.png"));
string Now() => DateTime.UtcNow.ToString("u");

foreach (var t in data.GetProperty("tenders").EnumerateArray())
{
    var key = t.GetProperty("key").GetString()!;
    var stop = t.GetProperty("stop").GetInt32();
    var itemName = t.GetProperty("item").GetString()!;
    Log($"== {key}: {itemName} (stop {stop})");

    // ---- 1. Purchase request by Sameh, accepted by Sara -------------------------------------------------------
    var prId = await Get(async sp => await sp.GetRequiredService<ApplicationDbContext>().PurchaseRequests
        .Where(r => r.Items.Any(i => i.Name == itemName)).Select(r => (int?)r.Id).FirstOrDefaultAsync());
    if (prId == null)
    {
        prId = await Get(async sp =>
        {
            var db = sp.GetRequiredService<ApplicationDbContext>();
            var userManager = sp.GetRequiredService<UserManager<ApplicationUser>>();
            var sameh = await userManager.FindByIdAsync(u["sameh"]);
            var form = new RequestForm
            {
                Justification = t.GetProperty("justification").GetString()!,
                Priority = Enum.Parse<RequestPriority>(t.GetProperty("priority").GetString()!),
                EvaluationMethod = TenderEvaluationMethod.Weighted,
                NeededBy = DateTime.UtcNow.Date.AddDays(t.GetProperty("neededDays").GetInt32()),
                Items = [new RequestItemForm { Name = itemName, CategoryId = categories[t.GetProperty("category").GetString()!],
                    Specifications = t.GetProperty("specifications").GetString(), Quantity = t.GetProperty("quantity").GetInt32(), Unit = t.GetProperty("unit").GetString()! }],
                Attachments = []
            };
            var imagePath = new[] { ".jpg", ".jpeg", ".png" }.Select(e => Path.Combine(images, t.GetProperty("image").GetString() + e)).FirstOrDefault(File.Exists);
            MemoryStream? stream = null;
            if (imagePath != null)
            {
                stream = new MemoryStream(File.ReadAllBytes(imagePath));
                form.ItemImage = new FormFile(stream, 0, stream.Length, "ItemImage", Path.GetFileName(imagePath))
                    { Headers = new HeaderDictionary(), ContentType = imagePath.EndsWith(".png") ? "image/png" : "image/jpeg" };
            }
            var storage = Path.Combine(root, "Areas", "Department", "App_Data", "Uploads");
            return await new DepartmentRequestService(db, userManager).Save(sameh!, form, true, storage);
        });
        Log($"   PR-{prId} submitted by Sameh");
    }
    await Run(async sp =>
    {
        var db = sp.GetRequiredService<ApplicationDbContext>();
        var request = await db.PurchaseRequests.Include(r => r.Reviews).SingleAsync(r => r.Id == prId);
        if (request.Status != PurchaseRequestStatus.Submitted) return;
        var now = DateTime.UtcNow;
        request.Status = PurchaseRequestStatus.Accepted;
        var pending = request.Reviews.Where(x => x.Decision == ReviewDecision.Pending).OrderByDescending(x => x.SubmissionNumber).First();
        pending.Decision = ReviewDecision.Accepted; pending.Comment = "Requirements are complete and the budget is available."; pending.ReviewedByUserId = u["sara"]; pending.ReviewedAt = now;
        db.Notifications.Add(new Notification { UserId = request.CreatedByUserId, Title = "Purchase request decision", Message = "Accepted: Requirements are complete and the budget is available.", Link = $"/Department/PurchaseRequests/Details/{prId}", CreatedAt = now });
        db.Notifications.Add(new Notification { UserId = u["sara"], Title = "Prepare the tender", Message = $"PR-{prId} ({request.Title}) is accepted. Prepare its tender: requirements, documents and the evaluation committee.", Link = $"/SourcingOfficer/PurchaseRequests/Prepare/{prId}", CreatedAt = now });
        db.AuditLogs.Add(new AuditLog { UserId = u["sara"], EntityName = "PurchaseRequest", EntityId = prId.ToString(), Action = "PurchaseRequestReviewAccepted", CreatedAt = now,
            NewValues = JsonSerializer.Serialize(new { Decision = "Accepted", Comment = pending.Comment, ReviewedAt = now }) });
        await db.SaveChangesAsync();
        Log($"   PR-{prId} accepted by Sara");
    });
    if (stop == 1) continue;

    // ---- 2. Tender, committee and weight request --------------------------------------------------------------
    var tenderId = await Get(async sp => await sp.GetRequiredService<ApplicationDbContext>().Tenders.Where(x => x.PurchaseRequestId == prId).Select(x => (int?)x.Id).SingleOrDefaultAsync());
    if (tenderId == null)
    {
        var estimated = t.GetProperty("estimated").GetDecimal();
        tenderId = await Get(sp => sp.GetRequiredService<TenderWorkflowService>().Create(new RequirementsInput
        {
            PurchaseRequestId = prId.Value, SourcingManagerId = u["majed"], DepartmentRepresentativeId = u["tariq"], FinancialRepresentativeId = u["amal"], TechnicalRepresentativeId = u["saja"],
            EstimatedValue = estimated, Currency = "JOD",
            CommercialConditions = "Prices in Jordanian Dinar including 16% sales tax, delivery and installation. Payment within 30 days of delivery and acceptance. Offers valid for at least 90 days.",
            DeliveryExpectations = "Delivery and installation at the head office in Amman within 30 calendar days of the purchase order, with a delivery note signed by the IT department.",
            MandatoryCompliance = "Authorised dealer for the offered brand, valid commercial registration, manufacturer warranty, and full compliance with the listed specifications.",
            RequiredDocuments = ["OfficialQuotation", "ProductDatasheet"]
        }, P(u["sara"])));
        Log($"   T-{tenderId:0000} created by Sara");
    }
    var id = tenderId.Value;
    var state = await Get(async sp => { var db = sp.GetRequiredService<ApplicationDbContext>(); return (Tender: await db.Tenders.AsNoTracking().SingleAsync(x => x.Id == id), Weights: await db.Set<WeightRound>().AsNoTracking().Include(x => x.Proposals).SingleOrDefaultAsync(x => x.TenderId == id)); });
    if (state.Weights == null) { await Flow(w => w.RequestWeights(id, P(u["sara"]))); Log("   weights requested from the committee"); }
    var voters = stop == 2 ? new[] { "majed", "tariq", "amal" } : new[] { "majed", "tariq", "amal", "saja" };
    var voted = await Get(async sp => await sp.GetRequiredService<ApplicationDbContext>().Set<WeightProposal>().Where(x => x.WeightRound.TenderId == id).Select(x => x.MemberUserId).ToListAsync());
    foreach (var v in voters.Where(v => !voted.Contains(u[v])))
    {
        var pair = weights.GetProperty(v);
        await Flow(w => w.Propose(id, pair[0].GetDecimal(), pair[1].GetDecimal(), P(u[v])));
        Log($"   {v} proposed {pair[0].GetDecimal()}% financial / {pair[1].GetDecimal()}% technical");
    }
    if (stop <= 3) continue;

    // ---- 3. Approval, publishing and invitations --------------------------------------------------------------
    var weightState = await Get(async sp => await sp.GetRequiredService<ApplicationDbContext>().Set<WeightRound>().Where(x => x.TenderId == id).Select(x => x.State).SingleAsync());
    if (weightState == WeightState.AwaitingApproval) { await Flow(w => w.ApproveWeights(id, P(u["sara"]), criteria)); Log("   weights approved by Sara with three technical criteria"); }
    var tenderStatus = await Get(async sp => await sp.GetRequiredService<ApplicationDbContext>().Tenders.Where(x => x.Id == id).Select(x => x.Status).SingleAsync());
    if (tenderStatus == TenderStatus.Draft)
    {
        // Closing date: 1 December 2026 at 23:00 Jordan time for the open tenders, never later than the department's needed-by date.
        var deadline = new DateTimeOffset(2026, 12, 1, 23, 0, 0, TimeSpan.FromHours(3));
        await Flow(w => w.OpenBidding(id, deadline, P(u["sara"])));
        Log($"   published, closing {deadline:dd MMM yyyy HH:mm} Jordan time");
    }
    var supplierIds = await Get(async sp => await sp.GetRequiredService<ApplicationDbContext>().Users.Where(x => x.SupplierId != null).ToDictionaryAsync(x => x.Email!.Split('@')[0], x => x.SupplierId!.Value));
    var invited = await Get(async sp => await sp.GetRequiredService<ApplicationDbContext>().TenderInvitations.Where(x => x.TenderRound.TenderId == id).Select(x => x.SupplierId).ToListAsync());
    foreach (var s in new[] { "ali", "mosa", "omar" }.Where(s => !invited.Contains(supplierIds[s])))
        await Flow(w => w.Invite(id, supplierIds[s], P(u["sara"])));
    var offers = t.GetProperty("offers").EnumerateArray().ToList();
    foreach (var o in offers)
    {
        var s = o.GetProperty("supplier").GetString()!;
        var invitation = await Get(async sp => await sp.GetRequiredService<ApplicationDbContext>().TenderInvitations.AsNoTracking().SingleAsync(x => x.TenderRound.TenderId == id && x.SupplierId == supplierIds[s]));
        if (invitation.Status == InvitationStatus.Pending) { await Flow(w => w.Respond(id, invitation.Id, true, P(u[s]))); Log($"   {s} accepted the invitation"); }
        if (!o.GetProperty("submit").GetBoolean()) continue;
        var hasBid = await Get(async sp => await sp.GetRequiredService<ApplicationDbContext>().Bids.AnyAsync(b => b.TenderInvitationId == invitation.Id && b.Status == BidStatus.Submitted));
        if (hasBid) continue;
        var item = await Get(async sp => await sp.GetRequiredService<ApplicationDbContext>().TenderItems.AsNoTracking().SingleAsync(x => x.TenderId == id));
        var input = new SupplierDraftOfferInputModel
        {
            CurrencyCode = "JOD", DeliveryDays = o.GetProperty("delivery").GetInt32(), PaymentTerms = "30 days from delivery and acceptance",
            TechnicalProposal = o.GetProperty("proposal").GetString(), ValidUntil = DateTime.UtcNow.Date.AddDays(90),
            WarrantyMonths = o.GetProperty("warranty").GetInt32(), WarrantyType = "Onsite, parts and labour", WarrantyTerms = "Manufacturer warranty with local onsite service.",
            AfterSalesSupport = "Local support team in Amman, response within one business day.", TaxMode = "Rate", TaxRate = 16m, TaxIncludesCharges = false,
            Items = [new SupplierDraftItemInputModel { TenderItemId = item.Id, Name = item.Name, RequiredQuantity = item.Quantity, Quantity = item.Quantity,
                UnitPrice = o.GetProperty("unitPrice").GetDecimal(), OfferedProduct = o.GetProperty("product").GetString(), OfferedSpecifications = o.GetProperty("proposal").GetString() }]
        };
        var files = new List<SupplierOfferService.UploadedOfferFile>();
        foreach (var (type, file, label) in new[] { ("OfficialQuotation", $"{key}-{s}-quotation.pdf", "Quotation"), ("ProductDatasheet", $"{key}-{s}-datasheet.pdf", "Datasheet") })
        {
            var company = data.GetProperty("suppliers").GetProperty(s).GetProperty("company").GetString()!.Replace(' ', '-');
            var (upload, error) = SupplierOfferService.ReadUpload(type, $"{company}-{label}.pdf", File.ReadAllBytes(Path.Combine(assets, file)));
            if (error != null) throw new Exception(error);
            files.Add(upload!);
        }
        var (bidId, errors) = await Get(sp => sp.GetRequiredService<SupplierOfferService>().SubmitOfferAsync(u[s], invitation.Id, input, files));
        if (errors.Count > 0) throw new Exception($"{s} offer: " + string.Join("; ", errors));
        Log($"   {s} submitted offer #{bidId} at {o.GetProperty("unitPrice").GetDecimal():N2} JOD per unit");
    }
    if (stop <= 5) continue;

    // ---- 4. Closing, compliance, AI analysis and the evaluation meeting ----------------------------------------
    var roundStatus = await Get(async sp => await sp.GetRequiredService<ApplicationDbContext>().TenderRounds.Where(x => x.TenderId == id).Select(x => x.Status).SingleAsync());
    if (roundStatus == TenderRoundStatus.Open) { await Flow(w => w.CloseBidding(id, P(u["sara"]))); Log("   bidding closed"); }
    var unchecked_ = await Get(async sp => await sp.GetRequiredService<ApplicationDbContext>().Bids.Where(b => b.TenderInvitation.TenderRound.TenderId == id && b.Status == BidStatus.Submitted && b.ComplianceStatus == ComplianceStatus.NotChecked).Select(b => b.Id).ToListAsync());
    foreach (var bid in unchecked_)
        await Flow(w => w.Compliance(id, bid, ComplianceStatus.Compliant, "Checked automatically: the offer and its supporting documents were received.", "", P(u["sara"])));
    var hasSession = await Get(async sp => await sp.GetRequiredService<ApplicationDbContext>().Set<EvaluationSession>().AnyAsync(x => x.TenderRound.TenderId == id));
    if (!hasSession)
    {
        await Flow(w => w.Proceed(id, P(u["sara"])));
        var ai = await Get(async sp => await sp.GetRequiredService<ApplicationDbContext>().Set<AiAnalysis>().Where(x => x.TenderRound.TenderId == id && x.Status == "Completed").OrderByDescending(x => x.Id).FirstOrDefaultAsync());
        if (ai == null)
        {
            Log("   running the AI analysis of the offers (local Ollama)...");
            await Run(sp => sp.GetRequiredService<OllamaBidAnalysisService>().Analyze(id, P(u["sara"]), CancellationToken.None));
            ai = await Get(async sp => await sp.GetRequiredService<ApplicationDbContext>().Set<AiAnalysis>().Where(x => x.TenderRound.TenderId == id).OrderByDescending(x => x.Id).FirstAsync());
            if (ai.Status != "Completed") throw new Exception("AI analysis status: " + ai.Status);
        }
        if (ai.ReviewedAt == null) await Flow(w => w.ReviewAi(id, ai.Id, P(u["sara"])));
        Log($"   AI analysis #{ai.Id} completed and reviewed by Sara");
        var starts = DateTimeOffset.UtcNow.AddSeconds(20);
        await Run(sp => sp.GetRequiredService<EvaluationSchedulingService>().Schedule(id, new SessionInput { AiAnalysisId = ai.Id, StartsAt = starts,
            EndsAt = new DateTimeOffset(2026, 12, 1, 22, 0, 0, TimeSpan.FromHours(3)), CreateJitsiMeeting = true }, P(u["sara"]), CancellationToken.None));
        Log("   evaluation meeting scheduled and the committee invited");
        var wait = starts - DateTimeOffset.UtcNow + TimeSpan.FromSeconds(2);
        if (wait > TimeSpan.Zero) await Task.Delay(wait);
    }

    // ---- 5. Committee scoring (signed in the meeting) -----------------------------------------------------------
    var bidsBySupplier = await Get(async sp => await sp.GetRequiredService<ApplicationDbContext>().Bids.Where(b => b.TenderInvitation.TenderRound.TenderId == id && b.Status == BidStatus.Submitted)
        .Select(b => new { b.Id, Email = sp.GetRequiredService<ApplicationDbContext>().Users.Where(x => x.SupplierId == b.TenderInvitation.SupplierId).Select(x => x.Email).First() }).ToDictionaryAsync(x => x.Email!.Split('@')[0], x => x.Id));
    var scorers = stop == 6 ? new[] { "majed", "tariq", "amal" } : new[] { "majed", "tariq", "amal", "saja" };
    var submittedMembers = await Get(async sp => await sp.GetRequiredService<ApplicationDbContext>().Set<EvaluationParticipation>().Where(x => x.EvaluationSession.TenderRound.TenderId == id && x.SubmittedAt != null).Select(x => x.CommitteeMember.UserId).ToListAsync());
    var scores = t.GetProperty("scores");
    foreach (var m in scorers.Where(m => !submittedMembers.Contains(u[m])))
    {
        var list = new List<ScoreInput>();
        foreach (var (supplier, bidId) in bidsBySupplier)
        {
            var row = scores.GetProperty(m).GetProperty(supplier).EnumerateArray().Select(x => (decimal?)x.GetDecimal()).ToList();
            list.Add(new ScoreInput { BidId = bidId, Financial = row[0], TechnicalScores = row.Skip(1).ToList(),
                Justification = supplier switch
                {
                    "mosa" => "Lowest total price and fully compliant; warranty terms are standard and the delivery time is the longest of the three offers.",
                    "omar" => "Fastest delivery and strong after-sales support, but the highest price of the three compliant offers.",
                    _ => "Fully compliant with the specifications, strong warranty and local support, at a competitive price."
                } });
        }
        await Flow(w => w.SubmitEvaluation(id, list, P(u[m]), Signature(m)));
        Log($"   {m} submitted and signed the evaluation");
    }
    if (stop == 6) continue;

    // ---- 6. Report signatures, Manager executive summary and Top Committee votes -------------------------------
    async Task<(int ReportId, string Hash, SessionState State, TenderStatus Status)> Report() => await Get(async sp =>
    {
        var db = sp.GetRequiredService<ApplicationDbContext>();
        var s = await db.Set<EvaluationSession>().AsNoTracking().Include(x => x.Report).SingleAsync(x => x.TenderRound.TenderId == id);
        return (s.ReportId!.Value, s.Report!.PdfSha256!, s.State, await db.Tenders.Where(x => x.Id == id).Select(x => x.Status).SingleAsync());
    });
    var r = await Report();
    Log($"   report #{r.ReportId} generated; session {r.State}, tender {r.Status}");
    if (r.State == SessionState.ReportDraft)
    {
        foreach (var m in new[] { "majed", "tariq", "amal", "saja" })
        {
            await Run(sp => sp.GetRequiredService<TenderWorkflowService>().ReportPdf(r.ReportId, P(u[m])));
            await Flow(w => w.FinalizeReport(id, "I reviewed the report and confirm the committee's evaluation.", P(u[m]), r.Hash, true));
        }
        r = await Report();
        Log($"   report signed by the four committee members; session {r.State}, tender {r.Status}");
    }
    if (r.Status == TenderStatus.OfficerReview) { await Flow(w => w.Forward(id, P(u["sara"]))); r = await Report(); Log("   report forwarded to Majed"); }
    if (r.Status == TenderStatus.ManagerReview)
    {
        await Run(sp => sp.GetRequiredService<TenderWorkflowService>().ReportPdf(r.ReportId, P(u["majed"])));
        await Flow(w => w.SubmitExecutiveSummary(id, new ExecutiveSummaryInput
        {
            BusinessNeed = "Eight meeting rooms have no video conferencing equipment, so meetings with the regional offices depend on personal laptops.",
            KeyFindings = "Three compliant offers were received and evaluated by the four committee members using the approved 40% financial and 60% technical weights.",
            RecommendationReasons = "The recommended offer achieved the highest final weighted score, with the strongest technical compliance, warranty and local support at a competitive price.",
            Risks = "Delivery depends on display stock; the supplier confirmed availability within the offered delivery period.",
            ManagerAssessment = "I reviewed the signed evaluation report and support the committee's recommendation for approval."
        }, P(u["majed"]), r.Hash, true, Signature("majed")));
        r = await Report();
        Log($"   executive summary signed by Majed; tender {r.Status}");
    }
    foreach (var m in new[] { "anfal", "mohammad", "batool" })
    {
        await Run(sp => sp.GetRequiredService<TenderWorkflowService>().ReportPdf(r.ReportId, P(u[m])));
        await Flow(w => w.TopVote(id, VoteDecision.Approve, "", P(u[m]), r.Hash, true, Signature(m)));
        Log($"   {m} approved");
    }
}

// ---- Summary -------------------------------------------------------------------------------------------------------
await Run(async sp =>
{
    var db = sp.GetRequiredService<ApplicationDbContext>();
    foreach (var pr in await db.PurchaseRequests.AsNoTracking().Include(x => x.Items).Include(x => x.Tenders).OrderBy(x => x.Id).ToListAsync())
    {
        var t = pr.Tenders.SingleOrDefault();
        Console.WriteLine($"PR-{pr.Id} {pr.Items.First().Name}: {(t == null ? pr.Status.ToString() : $"T-{t.Id:0000} {t.Status}")}");
    }
});
