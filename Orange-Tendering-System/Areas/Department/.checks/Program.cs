using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.Razor;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orange_Tendering_System.Areas.Department.Controllers;
using Orange_Tendering_System.Areas.Department.Services;
using Orange_Tendering_System.Areas.Department.ViewModels;
using Orange_Tendering_System.Data;
using Orange_Tendering_System.Models;
using Orange_Tendering_System.Models.Entities;

try {
var checks = 0;
void Check(bool condition, string message) { if (!condition) throw new Exception(message); checks++; Console.WriteLine("PASS " + message); }
var request = new PurchaseRequest { Id = 9, CreatedByUserId = "owner", Status = PurchaseRequestStatus.Draft };
Check(RequestDisplay.Editable(request, "owner"), "Creator can edit draft");
Check(!RequestDisplay.Editable(request, "other"), "Different employee cannot edit creator's request");
foreach (var status in Enum.GetValues<PurchaseRequestStatus>())
{
    request.Status = status;
    Check(RequestDisplay.Editable(request, "owner") == (status is PurchaseRequestStatus.Draft or PurchaseRequestStatus.Returned), "Edit gate: " + status);
}
request.Status = PurchaseRequestStatus.Accepted;
Check(RequestDisplay.Status(request) == "Accepted", "Accepted is displayed without renaming");
request.Status = PurchaseRequestStatus.Approved;
Check(RequestDisplay.Status(request) == "Accepted", "Existing Approved records use the Accepted UI label");
request.Status = PurchaseRequestStatus.Draft;
request.Tenders.Add(new Tender { Id = 1 });
Check(!RequestDisplay.Editable(request, "owner") && RequestDisplay.Status(request) == "Converted to Tender", "Linked tender blocks edits and shows conversion");
request.Tenders.Clear();
request.Items.Add(new PurchaseRequestItem { Id = 1, Name = "Laptop", Quantity = 10, Unit = "pcs" });
request.Reviews.Add(new PurchaseRequestReview { Id = 1, SubmissionNumber = 1 });
var version = DepartmentRequestService.Version(request);
Check(version == DepartmentRequestService.Version(request), "Version hash stable");
request.Items.First().Quantity = 15;
Check(version != DepartmentRequestService.Version(request), "Item changes invalidate stale form");
version = DepartmentRequestService.Version(request);
request.Reviews.First().Comment = "Revise specifications";
Check(version != DepartmentRequestService.Version(request), "Officer decision changes invalidate stale form");
var original = new RequestSnapshot("Request", "Reason", RequestPriority.High, DateTime.UtcNow.Date, [new() { Name = "Laptop", Quantity = 10, Unit = "pcs" }]);
var frozen = JsonSerializer.Serialize(original);
original.Items[0].Quantity = 15;
Check(JsonSerializer.Deserialize<RequestSnapshot>(frozen)!.Items[0].Quantity == 10, "Submitted snapshot retains original quantity");
var errors = new List<ValidationResult>();
var invalidItem = new RequestItemForm { Name = "Laptop", Quantity = 0, Unit = "pcs" };
Check(!Validator.TryValidateObject(invalidItem, new ValidationContext(invalidItem), errors, true), "Zero quantity rejected");
string[] officialStatuses = ["Draft", "Submitted", "Under Review", "Accepted", "Rejected", "Returned", "Converted to Tender"];
Check(RequestDisplay.Statuses.SequenceEqual(officialStatuses), "Seven status labels use the agreed Accepted terminology");
foreach (var type in new[] { typeof(DashboardController), typeof(PurchaseRequestsController) })
    Check(type.GetCustomAttributes(typeof(AuthorizeAttribute), true).Cast<AuthorizeAttribute>().Single().Roles == SystemRoles.RequestingDepartment, "Role restriction: " + type.Name);
foreach (var method in typeof(PurchaseRequestsController).GetMethods().Where(m => m.GetCustomAttributes(typeof(HttpPostAttribute), true).Length > 0))
    Check(method.GetCustomAttributes(typeof(ValidateAntiForgeryTokenAttribute), true).Length == 1, "CSRF protection: " + method.Name);
var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlServer("Server=localhost;Database=Checks;Trusted_Connection=True;TrustServerCertificate=True").Options;
using var db = new ApplicationDbContext(options);
var manager = new TestUserManager();
var service = new DepartmentRequestService(db, manager);
var sql = service.Requests(42).ToQueryString();
Check(sql.Contains("DepartmentId") && sql.Contains("42"), "Database query includes department scope");
var env = new TestEnvironment(Path.GetFullPath(args[0]));
var controller = new PurchaseRequestsController(db, manager, env, Microsoft.Extensions.Logging.Abstractions.NullLogger<PurchaseRequestsController>.Instance)
{ ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() } };
Check(await controller.Create() is ForbidResult, "Unprovisioned user denied create");
Check(await controller.Details(9) is ForbidResult, "Unprovisioned user denied details");
Check(await controller.Download(9) is ForbidResult, "Unprovisioned user denied download");
var dashboard = new DashboardController(db, manager) { ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() } };
Check(await dashboard.Index() is ForbidResult, "Unprovisioned user denied dashboard");

