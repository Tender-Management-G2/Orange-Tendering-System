using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Core.Infrastructure;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Orange_Tendering_System.Services;

// Keep MVC's real antiforgery validation. Give its rejected result a body so the
// application's HTML status-page middleware cannot re-execute POST as /status/400
// (Home.HttpStatus is GET-only, which otherwise replaces the rejection with 405).
// Always-run is essential: authorization filters short-circuit normal result filters.
[AttributeUsage(AttributeTargets.Class)]
public sealed class AgentAntiforgeryFailureAttribute : Attribute, IAlwaysRunResultFilter
{
    public void OnResultExecuting(ResultExecutingContext context)
    {
        if (context.Result is IAntiforgeryValidationFailedResult)
            context.Result = new JsonResult(new { error = "Missing or invalid antiforgery token. Refresh the page and retry." }) { StatusCode = StatusCodes.Status400BadRequest };
    }
    public void OnResultExecuted(ResultExecutedContext context) { }
}
