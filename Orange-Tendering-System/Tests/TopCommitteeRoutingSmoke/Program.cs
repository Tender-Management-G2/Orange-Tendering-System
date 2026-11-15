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
        .Cast<AuthorizeAttribute>().Any(a => a.Roles == SystemRoles.TopCommittee))
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
