using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
namespace Orange_Tendering_System.Areas.TopCommittee.Controllers;
// A response body keeps the shared status-page middleware from re-executing a failed POST as GET-only /status.
[AttributeUsage(AttributeTargets.Class)]
public sealed class PrivateFormFeedbackAttribute : Attribute,IAlwaysRunResultFilter
{
    public void OnResultExecuting(ResultExecutingContext context)
    {
        if(context.Result is AntiforgeryValidationFailedResult)
            context.Result=new ContentResult {StatusCode=400,ContentType="text/plain; charset=utf-8",Content="Your form session expired or could not be verified. Reload the page and try again."};
    }
    public void OnResultExecuted(ResultExecutedContext context) { }
}
