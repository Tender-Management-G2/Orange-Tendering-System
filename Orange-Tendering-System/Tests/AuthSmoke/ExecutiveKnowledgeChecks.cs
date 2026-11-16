using System.Security.Claims;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Orange_Tendering_System.Controllers;
using Orange_Tendering_System.Services;
using Orange_Tendering_System.ViewModels;

public static class ExecutiveKnowledgeChecks
{
    public static async Task Run(bool live)
    {
        var count = 0;
        void Check(bool ok, string label) { if (!ok) throw new Exception("FAIL " + label); Console.WriteLine("PASS KNOWLEDGE " + ++count + " " + label); }
        var builder = WebApplication.CreateBuilder(); builder.WebHost.UseTestServer();
        builder.Services.AddControllersWithViews().AddApplicationPart(typeof(ExecutiveAgentController).Assembly);
        builder.Services.AddDataProtection().UseEphemeralDataProtectionProvider();
        await using var app = builder.Build();
        app.MapControllers(); app.MapControllerRoute("areas", "{area:exists}/{controller=Dashboard}/{action=Index}/{id?}");
        app.MapControllerRoute("shared", "{controller=Home}/{action=Index}/{id?}"); await app.StartAsync();
        var links = app.Services.GetRequiredService<LinkGenerator>();
        var descriptors = app.Services.GetRequiredService<IActionDescriptorCollectionProvider>();
        using var http = new HttpClient();
        var model = new ExecutiveAgentClient(http, Options.Create(new ExecutiveAgentOptions { MaxTransientRetries = 0 }), NullLogger<ExecutiveAgentClient>.Instance);
        string instruction = "";
        if (live)
        {
            var root = Environment.GetEnvironmentVariable("AUTH_PROJECT_ROOT") ?? throw new Exception("AUTH_PROJECT_ROOT required");
            var source = await File.ReadAllTextAsync(Path.Combine(root, "Services", "ExecutiveAgentService.cs"));
            var literal = Regex.Match(source, "content = (\"(?:\\\\.|[^\"\\\\])*\")");
            instruction = JsonSerializer.Deserialize<string>(literal.Groups[1].Value) ?? throw new Exception("Production instruction not found");
        }
        foreach (var workspace in WorkspaceNavigation.Workspaces)
        {
            var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Role, workspace.Role)], "TestRole"));
            var pages = workspace.Items.Select(i => Page(i.Area ?? workspace.Area, i))
                .Concat(WorkspaceNavigation.SharedItems.Select(i => Page(i.Area ?? "", i))).DistinctBy(p => p.Id).ToArray();
            foreach (var page in pages)
            {
                var parts = page.Id.Split(':');
                Check(descriptors.ActionDescriptors.Items.OfType<ControllerActionDescriptor>().Any(a => a.ControllerName == parts[1] && a.ActionName == parts[2] &&
                    (a.RouteValues.TryGetValue("area", out var actionArea) ? actionArea ?? "" : "") == parts[0] &&
                    !(a.ActionConstraints?.OfType<Microsoft.AspNetCore.Mvc.ActionConstraints.HttpMethodActionConstraint>().Any(c => !c.HttpMethods.Contains("GET")) ?? false)),
                    page.Id + " is a registered GET destination");
                Check(!ExecutiveSiteKnowledge.PageGuide(page.Label).StartsWith("Use the existing"), page.Id + " has reviewed procedural coverage");
                foreach (var phrase in page.Aliases.Append(page.Label))
                {
                    var match = ExecutiveNavigationService.Match("افتحلي صفحة " + phrase, workspace.Area, pages);
                    // Shared and role-local Clarifications/Notifications intentionally share a label;
                    // the resolver prefers the current workspace's equivalent menu destination.
                    Check(match.Choices.Any(p => p.Url == page.Url || (page.Area == "" && p.Area == workspace.Area && p.Id[(p.Id.IndexOf(':') + 1)..] == page.Id[1..])), page.Id + " resolves alias: " + phrase);
                }
            }
            var guide = JsonSerializer.SerializeToElement(ExecutiveSiteKnowledge.Read(principal, pages, "workflow"));
            Check(guide.GetProperty("roleGuides").GetArrayLength() == 1 && guide.GetProperty("roleGuides")[0].GetProperty("role").GetString() == workspace.Role,
                workspace.Role + " knowledge is scoped to supplied current role");
            foreach (var command in new[] { "افتح التقارير وبعدين اعتمدها", "open reports then cancel tender", "احذف كل المستخدمين", "https://evil.invalid", "افتح عطاء 987654" })
                Check(ExecutiveNavigationService.Match(command, workspace.Area, pages).State == "Unrecognized", workspace.Role + " compound/mutation/record command is not reduced to generic navigation");
            if (!live) continue;
            foreach (var command in new[] { "بأقل من 80 كلمة، اشرح لي خطوات العمل المتاحة لدوري في الموقع. شرح فقط، لا تفتح ولا توافق ولا تنفذ أي شيء.", "In under 80 words, explain the workflow for my role on this website. Explanation only: do not navigate, prepare approvals or change any records." })
            {
                var context = ExecutiveSiteKnowledge.ContextPrompt(principal, pages, command);
                Check(context.Contains(ExecutiveSiteKnowledge.RoleGuide(workspace.Role)) && context.Contains("automatically loaded by the SERVER"), workspace.Role + " reviewed guide is loaded before model inference, not counted as a tool call");
                var messages = new List<object> { new { role = "system", content = instruction }, new { role = "system", content = context }, new { role = "user", content = command } };
                var answer = await model.Next(messages, CancellationToken.None);
                // A reread is optional; only an actual native structured call can invoke it.
                // These are sequential read steps, not retries or manufactured tool replies.
                for(var step = 0; answer.TryGetProperty("tool_calls", out var calls) && calls.GetArrayLength() > 0; step++)
                {
                    Check(step < 2 && calls.GetArrayLength() == 1 && calls[0].GetProperty("function").GetProperty("name").GetString() == "site_help", workspace.Role + " explanation may only reread guide; no navigation or mutation tool");
                    Console.WriteLine("KNOWLEDGE_NATIVE " + workspace.Role + ": " + answer.GetRawText());
                    var topic = calls[0].GetProperty("function").GetProperty("arguments").GetProperty("topic").GetString()!;
                    messages.Add(answer);
                    messages.Add(new { role = "tool", tool_name = "site_help", content = ExecutiveSiteKnowledge.ModelJson(ExecutiveSiteKnowledge.Read(principal, pages, topic)) });
                    answer = await model.Next(messages, CancellationToken.None);
                }
                Check((!answer.TryGetProperty("tool_calls", out var extra) || extra.GetArrayLength() == 0) && answer.GetProperty("content").GetString() is { Length: > 30 }, workspace.Role + " real Qwen answers from reviewed knowledge without a mutation tool");
                var text = answer.GetProperty("content").GetString()!;
                Console.WriteLine("KNOWLEDGE_REPLY " + workspace.Role + ": " + text);
                var roleName = ExecutiveSiteKnowledge.RoleLabel(workspace.Role).Replace("عضو ", "");
                Check(text.Contains(workspace.Role, StringComparison.OrdinalIgnoreCase) || ExecutiveNavigationService.Normalize(text).Contains(ExecutiveNavigationService.Normalize(roleName)), workspace.Role + " response names the authenticated role");
                var arabicLetters = text.Count(c => c is >= '\u0620' and <= '\u064a');
                Check(command.Any(c => c is >= '\u0600' and <= '\u06ff') ? arabicLetters > 20 : text.Count(c => c is >= 'A' and <= 'Z' or >= 'a' and <= 'z') > arabicLetters, workspace.Role + " answer follows the command language");
            }
        }
        Console.WriteLine($"RESULT KNOWLEDGE_PASS={count} FAIL=0 LIVE_MODEL={live}. Registered routes, aliases and procedural knowledge only. No SQL permission or business execution is claimed by this test.");
        ExecutivePage Page(string area, NavItem i) => new($"{area}:{i.Controller}:{i.Action}", i.Label, area,
            links.GetPathByAction(i.Action, i.Controller, new { area }) ?? throw new Exception("Missing route " + i.Label), ExecutiveNavigationService.Aliases(i.Label));
    }
}