var services = new ServiceCollection();
services.AddLogging();
services.AddSingleton<IWebHostEnvironment>(env);
services.AddSingleton<Microsoft.Extensions.Hosting.IHostEnvironment>(env);
services.AddDataProtection().UseEphemeralDataProtectionProvider();
services.AddControllersWithViews().AddApplicationPart(typeof(PurchaseRequestsController).Assembly);
services.AddSingleton<System.Diagnostics.DiagnosticListener>(new System.Diagnostics.DiagnosticListener("Checks"));
services.AddSingleton<System.Diagnostics.DiagnosticSource>(p => p.GetRequiredService<System.Diagnostics.DiagnosticListener>());
using var provider = services.BuildServiceProvider();
async Task<string> Render(string viewPath, object model)
{
    using var scope = provider.CreateScope();
    var http = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
    http.Request.Scheme = "https"; http.Request.Host = new HostString("localhost");
    var route = new RouteData(); route.Routers.Add(new TestRouter()); route.Values["area"] = "Department"; route.Values["controller"] = "PurchaseRequests"; route.Values["action"] = "Create";
    var action = new ActionContext(http, route, new ActionDescriptor());
    var engine = scope.ServiceProvider.GetRequiredService<IRazorViewEngine>();
    var result = engine.GetView(null, viewPath, true);
    if (!result.Success) throw new Exception(string.Join(",", result.SearchedLocations));
    var data = new ViewDataDictionary(new EmptyModelMetadataProvider(), new ModelStateDictionary()) { Model = model };
    var temp = new TempDataDictionary(http, scope.ServiceProvider.GetRequiredService<ITempDataProvider>());
    using var output = new StringWriter();
    await result.View.RenderAsync(new ViewContext(action, result.View, data, temp, output, new HtmlHelperOptions()));
    return output.ToString();
}
var formHtml = await Render("/Areas/Department/Views/PurchaseRequests/Create.cshtml", new RequestForm());
Check(formHtml.Contains("__RequestVerificationToken"), "Create form renders antiforgery token");
Check(formHtml.Contains("Items[0].Name") && formHtml.Contains("Items[0].Quantity"), "Indexed item fields render");
Check(formHtml.Contains("Save draft") && formHtml.Contains("Submit to Sourcing Officer"), "Draft and submit actions render");
var submitIntents = System.Text.RegularExpressions.Regex.Matches(formHtml, "name=\"intent\" value=\"([^\"]+)\"")
    .Select(m => m.Groups[1].Value).ToArray();
Check(submitIntents.SequenceEqual(new[] { "save", "submit" }), "Form offers draft/submit only, without intermediate approval");
var listHtml = await Render("/Areas/Department/Views/PurchaseRequests/Index.cshtml", new RequestList());
Check(listHtml.Contains("Converted to Tender") && listHtml.Contains("Under Review"), "List renders official status filters");
var statusSelect = System.Text.RegularExpressions.Regex.Match(listHtml, "<select name=\"status\"[^>]*>(.*?)</select>", System.Text.RegularExpressions.RegexOptions.Singleline);
var filterStatuses = System.Text.RegularExpressions.Regex.Matches(statusSelect.Groups[1].Value, "<option value=\"([^\"]+)\"")
    .Select(m => System.Net.WebUtility.HtmlDecode(m.Groups[1].Value)).ToArray();
