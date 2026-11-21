using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orange_Tendering_System.Controllers;

// Runs the actual ExecutiveAgentController authorization/antiforgery filters and the
// application's status-page middleware in-memory. No SQL/identity/workflow replacements
// are used to claim business execution; controller actions must never be reached here.
public static class ExecutiveAgentCsrfChecks
{
    public static async Task Run(bool expectFixed = false)
    {
        using var server = new TestServer(new WebHostBuilder().ConfigureServices(s => {
            s.AddLogging(x=>x.SetMinimumLevel(LogLevel.Error));
            s.AddDataProtection().UseEphemeralDataProtectionProvider();
            s.AddAuthentication("csrf-probe").AddScheme<AuthenticationSchemeOptions, CsrfProbeAuthentication>("csrf-probe", _=>{});
            s.AddAuthorization();
            s.AddControllersWithViews(o=>o.Filters.Add<CsrfPipelineReachedFilter>()).AddApplicationPart(typeof(ExecutiveAgentController).Assembly).AddApplicationPart(typeof(AgentCsrfProbeController).Assembly);
            s.AddSingleton<IControllerActivator,CsrfProbeControllerActivator>();
        }).Configure(app => {
            app.UseStatusCodePagesWithReExecute("/status/{0}");
            app.UseRouting();app.UseAuthentication();app.UseAuthorization();
            app.UseEndpoints(endpoints=>{endpoints.MapControllers();endpoints.MapControllerRoute("default","{controller=Home}/{action=Index}/{id?}");});
        }));
        using var client=server.CreateClient();client.BaseAddress=new Uri("https://localhost");
        var count=0;
        foreach(var endpoint in new[]{"Run","Confirm","Challenge","Transcribe","Navigate"})
        {
            var missing=await client.PostAsJsonAsync("/ExecutiveAgent/"+endpoint,new{requestId=Guid.NewGuid(),command="This must never execute",actionId=Guid.NewGuid(),confirmed=true});
            var body=await missing.Content.ReadAsStringAsync();
            Console.WriteLine($"CSRF_ACTUAL endpoint={endpoint} token=missing status={(int)missing.StatusCode} contentType={missing.Content.Headers.ContentType} body={body[..Math.Min(body.Length,700)]}");
            if(expectFixed && (missing.StatusCode!=HttpStatusCode.BadRequest || !body.Contains("antiforgery",StringComparison.OrdinalIgnoreCase)))throw new Exception("FAIL missing CSRF on "+endpoint);
            if(expectFixed)count++;
            var tokenResponse=await client.GetAsync("/csrf-probe/token");
            var token=(await tokenResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString();
            if(tokenResponse.Headers.TryGetValues("Set-Cookie",out var cookies))client.DefaultRequestHeaders.Remove("Cookie");
            if(cookies!=null)client.DefaultRequestHeaders.TryAddWithoutValidation("Cookie",string.Join("; ",cookies.Select(x=>x.Split(';')[0])));
            var validRequest=new HttpRequestMessage(HttpMethod.Post,"/ExecutiveAgent/"+endpoint){Content=JsonContent.Create(new{requestId=Guid.NewGuid(),command="",actionId=Guid.NewGuid(),confirmed=false})};
            validRequest.Headers.Add("RequestVerificationToken",token);
            var valid=await client.SendAsync(validRequest);var validBody=await valid.Content.ReadAsStringAsync();
            Console.WriteLine($"CSRF_ACTUAL endpoint={endpoint} token=valid status={(int)valid.StatusCode} body={validBody}");
            if(expectFixed && (valid.StatusCode!=HttpStatusCode.OK || !validBody.Contains("pipelineReached")))throw new Exception("FAIL valid CSRF on "+endpoint);
            if(expectFixed)count++;
            var request=new HttpRequestMessage(HttpMethod.Post,"/ExecutiveAgent/"+endpoint){Content=JsonContent.Create(new{requestId=Guid.NewGuid(),command="This must never execute",actionId=Guid.NewGuid(),confirmed=true})};
            request.Headers.Add("RequestVerificationToken","invalid-token");
            var invalid=await client.SendAsync(request);body=await invalid.Content.ReadAsStringAsync();
            Console.WriteLine($"CSRF_ACTUAL endpoint={endpoint} token=invalid status={(int)invalid.StatusCode} contentType={invalid.Content.Headers.ContentType} body={body[..Math.Min(body.Length,700)]}");
            if(expectFixed && (invalid.StatusCode!=HttpStatusCode.BadRequest || !body.Contains("antiforgery",StringComparison.OrdinalIgnoreCase)))throw new Exception("FAIL invalid CSRF on "+endpoint);
            if(expectFixed)count++;
        }
        if(expectFixed)Console.WriteLine($"RESULT CSRF_AUTH_FILTER_PASS={count} FAIL=0. Real agent controller filters; no business action or SQL execution occurred.");
    }
}

public sealed class CsrfProbeAuthentication(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync() => Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier,"isolated-csrf-probe")],Scheme.Name)),Scheme.Name)));
}

// Intentionally unused by business tests; a GET-only status target reproduces Home.HttpStatus's route contract.
public sealed class AgentCsrfProbeController : Controller
{
    [HttpGet("/csrf-probe/token")]
    public IActionResult Token([FromServices] IAntiforgery antiforgery) => Json(new{token=antiforgery.GetAndStoreTokens(HttpContext).RequestToken});
}

// Only this in-memory diagnostic host registers these. Valid requests stop AFTER
// the real controller's authorization/CSRF filters and before any business action.
public sealed class CsrfPipelineReachedFilter : IActionFilter
{
    public void OnActionExecuting(ActionExecutingContext context)
    { if(context.Controller is ExecutiveAgentController)context.Result=new JsonResult(new{pipelineReached=true}); }
    public void OnActionExecuted(ActionExecutedContext context) { }
}
public sealed class CsrfProbeControllerActivator : IControllerActivator
{
    public object Create(ControllerContext context) => context.ActionDescriptor.ControllerTypeInfo.AsType()==typeof(ExecutiveAgentController)
        ? new ExecutiveAgentController(null!,null!,null!,null!) : ActivatorUtilities.CreateInstance(context.HttpContext.RequestServices,context.ActionDescriptor.ControllerTypeInfo.AsType());
    public void Release(ControllerContext context,object controller) { }
}
