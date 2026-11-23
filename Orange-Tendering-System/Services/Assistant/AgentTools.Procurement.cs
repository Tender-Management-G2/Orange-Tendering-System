using System.Globalization;
using System.Security.Claims;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Orange_Tendering_System.Models;
using Orange_Tendering_System.Models.Entities;
using Orange_Tendering_System.ViewModels;

namespace Orange_Tendering_System.Services.Assistant;

public sealed partial class AgentTools
{
    private static ToolInfo[] ProcurementRegistry =>
    [
        new("accept_request", "Accept purchase request", "قبول طلب الشراء", "update", SystemRoles.SourcingOfficer, "PurchaseRequestService.Decide", true, false),
        new("invite_suppliers", "Invite selected suppliers", "دعوة الموردين المحددين", "update", SystemRoles.SourcingOfficer, "TenderWorkflowService.Invite", true, false),
        new("schedule_evaluation", "Schedule evaluation meeting", "تحديد اجتماع التقييم", "update", SystemRoles.SourcingOfficer, "EvaluationSchedulingService.Schedule", true, false),
        new("forward_report", "Forward evaluation report to Manager", "إرسال تقرير التقييم للمانجر", "update", SystemRoles.SourcingOfficer, "TenderWorkflowService.Forward", true, false),
        new("accept_invitation", "Accept my invitation", "قبول دعوتي", "update", SystemRoles.Supplier, "TenderWorkflowService.Respond", true, false),
        new("prepare_offer", "Prepare my offer", "تجهيز عرضي", "update", SystemRoles.Supplier, "SupplierOfferService.SaveDraftAsync", true, true),
        new("attach_offer", "Attach a document to my offer", "إرفاق مستند بعرضي", "update", SystemRoles.Supplier, "SupplierOfferService.SaveDraftAsync", true, true),
        new("submit_offer", "Submit my offer", "إرسال عرضي", "update", SystemRoles.Supplier, "SupplierOfferService.SubmitOfferAsync", true, false)
    ];

    internal static string? ProcurementRole(string tool) => tool switch
    {
        "accept_request" or "invite_suppliers" or "schedule_evaluation" or "forward_report" => SystemRoles.SourcingOfficer,
        "accept_invitation" or "prepare_offer" or "attach_offer" or "submit_offer" => SystemRoles.Supplier,
        _ => null
    };

    private static string? DetectProcurement(string text)
    {
        if (Regex.IsMatch(text, @"(?:accept|approve).*(?:purchase request|\bpr\b)|(?:اقبل|اعتمد|وافق|مش مقبول|غير مقبول).*(?:طلب|ريكويست|ريموست)|(?:طلب|ريكويست|ريموست).*(?:مش مقبول|غير مقبول|بانتظار القبول)")) return "accept_request";
        if (Regex.IsMatch(text, @"\binvite\b.*supplier|(?:send|ابعث|ابعت|ارسل).*(?:دعوه|دعوات|انفتيشن|invitation)|ادع.*مورد")) return "invite_suppliers";
        if (Regex.IsMatch(text, @"\b(schedule|book)\b.*(?:meeting|evaluation)|(?:حدد|جدول|رتب|اعمل).*(?:اجتماع|ميتنق|ميتنج|ميتينج)")) return "schedule_evaluation";
        if (Regex.IsMatch(text, @"(?:forward|send).*\bmanager\b|(?:ابعث|ابعت|ارسل|حول).*(?:مانجر|مدير التوريد)")) return "forward_report";
        if (Regex.IsMatch(text, @"accept.*(?:my invitation|\binv-?\d)|اقبل.*(?:دعوتي|الدعوه)")) return "accept_invitation";
        if (Regex.IsMatch(text, @"\battach\b.*\boffer\b|(?:ارفق|ضيف).*?(?:مرفق|ملف|مستند).*?عرض")) return "attach_offer";
        if (Regex.IsMatch(text, @"(?:submit|send).*\b(?:my|the|this|draft)\b.*\boffer\b|(?:ابعث|ابعت|ارسل).*(?:عرضي|العرض)")) return "submit_offer";
        if (Regex.IsMatch(text, @"(?:prepare|draft|fill out).*\boffer\b|قدملي عليها|قدم لي عليها|(?:جهز|اعمل|عبي|قدملي|قدم لي).*(?:عرض|عطاء)")) return "prepare_offer";
        return null;
    }

