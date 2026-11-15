using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Orange_Tendering_System.Areas.TopCommittee.Controllers;
using Orange_Tendering_System.Models;

// Discover real MVC actions without connecting to, or modifying, a database.
var services = new ServiceCollection();
services.AddLogging();
services.AddControllersWithViews().AddApplicationPart(typeof(SignedReportsController).Assembly);
using var provider = services.BuildServiceProvider();
var actions = provider.GetRequiredService<IActionDescriptorCollectionProvider>().ActionDescriptors.Items
    .OfType<ControllerActionDescriptor>()
    .Where(a => a.ControllerName == "SignedReports" && a.RouteValues["area"] == "TopCommittee").ToList();
foreach (var name in new[] { "Review", "Pdf", "Verify", "Receipt", "Vote", "Return", "ActivateRevision", "Resubmit" })
{
    var action = actions.Single(a => a.ActionName == name);
    if (!action.ControllerTypeInfo.GetCustomAttributes(typeof(AuthorizeAttribute), true)
        .Cast<AuthorizeAttribute>().Any(a => a.Roles == Orange_Tendering_System.Areas.TopCommittee.Services.TopExecutiveRoles.Allowed))
        throw new Exception($"Missing Top Committee authorization: {name}");
    if (name is "Vote" or "Return" or "ActivateRevision" or "Resubmit")
    {
        if (!action.MethodInfo.IsDefined(typeof(HttpPostAttribute), true) ||
            !action.ControllerTypeInfo.IsDefined(typeof(AutoValidateAntiforgeryTokenAttribute), true))
            throw new Exception($"Missing POST/antiforgery protection: {name}");
    }
    Console.WriteLine($"PASS TopCommittee/SignedReports/{name}");
}
if (actions.Single(a => a.ActionName == "Vote").Parameters.Single(p => p.Name == "id").ParameterType != typeof(int))
    throw new Exception("Vote must receive the report ID used by the report review form.");
Console.WriteLine("PASS report-ID vote binding; MVC actions are unique and protected.");

// Final PDF visibility must agree with download eligibility for saved signature records.
var report = new Orange_Tendering_System.Models.Entities.TenderReport
{
    Id = 101, ReportType = Orange_Tendering_System.Models.Entities.ReportType.ExecutiveSummary,
    Status = Orange_Tendering_System.Models.Entities.ReportStatus.Accepted
};
var round = new Orange_Tendering_System.Models.Entities.CommitteeRound
{
    TenderReportId = report.Id, TenderReport = report,
    Status = Orange_Tendering_System.Models.Entities.CommitteeRoundStatus.Approved
};
var details = new Orange_Tendering_System.Areas.TopCommittee.ViewModels.RoundDetails
{
    Round = round,
    OriginalSummaryPdf = new() { ContentHash = "original-hash" },
    FinalSignedSummaryPdf = new()
};
for (var i = 1; i <= 4; i++)
    details.Members.Add(new() { Id = i, UserId = $"top-{i}", CommitteeType = Orange_Tendering_System.Models.Entities.CommitteeType.Top });
for (var count = 0; count <= 4; count++)
{
    if (details.FinalSignedPdfReady != (count == 4)) throw new Exception($"Incorrect final PDF eligibility at {count} signatures.");
    if (count < 4) round.Votes.Add(new()
    {
        CommitteeMemberId = count + 1, Decision = Orange_Tendering_System.Models.Entities.VoteDecision.Approve,
        SignatureReportId = report.Id, SignatureDocumentHash = "original-hash", ElectronicSignatureSvg = "<svg />"
    });
}
round.Votes.First().SignatureDocumentHash = "different-report";
if (details.FinalSignedPdfReady) throw new Exception("Mismatched report signature allowed final PDF.");
round.Votes.First().SignatureDocumentHash = "original-hash";
details.FinalSignedSummaryPdf = null;
if (details.FinalSignedPdfReady) throw new Exception("Missing final artifact allowed final PDF.");
Console.WriteLine("PASS final PDF requires four matching saved signatures and a final artifact.");

// Executive labels follow persisted assignments, never the member's display order.
var executive = new Orange_Tendering_System.Models.Entities.CommitteeMember
{
    Id = 900, CommitteeType = Orange_Tendering_System.Models.Entities.CommitteeType.Top,
    ExecutivePosition = Orange_Tendering_System.Models.Entities.TopExecutivePosition.CFO,
    User = new() { FullName = "Actual Executive Name" }
};
if (executive.ExecutiveDisplayName != "CFO — Chief Financial Officer · Actual Executive Name")
    throw new Exception("Executive title must come from the explicit assignment and retain the real name.");
executive.ExecutivePosition = Orange_Tendering_System.Models.Entities.TopExecutivePosition.CEO;
if (!executive.ExecutiveDisplayName.StartsWith("CEO — Chief Executive Officer"))
    throw new Exception("Executive title did not follow the assignment.");
executive.ExecutivePosition = null;
if (!executive.ExecutiveDisplayName.Contains("not assigned"))
    throw new Exception("Legacy member was assigned an invented title.");
Console.WriteLine("PASS stored executive positions preserve names and do not infer legacy titles.");

foreach (var executiveRole in new[] { "CEO", "CFO", "CTO", "COO" })
{
    var principal = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity(
        new[] { new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Role, executiveRole) }, "test"));
    if (!Orange_Tendering_System.Areas.TopCommittee.Services.TopExecutiveRoles.IsMember(principal) ||
        Orange_Tendering_System.Areas.TopCommittee.Services.TopExecutiveRoles.Title(new[] { executiveRole }) is null)
        throw new Exception($"Executive role not recognized: {executiveRole}");
}
if (Orange_Tendering_System.Areas.TopCommittee.Services.TopExecutiveRoles.IsMember(
    new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity(
        new[] { new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Role, "Supplier") }, "test"))))
    throw new Exception("Unrelated role received Top Committee access.");
Console.WriteLine("PASS executive role aliases and unrelated-role isolation.");
