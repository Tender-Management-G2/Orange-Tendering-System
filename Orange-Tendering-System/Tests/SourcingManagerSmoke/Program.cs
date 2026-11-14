using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Orange_Tendering_System.Areas.SourcingManager.Controllers;
using Orange_Tendering_System.Data;
using Orange_Tendering_System.Models.Entities;

var connection = Environment.GetEnvironmentVariable("SOURCING_MANAGER_TEST_CONNECTION")
    ?? throw new InvalidOperationException("Set SOURCING_MANAGER_TEST_CONNECTION to an isolated SQL Server database.");

var builder = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(connection);
if (!builder.InitialCatalog.StartsWith("OrangeManagerCheck_", StringComparison.OrdinalIgnoreCase))
    throw new InvalidOperationException("Sourcing Manager smoke tests require an OrangeManagerCheck_ database.");

var options = new DbContextOptionsBuilder<ApplicationDbContext>()
    .UseSqlServer(connection)
    .Options;

await using var db = new ApplicationDbContext(options);
// This harness owns an empty, isolated database. EnsureCreated keeps the smoke
// test runnable even when the application's next schema migration is pending.
await db.Database.EnsureCreatedAsync();

var suffix = Guid.NewGuid().ToString("N")[..8];
var manager = new ApplicationUser { Id = "manager-" + suffix, UserName = "manager-" + suffix, Email = "manager-" + suffix + "@example.test", FullName = "Smoke Test Manager", IsActive = true };
var officer = new ApplicationUser { Id = "officer-" + suffix, UserName = "officer-" + suffix, Email = "officer-" + suffix + "@example.test", FullName = "Smoke Test Officer", IsActive = true };
var committeeUsers = Enumerable.Range(1, 4).Select(number => new ApplicationUser
{
    Id = $"top-{number}-{suffix}", UserName = $"top-{number}-{suffix}", Email = $"top-{number}-{suffix}@example.test",
    FullName = $"Top Committee Member {number}", IsActive = true
}).ToList();
db.Users.AddRange(manager, officer);
db.Users.AddRange(committeeUsers);

var department = new Department { Name = "Smoke Test Department " + suffix };
db.Departments.Add(department);
await db.SaveChangesAsync();

var request = new PurchaseRequest
{
    DepartmentId = department.Id, CreatedByUserId = officer.Id, Title = "Smoke Test Purchase " + suffix,
    Justification = "Exercises the manager report workflow.", Status = PurchaseRequestStatus.Accepted, CreatedAt = DateTime.UtcNow
};
db.PurchaseRequests.Add(request);
await db.SaveChangesAsync();

var tender = new Tender
{
    PurchaseRequestId = request.Id, CreatedByUserId = officer.Id, Title = "Smoke Test Tender " + suffix,
    Description = "Synthetic tender for automated verification.", Status = TenderStatus.ManagerReview, CreatedAt = DateTime.UtcNow
};
db.Tenders.Add(tender);
await db.SaveChangesAsync();

var round = new TenderRound
{
    TenderId = tender.Id, RoundNumber = 1, Status = TenderRoundStatus.SentToEvaluation,
    CreatedByUserId = officer.Id, CreatedAt = DateTime.UtcNow
};
db.TenderRounds.Add(round);
foreach (var user in committeeUsers)
    db.CommitteeMembers.Add(new CommitteeMember { TenderId = tender.Id, UserId = user.Id, CommitteeType = CommitteeType.Top, AssignedAt = DateTime.UtcNow });
await db.SaveChangesAsync();

var approvedReport = Report(1, "Recommended supplier: Orange Test Supplier");
var rejectedReport = Report(2, "Recommendation requiring rejection");
var returnedReport = Report(3, "Recommendation requiring correction");
db.TenderReports.AddRange(approvedReport, rejectedReport, returnedReport);
await db.SaveChangesAsync();

var controller = ManagerController(db, manager.Id);
var review = await controller.Review(approvedReport.Id);
Check(review is ViewResult { Model: TenderReport }, "manager can open the submitted unified evaluation report");

