using System.Net.Http.Json;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Text.Unicode;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging.Abstractions;
using Orange_Tendering_System.Controllers;
using Orange_Tendering_System.Services;
using Orange_Tendering_System.ViewModels;

public static class ExecutiveModelDiagnosis
{
    public static async Task Run()
    {
        var root=Environment.GetEnvironmentVariable("AUTH_PROJECT_ROOT")!;
        var source=await File.ReadAllTextAsync(Path.Combine(root,"Services","ExecutiveAgentService.cs"));
        var literal=Regex.Match(source,"content = (\"(?:\\\\.|[^\"\\\\])*\")");
        if(!literal.Success)throw new Exception("Production system instruction was not found");
        var instruction=JsonSerializer.Deserialize<string>(literal.Groups[1].Value)!;
        // Reconstruct the Officer catalog from the actual navigation and registered routing,
        // without touching SQL or invoking any controller/business action.
        var builder=WebApplication.CreateBuilder();builder.WebHost.UseTestServer();
        builder.Services.AddControllersWithViews().AddApplicationPart(typeof(ExecutiveAgentController).Assembly);
        builder.Services.AddDataProtection().UseEphemeralDataProtectionProvider();
        await using var app=builder.Build();app.MapControllers();app.MapControllerRoute("default","{area:exists}/{controller=Dashboard}/{action=Index}/{id?}");app.MapControllerRoute("shared","{controller=Home}/{action=Index}/{id?}");await app.StartAsync();
        var links=app.Services.GetRequiredService<LinkGenerator>();
        var workspace=WorkspaceNavigation.ForArea("SourcingOfficer")!;
        var items=workspace.Items.Select(x=>(Area:x.Area??workspace.Area,Item:x)).Concat(WorkspaceNavigation.SharedItems.Select(x=>(Area:x.Area??"",Item:x)))
            .Concat([(Area:"",Item:new NavItem("Device passkey","Biometrics","Fingerprint","user","")),(Area:"",Item:new NavItem("How It Works","Home","HowItWorks","home",""))]);
        var pages=items.Select(x=>new ExecutivePage($"{x.Area}:{x.Item.Controller}:{x.Item.Action}",x.Item.Label,x.Area,links.GetPathByAction(x.Item.Action,x.Item.Controller,new{area=x.Area})??"",ExecutiveNavigationService.Aliases(x.Item.Label))).Where(x=>x.Url!="").ToArray();
        var web=new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var unicode=new JsonSerializerOptions(web){Encoder=JavaScriptEncoder.Create(UnicodeRanges.All)};
        var command="Find tender AGENT-EN-TEST, read its current status, then prepare sending its weight request to the evaluation committee. Do not execute or approve anything.";
        var diagnosticPrincipal=new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity([new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Role,workspace.Role)],"Diagnostic"));
        var catalogInstruction=ExecutiveSiteKnowledge.ContextPrompt(diagnosticPrincipal,pages,command);
        var folder=Path.Combine(root,"..",".build","executive-agent","model-diagnosis");Directory.CreateDirectory(folder);
        using var http=new HttpClient{Timeout=TimeSpan.FromSeconds(140)};
        foreach(var variant in new[]{"current-search-first"})
        {
            var messages=new object[]{new{role="system",content=instruction},new{role="system",content=catalogInstruction},new{role="user",content=command}};
            var request=new{model="qwen3.5:9b",stream=false,think=false,messages,tools=ExecutiveAgentClient.Tools,options=new{temperature=0,num_ctx=8192,num_predict=1024}};
            await File.WriteAllTextAsync(Path.Combine(folder,variant+"-request.json"),JsonSerializer.Serialize(request,web));
            var watch=System.Diagnostics.Stopwatch.StartNew();using var response=await http.PostAsJsonAsync("http://localhost:11434/api/chat",request);var raw=await response.Content.ReadAsStringAsync();watch.Stop();
            await File.WriteAllTextAsync(Path.Combine(folder,variant+"-response.json"),raw);
            Console.WriteLine($"LIVE_MODEL variant={variant} http={(int)response.StatusCode} elapsed={watch.Elapsed.TotalSeconds:F2}s body={raw[..Math.Min(raw.Length,1800)]}");
            if(variant=="current-search-first")
            {
                using var actualHttp=new HttpClient();
                var actualClient=new ExecutiveAgentClient(actualHttp,Options.Create(new ExecutiveAgentOptions{MaxTransientRetries=0,DiagnosticDirectory=Path.Combine(folder,"validated-client")}),NullLogger<ExecutiveAgentClient>.Instance);
                var validated=await actualClient.Next(messages,CancellationToken.None);
                var call=validated.GetProperty("tool_calls")[0].GetProperty("function");
                if(call.GetProperty("name").GetString()!="search_records"||call.GetProperty("arguments").GetProperty("query").GetString()!="AGENT-EN-TEST")throw new Exception("Actual Qwen selected an unexpected first tool.");
                Console.WriteLine("LIVE_MODEL_CLIENT_PASS=1 FAIL=0: actual Qwen response accepted by the production structured-call validator; zero backend tools dispatched.");
            }
        }
        Console.WriteLine("Diagnostic requests only. No returned tool was dispatched and no SQL/workflow action was executed.");
    }
}
