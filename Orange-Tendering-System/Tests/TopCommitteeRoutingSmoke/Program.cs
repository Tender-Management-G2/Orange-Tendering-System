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