    private static ToolSlot[] ProcurementSlots(string tool) => tool switch
    {
        "accept_request" => [new("id", "Which purchase request?", "أي طلب شراء بدك تقبل؟")],
        "invite_suppliers" => [new("id", "Which tender?", "أي عطاء؟"), new("supplierIds", "Which supplier numbers? Separate them with commas.", "أي موردين؟ اكتب أرقامهم وافصل بينهم بفاصلة.")],
        "schedule_evaluation" => [new("id", "Which tender?", "أي عطاء؟"), new("start", "Meeting start in Amman time (YYYY-MM-DD HH:mm).", "متى يبدأ الاجتماع بتوقيت عمّان؟ اكتب التاريخ والوقت مثل 2026-12-01 10:30."), new("duration", "How many minutes?", "كم مدة الاجتماع بالدقائق؟")],
        "forward_report" => [new("id", "Which signed report?", "تقرير أي عطاء بدك تبعث؟")],
        "attach_offer" => [new("id", "Which invitation number?", "شو رقم الدعوة؟"), new("fileNumber", "Which uploaded file number?", "أي ملف من اللي رفعتهم بالشات؟ اكتب رقمه."), new("documentType", "What type of offer document is it?", "شو نوع مستند العرض؟ اكتب الاسم أو الرمز.")],
        "accept_invitation" or "prepare_offer" or "submit_offer" => [new("id", "Which invitation number?", "شو رقم الدعوة؟"), .. (tool == "prepare_offer" ? OfferSlots : [])],
        _ => []
    };

    private static readonly ToolSlot[] OfferSlots =
    [
        new("currency", "Quote currency (JOD, USD or EUR)?", "بأي عملة رح تسعّر؟ JOD أو USD أو EUR."),
        new("lines", "Give each item as product | unit price | specifications. By voice, say “product Laptop X, price 100, specifications 16 GB”. Say “next item” between products.", "لكل صنف اكتب: اسم المنتج | سعر الوحدة | المواصفات. بالصوت احكي «المنتج لابتوب، السعر ١٠٠، المواصفات ذاكرة ١٦». وبين الأصناف احكي «الصنف التالي»."),
        new("delivery", "How many delivery days?", "كم يوم للتسليم؟"),
        new("payment", "What are the payment terms?", "شو شروط الدفع؟"),
        new("validity", "Offer valid until (YYYY-MM-DD)?", "لأي تاريخ العرض صالح؟ اكتب YYYY-MM-DD."),
        new("tax", "What is the total tax amount? Use 0 if exempt.", "كم إجمالي الضريبة؟ اكتب 0 إذا معفى.")
    ];

    private sealed record Candidate(int Id, string Title);
    private sealed record QuotedLine(string Product, decimal Price, string Specifications);

    private async Task<List<Candidate>> Candidates(string tool, AssistantUser user, int? only = null)
    {
        IQueryable<Candidate> candidates;
        if (tool == "accept_request")
            candidates = db.PurchaseRequests.Where(r => r.Status == PurchaseRequestStatus.Submitted && (only == null || r.Id == only)).OrderByDescending(r => r.Id)
                .Select(r => new Candidate(r.Id, r.Title));
        else if (user.Is(SystemRoles.Supplier))
        {
            var invitations = db.TenderInvitations.Where(i => (only == null || i.Id == only) && i.SupplierId == user.SupplierId && i.Supplier.Status == SupplierStatus.Approved
                && i.TenderRound.Status == TenderRoundStatus.Open && i.TenderRound.Deadline > workflow.Now);
            invitations = tool == "accept_invitation" ? invitations.Where(i => i.Status == InvitationStatus.Pending)
                : invitations.Where(i => i.Status == InvitationStatus.Accepted && !i.Bids.Any(b => b.Status != BidStatus.Draft));
            if (tool is "attach_offer" or "submit_offer") invitations = invitations.Where(i => i.Bids.Any(b => b.Status == BidStatus.Draft));
            candidates = invitations.OrderByDescending(i => i.Id).Select(i => new Candidate(i.Id, i.TenderRound.Tender.Title));
        }
        else
        {
            var tenders = db.Tenders.Where(t => only == null || t.Id == only);
            if (tool == "forward_report") tenders = tenders.Where(t => t.Status == TenderStatus.OfficerReview);
            else if (tool == "invite_suppliers") tenders = tenders.Where(t => t.Status == TenderStatus.Bidding
                && t.Rounds.Any(r => r.RoundNumber == t.Rounds.Max(rr => rr.RoundNumber) && r.Status == TenderRoundStatus.Open && r.Deadline > workflow.Now));
            else tenders = tenders.Where(t => t.PurchaseRequest.EvaluationMethod != TenderEvaluationMethod.LowestPrice
                && t.Rounds.Any(r => r.RoundNumber == t.Rounds.Max(rr => rr.RoundNumber) && r.Status == TenderRoundStatus.Closed)
                && db.Set<AiAnalysis>().Any(a => a.TenderRound.TenderId == t.Id && a.TenderRound.RoundNumber == t.Rounds.Max(r => r.RoundNumber)
                    && a.Status == "Completed" && a.ReviewedAt != null));
            candidates = tenders.OrderByDescending(t => t.Id).Select(t => new Candidate(t.Id, t.Title));
        }
        return await candidates.AsNoTracking().Take(12).ToListAsync();
    }

