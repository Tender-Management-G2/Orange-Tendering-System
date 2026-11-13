using Microsoft.AspNetCore.Mvc.ActionConstraints;
using Orange_Tendering_System.Models;

namespace Orange_Tendering_System.Areas.TopCommittee.Controllers;

// Only Top Committee receives this alias. Other roles retain the existing shared Analytics route.
[AttributeUsage(AttributeTargets.Method)]
public sealed class TopCommitteeReportsRouteAttribute : Attribute, IActionConstraint
{
    public int Order => 0;
    public bool Accept(ActionConstraintContext context) => context.RouteContext.HttpContext.User.IsInRole(SystemRoles.TopCommittee);
}