Check(filterStatuses.SequenceEqual(officialStatuses), "Rendered status filter contains exactly the seven official statuses");
request.Department = new Department { Name = "IT" };
request.Reviews.First().SubmittedSnapshotJson = frozen;
var detailsHtml = await Render("/Areas/Department/Views/PurchaseRequests/Details.cshtml", new RequestDetails { Request = request, CanEdit = true });
Check(detailsHtml.Contains("Laptop") && detailsHtml.Contains("Revise specifications"), "Details renders items and Officer comments");
var dashHtml = await Render("/Areas/Department/Views/Dashboard/Index.cshtml", new DepartmentDashboard { DepartmentName = "IT", Counts = RequestDisplay.Statuses.ToDictionary(s => s, _ => 0) });
Check(dashHtml.Contains("Converted to Tender") && dashHtml.Contains("No department notifications"), "Dashboard renders seven statuses and empty notifications");
var editHtml = await Render("/Areas/Department/Views/PurchaseRequests/Edit.cshtml", new RequestForm { Id = 9, ReturnComment = "Please clarify the quantity." });
Check(editHtml.Contains("Please clarify the quantity.") && editHtml.Contains("View earlier submissions"), "Edit renders return comments and preserved-history link");
request.Reviews.First().SubmittedSnapshotJson = "invalid old JSON";
var legacyHtml = await Render("/Areas/Department/Views/PurchaseRequests/Details.cshtml", new RequestDetails { Request = request });
Check(legacyHtml.Contains("stored record has been preserved"), "Malformed old snapshot does not break details");
request.Title = "<script>alert(1)</script>";
var escapedHtml = await Render("/Areas/Department/Views/PurchaseRequests/Index.cshtml", new RequestList { Requests = [request], UserId = "other" });
Check(!escapedHtml.Contains("<script>alert(1)</script>") && escapedHtml.Contains("&lt;script&gt;"), "Request content is HTML escaped");
Check(!escapedHtml.Contains(" · Edit"), "Other employee is not offered edit action");
async Task<bool> FileValid(string name, byte[] bytes)
{
    controller.ModelState.Clear();
    var file = new FormFile(new MemoryStream(bytes), 0, bytes.Length, "Attachments", name);
    await (Task)typeof(PurchaseRequestsController).GetMethod("ValidateAttachments", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.Invoke(controller, [new List<IFormFile> { file }])!;
    return controller.ModelState.IsValid;
}
Check(!await FileValid("bad.html", "%PDF-1.7"u8.ToArray()), "Executable document extension rejected");
Check(!await FileValid("fake.pdf", "not a PDF file"u8.ToArray()), "Mismatched file signature rejected");
Check(!await FileValid("empty.pdf", []), "Empty attachment rejected");
Check(await FileValid("support.pdf", "%PDF-1.7"u8.ToArray()), "Allowed PDF signature accepted");
Console.WriteLine($"Completed {checks} checks.");

} catch (Exception exception) { Console.Error.WriteLine(exception); Environment.ExitCode = 1; }
sealed class TestUserManager : UserManager<ApplicationUser>
{
    public TestUserManager() : base(new Microsoft.AspNetCore.Identity.EntityFrameworkCore.UserStore<ApplicationUser>(new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlServer("Server=localhost;Database=Checks;Trusted_Connection=True").Options)), Microsoft.Extensions.Options.Options.Create(new IdentityOptions()), new PasswordHasher<ApplicationUser>(), [], [], new UpperInvariantLookupNormalizer(), new IdentityErrorDescriber(), new ServiceCollection().BuildServiceProvider(), Microsoft.Extensions.Logging.Abstractions.NullLogger<UserManager<ApplicationUser>>.Instance) { }
    public override Task<ApplicationUser?> GetUserAsync(ClaimsPrincipal principal) => Task.FromResult<ApplicationUser?>(null);
}
sealed class TestEnvironment(string root) : IWebHostEnvironment
{
    public string ApplicationName { get; set; } = typeof(PurchaseRequestsController).Assembly.GetName().Name!;
    public string EnvironmentName { get; set; } = "Development";
    public string ContentRootPath { get; set; } = root;
    public IFileProvider ContentRootFileProvider { get; set; } = new PhysicalFileProvider(root);
    public string WebRootPath { get; set; } = Path.Combine(root, "wwwroot");
    public IFileProvider WebRootFileProvider { get; set; } = new PhysicalFileProvider(Path.Combine(root, "wwwroot"));
}




sealed class TestRouter : IRouter
{
    public Task RouteAsync(RouteContext context) => Task.CompletedTask;
    public VirtualPathData? GetVirtualPath(VirtualPathContext context) => new(this, "/Department/" + context.Values["controller"] + "/" + context.Values["action"]);
}