    private Task<List<Candidate>> Suppliers(int tenderId, int[]? selection = null)
    {
        var round = db.TenderRounds.Where(r => r.TenderId == tenderId).OrderByDescending(r => r.RoundNumber).Select(r => r.Id).Take(1);
        var query = db.Suppliers.Where(s => s.Status == SupplierStatus.Approved
            && db.Users.Any(u => u.SupplierId == s.Id && u.IsActive && u.EmailConfirmed)
            && !db.TenderInvitations.Any(i => round.Contains(i.TenderRoundId) && i.SupplierId == s.Id));
        if (selection != null) query = query.Where(s => selection.Contains(s.Id));
        return query.OrderBy(s => s.CompanyName).Select(s => new Candidate(s.Id, s.CompanyName)).Take(25).ToListAsync();
    }

    internal async Task<string> ProcurementQuestion(AgentStep step, AgentPlan plan, ToolSlot slot, AssistantUser user, bool ar)
    {
        var question = ar ? slot.Ar : slot.En;
        if (ProcurementRole(step.Tool) == null) return question;
        if (slot.Key == "id")
        {
            var choices = await Candidates(step.Tool, user);
            return choices.Count == 0 ? question + (ar ? " ما في سجلات جاهزة لهذا الإجراء حاليًا." : " No records are ready for this action.")
                : question + "\n" + FormatChoices(choices, Prefix(step.Tool));
        }
        if (slot.Key == "supplierIds")
            return question + "\n" + FormatChoices(await Suppliers(int.Parse(step.Values["id"])), "S-");
        if (slot.Key == "lines" && await supplierOffers.GetOfferFormAsync(user.Id, int.Parse(step.Values["id"])) is { } offer)
            return question + "\n" + string.Join("\n", offer.ItemContext.Select(i => $"- {i.Name} · {i.Quantity:0.###} {i.Unit}"));
        if (slot.Key == "fileNumber" && plan.ConversationId is int conversation)
        {
            var uploads = await chatFiles.ConversationUploadsAsync(user.Id, conversation, CancellationToken.None);
            return uploads.Count == 0
                ? ar ? "ارفع المستند بالمحادثة وبعدين احكي «ارفق الملف بعرضي»." : "Upload the document to this chat, then say “attach the file to my offer”."
                : question + "\n" + string.Join("\n", uploads.Select((f, i) => $"- {i + 1}. {f.FileName}"));
        }
        if (slot.Key == "documentType" && await supplierOffers.GetOfferFormAsync(user.Id, int.Parse(step.Values["id"])) is { } draft)
            return question + "\n" + string.Join("\n", draft.RequiredDocuments.Select(type => $"- {type} · {OfferDocumentTypes.Label(type)}"));
        return question;
    }

    private static string Prefix(string tool) => tool switch
    {
        "accept_request" => "PR-",
        "accept_invitation" or "prepare_offer" or "attach_offer" or "submit_offer" => "INV-",
        _ => "T-"
    };
    private static string FormatChoices(IEnumerable<Candidate> rows, string prefix) =>
        string.Join("\n", rows.Select(r => $"- {prefix}{r.Id:0000} · {r.Title}"));