await controller.Approve(approvedReport.Id, "Approved after reviewing the unified evaluation.");
db.ChangeTracker.Clear();
var accepted = await db.TenderReports.SingleAsync(r => r.Id == approvedReport.Id);
Check(accepted.Status == ReportStatus.Accepted && accepted.ReviewedByUserId == manager.Id && accepted.ReviewComment!.StartsWith("Approved"),
    "approval stores the manager decision and comment");

await controller.Reject(rejectedReport.Id, "Commercial terms do not meet the required policy.");
db.ChangeTracker.Clear();
var rejected = await db.TenderReports.SingleAsync(r => r.Id == rejectedReport.Id);
Check(rejected.Status == ReportStatus.Rejected && rejected.ReviewComment!.Contains("policy"), "rejection stores its mandatory reason");

await controller.Return(returnedReport.Id, "Please correct the quantity comparison.");
db.ChangeTracker.Clear();
var returned = await db.TenderReports.SingleAsync(r => r.Id == returnedReport.Id);
Check(returned.Status == ReportStatus.Returned && returned.ReviewComment!.Contains("correct"), "return-for-review stores its mandatory comment");

Check(await db.AuditLogs.CountAsync(log => log.EntityName == "TenderReport" && log.UserId == manager.Id &&
    (log.Action == "Accepted" || log.Action == "Rejected" || log.Action == "Returned")) == 3,
    "each manager decision is audit logged");
Check(await db.Notifications.CountAsync(notification => notification.UserId == officer.Id && notification.Title == "Evaluation Report Decision") == 3,
    "the report creator receives each manager decision notification");

await controller.SaveSummaryDraft(approvedReport.Id, "Executive decision summary.", "Cost, supplier, and compliance comparison.");
db.ChangeTracker.Clear();
var summary = await db.TenderReports.SingleAsync(r => r.PreviousReportId == approvedReport.Id && r.ReportType == ReportType.ExecutiveSummary);
Check(summary.Status == ReportStatus.Draft && summary.Content.Contains("Executive decision summary"), "executive summary draft is created for forwarding");

await controller.SubmitToTopCommittee(approvedReport.Id, true, true, true);
db.ChangeTracker.Clear();
summary = await db.TenderReports.SingleAsync(r => r.Id == summary.Id);
Check(summary.Status == ReportStatus.Submitted && summary.SubmittedAt != null, "executive summary is submitted to the Top Committee");
Check(await db.CommitteeRounds.AnyAsync(r => r.TenderReportId == summary.Id && r.CommitteeType == CommitteeType.Top && r.Status == CommitteeRoundStatus.InProgress),
    "a Top Committee review round is created");
Check(await db.Notifications.CountAsync(notification => notification.Title == "New Executive Summary") == 4,
    "all four Top Committee members are notified");
Check(await db.AuditLogs.AnyAsync(log => log.EntityId == summary.Id.ToString() && log.Action == "Forward"), "forwarding is audit logged");

var duplicateForward = await controller.Forward(approvedReport.Id);
Check(duplicateForward is RedirectToActionResult { ActionName: "Forwarding" },
    "an evaluation already with the Top Committee cannot be opened for a second forwarding");

Console.WriteLine("Sourcing Manager smoke checks passed.");

TenderReport Report(int version, string content) => new()
{
    TenderRoundId = round.Id, ReportType = ReportType.FullEvaluation, VersionNumber = version,
    Content = content, RankingSnapshotJson = "[{\"rank\":1,\"supplier\":\"Orange Test Supplier\",\"eligible\":true}]",
    Status = ReportStatus.Submitted, CreatedByUserId = officer.Id, CreatedAt = DateTime.UtcNow, SubmittedAt = DateTime.UtcNow
};

ReportsController ManagerController(ApplicationDbContext context, string userId)
{
    var http = new DefaultHttpContext();
    http.User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, userId) }, "SmokeTest"));
    return new ReportsController(context)
    {
        ControllerContext = new ControllerContext { HttpContext = http },
        TempData = new TempDataDictionary(http, new NullTempDataProvider())
    };
}

static void Check(bool condition, string description)
{
    if (!condition) throw new InvalidOperationException("FAILED: " + description);
    Console.WriteLine("PASS " + description);
}

sealed class NullTempDataProvider : ITempDataProvider
{
    public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();
    public void SaveTempData(HttpContext context, IDictionary<string, object> values) { }
}
