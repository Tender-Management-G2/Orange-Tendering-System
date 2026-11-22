using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Orange_Tendering_System.Data;
using Orange_Tendering_System.Models.Entities;

namespace Orange_Tendering_System.Services.Assistant;

// Runs agent plans: intent → plan (registered tools only) → permission check → slot collection with validation → full summary →
// explicit confirmation bound to the plan's ActionId → permission re-check → execution through the existing service → database
// verification → audit → per-step report. The same engine serves typed and spoken commands.
public sealed class AgentEngine(ApplicationDbContext db, AgentTools tools, AiKnowledgeService knowledge, TimeProvider clock)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    public static AgentPlan? Read(string? json)
    {
        if (json == null) return null;
        try { using var d = JsonDocument.Parse(json); return d.RootElement.TryGetProperty("actionId", out _) && d.RootElement.TryGetProperty("steps", out _) ? d.RootElement.Deserialize<AgentPlan>(Json) : null; }
        catch (JsonException) { return null; }
    }

    private ActionOutcome Out(AgentPlan plan, string text, string speech, string kind) => new(text, speech, kind, JsonSerializer.Serialize(plan, Json));

    public async Task<ActionOutcome> StartAsync(AssistantUser user, List<string> toolIds, string message, bool ar, CancellationToken ct, int? conversationId = null)
    {
        foreach (var id in toolIds.Distinct())
            if (await tools.Denial(id, user, ar) is { } denial)
            {
                Audit(user, "AiActionRejected", Guid.Empty, new { Tool = id, Reason = denial });
                await db.SaveChangesAsync(ct);
                return new(denial, denial);
            }
        if (toolIds is ["list_departments"]) return new(await tools.ListDepartments(ar, ct), ar ? "هذه قائمة الأقسام." : "Here is the list of departments.", "data");
        if (toolIds is ["prepare_user_role_change"])
        {
            var output = await tools.PrepareUserRoleChange(message, user, ar, ct);
            Audit(user, "AiRoleChangePrepared", Guid.Empty, new { QueryHash = Orange_Tendering_System.Services.Reports.ReportCryptography.Hash(message) });
            await db.SaveChangesAsync(ct);
            return new(output, ar ? "جهزت ملخصًا فقط؛ لم أغيّر أي دور." : "I prepared the summary only; no role was changed.", "data");
        }
        if (toolIds is ["list_users"] or ["find_user"] or ["get_user_details"])
        {
            var output = await tools.ReadUsers(toolIds[0], message, ar, ct);
            Audit(user, "AiUserLookup", Guid.Empty, new { Tool = toolIds[0], QueryHash = Orange_Tendering_System.Services.Reports.ReportCryptography.Hash(message) });
            await db.SaveChangesAsync(ct);
            return new(output, ar ? "تم البحث في حسابات الموظفين المصرح بعرضها." : "I searched the employee accounts you are authorized to view.", "data");
        }
        var references = toolIds.Any(id => AgentTools.ProcurementRole(id) != null) ? [] : await knowledge.RetrieveAsync(user.Id, message, ct);
        var plan = new AgentPlan
        {
            Arabic = ar, ExpiresAt = Now + AgentTools.PlanLifetime, ConversationId = conversationId, WorkspaceRole = user.Role,
            KnowledgeSources = references.Select(x => $"{SafeReference(x.Title)} v{x.Version}, chunk {x.Chunk}").Distinct().Take(5).ToList()
        };
        for (var i = 0; i < toolIds.Count; i++)
        {
            var values = await tools.ExtractAsync(toolIds[i], message, user, firstOfPlan: i == 0);
            // "Create a department, then a user in it": the user's department is the one created by the earlier step.
            if (toolIds[i] == "create_user" && toolIds.Take(i).Contains("create_department")) values["department"] = "@" + toolIds.IndexOf("create_department");
            plan.Steps.Add(new AgentStep { Tool = toolIds[i], Values = values });
            // Values found in the sentence are validated exactly like typed answers; invalid ones are asked again.
            foreach (var key in values.Keys.ToList())
            {
                if (key == "department" && values[key].StartsWith('@') && toolIds[i] == "create_user" &&
                    toolIds.Take(i).Contains("create_department")) continue;
                var (ok, _) = await tools.Validate(toolIds[i], key, values[key], plan with { Current = i }, user, ar);
                if (ok == null) values.Remove(key); else values[key] = ok;
            }
        }
        Audit(user, "AiPlanStarted", plan.ActionId, new { Tools = toolIds });
        await db.SaveChangesAsync(ct);
        var intro = plan.Steps.Count > 1
            ? (ar ? $"تمام، هاي الخطة ({plan.Steps.Count} خطوات): " : $"Okay, here is the plan ({plan.Steps.Count} steps): ") + string.Join(ar ? " ثم " : " → ", plan.Steps.Select(s => ar ? AgentTools.Registry.First(r => r.Id == s.Tool).Ar : AgentTools.Registry.First(r => r.Id == s.Tool).En)) + "\n\n"
            : "";
        return await Next(plan, user, intro, null, ct);
    }

    private async Task<ActionOutcome> Next(AgentPlan plan, AssistantUser user, string intro, string? problem, CancellationToken ct)
    {
        var ar = plan.Arabic;
        for (var i = 0; i < plan.Steps.Count; i++)
            foreach (var slot in AgentTools.Slots(plan.Steps[i].Tool).Where(s => !s.Optional))
                if (!plan.Steps[i].Values.ContainsKey(slot.Key))
                {
                    var q = await tools.ProcurementQuestion(plan.Steps[i], plan, slot, user, ar);
                    var label = plan.Steps.Count > 1 ? $"({(ar ? "الخطوة" : "Step")} {i + 1}) " : "";
                    var p = plan with { Current = i, Asking = slot.Key, State = "Collecting", ExpiresAt = Now + AgentTools.PlanLifetime };
                    return Out(p, intro + (problem != null ? problem + "\n\n" : "") + label + q, (problem != null ? problem + " " : "") + q, "agent-plan");
                }
        var confirming = plan with { Asking = null, State = "Confirming", ExpiresAt = Now + AgentTools.PlanLifetime };
        var sb = new StringBuilder(intro).Append(ar ? "### راجع قبل التنفيذ\n" : "### Review before execution\n");
        for (var i = 0; i < confirming.Steps.Count; i++) sb.AppendLine(await tools.Describe(confirming.Steps[i], i, confirming, ar)).AppendLine();
        if (confirming.KnowledgeSources.Count > 0)
        {
            sb.AppendLine(ar ? "### مراجع للمراجعة (للاستدلال فقط؛ الصلاحيات والتنفيذ من أدوات النظام)" : "### Retrieved references (informational only; tools enforce permissions and execution)");
            foreach (var source in confirming.KnowledgeSources) sb.Append("- ").AppendLine(source);
            sb.AppendLine();
        }
        sb.Append(ar ? "لن يُنفَّذ أي شيء قبل تأكيدك. قل **«أكّد»** أو اضغط زر التأكيد، أو قل **«إلغاء»**." : "Nothing runs before you confirm. Say **\"confirm\"** or press Confirm, or say **\"cancel\"**.");
        // Spoken summary: what will be done and the main value of each step, then the explicit question.
        string Main(AgentStep s) => string.Join(". ", new[]
        {
            s.Values.GetValueOrDefault("name") ?? s.Values.GetValueOrDefault("fullName") ?? s.Values.GetValueOrDefault("title"),
            s.Values.GetValueOrDefault("spokenDetails")
        }.Where(value => !string.IsNullOrWhiteSpace(value)));
        var spoken = string.Join(ar ? "، ثم " : ", then ", confirming.Steps.Select(s => $"{(ar ? AgentTools.Registry.First(r => r.Id == s.Tool).Ar : AgentTools.Registry.First(r => r.Id == s.Tool).En)} {Main(s)}".Trim()));
        var speech = ar ? $"رح {spoken}. بتأكد؟ قول أكّد أو إلغاء." : $"I will {spoken}. Do you confirm? Say confirm or cancel.";
        return Out(confirming, sb.ToString(), speech, "agent-confirm");
    }

    private static readonly Regex Confirm = new(@"^(confirm|confirmed|yes|go ahead|do it|execute|نعم|اه|ايوه|ايوا|اكد|اكيد|تاكيد|موافق|نفذ|نفذها|توكل)$", RegexOptions.IgnoreCase);
    private static readonly Regex Cancel = new(@"^(cancel(?: this)?|stop|abort|never ?mind|no|الغ|الغي|الغاء|بلاش|وقف|توقف|لا)$", RegexOptions.IgnoreCase);

    public async Task<ActionOutcome> ContinueAsync(AgentPlan plan, AssistantUser user, ClaimsPrincipal principal, string message, Guid? confirmedActionId, CancellationToken ct)
    {
        var ar = plan.Arabic;
        var t = AssistantIntentParser.Normalize(AssistantIntentParser.Strip(message)).TrimEnd('.', '!', '?', '؟').Trim();
        if (plan.WorkspaceRole != null && plan.WorkspaceRole != user.Role)
            return new(ar ? "ارجع لمساحة العمل اللي بدأت فيها الإجراء." : "Return to the workspace where you started this action.",
                ar ? "ارجع لمساحة العمل الأصلية." : "Return to the original workspace.");
        if (plan.State is "Done" or "Partial" or "Failed" or "Cancelled")
            return Out(plan, ar ? "هذه العملية انتهت ولن تُنفَّذ مرة ثانية. اطلب أمرًا جديدًا." : "That operation is finished and will not run again. Ask for a new command.", ar ? "العملية انتهت." : "That operation is finished.", "agent-done");
        if (Now > plan.ExpiresAt)
            return Out(plan with { State = "Cancelled" }, ar ? "انتهت صلاحية هذا الطلب (30 دقيقة). ابدأ من جديد." : "This request expired (30 minutes). Please start again.", ar ? "انتهت صلاحية الطلب." : "This request expired.", "agent-done");
        if (Cancel.IsMatch(t))
        {
            Audit(user, "AiPlanCancelled", plan.ActionId, new { });
            await db.SaveChangesAsync(ct);
            return Out(plan with { State = "Cancelled" }, ar ? "تم الإلغاء. لم يُنفَّذ أي شيء." : "Cancelled. Nothing was executed.", ar ? "تمام، ألغيت." : "Okay, cancelled.", "agent-done");
        }
        if (plan.State == "Collecting")
        {
            if (AgentTools.IsCreationFollowUp(message))
                return await Next(plan, user, "", null, ct);
            var step = plan.Steps[plan.Current];
            var values = new Dictionary<string, string>(step.Values);
            var inputs = step.Tool == "create_user"
                ? await tools.ExtractAsync(step.Tool, message, user, firstOfPlan: true)
                : [];
            if (inputs.Count == 0) inputs.Add(plan.Asking!, message);
            string? problem = null;
            foreach (var (key, input) in inputs)
            {
                if (values.ContainsKey(key)) continue;
                var (value, error) = await tools.Validate(step.Tool, key, input, plan, user, ar);
                if (value != null) values[key] = value;
                else if (key == plan.Asking) problem = error;
            }
            var steps = plan.Steps.ToList();
            steps[plan.Current] = step with { Values = values };
            plan = plan with { Steps = steps };
            return await Next(plan, user, "", problem, ct);
        }
        // Confirming: only an explicit confirmation, and a button confirmation must carry THIS plan's ActionId.
        if (confirmedActionId != null && confirmedActionId != plan.ActionId)
            return Out(plan, ar ? "هذا التأكيد يخص طلبًا أقدم، لذلك لم يُنفَّذ. راجع الملخص الحالي." : "That confirmation belongs to an older proposal, so nothing ran. Review the current summary.", ar ? "التأكيد يخص طلب قديم." : "That confirmation was for an older proposal.", "agent-confirm");
        if (!Confirm.IsMatch(t))
            return Out(plan, ar ? "لم أنفّذ شيئًا بعد. قل «أكّد» للتنفيذ أو «إلغاء»." : "Nothing has run yet. Say \"confirm\" to execute or \"cancel\".", ar ? "قول أكّد أو إلغاء." : "Say confirm or cancel.", "agent-confirm");
        if (plan.Steps.Any(step => AgentTools.Slots(step.Tool).Any(slot => !slot.Optional && !step.Values.ContainsKey(slot.Key))))
            return await Next(plan, user, "", null, ct);
        return await ExecuteAsync(plan, user, principal, ct);
    }

    private async Task<ActionOutcome> ExecuteAsync(AgentPlan plan, AssistantUser user, ClaimsPrincipal principal, CancellationToken ct)
    {
        var ar = plan.Arabic;
        var steps = plan.Steps.ToList();
        var report = new StringBuilder(ar ? "### نتيجة التنفيذ\n" : "### Execution result\n");
        string? url = null; var failed = false;
        for (var i = 0; i < steps.Count; i++)
        {
            var step = steps[i];
            var key = $"{plan.ActionId}:{i}";
            // Idempotency: a step already executed for this ActionId (retry, double click, refresh) is never executed again.
            var previous = await db.AuditLogs.AsNoTracking().Where(a => a.EntityName == "AiAgentStep" && a.EntityId == key && a.Action == "AiToolExecuted").Select(a => a.NewValues).FirstOrDefaultAsync(ct);
            if (previous != null)
            {
                var prior = JsonSerializer.Deserialize<JsonElement>(previous);
                steps[i] = step with { Status = "Done", ResultRef = prior.GetProperty("ref").GetString(), ResultText = prior.GetProperty("text").GetString() };
                report.AppendLine($"✅ {i + 1}. {steps[i].ResultText}");
                continue;
            }
            if (failed) { report.AppendLine($"⏸ {i + 1}. {(ar ? "لم تُنفَّذ بسبب فشل خطوة سابقة." : "Not run because an earlier step failed.")}"); continue; }
            if (await tools.Denial(step.Tool, user, ar) is { } denial)   // permissions re-checked immediately before execution
            {
                steps[i] = step with { Status = "Failed", ResultText = denial }; failed = true;
                Audit(user, "AiToolRejected", plan.ActionId, new { Step = i, step.Tool, Reason = denial });
                report.AppendLine($"❌ {i + 1}. {denial}"); continue;
            }
            var plan2 = plan with { Steps = steps };
            var (ok, text, reference, link) = await tools.Execute(step, plan2, user, principal, ar, ct);
            if (ok)
            {
                steps[i] = step with { Status = "Done", ResultRef = reference, ResultText = text };
                db.AuditLogs.Add(new AuditLog { UserId = user.Id, EntityName = "AiAgentStep", EntityId = key, Action = "AiToolExecuted", NewValues = JsonSerializer.Serialize(new { tool = step.Tool, @ref = reference, text }, Json), CreatedAt = Now });
                await db.SaveChangesAsync(ct);
                report.AppendLine($"✅ {i + 1}. {text}" + (link != null ? $" {link}" : ""));
                url = link ?? url;
            }
            else
            {
                steps[i] = step with { Status = "Failed", ResultText = text }; failed = true;
                Audit(user, "AiToolFailed", plan.ActionId, new { Step = i, step.Tool, Reason = text });
                await db.SaveChangesAsync(ct);
                report.AppendLine($"❌ {i + 1}. {text}");
            }
        }
        var done = steps.Count(s => s.Status == "Done");
        var state = done == steps.Count ? "Done" : done == 0 ? "Failed" : "Partial";
        if (state == "Partial") report.AppendLine().Append(ar ? $"**تم تنفيذ {done} من {steps.Count} خطوات فقط.** الخطوات المنفذة باقية كما هي." : $"**Only {done} of {steps.Count} steps completed.** Completed steps remain in place.");
        var speech = state switch
        {
            "Done" => ar ? "تم. نفذت كل الخطوات وتأكدت منها." : "Done. All steps were executed and verified.",
            "Partial" => ar ? $"نفذت {done} من {steps.Count} خطوات، في خطوة فشلت. التفاصيل بالشات." : $"I completed {done} of {steps.Count} steps; one failed. Details are in the chat.",
            _ => ar ? "ما قدرت أنفذ العملية. السبب بالشات." : "I couldn't execute it. The reason is in the chat."
        };
        return Out(plan with { Steps = steps, State = state }, report.ToString().Trim(), speech, "agent-done");
    }

    private void Audit(AssistantUser user, string action, Guid actionId, object values) =>
        db.AuditLogs.Add(new AuditLog { UserId = user.Id, EntityName = "AiAgentPlan", EntityId = actionId.ToString(), Action = action, NewValues = JsonSerializer.Serialize(values, Json), CreatedAt = Now });

    private static string SafeReference(string title) => new(title.Where(c => !char.IsControl(c) && c is not ('[' or ']' or '<' or '>' or '\n' or '\r')).Take(160).ToArray());
}
