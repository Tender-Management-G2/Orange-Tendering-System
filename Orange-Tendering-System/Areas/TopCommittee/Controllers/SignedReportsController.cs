using Orange_Tendering_System.Areas.TopCommittee.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Orange_Tendering_System.Controllers;
using Orange_Tendering_System.Models;
using Orange_Tendering_System.Models.Entities;
using Orange_Tendering_System.Services;

namespace Orange_Tendering_System.Areas.TopCommittee.Controllers;

// Manager-signed full evaluations use the same version, receipts and revision
// workflow as the Manager. Legacy executive summaries retain their own workflow.
[Area("TopCommittee"), Authorize(Roles = TopExecutiveRoles.Allowed)]
public sealed class SignedReportsController(IServiceProvider services) : ReportControllerBase(TopCommitteeWorkflow.Create(services))
{
    [HttpPost]
    public Task<IActionResult> Vote(int id, VoteDecision decision, string comment, string documentHash, bool confirmed, string? signatureImage = null, TopRejectionReason? rejectionReason = null) =>
        Execute(id, async () =>
        {
            var report = await Workflow.ReviewReport(id, User);
            TenderWorkflowService.Require(report.IsCurrent && report.CanVoteTop,
                "Only the current Manager-approved report awaiting your Top Committee decision can be signed.");
            await Workflow.TopVote(report.Tender.Id, decision, comment, User, documentHash, confirmed, ReadSignature(signatureImage), rejectionReason);
        });
}
