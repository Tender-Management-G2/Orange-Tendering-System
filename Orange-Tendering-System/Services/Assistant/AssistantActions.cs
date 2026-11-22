using System.Globalization;
using System.Security.Claims;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Orange_Tendering_System.Data;
using Orange_Tendering_System.Models;
using Orange_Tendering_System.Models.Entities;
using Orange_Tendering_System.ViewModels;

namespace Orange_Tendering_System.Services.Assistant;

// One assistant answer produced by an action: text for the chat, a short spoken line, an optional validated internal URL to open,
// and conversation state (the in-progress tender draft) that only this server code writes.
public sealed record ActionOutcome(string Text, string Speech, string Kind = "text", string? Payload = null, string? NavigateUrl = null);

// Pending tender draft, stored in the chat message payload. Values are user-provided text, validated here before every step.
public sealed record TenderDraft
{
    public Guid DraftId { get; init; } = Guid.NewGuid();
    public bool Arabic { get; init; }
    public int? PurchaseRequestId { get; init; }
    public string? Title { get; init; }
    public string? Description { get; init; }
    public string? TechnicalRequirements { get; init; }
    public string? CommercialConditions { get; init; }
    public string? DeliveryExpectations { get; init; }
    public string? MandatoryCompliance { get; init; }
    public decimal? EstimatedValue { get; init; }
    public string Currency { get; init; } = "JOD";
    public string? DepartmentRepresentativeId { get; init; }
    public string? Asking { get; init; }
    public int? CreatedTenderId { get; init; }
    public string State { get; init; } = "Collecting";   // Collecting | Confirming | Created | Cancelled
}

