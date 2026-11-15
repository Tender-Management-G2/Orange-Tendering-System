using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Orange_Tendering_System.Models;
using Orange_Tendering_System.Services;
using Orange_Tendering_System.ViewModels;

namespace Orange_Tendering_System.Areas.Admin.Controllers;

[Area("Admin"), Authorize(Roles = SystemRoles.Admin)]
public sealed class CommitteesController(TenderWorkflowService workflow) : Controller
{
    [HttpGet]
    public IActionResult Index() => View();

    [HttpGet]
    public async Task<IActionResult> Members(string committee)
    {
        if (committee is not ("Evaluation" or "Top")) return NotFound();
        return View(await workflow.StandingCommittee(committee, User));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveStanding(StandingCommitteeInput input)
    {
        if (input.Committee is not ("Evaluation" or "Top")) return BadRequest();
        if (ModelState.IsValid)
        {
            try
            {
                await workflow.SaveStandingCommittee(input, User);
                TempData["SuccessMessage"] = "committee members saved. New tenders will use this configuration.";
                return RedirectToAction(nameof(Members), new { committee = input.Committee });
            }
            catch (WorkflowException error) { ModelState.AddModelError("", error.Message); }
        }
        return View("Members", await workflow.StandingCommittee(input.Committee, User));
    }

    [HttpGet]
    public async Task<IActionResult> Assignments(string? search) => View(await workflow.AdminCommittees(search, User));

    [HttpGet]
    public async Task<IActionResult> Manage(int id)
    {
        var model = await workflow.AdminCommitteeDetails(id, User);
        return model == null ? NotFound() : View(model);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Save(int id, AdminCommitteeInput input)
    {
        if (id != input.TenderId) return BadRequest();
        if (ModelState.IsValid)
        {
            try
            {
                await workflow.SaveAdminCommittee(input, User);
                TempData["SuccessMessage"] = "Committee members saved successfully.";
                return RedirectToAction(nameof(Manage), new { id });
            }
            catch (WorkflowException error) { ModelState.AddModelError("", error.Message); }
        }
        var model = await workflow.AdminCommitteeDetails(id, User);
        if (model == null) return NotFound();
        if (input.CommitteeType == Orange_Tendering_System.Models.Entities.CommitteeType.Evaluation)
        {
            model.SourcingManagerId = input.MemberOneId;
            model.TechnicalOneId = input.MemberTwoId;
            model.TechnicalTwoId = input.MemberThreeId;
            model.FinancialId = input.MemberFourId;
        }
        else if (input.CommitteeType == Orange_Tendering_System.Models.Entities.CommitteeType.Top)
        {
            model.CeoId = input.MemberOneId;
            model.TopMemberOneId = input.MemberTwoId;
            model.TopMemberTwoId = input.MemberThreeId;
            model.TopMemberThreeId = input.MemberFourId;
        }
        return View("Manage", model);
    }
}
