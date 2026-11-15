using System.Security.Claims;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Orange_Tendering_System;
using Orange_Tendering_System.Data;
using Orange_Tendering_System.Models;
using Orange_Tendering_System.Models.Entities;
using Orange_Tendering_System.Services;

// Real SQL Server check of the Role-Based Clarification Chat: the full role-pair matrix (allowed and refused), context checks,
// resolve/continue rules, and separation from the AI Chat Assistant tables.
public static class ClarificationChecks
{
    static int failures;
    static void Check(bool ok, string name) { Console.WriteLine((ok ? "PASS " : "FAIL ") + name); if (!ok) failures++; }

    public static async Task Run()
    {
        var connection = Environment.GetEnvironmentVariable("UI_FIXTURE_CONNECTION")!;
        if (!new SqlConnectionStringBuilder(connection).InitialCatalog.StartsWith("OrangeAuthCheck_UiE2E_", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Isolated UI database required.");
        var state = JsonNode.Parse(await File.ReadAllTextAsync(Environment.GetEnvironmentVariable("UI_FIXTURE_STATE")!))!;
        var root = Environment.GetEnvironmentVariable("AUTH_PROJECT_ROOT")!;
        Environment.SetEnvironmentVariable("DemoData__Enabled", "false");
        Environment.SetEnvironmentVariable("ConnectionStrings__Default Connection", connection);
        Environment.SetEnvironmentVariable("BootstrapAdmin__Email", state["accounts"]![0]!["email"]!.GetValue<string>());
        Environment.SetEnvironmentVariable("BootstrapAdmin__Password", state["accounts"]![0]!["password"]!.GetValue<string>());
        using var factory = new WebApplicationFactory<WebAppMarker>().WithWebHostBuilder(b => b.UseContentRoot(root).UseEnvironment("Development"));

        var cache = new Dictionary<string, (ClaimsPrincipal P, string Id)>();
        async Task<(ClaimsPrincipal P, string Id)> Who(string area)
        {
            if (cache.TryGetValue(area, out var c)) return c;
            var email = state["accounts"]!.AsArray().Single(x => x!["area"]!.GetValue<string>() == area)!["email"]!.GetValue<string>();
            using var scope = factory.Services.CreateScope();
            var id = await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Users.Where(u => u.Email == email).Select(u => u.Id).SingleAsync();
            return cache[area] = (new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, id)], "test")), id);
        }
        async Task<List<ClarificationOption>> Options(string area) { using var s = factory.Services.CreateScope(); return await s.ServiceProvider.GetRequiredService<ClarificationService>().Options((await Who(area)).P); }
        async Task<(int? Id, string? Error)> Start(string from, ClarificationOption o, string subject = "Check")
        {
            using var s = factory.Services.CreateScope();
            try { return (await s.ServiceProvider.GetRequiredService<ClarificationService>().CreateAsync((await Who(from)).P, o.RecipientId, o.ContextType, o.ContextId, subject, "Hello"), null); }
            catch (WorkflowException e) { return (null, e.Message); }
        }
        var ids = new Dictionary<string, string>();
        foreach (var a in new[] { "Department", "SourcingOfficer", "EvaluationCommittee", "SourcingManager", "TopCommittee", "Supplier", "SupplierTwo", "Admin" }) ids[a] = (await Who(a)).Id;
        string RoleOfOption(ClarificationOption o) => o.Role;

        // 1. What each role is offered (the approved chain only)
        var allowedRoles = new Dictionary<string, string[]>
        {
            ["Department"] = ["Sourcing Officer"],
            ["SourcingOfficer"] = ["Requesting Department", "Supplier", "Evaluation Committee"],
            ["EvaluationCommittee"] = ["Sourcing Officer", "Sourcing Manager"],
            ["SourcingManager"] = ["Evaluation Committee", "Top Committee"],
            ["TopCommittee"] = ["Sourcing Manager"],
            ["Supplier"] = []
        };
        foreach (var (area, roles) in allowedRoles)
        {
            var opts = await Options(area);
            var offered = opts.Select(RoleOfOption).Distinct().OrderBy(x => x).ToArray();
            Check(offered.SequenceEqual(roles.OrderBy(x => x)), $"{area}: may chat only with [{string.Join(", ", roles)}] — offered [{string.Join(", ", offered)}]");
        }
        using (var s = factory.Services.CreateScope())
        {
            var denied = false; try { await s.ServiceProvider.GetRequiredService<ClarificationService>().Options((await Who("Admin")).P); } catch (WorkflowException) { denied = true; }
            Check(denied, "Admin has no clarification chat");
        }

        // 2. Every allowed pair can actually start a conversation (real SQL rows)
        async Task Pair(string from, string toRole, string to)
        {
            var o = (await Options(from)).First(x => x.Role == toRole && x.RecipientId == ids[to]);
            var r = await Start(from, o, $"{from}->{to}");
            Check(r.Id != null, $"Allowed: {from} -> {to} ({o.Context})");
        }
        await Pair("Department", "Sourcing Officer", "SourcingOfficer");
        await Pair("SourcingOfficer", "Requesting Department", "Department");
        await Pair("SourcingOfficer", "Supplier", "Supplier");
        await Pair("SourcingOfficer", "Evaluation Committee", "EvaluationCommittee");
        await Pair("EvaluationCommittee", "Sourcing Officer", "SourcingOfficer");
        await Pair("EvaluationCommittee", "Sourcing Manager", "SourcingManager");
        await Pair("SourcingManager", "Evaluation Committee", "EvaluationCommittee");
        await Pair("SourcingManager", "Top Committee", "TopCommittee");
        await Pair("TopCommittee", "Sourcing Manager", "SourcingManager");

        // 3. Forged requests for refused pairs are rejected by the server even if the UI is bypassed
        async Task Refused(string from, string to, ClarificationOption sample)
        {
            var o = sample with { RecipientId = ids[to] };
            var r = await Start(from, o, "forged");
            Check(r.Id == null, $"Refused: {from} -> {to}");
        }
        var deptOpt = (await Options("Department")).First(); var offOptT = (await Options("SourcingOfficer")).First(x => x.ContextType == "Tender");
        var evalOpt = (await Options("EvaluationCommittee")).First(); var mgrOpt = (await Options("SourcingManager")).First(); var topOpt = (await Options("TopCommittee")).First();
        foreach (var to in new[] { "SourcingManager", "EvaluationCommittee", "TopCommittee", "Supplier" }) await Refused("Department", to, deptOpt);
        foreach (var to in new[] { "SourcingManager", "TopCommittee" }) await Refused("SourcingOfficer", to, offOptT);
        foreach (var to in new[] { "TopCommittee", "Supplier", "Department" }) await Refused("EvaluationCommittee", to, evalOpt);
        foreach (var to in new[] { "SourcingOfficer", "Supplier", "Department" }) await Refused("SourcingManager", to, mgrOpt);
        foreach (var to in new[] { "SourcingOfficer", "EvaluationCommittee", "Supplier", "Department" }) await Refused("TopCommittee", to, topOpt);
        foreach (var to in new[] { "Department", "SourcingManager", "EvaluationCommittee", "TopCommittee", "SupplierTwo" }) await Refused("Supplier", to, offOptT);
        await Refused("Admin", "SourcingOfficer", offOptT);

        // 4. Context rules: right pair but a record the sender does not belong to
        var wrongTender = await Start("EvaluationCommittee", evalOpt with { ContextId = 99999 }, "wrong record");
        Check(wrongTender.Id == null, "Refused: committee member cannot start a chat about a tender they are not assigned to");
        var wrongRequest = await Start("Department", deptOpt with { ContextId = 99999 }, "wrong record");
        Check(wrongRequest.Id == null, "Refused: Department cannot use a purchase request of another department / non-existent one");

        // 5. Threads: participants only, continue rule, resolve
        using (var s = factory.Services.CreateScope())
        {
            var db = s.ServiceProvider.GetRequiredService<ApplicationDbContext>(); var svc = s.ServiceProvider.GetRequiredService<ClarificationService>();
            var thread = await db.Clarifications.Include(c => c.Messages).FirstAsync(c => c.Subject == "Department->SourcingOfficer");
            Check(thread.FromUserId == ids["Department"] && thread.ToUserId == ids["SourcingOfficer"] && thread.PurchaseRequestId != null && thread.Messages.Count == 1, "Thread stored with sender, recipient, linked request and first message");
            Check(await db.Notifications.AnyAsync(n => n.UserId == ids["SourcingOfficer"] && n.Title == "New clarification" && n.Link == $"/Clarifications/Details/{thread.Id}"), "Recipient notified with a link to the thread");
            Check(await svc.CanContinue(thread), "Allowed pair can continue the thread");
            Check(!await svc.ResolveAsync(ids["SupplierTwo"], thread.Id) && !await svc.ResolveAsync(ids["EvaluationCommittee"], thread.Id), "A non-participant cannot resolve it");
            Check(await svc.ResolveAsync(ids["SourcingOfficer"], thread.Id) && (await db.Clarifications.AsNoTracking().SingleAsync(c => c.Id == thread.Id)).Status == ClarificationStatus.Closed, "A participant can mark it resolved");
            Check(!await svc.ResolveAsync(ids["SourcingOfficer"], thread.Id), "A resolved thread cannot be resolved again");
            // A forged thread between a disallowed pair (e.g. inserted directly) cannot continue
            var bad = new Clarification { FromUserId = ids["Department"], ToUserId = ids["SupplierTwo"], Subject = "forged pair", CreatedAt = DateTime.UtcNow };
            Check(!await svc.CanContinue(bad), "A thread between a disallowed pair cannot continue (reply blocked)");
            Check(!ClarificationPolicy.Allowed(SystemRoles.RequestingDepartment, SystemRoles.Supplier) && ClarificationPolicy.Allowed(SystemRoles.SourcingOfficer, SystemRoles.Supplier), "Policy matrix: Department-Supplier refused, Officer-Supplier allowed");
            // 6. Separate from AI chat
            Check(await db.Set<AiChatConversation>().CountAsync() == 0 && await db.Set<AiChatMessage>().CountAsync() == 0, "Clarification chats created no AI chat conversations or messages (separate tables and endpoints)");
        }
        Console.WriteLine(failures == 0 ? "Clarification chat checks passed." : $"{failures} clarification check(s) FAILED.");
        if (failures > 0) Environment.ExitCode = 1;
    }
}