// Controlled action layer of Orange AI. Every action is a fixed server method: it resolves records through AssistantAccess scopes,
// builds URLs from fixed route templates only, and creates tenders only through TenderWorkflowService.Create after an explicit
// confirmation. The model is never involved in choosing, authorizing or executing an action.
public sealed class AssistantActions(ApplicationDbContext db, AssistantAccess access, TenderWorkflowService workflow, UserManager<ApplicationUser> users, TimeProvider clock, ILogger<AssistantActions> logger)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    public static readonly string[] Currencies = ["JOD", "USD", "EUR", "SAR", "AED", "EGP"];

    // ---------------- navigation ----------------

    // Route templates for a tender the user may already open. /Workflow/Details/{id} redirects to the user's own area (WorkflowController);
    // a department opens tenders through its purchase request, as in the global search.
    public static string TenderUrl(AssistantUser user, int tenderId, int purchaseRequestId) =>
        user.Role == SystemRoles.RequestingDepartment ? $"/Department/PurchaseRequests/Details/{purchaseRequestId}" : $"/Workflow/Details/{tenderId}";

    public async Task<ActionOutcome> OpenLatestAsync(AssistantUser user, bool ar, CancellationToken ct)
    {
        if (user.Role == SystemRoles.Supplier)
        {
            var invitation = await db.TenderInvitations.AsNoTracking().Where(i => i.SupplierId == user.SupplierId)
                .OrderByDescending(i => i.InvitedAt).ThenByDescending(i => i.Id)
                .Select(i => new { i.Id, i.TenderRound.TenderId, i.TenderRound.Tender.Title }).FirstOrDefaultAsync(ct);
            if (invitation == null) return Empty(ar);
            Audit(user, "AiNavigate", invitation.TenderId.ToString(), new { Action = "open_latest_tender", Scope = "own_invitations" });
            await db.SaveChangesAsync(ct);
            return Go(ar, $"/Supplier/Invitations/Details/{invitation.Id}", invitation.TenderId, invitation.Title, latest: true);
        }
        if (!AssistantDataTools.Allowed(user).Contains(AssistantTopic.OpenTenders)) return Denied(user, ar);
        // Deterministic: newest creation time first, then the highest ID.
        var tender = await access.Tenders(user).OrderByDescending(t => t.CreatedAt).ThenByDescending(t => t.Id)
            .Select(t => new { t.Id, t.Title, t.PurchaseRequestId }).FirstOrDefaultAsync(ct);
        if (tender == null) return Empty(ar);
        Audit(user, "AiNavigate", tender.Id.ToString(), new { Action = "open_latest_tender", Scope = "authorized" });
        await db.SaveChangesAsync(ct);
        return Go(ar, TenderUrl(user, tender.Id, tender.PurchaseRequestId), tender.Id, tender.Title, latest: true);
    }

    public async Task<ActionOutcome> OpenTenderAsync(AssistantUser user, int tenderId, bool ar, CancellationToken ct)
    {
        if (user.Role == SystemRoles.Supplier)
        {
            var invitation = await db.TenderInvitations.AsNoTracking().Where(i => i.SupplierId == user.SupplierId && i.TenderRound.TenderId == tenderId)
                .OrderByDescending(i => i.Id).Select(i => new { i.Id, i.TenderRound.Tender.Title }).FirstOrDefaultAsync(ct);
            if (invitation == null) return NotVisible(user, tenderId, ar);
            return Go(ar, $"/Supplier/Invitations/Details/{invitation.Id}", tenderId, invitation.Title);
        }
        var tender = await access.Tenders(user).Where(t => t.Id == tenderId).Select(t => new { t.Id, t.Title, t.PurchaseRequestId }).SingleOrDefaultAsync(ct);
        if (tender == null) return NotVisible(user, tenderId, ar);
        Audit(user, "AiNavigate", tender.Id.ToString(), new { Action = "open_tender" });
        await db.SaveChangesAsync(ct);
        return Go(ar, TenderUrl(user, tender.Id, tender.PurchaseRequestId), tender.Id, tender.Title);
    }

    private ActionOutcome NotVisible(AssistantUser user, int tenderId, bool ar)
    {
        Audit(user, "AiActionRejected", tenderId.ToString(), new { Action = "open_tender", Reason = "Not in the user's authorized scope" });
        db.SaveChanges();
        return new(ar ? $"لم أجد المناقصة T-{tenderId:0000} ضمن السجلات المصرّح لك بفتحها." : $"I couldn't find tender T-{tenderId:0000} among the records you are authorized to open.",
            ar ? "لم أجد هذه المناقصة ضمن صلاحياتك." : "I couldn't find that tender in your authorized records.");
    }

    private static ActionOutcome Go(bool ar, string url, int id, string title, bool latest = false)
    {
        var text = ar
            ? $"{(latest ? "أكيد، رح أفتحلك أحدث عطاء بتقدر تشوفه" : "تمام، رح أفتح المناقصة")}: **T-{id:0000} – {title}**\n\nجارٍ الانتقال إلى {url}"
            : $"{(latest ? "Sure — opening the newest tender you can access" : "Opening tender")}: **T-{id:0000} – {title}**\n\nGoing to {url}";
        var speech = ar ? (latest ? $"أكيد، رح أفتحلك أحدث عطاء: {title}" : $"تمام، رح أفتح المناقصة {title}") : (latest ? $"Sure, opening your latest tender: {title}" : $"Opening tender {title}");
        return new(text, speech, "navigate", JsonSerializer.Serialize(new { url, tenderId = id }, Json), url);
    }
    private static ActionOutcome Empty(bool ar) => new(ar ? "لا توجد مناقصات ضمن نطاق صلاحياتك حتى الآن." : "There are no tenders in your authorized scope yet.",
        ar ? "ما في مناقصات ضمن صلاحياتك حاليًا." : "There are no tenders you can open yet.");
    private ActionOutcome Denied(AssistantUser user, bool ar)
    {
        Audit(user, "AiActionRejected", user.Role, new { Action = "open_tender", Reason = "Role has no tender access" });
        db.SaveChanges();
        return new(ar ? $"دورك (**{Labels.Role(user.Role, true)}**) لا يتيح فتح سجلات المناقصات." : $"Your role (**{user.Role}**) does not give access to tender records.",
            ar ? "دورك لا يسمح بفتح المناقصات." : "Your role cannot open tender records.");
    }

    // ---------------- evaluate an offer (committee members) ----------------

    // "Show me an offer to evaluate": opens the tender whose scoring is waiting for THIS member (their participation not yet
    // submitted), else their assigned tenders in evaluation. Scoring itself happens on the existing workflow page.
    public async Task<ActionOutcome> OpenEvaluationAsync(AssistantUser user, bool ar, CancellationToken ct)
    {
        if (user.Role is not (SystemRoles.EvaluationCommittee or SystemRoles.SourcingManager))
            return new(ar ? "تقييم العروض متاح لأعضاء لجنة التقييم فقط (ومنهم مدير التوريد)." : "Evaluating offers is available to Evaluation Committee members only (including the Sourcing Manager).",
                ar ? "تقييم العروض لأعضاء لجنة التقييم فقط." : "Only evaluation committee members can evaluate offers.");
        var now = clock.GetUtcNow().UtcDateTime;
        var pending = await db.Set<EvaluationParticipation>().AsNoTracking()
            .Where(p => p.CommitteeMember.UserId == user.Id && p.SubmittedAt == null && p.EvaluationSession.State == SessionState.Scheduled)
            .OrderBy(p => p.EvaluationSession.EndsAt)
            .Select(p => new { p.EvaluationSession.TenderRound.TenderId, p.EvaluationSession.TenderRound.Tender.Title, p.EvaluationSession.StartsAt, p.EvaluationSession.EndsAt, Offers = p.EvaluationSession.EligibleBids.Count() })
            .Take(10).ToListAsync(ct);
        if (pending.Count == 1 || (pending.Count > 1 && pending.Count(p => p.StartsAt <= now) == 1))
        {
            var p = pending.Count == 1 ? pending[0] : pending.First(x => x.StartsAt <= now);
            var url = TenderUrl(user, p.TenderId, 0);
            Audit(user, "AiNavigate", p.TenderId.ToString(), new { Action = "open_evaluation" });
            await db.SaveChangesAsync(ct);
            var window = p.StartsAt > now
                ? (ar ? $"\n\nنافذة التقييم تبدأ {JordanTime.Display(p.StartsAt)}." : $"\n\nThe scoring window opens {JordanTime.Display(p.StartsAt)}.")
                : (ar ? $"\n\nنافذة التقييم مفتوحة حتى {JordanTime.Display(p.EndsAt)}." : $"\n\nThe scoring window is open until {JordanTime.Display(p.EndsAt)}.");
            return new((ar ? $"تمام، هاي المناقصة اللي بانتظار تقييمك: **T-{p.TenderId:0000} – {p.Title}** ({p.Offers} عروض مؤهلة)." : $"Here is the tender waiting for your evaluation: **T-{p.TenderId:0000} – {p.Title}** ({p.Offers} eligible offers).") + window + $"\n\n{url}",
                ar ? $"تمام، رح أفتحلك {p.Title} عشان تقيّم العروض." : $"Opening {p.Title} so you can score the offers.", "navigate", JsonSerializer.Serialize(new { url, tenderId = p.TenderId }, Json), url);
        }
        if (pending.Count > 1)
            return new((ar ? "عندك أكثر من مناقصة بانتظار تقييمك. اختر وحدة:\n" : "Several tenders are waiting for your evaluation. Choose one:\n")
                + string.Join("\n", pending.Select(p => $"- **T-{p.TenderId:0000} – {p.Title}** ({JordanTime.Display(p.StartsAt)} → {JordanTime.Display(p.EndsAt)}): {TenderUrl(user, p.TenderId, 0)}")),
                ar ? "عندك أكثر من مناقصة بانتظار التقييم، اختار وحدة من القائمة." : "Several tenders need your evaluation; choose one from the list.", "clarify");
        var assigned = await access.Tenders(user).Where(t => t.Status == TenderStatus.Evaluation).OrderByDescending(t => t.CreatedAt).ThenByDescending(t => t.Id)
            .Select(t => new { t.Id, t.Title }).Take(5).ToListAsync(ct);
        if (assigned.Count == 1)
        {
            var url = TenderUrl(user, assigned[0].Id, 0);
            return new(ar ? $"ما في جلسة تقييم مفتوحة باسمك حاليًا، بس هاي المناقصة المسندة لك وهي بمرحلة التقييم: **T-{assigned[0].Id:0000} – {assigned[0].Title}**\n\n{url}" : $"No scoring session is open for you right now, but this assigned tender is in evaluation: **T-{assigned[0].Id:0000} – {assigned[0].Title}**\n\n{url}",
                ar ? $"رح أفتحلك {assigned[0].Title}." : $"Opening {assigned[0].Title}.", "navigate", JsonSerializer.Serialize(new { url }, Json), url);
        }
        return new(ar ? "ما في عروض بانتظار تقييمك حاليًا. بتوصلك تنبيه لما يفتح مسؤول التوريد جلسة التقييم." : "There are no offers waiting for your evaluation right now. You'll be notified when the Sourcing Officer opens a scoring session.",
            ar ? "ما في عروض بانتظار تقييمك حاليًا." : "No offers are waiting for your evaluation right now.");
    }

    public static bool IsEvaluateRequest(string message)
    {
        var t = " " + AssistantIntentParser.Normalize(AssistantIntentParser.Strip(message)) + " ";
        if (Regex.IsMatch(t, @"\s(how|كيف|اشرح|explain)\s")) return false;
        return Regex.IsMatch(t, @"\s(اقيم|اقيمو|اقيمه|اقيمها|نقيم|يقيم|قيملي|قيم العرض|قيم العروض|تقييم العرض|تقييم العروض|احط علامات|اعطي علامات|ادخل العلامات|evaluate|score the|give my scores|enter my scores|rate the offers?)\b");
    }

    // ---------------- reports waiting for my signature ----------------

    public static bool IsSignatureLookup(string message)
    {
        var t = " " + AssistantIntentParser.Normalize(AssistantIntentParser.Strip(message)) + " ";
        var sign = Regex.IsMatch(t, @"(توقيع|اوقع|يوقع|نوقع|توقع|امضاء|امضي|\bsign\b|signature|signing)");
        var lookup = Regex.IsMatch(t, @"(\sاي\s|بده|بدها|بدو|تحتاج|محتاج|ناقص|بانتظار|شوف|دور|which|any\b|need|waiting|pending|find|show)");
        return sign && lookup && !TenderRefPattern.IsMatch(t) && !Regex.IsMatch(t, @"\s(how|كيف|اشرح|explain)\s");
    }
    private static readonly Regex TenderRefPattern = new(@"\bt-?\d{1,7}\b|(?:مناقصه|عطاء)\s*(?:رقم)?\s*\d{1,7}", RegexOptions.IgnoreCase);

    // Finds the report/decision that needs THIS user's signature and opens its tender page (the signing itself stays in the existing
    // workflow with its identity confirmation). Committee report signatures, then Manager review, then Top Committee vote.
    public async Task<ActionOutcome> OpenSignatureAsync(AssistantUser user, bool ar, CancellationToken ct)
    {
        var items = new List<(int Id, string Title, string What)>();
        if (user.Role is SystemRoles.EvaluationCommittee or SystemRoles.SourcingManager)
            items.AddRange((await db.Set<EvaluationParticipation>().AsNoTracking()
                .Where(p => p.CommitteeMember.UserId == user.Id && p.EvaluationSession.ReportSentAt != null && p.EvaluationSession.State == SessionState.ReportDraft && p.EvaluationSession.ReportId != null
                    && !db.Set<ReportSignature>().Any(s => s.TenderReportId == p.EvaluationSession.ReportId && s.UserId == user.Id && s.Stage == ReportSigningStage.Evaluation && s.InvalidatedAt == null))
                .Select(p => new { p.EvaluationSession.TenderRound.TenderId, p.EvaluationSession.TenderRound.Tender.Title }).ToListAsync(ct))
                .Select(x => (x.TenderId, x.Title, ar ? "توقيع تقرير التقييم" : "sign the evaluation report")));
        if (user.Role == SystemRoles.SourcingManager)
            items.AddRange((await access.Tenders(user).Where(t => t.Status == TenderStatus.ManagerReview).Select(t => new { t.Id, t.Title }).ToListAsync(ct))
                .Select(x => (x.Id, x.Title, ar ? "مراجعة التقرير وتوقيعه كمدير" : "review and sign the report as Manager")));
        if (user.Role == SystemRoles.TopCommittee)
            items.AddRange((await access.Tenders(user).Where(t => t.Status == TenderStatus.TopCommitteeReview && !db.CommitteeVotes.Any(v => v.CommitteeRound.TenderRound.TenderId == t.Id && v.CommitteeMember.UserId == user.Id))
                .Select(t => new { t.Id, t.Title }).ToListAsync(ct)).Select(x => (x.Id, x.Title, ar ? "التصويت والتوقيع" : "vote and sign")));
        items = items.DistinctBy(i => i.Id).ToList();
        if (items.Count == 0)
            return new(ar ? "ما في أي تقرير أو قرار بانتظار توقيعك حاليًا." : "Nothing is waiting for your signature right now.", ar ? "ما في شي بانتظار توقيعك حاليًا." : "Nothing is waiting for your signature right now.");
        if (items.Count > 1)
            return new((ar ? "في أكثر من مناقصة بانتظار توقيعك:\n" : "Several tenders are waiting for your signature:\n") + string.Join("\n", items.Select(i => $"- **T-{i.Id:0000} – {i.Title}** ({i.What}): {TenderUrl(user, i.Id, 0)}")),
                ar ? $"في {items.Count} مناقصات بانتظار توقيعك، اختار وحدة من القائمة." : $"{items.Count} tenders are waiting for your signature; choose one.", "clarify");
        var one = items[0]; var url = TenderUrl(user, one.Id, 0);
        Audit(user, "AiNavigate", one.Id.ToString(), new { Action = "open_signature" });
        await db.SaveChangesAsync(ct);
        return new((ar ? $"لقيتها: **T-{one.Id:0000} – {one.Title}** بانتظار {one.What}. رح أفتحلك صفحتها؛ راجع التقرير ثم وقّع من هناك (التوقيع يحتاج تأكيد هويتك)." : $"Found it: **T-{one.Id:0000} – {one.Title}** is waiting for you to {one.What}. Opening it — review the report, then sign there (signing needs your identity confirmation).") + $"\n\n{url}",
            ar ? $"لقيتها. {one.Title} بانتظار توقيعك، رح أفتحلك صفحتها." : $"Found it. {one.Title} is waiting for your signature; opening it.", "navigate", JsonSerializer.Serialize(new { url, tenderId = one.Id }, Json), url);
    }

    // ---------------- weight proposals (conversation) ----------------

    public sealed record WeightsDraft
    {
        public Guid DraftId { get; init; } = Guid.NewGuid();
        public bool Arabic { get; init; }
        public int? TenderId { get; init; }
        public string? Title { get; init; }
        public decimal? Financial { get; init; }
        public decimal? Technical { get; init; }
        public string State { get; init; } = "Choosing";   // Choosing | Asking | Confirming | Submitted | Cancelled
    }

    public static bool IsWeightsRequest(string message)
    {
        var t = " " + AssistantIntentParser.Normalize(AssistantIntentParser.Strip(message)) + " ";
        return Regex.IsMatch(t, @"(اوزان|الاوزان|الوزن|وزن|weights?\b)") && !Regex.IsMatch(t, @"\s(how|كيف|اشرح|explain|what is|ما هو|ما هي|approve|اعتمد)\s");
    }

    private async Task<List<(int TenderId, string Title, string Requirements)>> PendingWeights(AssistantUser user, CancellationToken ct) =>
        (await db.Set<WeightRound>().AsNoTracking()
            .Where(w => w.State == WeightState.Proposals && !w.Proposals.Any(p => p.MemberUserId == user.Id)
                && db.CommitteeMembers.Any(m => m.TenderId == w.TenderId && m.UserId == user.Id && m.CommitteeType == CommitteeType.Evaluation)
                && w.Version == db.Set<WeightRound>().Where(x => x.TenderId == w.TenderId).Max(x => x.Version))
            .OrderBy(w => w.CreatedAt).Select(w => new { w.TenderId, w.Tender.Title, w.RequirementsJson }).Take(10).ToListAsync(ct))
        .Select(x => (x.TenderId, x.Title, x.RequirementsJson)).ToList();

    public async Task<ActionOutcome> StartWeightsAsync(AssistantUser user, bool ar, CancellationToken ct)
    {
        if (user.Role is not (SystemRoles.EvaluationCommittee or SystemRoles.SourcingManager))
            return new(ar ? "مقترحات الأوزان يقدّمها أعضاء لجنة التقييم الأربعة فقط." : "Weight proposals are submitted by the four evaluation committee members only.", ar ? "الأوزان لأعضاء لجنة التقييم فقط." : "Only evaluation committee members propose weights.");
        var pending = await PendingWeights(user, ct);
        if (pending.Count == 0)
            return new(ar ? "ما في مناقصة بانتظار مقترح الأوزان منك حاليًا." : "No tender is waiting for your weight proposal right now.", ar ? "ما في مناقصة بانتظار أوزانك حاليًا." : "No tender is waiting for your weights right now.");
        var draft = new WeightsDraft { Arabic = ar };
        if (pending.Count > 1)
            return new((ar ? "في أكثر من مناقصة بانتظار أوزانك. اختر رقمًا:\n" : "Several tenders are waiting for your weights. Choose a number:\n") + string.Join("\n", pending.Select((p, i) => $"{i + 1}. T-{p.TenderId:0000} – {p.Title}")),
                ar ? "في أكثر من مناقصة بانتظار أوزانك، اختار رقم." : "Several tenders need your weights; choose a number.", "weights-draft", JsonSerializer.Serialize(draft, Json));
        return AskWeights(draft with { TenderId = pending[0].TenderId, Title = pending[0].Title, State = "Asking" }, pending[0].Requirements);
    }

    private static ActionOutcome AskWeights(WeightsDraft d, string requirementsJson, string? problem = null)
    {
        var ar = d.Arabic;
        string description = "", items = "";
        try
        {
            using var doc = JsonDocument.Parse(requirementsJson);
            var r = doc.RootElement;
            description = r.TryGetProperty("Description", out var de) ? de.GetString() ?? "" : "";
            if (r.TryGetProperty("Items", out var it) && it.ValueKind == JsonValueKind.Array)
                items = string.Join("\n", it.EnumerateArray().Take(8).Select(x => $"- {x.GetProperty("Name").GetString()}: {x.GetProperty("Quantity").GetDecimal():0.##} {(x.TryGetProperty("Unit", out var u) ? u.GetString() : "")}"));
        }
        catch (Exception e) when (e is JsonException or KeyNotFoundException or InvalidOperationException) { }
        if (description.Length > 400) description = description[..399] + "…";
        var text = (problem != null ? problem + "\n\n" : "") + (ar
            ? $"### T-{d.TenderId:0000} – {d.Title}\nهاي المناقصة بانتظار مقترح الأوزان منك.\n\n{description}\n\n{items}\n\nكم **الوزن المالي** اللي بتقترحه؟ (الفني = 100 − المالي). مثال: «المالي 40» أو «40 مالي 60 فني»."
            : $"### T-{d.TenderId:0000} – {d.Title}\nThis tender is waiting for your weight proposal.\n\n{description}\n\n{items}\n\nWhat **financial weight** do you propose? (technical = 100 − financial). E.g. \"financial 40\" or \"40 financial, 60 technical\".");
        var speech = (problem != null ? problem + " " : "") + (ar ? $"المناقصة {d.Title} بانتظار أوزانك. كم الوزن المالي اللي بتقترحه؟" : $"Tender {d.Title} is waiting for your weights. What financial weight do you propose?");
        return new(text, speech, "weights-draft", JsonSerializer.Serialize(d with { State = "Asking" }, Json));
    }

    public static WeightsDraft? ReadWeights(string? json)
    {
        if (json == null) return null;
        try { using var doc = JsonDocument.Parse(json); return doc.RootElement.TryGetProperty("draftId", out _) && doc.RootElement.TryGetProperty("financial", out _) ? doc.RootElement.Deserialize<WeightsDraft>(Json) : null; }
        catch (JsonException) { return null; }
    }

    // Parses "40", "المالي 40", "40 مالي 60 فني", "technical 70", "30/70".
    public static (decimal? Financial, decimal? Technical) ParseWeights(string text)
    {
        var t = AssistantIntentParser.Normalize(text);
        var numbers = Regex.Matches(t, @"\d+(?:\.\d+)?").Select(m => decimal.Parse(m.Value, CultureInfo.InvariantCulture)).ToList();
        if (numbers.Count == 0) return (null, null);
        decimal? fin = null, tech = null;
        foreach (Match m in Regex.Matches(t, @"(مالي|المالي|financial|finance)\D{0,6}?(\d+(?:\.\d+)?)|(\d+(?:\.\d+)?)\s*%?\s*(مالي|المالي|financial)")) fin ??= decimal.Parse(m.Groups[2].Success && m.Groups[2].Value != "" ? m.Groups[2].Value : m.Groups[3].Value, CultureInfo.InvariantCulture);
        foreach (Match m in Regex.Matches(t, @"(فني|الفني|تقني|technical|tech)\D{0,6}?(\d+(?:\.\d+)?)|(\d+(?:\.\d+)?)\s*%?\s*(فني|الفني|تقني|technical)")) tech ??= decimal.Parse(m.Groups[2].Success && m.Groups[2].Value != "" ? m.Groups[2].Value : m.Groups[3].Value, CultureInfo.InvariantCulture);
        if (fin == null && tech == null) { fin = numbers[0]; if (numbers.Count > 1) tech = numbers[1]; }
        fin ??= 100 - tech; tech ??= 100 - fin;
        return (fin, tech);
    }

    public async Task<ActionOutcome> ContinueWeightsAsync(ClaimsPrincipal principal, AssistantUser user, WeightsDraft d, string message, CancellationToken ct)
    {
        var ar = d.Arabic;
        var t = " " + AssistantIntentParser.Normalize(AssistantIntentParser.Strip(message)) + " ";
        if (d.State is "Submitted" or "Cancelled")
            return new(ar ? (d.State == "Submitted" ? "مقترح الأوزان هذا مُرسل مسبقًا؛ لم يُرسل مرة ثانية." : "هذا الطلب ملغى.") : (d.State == "Submitted" ? "This weight proposal was already submitted; nothing was sent again." : "That request was cancelled."),
                ar ? "تم من قبل." : "Already done.", "weights-draft", JsonSerializer.Serialize(d, Json));
        if (Regex.IsMatch(t, @"\s(cancel|stop|never ?mind|الغ|الغي|الغاء|بلاش|وقف|توقف)\s?"))
            return new(ar ? "تم الإلغاء. لم يُرسل أي مقترح." : "Cancelled. No proposal was submitted.", ar ? "تمام، ألغيت." : "Okay, cancelled.", "weights-draft", JsonSerializer.Serialize(d with { State = "Cancelled" }, Json));
        var pending = await PendingWeights(user, ct);
        if (d.State == "Choosing")
        {
            var m = Regex.Match(AssistantIntentParser.Normalize(message), @"\d+");
            var n = m.Success ? int.Parse(m.Value) : 0;
            var pick = n >= 1 && n <= pending.Count ? pending[n - 1] : pending.FirstOrDefault(p => p.TenderId == n);
            if (pick.Title == null) return new(ar ? "اختر رقمًا من القائمة." : "Choose a number from the list.", ar ? "اختار رقم من القائمة." : "Choose a number from the list.", "weights-draft", JsonSerializer.Serialize(d, Json));
            return AskWeights(d with { TenderId = pick.TenderId, Title = pick.Title }, pick.Requirements);
        }
        if (d.State == "Asking")
        {
            var (fin, tech) = ParseWeights(message);
            var req = pending.FirstOrDefault(p => p.TenderId == d.TenderId).Requirements ?? "{}";
            if (fin is null || tech is null) return AskWeights(d, req, ar ? "ما فهمت الرقم." : "I didn't catch a number.");
            try { WeightedCalculation.WholeWeightProposal(fin.Value, tech.Value); }
            catch (WorkflowException e) { return AskWeights(d, req, (ar ? "القيم غير صالحة: " : "Invalid weights: ") + e.Message); }
            var c = d with { Financial = fin, Technical = tech, State = "Confirming" };
            return new(ar ? $"### تأكيد مقترح الأوزان\n- **المناقصة:** T-{d.TenderId:0000} – {d.Title}\n- **المالي:** {fin:0.##}%\n- **الفني:** {tech:0.##}%\n\nالمقترح نهائي ولا يمكن تعديله بعد الإرسال. قل **«أكّد»** للإرسال أو **«إلغاء»**."
                          : $"### Confirm weight proposal\n- **Tender:** T-{d.TenderId:0000} – {d.Title}\n- **Financial:** {fin:0.##}%\n- **Technical:** {tech:0.##}%\n\nThe proposal is final once submitted. Say **\"confirm\"** to submit or **\"cancel\"**.",
                ar ? $"المالي {fin:0.##} والفني {tech:0.##}. بتأكد الإرسال؟ قول أكّد أو إلغاء." : $"Financial {fin:0.##}, technical {tech:0.##}. Shall I submit? Say confirm or cancel.", "weights-confirm", JsonSerializer.Serialize(c, Json));
        }
        // Confirming
        if (!Regex.IsMatch(t, @"\s(confirm|confirmed|yes|submit|go ahead|نعم|اه|ايوه|اكد|اكيد|تاكيد|موافق|ارسل|ارسله|ابعت|ابعته)\s?"))
            return new(ar ? $"لم أرسل شيئًا بعد. المالي {d.Financial:0.##}% والفني {d.Technical:0.##}%. قل «أكّد» أو «إلغاء»." : $"Nothing has been submitted yet. Financial {d.Financial:0.##}%, technical {d.Technical:0.##}%. Say \"confirm\" or \"cancel\".",
                ar ? "قول أكّد أو إلغاء." : "Say confirm or cancel.", "weights-confirm", JsonSerializer.Serialize(d, Json));
        try
        {
            await workflow.Propose(d.TenderId!.Value, d.Financial!.Value, d.Technical!.Value, principal);
            Audit(user, "AiWeightProposalSubmitted", d.TenderId.ToString()!, new { d.DraftId, d.Financial, d.Technical });
            await db.SaveChangesAsync(ct);
            var url = TenderUrl(user, d.TenderId.Value, 0);
            return new(ar ? $"✅ تم إرسال مقترح الأوزان للمناقصة **T-{d.TenderId:0000}**: المالي {d.Financial:0.##}% والفني {d.Technical:0.##}%. لما يكتمل مقترح الأعضاء الأربعة، النظام بيحسب المتوسط تلقائيًا ويعتمده المسؤول.\n\n{url}"
                          : $"✅ Weight proposal submitted for **T-{d.TenderId:0000}**: financial {d.Financial:0.##}%, technical {d.Technical:0.##}%. When all four members have proposed, the system averages them automatically for approval.\n\n{url}",
                ar ? "تم إرسال أوزانك." : "Your weights were submitted.", "weights-draft", JsonSerializer.Serialize(d with { State = "Submitted" }, Json));
        }
        catch (WorkflowException e)
        {
            db.ChangeTracker.Clear();
            Audit(user, "AiActionRejected", d.TenderId.ToString()!, new { Action = "propose_weights", Reason = e.Message });
            await db.SaveChangesAsync(ct);
            return new((ar ? "لم يُرسل المقترح: " : "The proposal was not submitted: ") + e.Message, ar ? "ما انبعت المقترح، السبب بالشات." : "It was not submitted; the reason is in the chat.", "weights-draft", JsonSerializer.Serialize(d with { State = "Cancelled" }, Json));
        }
    }

    // ---------------- tender draft creation ----------------

    private sealed record Candidate(int Id, string Title, string Department, int DepartmentId, string Justification, string Items);

    private async Task<List<Candidate>> Candidates(CancellationToken ct) =>
        (await db.PurchaseRequests.AsNoTracking().Where(r => r.Status == PurchaseRequestStatus.Accepted && !r.Tenders.Any() && r.Items.Any())
            .OrderByDescending(r => r.Id).Take(10)
            .Select(r => new { r.Id, r.Title, Department = r.Department.Name, r.DepartmentId, r.Justification, Items = r.Items.OrderBy(i => i.Id).Select(i => new { i.Name, i.Quantity, i.Unit, i.Specifications }).ToList() })
            .ToListAsync(ct))
        .Select(r => new Candidate(r.Id, r.Title, r.Department, r.DepartmentId, r.Justification,
            string.Join("\n", r.Items.Select(i => $"- {i.Name}: {i.Quantity:0.##} {i.Unit}{(string.IsNullOrWhiteSpace(i.Specifications) ? "" : " — " + i.Specifications)}")))).ToList();

    // Eligible department representatives, same rule as the New Tender form: active, the full role set, not Admin/Supplier/Officer.
    private async Task<List<(string Id, string Name)>> Representatives(int departmentId)
    {
        var result = new List<(string, string)>();
        foreach (var person in (await users.GetUsersInRoleAsync(SystemRoles.DepartmentRepresentative)).Where(u => u.IsActive && u.DepartmentId == departmentId).OrderBy(u => u.FullName))
        {
            var roles = await users.GetRolesAsync(person);
            if (roles.Contains(SystemRoles.Admin) || roles.Contains(SystemRoles.Supplier) || roles.Contains(SystemRoles.SourcingOfficer) ||
                SystemRoles.AccountRoles(SystemRoles.DepartmentRepresentative).Any(r => !roles.Contains(r))) continue;
            result.Add((person.Id, string.IsNullOrWhiteSpace(person.FullName) ? person.Email ?? person.Id : person.FullName));
        }
        return result;
    }

    public async Task<ActionOutcome> StartTenderAsync(AssistantUser user, AssistantIntent intent, CancellationToken ct)
    {
        var ar = intent.Arabic;
        var actor = await db.Users.SingleAsync(u => u.Id == user.Id, ct);
        if (user.Role != SystemRoles.SourcingOfficer || !await workflow.IsOfficer(actor))
        {
            Audit(user, "AiActionRejected", user.Role, new { Action = "create_tender_draft", Reason = "Only the single active Sourcing Officer may create tenders" });
            await db.SaveChangesAsync(ct);
            return new(ar ? "إنشاء المناقصات متاح فقط لمسؤول التوريد النشط في النظام. دورك الحالي لا يسمح بإنشاء مناقصة، لكن يمكنني مساعدتك في عرض المناقصات المتاحة لك."
                          : "Only the system's single active Sourcing Officer can create tenders. Your role cannot create one, but I can help you view the tenders available to you.",
                ar ? "إنشاء المناقصات متاح لمسؤول التوريد فقط." : "Only the Sourcing Officer can create tenders.");
        }
        var draft = new TenderDraft
        {
            Arabic = ar, Title = Clean(intent.Title, 200),
            EstimatedValue = intent.Amount is > 0 ? intent.Amount : null, Currency = intent.Currency ?? "JOD"
        };
        var candidates = await Candidates(ct);
        if (candidates.Count == 0)
            return new(ar ? "لا يوجد طلب شراء مقبول بدون مناقصة. المناقصة في هذا النظام تبدأ دائمًا من طلب شراء مقبول، لذلك لم يتم إنشاء أي شيء." : "There is no accepted purchase request without a tender. In this system a tender always starts from an accepted purchase request, so nothing was created.",
                ar ? "ما في طلب شراء مقبول جاهز لمناقصة." : "There's no accepted purchase request ready for a tender.");
        if (candidates.Count == 1) draft = await ApplyRequest(draft, candidates[0], ct);
        return await Next(user, draft, ct, intro: candidates.Count == 1
            ? (ar ? $"تمام، رح نجهز مسودة مناقصة من طلب الشراء **PR-{candidates[0].Id:0000} – {candidates[0].Title}** ({candidates[0].Department}).\n\n" : $"Great — I'll prepare a tender draft from purchase request **PR-{candidates[0].Id:0000} – {candidates[0].Title}** ({candidates[0].Department}).\n\n")
            : "");
    }

    private async Task<TenderDraft> ApplyRequest(TenderDraft draft, Candidate pr, CancellationToken ct)
    {
        var reps = await Representatives(pr.DepartmentId);
        return draft with
        {
            PurchaseRequestId = pr.Id,
            Title = draft.Title ?? null,
            Description = pr.Justification.Length > 10000 ? pr.Justification[..10000] : pr.Justification,
            TechnicalRequirements = pr.Items.Length > 10000 ? pr.Items[..10000] : pr.Items,
            DepartmentRepresentativeId = reps.Count == 1 ? reps[0].Id : null
        };
    }

    // Continues the conversation from the last server-written draft. Returns null when the message is not about the draft.
    public async Task<ActionOutcome> ContinueTenderAsync(ClaimsPrincipal principal, AssistantUser user, TenderDraft draft, string message, CancellationToken ct)
    {
        var ar = draft.Arabic;
        var t = " " + AssistantIntentParser.Normalize(AssistantIntentParser.Strip(message)) + " ";
        if (draft.State == "Created")
            return new(ar ? $"هذه المسودة محفوظة مسبقًا باسم T-{draft.CreatedTenderId:0000}، ولم يتم إنشاء نسخة ثانية." : $"This draft is already saved as T-{draft.CreatedTenderId:0000}; no second tender was created.",
                ar ? "المسودة محفوظة من قبل." : "That draft is already saved.", "tender-draft", JsonSerializer.Serialize(draft, Json));
        if (draft.State == "Cancelled")
            return new(ar ? "هذه المسودة ملغاة. اطلب «أنشئ عطاء جديد» للبدء من جديد." : "That draft was cancelled. Say \"create a new tender\" to start again.", ar ? "المسودة ملغاة." : "That draft was cancelled.", "tender-draft", JsonSerializer.Serialize(draft, Json));
        if (Regex.IsMatch(t, @"\s(cancel|stop|abort|never ?mind|forget it|الغ|الغي|الغاء|بلاش|وقف|توقف|خلص لا)\s?"))
        {
            Audit(user, "AiTenderDraftCancelled", draft.DraftId.ToString(), new { draft.PurchaseRequestId });
            await db.SaveChangesAsync(ct);
            return new(ar ? "تم إلغاء المسودة. لم يتم إنشاء أي مناقصة." : "Draft cancelled. No tender was created.", ar ? "تمام، ألغيت المسودة." : "Okay, I cancelled the draft.",
                "tender-draft", JsonSerializer.Serialize(draft with { State = "Cancelled" }, Json));
        }
        if (draft.State == "Confirming")
        {
            if (Regex.IsMatch(t, @"\s(confirm|confirmed|yes,? create|create it|go ahead|save it|نعم|اه|ايوه|ايوا|اكد|اكيد|تاكيد|موافق|انشيها|انشيه|احفظ|احفظها|اعتمد)\s?"))
                return await CreateAsync(principal, user, draft, ct);
            return Summary(draft, await RepresentativeName(draft), ar ? "لم أحفظ شيئًا بعد. " : "Nothing has been saved yet. ");
        }
        var answer = message.Trim();
        string? problem = null;
        switch (draft.Asking)
        {
            case "request":
                var candidates = await Candidates(ct);
                var m = Regex.Match(AssistantIntentParser.Normalize(answer), @"(\d+)");
                // A small number is the list position ("2", "الثاني رقم 2"); a larger one is the PR number ("PR-0012").
                var pick = !m.Success ? null : int.Parse(m.Value) is var n && n >= 1 && n <= candidates.Count && !AssistantIntentParser.Normalize(answer).Contains("pr") ? candidates[n - 1] : candidates.FirstOrDefault(c => c.Id == n);
                if (pick == null) problem = ar ? "لم أتعرف على الطلب. اختر رقمًا من القائمة." : "I didn't recognize that request. Choose a number from the list.";
                else draft = await ApplyRequest(draft, pick, ct);
                break;
            case "title": draft = draft with { Title = Clean(answer, 200) }; if (draft.Title == null) problem = ar ? "اكتب اسمًا للمناقصة (حتى 200 حرف)." : "Give the tender a name (up to 200 characters)."; break;
            case "value":
                var (value, currency) = Money(answer);
                if (value is not > 0) problem = ar ? "اكتب القيمة التقديرية كرقم، مثل 50000 أو 50 ألف دينار." : "Give the estimated value as a number, e.g. 50000 or 50,000 JOD.";
                else draft = draft with { EstimatedValue = value, Currency = currency ?? draft.Currency };
                break;
            case "commercial": draft = draft with { CommercialConditions = Clean(answer, 10000) }; break;
            case "delivery": draft = draft with { DeliveryExpectations = Clean(answer, 4000) }; break;
            case "compliance": draft = draft with { MandatoryCompliance = Clean(answer, 4000) }; break;
            case "representative":
                var reps = draft.PurchaseRequestId is { } prId ? await Representatives((await db.PurchaseRequests.AsNoTracking().SingleAsync(r => r.Id == prId, ct)).DepartmentId) : [];
                var r = Regex.Match(AssistantIntentParser.Normalize(answer), @"(\d+)");
                var chosen = r.Success && int.Parse(r.Value) is var k && k >= 1 && k <= reps.Count ? reps[k - 1] : reps.FirstOrDefault(x => AssistantIntentParser.Normalize(x.Name).Contains(AssistantIntentParser.Normalize(answer).Trim()));
                if (chosen.Id == null) problem = ar ? "اختر رقم ممثل القسم من القائمة." : "Choose the department representative's number from the list.";
                else draft = draft with { DepartmentRepresentativeId = chosen.Id };
                break;
        }
        return await Next(user, draft, ct, problem: problem);
    }

    private async Task<ActionOutcome> Next(AssistantUser user, TenderDraft draft, CancellationToken ct, string intro = "", string? problem = null)
    {
        var ar = draft.Arabic;
        string asking, question, speech;
        if (draft.PurchaseRequestId == null)
        {
            var list = await Candidates(ct);
            asking = "request";
            question = (ar ? "من أي طلب شراء مقبول تريد إنشاء المناقصة؟ اختر رقمًا:\n" : "Which accepted purchase request should the tender start from? Choose a number:\n")
                + string.Join("\n", list.Select((c, i) => $"{i + 1}. PR-{c.Id:0000} – {c.Title} ({c.Department})"));
            speech = ar ? "من أي طلب شراء بدك المناقصة؟ اختار رقم من القائمة." : "Which purchase request should the tender start from? Choose a number from the list.";
        }
        else if (draft.Title == null) { asking = "title"; question = ar ? "تمام، شو اسم العطاء؟" : "Okay, what is the tender's name?"; speech = question; }
        else if (draft.EstimatedValue == null) { asking = "value"; question = ar ? "ممتاز. كم القيمة التقديرية للمشتريات؟ (مثال: 50 ألف دينار)" : "Great. What is the estimated procurement value? (e.g. 50,000 JOD)"; speech = ar ? "ممتاز. كم القيمة التقديرية؟" : "Great. What is the estimated value?"; }
        else if (draft.CommercialConditions == null) { asking = "commercial"; question = ar ? "ما الشروط التجارية؟ (مدة صلاحية الأسعار، شروط الدفع، الضرائب)" : "What are the commercial conditions? (price validity, payment terms, taxes)"; speech = ar ? "شو الشروط التجارية؟" : "What are the commercial conditions?"; }
        else if (draft.DeliveryExpectations == null) { asking = "delivery"; question = ar ? "ما متطلبات التسليم؟ (المدة والموقع)" : "What are the delivery expectations? (lead time and location)"; speech = ar ? "شو متطلبات التسليم؟" : "What are the delivery expectations?"; }
        else if (draft.MandatoryCompliance == null) { asking = "compliance"; question = ar ? "ما الوثائق الإلزامية التي يجب أن يرفقها كل مورد مع عرضه؟" : "Which mandatory documents must every offer include?"; speech = ar ? "شو الوثائق الإلزامية المطلوبة من الموردين؟" : "Which mandatory documents must suppliers include?"; }
        else if (draft.DepartmentRepresentativeId == null)
        {
            var departmentId = (await db.PurchaseRequests.AsNoTracking().SingleAsync(r => r.Id == draft.PurchaseRequestId, ct)).DepartmentId;
            var reps = await Representatives(departmentId);
            if (reps.Count == 0)
                return new(ar ? "لا يوجد ممثل قسم نشط مؤهل في القسم الطالب، لذلك لا يمكن إنشاء المناقصة. اطلب من مدير النظام تعيين ممثل للقسم." : "The requesting department has no eligible active Department Representative, so the tender cannot be created. Ask the Admin to assign one.",
                    ar ? "ما في ممثل قسم مؤهل، لازم مدير النظام يعيّن واحد." : "There's no eligible department representative; the Admin must assign one.", "tender-draft", JsonSerializer.Serialize(draft with { State = "Cancelled" }, Json));
            asking = "representative";
            question = (ar ? "اختر ممثل القسم في لجنة التقييم:\n" : "Choose the department representative for the evaluation committee:\n") + string.Join("\n", reps.Select((p, i) => $"{i + 1}. {p.Name}"));
            speech = ar ? "اختار ممثل القسم من القائمة." : "Choose the department representative from the list.";
        }
        else
        {
            var confirming = draft with { Asking = null, State = "Confirming" };
            return Summary(confirming, await RepresentativeName(confirming), intro);
        }
        var text = intro + (problem != null ? problem + "\n\n" : "") + question;
        return new(text, (problem != null ? problem + " " : "") + speech, "tender-draft", JsonSerializer.Serialize(draft with { Asking = asking, State = "Collecting" }, Json));
    }

    private async Task<string?> RepresentativeName(TenderDraft d) =>
        d.DepartmentRepresentativeId == null ? null : await db.Users.AsNoTracking().Where(u => u.Id == d.DepartmentRepresentativeId).Select(u => u.FullName).SingleOrDefaultAsync();

    private ActionOutcome Summary(TenderDraft d, string? representative, string intro = "")
    {
        var ar = d.Arabic;
        string Short(string? s) => s == null ? "—" : (s.Length > 220 ? s[..219] + "…" : s).Replace("\n", " · ");
        var lines = ar ? new[]
        {
            $"- **طلب الشراء:** PR-{d.PurchaseRequestId:0000}", $"- **الاسم:** {d.Title}", $"- **القيمة التقديرية:** {d.EstimatedValue:N2} {d.Currency}",
            $"- **الوصف:** {Short(d.Description)}", $"- **المتطلبات الفنية:** {Short(d.TechnicalRequirements)}", $"- **الشروط التجارية:** {Short(d.CommercialConditions)}",
            $"- **التسليم:** {Short(d.DeliveryExpectations)}", $"- **الوثائق الإلزامية:** {Short(d.MandatoryCompliance)}",
            $"- **لجنة التقييم:** اللجنة الدائمة (المدير، المالي، الفني) + ممثل القسم {representative}"
        } : new[]
        {
            $"- **Purchase request:** PR-{d.PurchaseRequestId:0000}", $"- **Name:** {d.Title}", $"- **Estimated value:** {d.EstimatedValue:N2} {d.Currency}",
            $"- **Description:** {Short(d.Description)}", $"- **Technical requirements:** {Short(d.TechnicalRequirements)}", $"- **Commercial conditions:** {Short(d.CommercialConditions)}",
            $"- **Delivery:** {Short(d.DeliveryExpectations)}", $"- **Mandatory compliance:** {Short(d.MandatoryCompliance)}",
            $"- **Evaluation committee:** standing committee (Manager, Financial, Technical) + department representative {representative}"
        };
        var text = intro + (ar ? "### ملخص مسودة المناقصة\n" : "### Tender draft summary\n") + string.Join("\n", lines)
            + (ar ? "\n\nستُحفظ كمسودة (Draft) فقط، ولن تُفتح للتقديم ولن تُرسل أي دعوات. قل **«أكّد»** للحفظ أو **«إلغاء»**." : "\n\nIt will be saved as a **Draft** only — not opened for bidding, no invitations sent. Say **\"confirm\"** to save or **\"cancel\"**.");
        var speech = ar ? $"هذا ملخص مسودة {d.Title} بقيمة {d.EstimatedValue:0.##} {d.Currency}. بدك أأكد الحفظ؟ قول أكّد أو إلغاء." : $"Here is the draft summary for {d.Title}, valued at {d.EstimatedValue:0.##} {d.Currency}. Should I save it? Say confirm or cancel.";
        return new(text, speech, "tender-confirm", JsonSerializer.Serialize(d with { State = "Confirming", Asking = null }, Json));
    }

    // Executes the confirmed draft through the existing TenderWorkflowService.Create (its validation, committee rules and transaction).
    private async Task<ActionOutcome> CreateAsync(ClaimsPrincipal principal, AssistantUser user, TenderDraft d, CancellationToken ct)
    {
        var ar = d.Arabic;
        if (d.CreatedTenderId is { } done) return Created(user, d, done, duplicate: true);
        var defaults = await workflow.StandingEvaluationDefaults();
        var input = new RequirementsInput
        {
            PurchaseRequestId = d.PurchaseRequestId ?? 0, Title = d.Title ?? "", Description = d.Description ?? "", TechnicalRequirements = d.TechnicalRequirements ?? "",
            CommercialConditions = d.CommercialConditions ?? "", DeliveryExpectations = d.DeliveryExpectations ?? "", MandatoryCompliance = d.MandatoryCompliance ?? "",
            EstimatedValue = d.EstimatedValue ?? 0, Currency = d.Currency, IsCritical = false,
            SourcingManagerId = defaults.GetValueOrDefault("evaluation-manager") ?? "", FinancialRepresentativeId = defaults.GetValueOrDefault("evaluation-financial") ?? "",
            TechnicalRepresentativeId = defaults.GetValueOrDefault("evaluation-technical-1") ?? "", DepartmentRepresentativeId = d.DepartmentRepresentativeId ?? ""
        };
        var errors = new List<System.ComponentModel.DataAnnotations.ValidationResult>();
        if (!System.ComponentModel.DataAnnotations.Validator.TryValidateObject(input, new(input), errors, true) || !Currencies.Contains(input.Currency))
            return Reject(user, d, string.Join(" ", errors.Select(e => e.ErrorMessage)).Trim() is { Length: > 0 } msg ? msg : "Invalid currency.");
        if (string.IsNullOrEmpty(input.SourcingManagerId) || string.IsNullOrEmpty(input.FinancialRepresentativeId) || string.IsNullOrEmpty(input.TechnicalRepresentativeId))
            return Reject(user, d, ar ? "اللجنة الدائمة للتقييم غير مكتملة. يجب على مدير النظام تعيينها أولًا." : "The standing evaluation committee is incomplete. The Admin must configure it first.");
        try
        {
            var id = await workflow.Create(input, principal);
            Audit(user, "AiTenderDraftCreated", id.ToString(), new { d.DraftId, d.PurchaseRequestId, TenderId = id });
            await db.SaveChangesAsync(ct);
            return Created(user, d with { CreatedTenderId = id, State = "Created" }, id, duplicate: false);
        }
        catch (WorkflowException e)
        {
            db.ChangeTracker.Clear();
            // A retried confirmation can lose the race to its first copy: that request now has the tender this officer just created.
            var existing = await db.Tenders.AsNoTracking().Where(x => x.PurchaseRequestId == input.PurchaseRequestId && x.CreatedByUserId == user.Id && x.Title == input.Title && x.CreatedAt > clock.GetUtcNow().UtcDateTime.AddMinutes(-30))
                .Select(x => (int?)x.Id).FirstOrDefaultAsync(ct);
            if (existing is { } same) return Created(user, d with { CreatedTenderId = same, State = "Created" }, same, duplicate: true);
            logger.LogInformation("AI tender draft rejected by the workflow: {Message}", e.Message);
            return Reject(user, d, e.Message);
        }
    }

    private ActionOutcome Created(AssistantUser user, TenderDraft d, int id, bool duplicate)
    {
        var ar = d.Arabic;
        var url = TenderUrl(user, id, d.PurchaseRequestId ?? 0);
        var text = ar
            ? $"✅ {(duplicate ? "المسودة محفوظة مسبقًا" : "تم حفظ مسودة المناقصة")}: **T-{id:0000} – {d.Title}** (الحالة: مسودة).\n\nالخطوة التالية في النظام: طلب مقترحات الأوزان من اللجنة. جارٍ فتح صفحة المناقصة {url}"
            : $"✅ {(duplicate ? "This draft was already saved" : "Tender draft saved")}: **T-{id:0000} – {d.Title}** (status: Draft).\n\nNext step in the workflow: request the committee's weight proposals. Opening {url}";
        var speech = ar ? $"تم. حفظت المسودة {d.Title} برقم {id}. رح أفتحلك صفحتها." : $"Done. I saved the draft {d.Title} as tender {id}. Opening it now.";
        return new(text, speech, "tender-created", JsonSerializer.Serialize(new { url, tenderId = id, draft = d with { CreatedTenderId = id, State = "Created" } }, Json), url);
    }

    private ActionOutcome Reject(AssistantUser user, TenderDraft d, string reason)
    {
        Audit(user, "AiActionRejected", d.DraftId.ToString(), new { Action = "create_tender_draft", d.PurchaseRequestId, Reason = reason });
        db.SaveChanges();
        var ar = d.Arabic;
        return new(ar ? $"لم يتم إنشاء المناقصة: {reason}\n\nلم يُحفظ أي شيء. يمكنك تعديل البيانات بالبدء من جديد أو استخدام نموذج «New Tender»." : $"The tender was not created: {reason}\n\nNothing was saved. You can start again or use the New Tender form.",
            ar ? "ما قدرت أنشئ المناقصة. شوف السبب بالشات." : "I couldn't create the tender. The reason is in the chat.", "tender-draft", JsonSerializer.Serialize(d with { State = "Cancelled" }, Json));
    }

    public static TenderDraft? ReadDraft(string? json)
    {
        if (json == null) return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            var element = doc.RootElement.TryGetProperty("draft", out var inner) ? inner : doc.RootElement;
            return element.TryGetProperty("draftId", out _) ? element.Deserialize<TenderDraft>(Json) : null;
        }
        catch (JsonException) { return null; }
    }

    private static string? Clean(string? s, int max)
    {
        s = s?.Trim();
        if (string.IsNullOrEmpty(s)) return null;
        s = new string(s.Where(c => !char.IsControl(c) || c == '\n').ToArray());
        return s.Length > max ? s[..max] : s;
    }

    // "50000", "50,000 JOD", "50 ألف دينار", "٥٠ الف", "50k usd".
    public static (decimal? Value, string? Currency) Money(string text)
    {
        var t = AssistantIntentParser.Normalize(text);
        var m = Regex.Match(t, @"(\d[\d,]*(?:\.\d+)?)\s*(k|الف|thousand|مليون|million|m\b)?");
        if (!m.Success || !decimal.TryParse(m.Groups[1].Value.Replace(",", ""), NumberStyles.Number, CultureInfo.InvariantCulture, out var v)) return (null, null);
        v *= m.Groups[2].Value switch { "k" or "الف" or "thousand" => 1000, "مليون" or "million" or "m" => 1_000_000, _ => 1 };
        var currency = Regex.Match(t, @"\b(jod|usd|eur|sar|aed|egp)\b|دينار|دولار|يورو|ريال|درهم") is { Success: true } c ? AssistantIntentParser.CurrencyCode(c.Value) : null;
        return (v is > 0 and < 1_000_000_000_000m ? decimal.Round(v, 2) : null, currency);
    }

    private void Audit(AssistantUser user, string action, string entityId, object values) =>
        db.AuditLogs.Add(new AuditLog { UserId = user.Id, EntityName = "AiAssistantAction", EntityId = entityId, Action = action, NewValues = JsonSerializer.Serialize(values, Json), CreatedAt = clock.GetUtcNow().UtcDateTime });
}
