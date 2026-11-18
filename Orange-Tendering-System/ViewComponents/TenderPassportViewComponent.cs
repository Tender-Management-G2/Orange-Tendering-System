using Microsoft.AspNetCore.Mvc;
using Orange_Tendering_System.Services;
using Orange_Tendering_System.ViewModels;

namespace Orange_Tendering_System.ViewComponents;

/// <summary>
/// The Tender Passport package (Views/Shared/Components/TenderPassport/Default.cshtml). Pass an already authorized
/// <see cref="WorkflowDetails"/>; suppliers get nothing. The trail URL points at the area's WorkflowControllerBase.Passport action.
/// </summary>
public sealed class TenderPassportViewComponent(TenderPassportService passports) : ViewComponent
{
    public async Task<IViewComponentResult> InvokeAsync(WorkflowDetails details, string? trailUrl = null)
    {
        var model = await passports.ForTenderAsync(details, trailUrl);
        return model == null ? Content(string.Empty) : View(model);
    }
}