    private async Task<Dictionary<string, string>> ExtractProcurement(string tool, string message, AssistantUser user)
    {
        var text = AssistantIntentParser.Normalize(message);
        var match = Regex.Match(text, $@"\b{Prefix(tool).TrimEnd('-')}\s*[-#]?\s*(\d+)\b", RegexOptions.IgnoreCase);
        var rows = match.Success ? [] : await Candidates(tool, user);
        var values = new Dictionary<string, string>();
        if (match.Success) values["id"] = match.Groups[1].Value;
        else if (rows.Count == 1) values["id"] = rows[0].Id.ToString();
        if (tool == "invite_suppliers")
        {
            var selected = Regex.Matches(text, @"\bS[-#]\s*(\d+)", RegexOptions.IgnoreCase).Select(m => m.Groups[1].Value).ToArray();
            if (selected.Length > 0) values["supplierIds"] = string.Join(",", selected);
        }
        return values;
    }

    private async Task<(string? Value, string? Problem)> ValidateProcurement(string tool, string key, string answer, AgentPlan plan, AssistantUser user, bool ar)
    {
        var text = AssistantIntentParser.Normalize(answer);
        int TenderOrInvitation() => int.TryParse(plan.Steps[plan.Current].Values.GetValueOrDefault("id"), out var target) ? target : 0;
        var valid = key switch
        {
            "id" => int.TryParse(Regex.Replace(text, @"^(PR|INV|T)\s*[-#]?\s*", "", RegexOptions.IgnoreCase), out var id)
                && (await Candidates(tool, user, id)).Count == 1 ? id.ToString() : null,
            "supplierIds" => await ValidateSuppliers(TenderOrInvitation(), text),
            "start" => DateTime.TryParseExact(text, ["yyyy-MM-dd HH:mm", "yyyy-MM-dd H:mm"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var start)
                && TimeZoneInfo.ConvertTimeToUtc(start, JordanTime.Zone) > workflow.Now ? start.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) : null,
            "duration" => int.TryParse(text, out var minutes) && minutes is >= 5 and <= 480 ? minutes.ToString() : null,
            "delivery" => int.TryParse(text, out var days) && days is >= 1 and <= 3650 ? days.ToString() : null,
            "payment" => answer.Length is >= 1 and <= 1000 ? answer : null,
            "validity" => DateTime.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _) ? text : null,
            "currency" => text switch
            {
                "jod" or "دينار" or "دينار اردني" => "JOD",
                "usd" or "دولار" or "دولار امريكي" => "USD",
                "eur" or "يورو" => "EUR",
                _ => null
            },
            "tax" => decimal.TryParse(text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var tax) && tax is >= 0 and <= 999_999_999 ? tax.ToString(CultureInfo.InvariantCulture) : null,
            "lines" => await ValidateOfferLines(TenderOrInvitation(), answer, user),
            "fileNumber" => await SelectUpload(plan, user, text),
            "documentType" => OfferDocumentTypes.All.FirstOrDefault(type =>
                AssistantIntentParser.Normalize(type.Key) == text || AssistantIntentParser.Normalize(type.Label) == text)?.Key,
            _ => null
        };
        return valid == null ? (null, ar ? "القيمة مش مطابقة للمطلوب. جرّب مرة ثانية." : "That value doesn't match the requested format or an available record.") : (valid, null);
    }

    private async Task<string?> SelectUpload(AgentPlan plan, AssistantUser user, string answer)
    {
        if (plan.ConversationId is not int conversation || !int.TryParse(answer, out var index) || index <= 0) return null;
        return (await chatFiles.ConversationUploadsAsync(user.Id, conversation, CancellationToken.None)).ElementAtOrDefault(index - 1)?.Id.ToString();
    }

    private async Task<string?> ValidateSuppliers(int tenderId, string text)
    {
        if (tenderId <= 0) return null;
        var parts = Regex.Split(text, @"\s*[,،]\s*");
        if (parts.Length > 25 || parts.Any(part => !Regex.IsMatch(part, @"^(?:s-)?[0-9]+$", RegexOptions.IgnoreCase))) return null;
        var ids = new List<int>();
        foreach (var part in parts)
        {
            if (!int.TryParse(Regex.Replace(part, "^s-", "", RegexOptions.IgnoreCase), out var id)) return null;
            if (!ids.Contains(id)) ids.Add(id);
        }
        return (await Suppliers(tenderId, ids.ToArray())).Count == ids.Count ? string.Join(",", ids) : null;
    }

    private async Task<string?> ValidateOfferLines(int invitationId, string answer, AssistantUser user)
    {
        var offer = await supplierOffers.GetOfferFormAsync(user.Id, invitationId);
        if (offer == null) return null;
        var rows = Regex.Split(answer, @"\n|(?:next item|الصنف التالي)\s*[:,،]?", RegexOptions.IgnoreCase)
            .Select(row => row.Trim(' ', ',', '،')).Where(row => row.Length > 0).ToArray();
        if (rows.Length != offer.ItemContext.Count) return null;
        var lines = new List<QuotedLine>();
        foreach (var row in rows)
        {
            var parts = row.Split('|', StringSplitOptions.TrimEntries);
            if (parts.Length == 1)
            {
                var spoken = Regex.Match(row, @"^(?:product|المنتج)\s*[: ]\s*(.+?)\s*[,، ]+\s*(?:price|السعر)\s*[: ]\s*([0-9٠-٩.]+)\s*[,، ]+\s*(?:specifications|المواصفات)\s*[: ]\s*(.+)$", RegexOptions.IgnoreCase);
                if (spoken.Success) parts = [spoken.Groups[1].Value.Trim(), spoken.Groups[2].Value, spoken.Groups[3].Value.Trim()];
            }
            if (parts.Length != 3 || parts[0].Length is < 1 or > 300 || parts[2].Length is < 1 or > 4000
                || !decimal.TryParse(AssistantIntentParser.Normalize(parts[1]), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var price)
                || price is <= 0m or > 999_999_999m) return null;
            lines.Add(new(parts[0], price, parts[2]));
        }
        return JsonSerializer.Serialize(lines, Json);
    }

    private async Task<string> DescribeProcurement(AgentStep step, AgentPlan plan, bool ar)
    {
        var v = step.Values;
        var id = int.Parse(v["id"]);
        var meta = Registry.First(t => t.Id == step.Tool);
        var title = step.Tool switch
        {
            "accept_request" => await db.PurchaseRequests.Where(r => r.Id == id).Select(r => r.Title).SingleAsync(),
            "accept_invitation" or "prepare_offer" or "attach_offer" or "submit_offer" => await db.TenderInvitations.Where(i => i.Id == id).Select(i => i.TenderRound.Tender.Title).SingleAsync(),
            _ => await db.Tenders.Where(t => t.Id == id).Select(t => t.Title).SingleAsync()
        };
        var detail = step.Tool switch
        {
            "invite_suppliers" => "\n" + FormatChoices(await Suppliers(id, v["supplierIds"].Split(',').Select(int.Parse).ToArray()), "S-"),
            "schedule_evaluation" => $"\n{v["start"]} · {v["duration"]} {(ar ? "دقيقة، بتوقيت عمّان" : "minutes, Amman time")}",
            "prepare_offer" => "\n" + string.Join("\n", JsonSerializer.Deserialize<QuotedLine[]>(v["lines"], Json)!.Select(line => $"- {line.Product} · {line.Price} {v["currency"]} · {line.Specifications}"))
                + $"\n{(ar ? "التسليم، الدفع، الصلاحية، الضريبة" : "Delivery days, payment, validity, tax")}: {v["delivery"]} · {v["payment"]} · {v["validity"]} · {v["tax"]}",
            "submit_offer" => await DescribeDraft(id, v, ar),
            "attach_offer" => "\n" + OfferDocumentTypes.Label(v["documentType"]) + " · " + await db.Set<AiChatFile>()
                .Where(f => f.Id == Guid.Parse(v["fileNumber"]) && f.ConversationId == plan.ConversationId).Select(f => f.FileName).SingleAsync(),
            _ => ""
        };
        v["title"] = $"{Prefix(step.Tool)}{id:0000} · {title}";
        if (detail.Length > 0) v["spokenDetails"] = detail.Replace("\n", ". ").Trim(' ', '.');
        return $"**{(ar ? meta.Ar : meta.En)}** · {Prefix(step.Tool)}{id:0000} · {title}{detail}";
    }

    private async Task<string> DescribeDraft(int invitationId, Dictionary<string, string> values, bool ar)
    {
        var bid = await db.Bids.AsNoTracking().Include(b => b.Items).ThenInclude(i => i.TenderItem).Include(b => b.Documents)
            .SingleAsync(b => b.TenderInvitationId == invitationId && b.Status == BidStatus.Draft);
        if (bid.Documents.Any(d => d.DocumentType == OfferDocumentTypes.BankGuarantee))
            bid.BankGuarantee = await db.BidBankGuarantees.AsNoTracking().SingleOrDefaultAsync(g => g.BidId == bid.Id);
        values["draftHash"] = SupplierOfferService.DraftFingerprint(bid);
        return "\n" + BidDetails(bid, ar);
    }

    private static string BidDetails(Bid bid, bool ar) =>
        string.Join("\n", bid.Items.OrderBy(i => i.TenderItemId).Select(i => $"- {i.TenderItem.Name} · {i.OfferedProduct} · {i.Quantity:0.###} × {i.UnitPrice:0.###} {bid.CurrencyCode}"))
        + $"\n{(ar ? "الإجمالي" : "Grand total")}: {OfferTotals.Of(bid).GrandTotal:0.###} {bid.CurrencyCode}"
        + $"\n{(ar ? "التسليم، الدفع، الصلاحية" : "Delivery days, payment, validity")}: {bid.DeliveryDays} · {bid.PaymentTerms} · {bid.ValidUntil:yyyy-MM-dd}"
        + $"\n{(ar ? "المرفقات" : "Attachments")}: {string.Join(", ", bid.Documents.Select(d => d.FileName).Order())}";

    private async Task<(bool Ok, string Text, string? Ref, string? Url)> ExecuteProcurement(
        AgentStep step, AgentPlan plan, AssistantUser user, ClaimsPrincipal principal, bool ar, CancellationToken ct)
    {
        var v = step.Values;
        var id = int.Parse(v["id"]);
        try
        {
            if ((await Candidates(step.Tool, user, id)).Count != 1)
                return (false, ar ? "تغيّرت حالة السجل. اطلب الإجراء من جديد." : "The record changed. Start a new request.", null, null);
            switch (step.Tool)
            {
                case "accept_request":
                    await requests.Decide(id, ReviewDecision.Accepted, "Confirmed by the Sourcing Officer in Orange AI.", principal);
                    return (true, ar ? "تم قبول طلب الشراء." : "Purchase request accepted.", id.ToString(), $"/SourcingOfficer/PurchaseRequests/Review/{id}");
                case "invite_suppliers":
                {
                    var ids = v["supplierIds"].Split(',').Select(int.Parse).ToArray();
                    await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
                    foreach (var supplier in ids) await workflow.Invite(id, supplier, principal);
                    await transaction.CommitAsync(ct);
                    return (true, ar ? $"تم إرسال {ids.Length} دعوة داخل النظام." : $"Sent {ids.Length} invitations in the system.", id.ToString(), $"/SourcingOfficer/Workflow?open={id}");
                }
                case "schedule_evaluation":
                {
                    var start = TimeZoneInfo.ConvertTimeToUtc(DateTime.ParseExact(v["start"], "yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture), JordanTime.Zone);
                    var aid = await db.Set<AiAnalysis>().Where(a => a.TenderRound.TenderId == id && a.Status == "Completed" && a.ReviewedAt != null
                        && a.TenderRound.RoundNumber == db.TenderRounds.Where(r => r.TenderId == id).Max(r => r.RoundNumber))
                        .OrderByDescending(a => a.Id).Select(a => a.Id).FirstAsync(ct);
                    await scheduling.Schedule(id, new SessionInput { AiAnalysisId = aid, StartsAt = new DateTimeOffset(start, TimeSpan.Zero),
                        EndsAt = new DateTimeOffset(start.AddMinutes(int.Parse(v["duration"])), TimeSpan.Zero) }, principal, ct);
                    return (true, ar ? "تم تحديد الاجتماع وإبلاغ اللجنة." : "Meeting scheduled and the committee notified.", id.ToString(), $"/SourcingOfficer/Workflow?open={id}");
                }
                case "forward_report":
                    await workflow.Forward(id, principal);
                    return (true, ar ? "تم إرسال التقرير للمانجر." : "Report sent to the Sourcing Manager.", id.ToString(), $"/SourcingOfficer/Workflow?open={id}");
                case "accept_invitation":
                {
                    var tender = await db.TenderInvitations.Where(i => i.Id == id && i.SupplierId == user.SupplierId).Select(i => i.TenderRound.TenderId).SingleAsync(ct);
                    await workflow.Respond(tender, id, true, principal);
                    return (true, ar ? "تم قبول الدعوة. احكي «جهز عرضي» لنعبيه." : "Invitation accepted. Say “prepare my offer” to fill it in.", id.ToString(), $"/Supplier/Offers/Create/{id}");
                }
                case "attach_offer":
                    return await AttachOffer(plan, user, id, v, ar, ct);
                default:
                    return await ApplyOffer(step, user, id, ar);
            }
        }
        catch (WorkflowException e) { return (false, e.Message, null, null); }
    }

    private async Task<(bool Ok, string Text, string? Ref, string? Url)> AttachOffer(
        AgentPlan plan, AssistantUser user, int invitationId, Dictionary<string, string> values, bool ar, CancellationToken ct)
    {
        var form = await supplierOffers.GetOfferFormAsync(user.Id, invitationId);
        if (form == null || plan.ConversationId is not int conversation)
            return (false, ar ? "المسودة غير متاحة." : "The draft is unavailable.", null, null);
        var (name, content) = await chatFiles.ReadConversationUploadAsync(user.Id, conversation, Guid.Parse(values["fileNumber"]), ct);
        var upload = SupplierOfferService.ReadUpload(values["documentType"], name, content);
        if (upload.File == null) return (false, upload.Error!, null, null);
        var result = await supplierOffers.SaveDraftAsync(user.Id, invitationId, form.Input, [upload.File]);
        return result.BidId is int bidId
            ? (true, ar ? "تم إرفاق المستند بمسودة العرض." : "Document attached to the offer draft.", bidId.ToString(), null)
            : (false, string.Join("\n", result.Errors), null, null);
    }

    private async Task<(bool Ok, string Text, string? Ref, string? Url)> ApplyOffer(AgentStep step, AssistantUser user, int invitationId, bool ar)
    {
        var form = await supplierOffers.GetOfferFormAsync(user.Id, invitationId);
        if (form == null) return (false, ar ? "العرض غير متاح." : "The offer is unavailable.", null, null);
        var input = form.Input;
        var v = step.Values;
        var submit = step.Tool == "submit_offer";
        if (!submit)
        {
            var lines = JsonSerializer.Deserialize<QuotedLine[]>(v["lines"], Json)!;
            if (lines.Length != input.Items.Count) return (false, ar ? "تغيّرت الأصناف." : "The tender items changed.", null, null);
            for (var index = 0; index < lines.Length; index++)
            {
                input.Items[index].OfferedProduct = lines[index].Product;
                input.Items[index].UnitPrice = lines[index].Price;
                input.Items[index].OfferedSpecifications = lines[index].Specifications;
            }
            input.CurrencyCode = v["currency"];
            if (form.RequiredCurrency != null && form.RequiredCurrency != input.CurrencyCode)
                return (false, ar ? $"عملة العطاء {form.RequiredCurrency}." : $"The tender requires {form.RequiredCurrency}.", null, null);
            input.DeliveryDays = int.Parse(v["delivery"]);
            input.PaymentTerms = v["payment"];
            input.ValidUntil = DateTime.ParseExact(v["validity"], "yyyy-MM-dd", CultureInfo.InvariantCulture);
            input.TaxMode = "Amount";
            input.TaxAmount = decimal.Parse(v["tax"], CultureInfo.InvariantCulture);
        }
        var result = submit ? await supplierOffers.SubmitOfferAsync(user.Id, invitationId, input, [], v["draftHash"])
            : await supplierOffers.SaveDraftAsync(user.Id, invitationId, input, []);
        if (result.BidId is not int bidId) return (false, string.Join("\n", result.Errors), null, $"/Supplier/Offers/Create/{invitationId}");
        return submit
            ? (true, ar ? "تم إرسال العرض." : "Offer submitted.", bidId.ToString(), $"/Supplier/Offers/Details/{bidId}")
            : (true, ar ? "تم حفظ المسودة. احكي «أرسل عرضي» لمراجعتها وتأكيد الإرسال." : "Draft saved. Say “submit my offer” to review and confirm submission.", bidId.ToString(), $"/Supplier/Offers/Create/{invitationId}");
    }
}
