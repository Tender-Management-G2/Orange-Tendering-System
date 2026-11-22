using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Orange_Tendering_System.Services.Radar;

// The action contract: what the user asked for, as data. It is produced by the rule parser below or by the local model through
// a JSON schema (RadarIntentRouter), carried across turns by the page, and always re-validated here: enums are checked and every
// supplier/tender reference is resolved against the caller's authorized snapshot. Models only classify; they never supply rows,
// totals or ids that are used unchecked, and nothing they write is executed as SQL or script.
//   Kind: report | navigate | question | unsupported | unclear
//   Supplier/SecondSupplier/Tender: the words the user used ("A21", "#2008", "servers"), or "selected".
public sealed record RadarIntent(string Kind, string? Format = null, string? Report = null, string? Destination = null,
    string? Supplier = null, string? SecondSupplier = null, string? Tender = null, int Months = 0, string? Category = null, bool AlsoAnswer = false);

public sealed record RadarNavigation(string Destination, int TenderId, int SupplierId, int RoundId, string? Category,
    int SecondSupplierId = 0, TenderOfferComparison? Comparison = null);
// Exactly one of Export, Navigation, Question (a focused clarification) or Refusal is set; Pending is the partly understood
// intent the page sends back with the user's answer to Question.
public sealed record RadarAction(RadarIntent Intent, RadarExportRequest? Export = null, RadarNavigation? Navigation = null, string? Question = null,
    string? Refusal = null, RadarIntent? Pending = null);

public static class RadarIntents
{
    public static readonly string[] Kinds = ["report", "navigate", "question", "unsupported", "unclear"];
    public static readonly string[] Formats = ["xlsx", "docx", "pdf"];
    public static readonly Dictionary<string, string> Reports = new()
    {
        ["suppliers"] = "all authorized suppliers: identity, category, status and participation metrics",
        ["supplier_comparison"] = "comparison of suppliers' participation, awards and relative prices",
        ["supplier_participation"] = "which suppliers were invited to / bid in which tenders and bidding cycles",
        ["market_overview"] = "supplier market overview: market metrics, categories, suppliers and rounds",
        ["supplier_profile"] = "one specific supplier's record, participation and offers",
        ["tenders"] = "all authorized tenders with status, rounds and participation",
        ["tender_summary"] = "one specific tender: lifecycle and offers",
        ["tender_lifecycle"] = "one specific tender's recorded workflow lifecycle",
        ["tender_offers"] = "financial offers submitted in one specific tender",
        ["supplier_offers"] = "financial offers across all authorized tenders",
        ["price_patterns"] = "relative price gaps between suppliers in shared closed rounds",
        ["submission_timing"] = "submission times of suppliers in shared closed rounds",
        ["conversation"] = "PDF intelligence report of this conversation and analysis"
    };
    public static readonly Dictionary<string, string> Destinations = new()
    {
        ["supplier_market"] = "Supplier Universe (the supplier market visualization)",
        ["categories"] = "Supplier Universe in Category mode",
        ["competition"] = "Supplier Universe in Competition mode (optionally one supplier's competitors)",
        ["tender_network"] = "Supplier Universe in Tender mode",
        ["supplier"] = "one specific supplier in the Supplier Universe",
        ["tenders"] = "the Tenders list",
        ["tender"] = "one specific tender",
        ["tender_lifecycle"] = "one specific tender's lifecycle",
        ["tender_offers"] = "one specific tender's financial offers",
        ["supplier_offer_comparison"] = "compare two suppliers' financial offers and submission times in a shared tender",
        ["price_patterns"] = "Price Patterns",
        ["submission_timing"] = "Submission Timing",
        ["overview"] = "the Overview dashboard"
    };
    public static readonly HashSet<string> TenderReports = ["tender_summary", "tender_lifecycle", "tender_offers"];
    public static readonly HashSet<string> TenderDestinations = ["tender", "tender_lifecycle", "tender_offers"];

    // ---------- rule parser ----------
    private static readonly RegexOptions I = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;
    private static bool Has(string text, string pattern) => Regex.IsMatch(text, pattern, I);
    private const string Xlsx = @"\b(excel|xlsx|xls|spreadsheets?|workbook|sheet)\b|اكسل|اكسيل|شيت|جدول بيانات";
    private const string Docx = @"\b(word|docx|ms ?word)\b|وورد|\bورد\b";
    private const string Pdf = @"\bpdf\b|بي ?دي ?اف|بى ?دى ?اف";
    private const string FileWord = @"\b(report|file|document|download|export)\b|تقرير|ملف|مستند|صدر|تصدير|نزل|حمل";
    private const string MakeVerb = @"\b(generate|create|make|export|download|prepare|produce|build|give|send|get|want|need|put|save|draft)\b|انشي|اعمل|سوي|حضر|جهز|صدر|اعطني|عطني|بدي|اريد|ابغي|ابي|نزل|حمل|اكتب";
    private const string NavVerb = @"\b(show|open|display|go to|take me|bring up|focus|navigate|view|see|zoom|highlight|switch to|where is)\b|اعرض|ارني|افتح|وريني|خذني|انتقل|اظهر|ركز";
    private const string Explain = @"\b(why|how|what|which|who|explain|analy[sz]e|tell me|describe|summari[sz]e|insight)\b|لماذا|ليش|كيف|ماذا|ما هو|ما هي|من هو|اشرح|حلل|وضح|فسر";
    private const string General = @"\b(all|every|each|whole|entire|overall|complete|full)\b|\bكل\b|جميع|كافه";
    private const string SupplierWord = @"\b(suppliers?|vendors?|companies|bidders?)\b|مورد|موردين|الموردين|شركات";
    private const string TenderWord = @"\b(tenders?|bidding|bids?|rounds?|cycles?|rfqs?|rfps?)\b|مناقص|عطاء|عطاءات|جوله|جولات";
    private const string Participation = @"\bparticipat\w*|\b(joined|took part|take part|involved|bid on|bidded|invited|invitations?|engagement)\b|مشارك|شارك|دعو|دعوات";
    private const string Comparison = @"\bcompar\w*|\b(versus|vs|against|side by side|rank\w*|benchmark\w*)\b|قارن|مقارن|ترتيب";
    private const string Market = @"\b(market|overview|landscape|summary|snapshot|universe)\b|سوق|نظره عامه|ملخص|عالم الموردين";
    private const string Lifecycle = @"\blife ?cycle\b|\b(timeline|stages|journey|workflow)\b|دوره حياه|مراحل|الجدول الزمني";
    private const string Offers = @"\b(offers?|financial|quotations?|quotes?|bid prices?|pricing)\b|عرض|عروض|مالي|اسعار العروض";
    private const string PricePattern = @"\bpric(e|ing) (patterns?|gaps?|behaviou?r)\b|\bprice patterns?\b|انماط الاسعار|نمط الاسعار|فارق السعر";
    private const string Timing = @"\btiming\b|\bsubmission times?\b|\bwhen .* submitted\b|توقيت|وقت التقديم";
    private const string Categories = @"\bcategor(y|ies)\b|\bsegments?\b|فئه|فئات|تصنيف";
    private const string Competition = @"\bcompet\w*|\brivals?\b|منافس|المنافسه";
    private const string Unsupported = @"\b(invoices?|payments?|salar(y|ies)|payroll|employees?|staff|purchase orders?|inventory|stock levels?)\b|فاتوره|فواتير|رواتب|موظف|مخزون";
    private const string ThisTender = @"\b(this|selected|current|that|same) (tender|rfq|rfp)\b|هذه المناقصه|المناقصه المحدده|هذا العطاء|العطاء المحدد";
    private const string ThisSupplier = @"\b(this|selected|current|that|same|these|those) (suppliers?|vendors?|companies)\b|هذا المورد|المورد المحدد|هؤلاء الموردين";

    // Arabic letter variants and diacritics are folded so spoken and typed spellings match the same patterns.
    public static string Fold(string text)
    {
        var s = Regex.Replace(text.ToLowerInvariant(), "[ً-ْٰـ]", "");
        s = s.Replace('أ', 'ا').Replace('إ', 'ا').Replace('آ', 'ا').Replace('ى', 'ي').Replace('ة', 'ه').Replace('ؤ', 'و').Replace('ئ', 'ي');
        return Regex.Replace(s, @"\s+", " ").Trim();
    }

    // Deterministic reading of the request. Returns null when the message is a normal question (no file or navigation signal).
    // A partial understanding comes back as Kind "unclear" with what was recognised filled in, so the model router or a focused
    // question can complete it.
    public static RadarIntent? Parse(string message, RadarSnapshot snapshot, RadarIntent? pending = null, string? lastReport = null)
    {
        var t = Fold(message);
        var format = Has(t, Xlsx) ? "xlsx" : Has(t, Docx) ? "docx" : Has(t, Pdf) ? "pdf" : null;
        var conversation = Has(t, @"\b(discussed|conversation|we talked|this chat|our chat)\b|المحادثه|ناقشنا|تحدثنا");
        var wantsFile = format != null ? Has(t, MakeVerb) || Has(t, FileWord) || t.Split(' ').Length <= 3 : Has(t, MakeVerb) && Has(t, @"\breport\b|تقرير");
        var supplier = SupplierRef(message, snapshot); var tender = TenderRef(message, snapshot);
        var months = Months(t);
        var category = CategoryRef(message, snapshot);

        // A short answer to the previous clarification ("PDF", "all suppliers", "servers") completes the pending intent. A message
        // that adds nothing to it (a new question) is read on its own, so a pending question never swallows the conversation.
        var answers = format != null || supplier != null || tender != null || Has(t, General) || ReportFor(t, supplier, tender) != null
            || NameIn(t, snapshot.Tenders.Select(x => x.Title).Concat(snapshot.Suppliers.Select(s => s.Name)), answer: true) != null;
        if (pending is { Kind: "unclear" } && answers && t.Split(' ').Length <= 8 && !Has(t, NavVerb) && !Has(t, Explain))
        {
            var c = pending with
            {
                Format = format ?? pending.Format, Months = months > 0 ? months : pending.Months,
                Supplier = pending.Destination == "supplier_offer_comparison" && pending.Supplier != null ? pending.Supplier
                    : supplier ?? pending.Supplier ?? (pending.Report is "supplier_profile" || pending.Destination is "supplier" ? NameIn(t, snapshot.Suppliers.Select(s => s.Name), answer: true) : null),
                SecondSupplier = pending.Destination == "supplier_offer_comparison"
                    ? pending.SecondSupplier ?? (pending.Supplier != null ? supplier : SecondSupplierRef(message, supplier, snapshot))
                    : pending.SecondSupplier,
                Tender = tender ?? pending.Tender ?? (pending.Report is { } r && TenderReports.Contains(r) || pending.Destination is { } d && (TenderDestinations.Contains(d) || d == "supplier_offer_comparison") ? NameIn(t, snapshot.Tenders.Select(x => x.Title), answer: true) : null)
            };
            if (c.Destination == null && c.Report == null) c = c with { Report = ReportFor(t, c.Supplier, c.Tender) ?? (Has(t, General) ? "suppliers" : null) };
            // Still missing the record the request is about: leave it unclear, so the model router can read the answer.
            var needsTender = (c.Report is { } nr && TenderReports.Contains(nr) || c.Destination is { } nd && TenderDestinations.Contains(nd)) && c.Tender == null;
            var needsSupplier = (c.Report == "supplier_profile" || c.Destination == "supplier") && c.Supplier == null;
            return c with { Kind = needsTender || needsSupplier ? "unclear" : c.Destination != null ? "navigate" : c.Report != null && c.Format != null ? "report" : "unclear" };
        }

        if (wantsFile && conversation) return new("report", format ?? "pdf", "conversation");
        if (wantsFile)
        {
            if (Has(t, Unsupported) && !Has(t, SupplierWord + "|" + TenderWord + "|" + Market)) return new("unsupported", format);
            var report = ReportFor(t, supplier, tender) ?? (IsGeneric(t) && supplier == null && tender == null && lastReport != null && Reports.ContainsKey(lastReport) ? lastReport : null);
            return new(report != null && format != null ? "report" : "unclear", format, report, Supplier: supplier, SecondSupplier: SecondSupplierRef(message, supplier, snapshot), Tender: tender, Months: months);
        }
        var secondSupplier = SecondSupplierRef(message, supplier, snapshot);
        if (Has(t, @"\bsupplier offer comparison\b|مقارنه عروض الموردين") ||
            secondSupplier != null && Has(t, Comparison + "|" + Timing + "|" + PricePattern) ||
            Has(t, Comparison) && supplier != null && tender != null && Has(t, Offers))
        {
            tender ??= Capture(message, @"(?:\b(?:in|for)\s+(?:the\s+)?tender|في\s+(?:عطاء|مناقص[ةه]))\s+([^,.;?!؟]{1,60})");
            return new("navigate", Destination: "supplier_offer_comparison", Supplier: supplier, SecondSupplier: secondSupplier, Tender: tender);
        }
        if (Has(t, NavVerb))
        {
            var destination = DestinationFor(t, supplier, tender, category);
            if (destination == null) return null;
            return new("navigate", Destination: destination, Supplier: supplier, Tender: tender, Months: months, Category: destination == "categories" ? category : null,
                AlsoAnswer: !PureNavigation(t, supplier, tender, category));
        }
        return null;
    }

    // "Show me the suppliers market" only moves the page; "show A21 and explain its prices" also needs an analysed answer.
    private static bool PureNavigation(string t, string? supplier, string? tender, string? category)
    {
        var rest = t;
        foreach (var r in new[] { supplier, tender, category }.Where(r => r is { Length: > 0 } && r != "selected")) rest = rest.Replace(Fold(r!), " ");
        if (Has(rest, Explain)) return false;
        return Regex.Replace(rest, @"[^\p{L}\p{N} ]", " ").Split(' ', StringSplitOptions.RemoveEmptyEntries).All(w => Regex.IsMatch(w,
            @"^(please|can|could|would|you|i|me|us|let|lets|let's|a|an|the|to|of|for|in|on|and|now|right|again|our|my|its|his|their|with|all|every|'s|s|show|open|display|go|take|bring|up|focus|navigate|view|see|zoom|highlight|switch|where|is|supplier|suppliers|vendors?|market|universe|categories|category|competition|competitors?|competing|mode|network|tenders?|lifecycle|life|cycle|offers?|financial|price|pricing|patterns?|submission|timing|analysis|overview|dashboard|section|page|visuali[sz]ation|chart|graph|tab|selected|this|that|current|map|relationships?)$" +
            @"|^(اعرض|ارني|افتح|وريني|خذني|انتقل|اظهر|ركز|لي|على|الى|في|سوق|عالم|الموردين|المورد|موردين|فئات|الفئات|المنافسه|منافسي|المنافسين|المناقصات|المناقصه|دوره|حياه|العروض|الماليه|انماط|الاسعار|توقيت|التقديم|تحليل|لوحه|من|فضلك)$"));
    }

    // The model router may complete an unclear request only when there is something to read: subject words the rules could not
    // classify, or an answer to a pending question. A bare "Generate an Excel sheet" gets the focused question, not a model guess.
    public static bool NeedsModel(string message, RadarIntent? intent, RadarIntent? pending) => intent is { Kind: "unclear" } && (pending != null || !IsGeneric(Fold(message)));

    // A file request that names no subject ("Generate an Excel sheet") refers to what was just shown, if anything.
    private static bool IsGeneric(string t) => !Has(t, string.Join("|", SupplierWord, TenderWord, Participation, Comparison, Market, Lifecycle, Offers, PricePattern, Timing, Categories, Competition, Unsupported));

    private static string? ReportFor(string t, string? supplier, string? tender)
    {
        var specificTender = tender != null;
        if (Has(t, Lifecycle)) return "tender_lifecycle";
        if (Has(t, Timing)) return "submission_timing";
        if (Has(t, PricePattern)) return "price_patterns";
        if (Has(t, Offers) && !Has(t, Participation)) return specificTender ? "tender_offers" : "supplier_offers";
        var suppliers = Has(t, SupplierWord) || supplier != null; var tenders = Has(t, TenderWord);
        if (Has(t, Participation)) return "supplier_participation";
        if (Has(t, Comparison) && suppliers) return "supplier_comparison";
        if (suppliers && tenders && !specificTender) return "supplier_participation";
        if (Has(t, Market)) return "market_overview";
        if (Has(t, Categories)) return "suppliers";
        if (supplier != null && supplier != "selected" || supplier == "selected" && !tenders) return "supplier_profile";
        if (specificTender) return "tender_summary";
        if (tenders) return "tenders";
        if (suppliers) return "suppliers";
        return null;
    }

    private static string? DestinationFor(string t, string? supplier, string? tender, string? category)
    {
        if (Has(t, Timing)) return "submission_timing";
        if (Has(t, PricePattern)) return "price_patterns";
        if (Has(t, Lifecycle)) return "tender_lifecycle";
        if (Has(t, Offers) && tender != null) return "tender_offers";
        if (Has(t, Competition)) return "competition";
        if (Has(t, Categories)) return "categories";
        if (Has(t, @"\btender (network|mode|view)\b")) return "tender_network";
        if (Has(t, Market)) return "supplier_market";
        if (supplier != null) return "supplier";
        if (tender != null) return "tender";
        if (category != null) return "categories";
        if (Has(t, @"\b(overview|dashboard|kpis?)\b|لوحه|نظره عامه")) return "overview";
        if (Has(t, TenderWord)) return "tenders";
        if (Has(t, SupplierWord)) return "supplier_market";
        return null;
    }

    private static int Months(string t)
    {
        var words = new Dictionary<string, int> { ["one"] = 1, ["two"] = 2, ["three"] = 3, ["four"] = 4, ["five"] = 5, ["six"] = 6, ["seven"] = 7, ["eight"] = 8, ["nine"] = 9, ["ten"] = 10, ["eleven"] = 11, ["twelve"] = 12 };
        var m = Regex.Match(t, @"\b(?:past|last|previous)\s+(\d{1,2}|one|two|three|four|five|six|seven|eight|nine|ten|eleven|twelve)\s+months?\b");
        if (m.Success) return int.TryParse(m.Groups[1].Value, out var n) ? n : words[m.Groups[1].Value];
        if (Has(t, @"\b(past|last|previous) (month)\b|الشهر الماضي")) return 1;
        if (Has(t, @"\b(past|last|previous) (year|twelve months)\b|السنه الماضيه|العام الماضي")) return 12;
        var ar = Regex.Match(t, @"(?:اخر|الاشهر|الشهور)\s*(?:ال)?(ثلاثه|ثلاث|سته|ست|اثني عشر)");
        if (ar.Success) return ar.Groups[1].Value.StartsWith("ثلاث") ? 3 : ar.Groups[1].Value.StartsWith("ست") ? 6 : 12;
        if (Has(t, @"الاشهر الثلاثه")) return 3;
        return 0;
    }

    // References are kept as the user said them; ResolveSupplier/ResolveTender decide what they mean in the authorized data.
    // A name is taken when it matches an authorized record, or when it is clearly a proper name (capitalised or quoted), so a
    // missing record is reported instead of silently replaced; ordinary words after "supplier"/"tender" are never names.
    private const string Stop = @"^(and|or|the|of|with|in|on|to|for|from|by|as|at|is|was|that|which|please|now|this|these|those|its|their|a|an|market|categor\w*|compet\w*|universe|list|report|participation|names?|comparison|comparisons|data|details|info|information|performance|metrics|ranking|directory|overview|offers?|prices?|pricing|activity|status|profiles?|network|relationships?|lifecycle|mode|view|records?|cycles?|rounds?|summary|analysis|timing|patterns?)$";
    private static string? Capture(string message, string pattern)
    {
        var m = Regex.Match(message, pattern, I); if (!m.Success) return null;
        var words = m.Groups[1].Value.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(w => w.TrimEnd('.', ',', '?', '!', ':', ';')).TakeWhile(w => w.Length > 0 && !Regex.IsMatch(w, Stop, I)).ToList();
        return words.Count == 0 ? null : string.Join(' ', words);
    }
    private static bool ProperName(string s) => char.IsUpper(s[0]) || char.IsDigit(s[0]);
    // A full name in the message; for a short answer to "which one?", a single distinctive word that matches exactly one record.
    private static string? NameIn(string folded, IEnumerable<string> names, bool answer = false)
    {
        var keyed = names.Select(n => (Name: n, Key: Fold(n.Replace("RADAR DEMO ", "", StringComparison.OrdinalIgnoreCase)))).ToList();
        var full = keyed.Where(n => n.Key.Length >= 4 && folded.Contains(n.Key)).OrderByDescending(n => n.Key.Length).Select(n => n.Name).FirstOrDefault();
        if (full != null || !answer) return full;
        var hits = folded.Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(w => w.Length >= 4 && !Regex.IsMatch(w, Stop + @"|^(tender|supplier|one|that|please|called|named)$", I))
            .SelectMany(w => keyed.Where(n => Regex.IsMatch(n.Key, $@"(^|\W){Regex.Escape(w)}(\W|$)"))).Select(n => n.Name).Distinct().ToList();
        return hits.Count == 1 ? hits[0] : null;
    }

    private static string? SupplierRef(string message, RadarSnapshot snapshot)
    {
        var code = Regex.Match(message, @"\b([A-Z]\d{1,4})(?:'s)?\b", I);
        if (code.Success && !Regex.IsMatch(code.Groups[1].Value, @"^[Qq][1-4]$")) return code.Groups[1].Value.ToUpperInvariant();
        if (NameIn(Fold(message), snapshot.Suppliers.Select(s => s.Name)) is { } full) return full;
        if (Capture(message, @"\b(?:supplier|vendor|company)\s+([\p{L}\p{N}][^,.;?!]{0,60})") is { } named && (ResolveSupplier(named, snapshot) != null || ProperName(named))) return named;
        return Has(Fold(message), ThisSupplier) ? "selected" : null;
    }
    private static string? SecondSupplierRef(string message, string? first, RadarSnapshot snapshot)
    {
        var codes = Regex.Matches(message, @"\b([A-Z]\d{1,4})\b", I).Select(m => m.Groups[1].Value.ToUpperInvariant()).Distinct().ToList();
        if (codes.Count >= 2 && first == codes[0]) return codes[1];
        var folded = Fold(message);
        return snapshot.Suppliers.Select(s => s.Name).Where(n => n != first && Fold(n.Replace("RADAR DEMO ", "", StringComparison.OrdinalIgnoreCase)) is { Length: >= 4 } k && folded.Contains(k)).FirstOrDefault();
    }
    private static string? TenderRef(string message, RadarSnapshot snapshot)
    {
        var id = Regex.Match(message, @"(?:#|\b(?:tender|rfq|rfp)\s+(?:id|number|no\.?)?\s*#?)\s*(\d{1,7})\b", I);
        if (id.Success) return "#" + id.Groups[1].Value;
        var quoted = Regex.Match(message, @"\btender\s+(?:called|named|titled)?\s*[""“']([^""”']{2,80})[""”']", I);
        if (quoted.Success) return quoted.Groups[1].Value.Trim();
        var folded = Fold(message);
        if (Has(folded, ThisTender)) return "selected";
        if (NameIn(folded, snapshot.Tenders.Select(x => x.Title)) is { } full) return full;
        var before = Regex.Match(message, @"\b([\p{L}\p{N}][\p{L}\p{N}\-]*(?:\s+[\p{L}\p{N}][\p{L}\p{N}\-]*){0,2})\s+tender\b", I);
        if (before.Success)
        {
            var words = before.Groups[1].Value.Split(' ').Reverse().TakeWhile(w => !Regex.IsMatch(w, Stop, I) && !Regex.IsMatch(w, @"^(every|each|all|any|one|which|specific|same|selected|current|supplier|suppliers|show|me|generate|pdf|excel|word|sheet|my|our|his|her)$", I)).Reverse().ToList();
            var name = string.Join(' ', words);
            if (name.Length > 0 && (ResolveTender(name, snapshot) != null || ProperName(name))) return name;
        }
        if (Capture(message, @"\btender\s+([\p{L}\p{N}][^,.;?!]{0,60})") is { } after && (ResolveTender(after, snapshot) != null || ProperName(after))) return after;
        return null;
    }
    private static string? CategoryRef(string message, RadarSnapshot snapshot)
    {
        var folded = Fold(message);
        return snapshot.Suppliers.Select(s => s.Category).Where(c => !string.IsNullOrWhiteSpace(c)).Distinct().OrderByDescending(c => c.Length)
            .FirstOrDefault(c => Fold(c) is { Length: >= 3 } k && Regex.IsMatch(folded, $@"(^|\W){Regex.Escape(k)}(\W|$)"));
    }
    private static int? ResolveTender(string reference, RadarSnapshot snapshot)
    {
        var wanted = Fold(reference);
        var matches = snapshot.Tenders.Where(t => Fold(t.Title.Replace("RADAR DEMO ", "", StringComparison.OrdinalIgnoreCase)).Contains(wanted)).ToList();
        return matches.Count == 1 ? matches[0].Id : null;
    }

    // ---------- resolution against the authorized snapshot ----------
    public static RadarAction Resolve(RadarIntent intent, RadarPageContext context, RadarSnapshot snapshot, bool arabic)
    {
        string L(string en, string ar) => arabic ? ar : en;
        if (intent.Kind == "unsupported")
            return new(intent, Refusal: L("Orange Intelligence reports cover suppliers, tenders, bidding cycles, offers and their analytics. That subject is not in the procurement records, so I have not created a substitute report.",
                "تغطي تقارير أورنج الموردين والمناقصات وجولات العطاءات والعروض وتحليلاتها. هذا الموضوع غير موجود في سجلات المشتريات، لذلك لم أنشئ تقريرا بديلا."));
        if (intent.Kind == "report" || intent.Kind == "unclear" && intent.Destination == null && (intent.Format != null || intent.Report != null))
        {
            if (intent.Report == "conversation") return new(intent, Export: new RadarExportRequest("pdf", "conversation", context.From, context.To, null, []));
            if (intent.Format == null || !Formats.Contains(intent.Format)) return Ask(intent, L("Should I create it as Excel, Word or PDF?", "هل تريده بصيغة Excel أو Word أو PDF؟"));
            if (intent.Report == null || !Reports.ContainsKey(intent.Report))
                return Ask(intent, L("What should the file contain: all suppliers, supplier participation in tenders, a supplier comparison, the market overview, or one tender?",
                    "ماذا يجب أن يحتوي الملف: جميع الموردين، مشاركة الموردين في المناقصات، مقارنة الموردين، نظرة عامة على السوق، أم مناقصة واحدة؟"));
            var (tenderId, tenderError) = ResolveTender(intent.Tender, context, snapshot, TenderReports.Contains(intent.Report), arabic);
            if (tenderError != null) return tenderError.StartsWith('?') ? Ask(intent, tenderError[1..]) : new(intent, Refusal: tenderError);
            var report = intent.Report;
            if (report == "tender_offers" && tenderId == null) report = "supplier_offers";
            var (ids, supplierError) = ResolveSuppliers(intent, context, snapshot, arabic);
            if (supplierError != null) return new(intent, Refusal: supplierError);
            if (report == "supplier_profile" && ids.Length == 0)
                return Ask(intent, L("Which supplier should the report cover?", "أي مورد يجب أن يغطي التقرير؟"));
            if (report == "supplier_profile" && ids.Length > 1) report = "supplier_comparison";
            var (from, to) = Period(intent.Months, context);
            var round = report == "tender_offers" && context.SelectedRoundId is { } r && snapshot.Round(r)?.TenderId == tenderId ? r : (int?)null;
            return new(intent with { Report = report }, Export: new RadarExportRequest(intent.Format, report, from, to, tenderId, ids, round, arabic ? "ar" : "en"));
        }
        if (intent.Kind == "navigate" || intent.Kind == "unclear" && intent.Destination != null)
        {
            var destination = intent.Destination != null && Destinations.ContainsKey(intent.Destination) ? intent.Destination : null;
            if (destination == null) return Ask(intent, L("Which view should I open: the Supplier Universe, a tender, Price Patterns or Submission Timing?", "أي عرض أفتح: عالم الموردين، مناقصة، أنماط الأسعار، أم توقيت التقديم؟"));
            if (destination == "supplier_offer_comparison")
            {
                var pairContext = context with {
                    SelectedSupplierIds = context.ComparisonSupplierA is { } a && context.ComparisonSupplierB is { } b ? [a, b] : context.SelectedSupplierIds
                };
                var pairIntent = intent with { Supplier = intent.Supplier ?? "selected" };
                var (pair, pairError) = ResolveSuppliers(pairIntent, pairContext, snapshot, arabic);
                if (pairError != null) return new(intent, Refusal: pairError);
                if (pair.Length != 2) return Ask(intent, L("Which two different suppliers should I compare?", "أي موردين مختلفين تريد مقارنتهما؟"));

                var choices = RadarOfferComparison.Build(snapshot, pair[0], pair[1]);
                if (choices.Count == 0) return new(intent, Refusal: L("These suppliers have no eligible offers in the same closed bidding cycle.", "لا توجد عروض مؤهلة للموردين في دورة عطاء مغلقة مشتركة."));
                var tenderContext = context with { SelectedTenderId = context.ComparisonTenderId ?? context.SelectedTenderId };
                var (requestedTender, error) = ResolveTender(intent.Tender, tenderContext, snapshot, false, arabic);
                var identified = intent with { Supplier = $"#{pair[0]}", SecondSupplier = $"#{pair[1]}" };
                if (error != null) return error.StartsWith('?') ? Ask(identified, error[1..]) : new(intent, Refusal: error);
                var chosen = requestedTender.HasValue ? choices.FirstOrDefault(x => x.TenderId == requestedTender) :
                    choices.FirstOrDefault(x => x.TenderId == tenderContext.SelectedTenderId) ?? (choices.Count == 1 ? choices[0] : null);
                if (chosen == null)
                {
                    if (requestedTender.HasValue) return new(intent, Refusal: L("There is no eligible shared bidding cycle for these suppliers in that tender.", "لا توجد دورة عطاء مشتركة مؤهلة لهذين الموردين في هذا العطاء."));
                    var names = string.Join(arabic ? "، " : ", ", choices.Take(5).Select(x => $"#{x.TenderId} {x.Title}"));
                    return Ask(identified, L($"Which shared tender should I use? Available choices include {names}.", $"أي عطاء مشترك تريد؟ تشمل الخيارات المتاحة {names}."));
                }
                return new(intent with { AlsoAnswer = false }, Navigation: new(destination, chosen.TenderId, pair[0], chosen.RoundId, null, pair[1], chosen));
            }
            var (tenderId, tenderError) = ResolveTender(intent.Tender, context, snapshot, TenderDestinations.Contains(destination), arabic);
            if (tenderError != null) return tenderError.StartsWith('?') ? Ask(intent, tenderError[1..]) : new(intent, Refusal: tenderError);
            var (ids, supplierError) = ResolveSuppliers(intent, context, snapshot, arabic);
            if (supplierError != null) return new(intent, Refusal: supplierError);
            if (destination == "supplier" && ids.Length == 0) return Ask(intent, L("Which supplier should I show?", "أي مورد أعرض؟"));
            string? category = null;
            if (intent.Category is { Length: > 0 } wanted)
            {
                category = snapshot.Suppliers.Select(x => x.Category).Distinct().FirstOrDefault(c => string.Equals(c?.Trim(), wanted.Trim(), StringComparison.OrdinalIgnoreCase));
                if (category == null) return new(intent, Refusal: L($"There is no supplier category named \"{Clip(wanted)}\" in your authorized data.", $"لا توجد فئة موردين باسم \"{Clip(wanted)}\" ضمن بياناتك المصرح بها."));
            }
            var roundId = destination == "tender_offers" && context.SelectedRoundId is { } rr && snapshot.Round(rr) is { Unsealed: true } round && round.TenderId == tenderId ? rr : 0;
            return new(intent, Navigation: new RadarNavigation(destination, tenderId ?? 0, ids.FirstOrDefault(), roundId, category));
        }
        return new(intent, Question: intent.Kind == "unclear" ? L("Do you want a file (Excel, Word or PDF), or should I show it on the page?", "هل تريد ملفا (Excel أو Word أو PDF) أم أعرضه على الصفحة؟") : null);

        static RadarAction Ask(RadarIntent i, string question) => new(i with { Kind = "unclear" }, Question: question, Pending: i with { Kind = "unclear" });
    }

    private static (string? From, string? To) Period(int months, RadarPageContext context) =>
        months is >= 1 and <= 36 ? (DateTime.UtcNow.Date.AddMonths(-months).ToString("yyyy-MM-dd"), DateTime.UtcNow.Date.ToString("yyyy-MM-dd")) : (context.From, context.To);

    // A tender is looked up only for tender-specific requests; general reports never demand one. "?" marks a question to ask.
    private static (int? Id, string? Error) ResolveTender(string? reference, RadarPageContext context, RadarSnapshot snapshot, bool required, bool arabic)
    {
        string L(string en, string ar) => arabic ? ar : en;
        if (reference is null or "selected")
        {
            if (context.SelectedTenderId is { } sel && snapshot.Tender(sel) != null && (required || reference == "selected")) return (sel, null);
            if (reference == "selected" || required) return (null, "?" + L("Which tender do you mean? Name it or give its number, for example #2008.", "أي مناقصة تقصد؟ اذكر اسمها أو رقمها، مثل #2008."));
            return (null, null);
        }
        if (Regex.IsMatch(reference, @"^\d{1,7}$")) reference = "#" + reference; // a bare number is a tender id
        if (reference.StartsWith('#'))
            return int.TryParse(reference[1..], out var id) && snapshot.Tender(id) != null ? (id, null)
                : (null, L($"Tender {reference} is not in your authorized reporting scope.", $"المناقصة {reference} غير موجودة ضمن نطاقك المصرح به."));
        var wanted = Fold(reference);
        var matches = snapshot.Tenders.Where(t => Fold(t.Title.Replace("RADAR DEMO ", "", StringComparison.OrdinalIgnoreCase)) is var title && (title == wanted || title.StartsWith(wanted + " ") || title.Contains(wanted))).ToList();
        if (matches.Count == 1) return (matches[0].Id, null);
        if (matches.Count > 1)
        {
            var exact = matches.Where(t => Fold(t.Title) == wanted).ToList();
            if (exact.Count == 1) return (exact[0].Id, null);
            return (null, "?" + L($"Several tenders match \"{Clip(reference)}\": {string.Join(", ", matches.Take(4).Select(t => $"#{t.Id} {t.Title}"))}. Which one?",
                $"عدة مناقصات تطابق \"{Clip(reference)}\": {string.Join("، ", matches.Take(4).Select(t => $"#{t.Id} {t.Title}"))}. أيها تقصد؟"));
        }
        return (null, L($"No tender called \"{Clip(reference)}\" is in your authorized reporting scope.", $"لا توجد مناقصة باسم \"{Clip(reference)}\" ضمن نطاقك المصرح به."));
    }

    private static (int[] Ids, string? Error) ResolveSuppliers(RadarIntent intent, RadarPageContext context, RadarSnapshot snapshot, bool arabic)
    {
        var ids = new List<int>();
        foreach (var reference in new[] { intent.Supplier, intent.SecondSupplier }.Where(r => !string.IsNullOrWhiteSpace(r)))
        {
            if (reference == "selected") { ids.AddRange((context.SelectedSupplierIds ?? []).Where(id => snapshot.Supplier(id) != null)); continue; }
            var id = ResolveSupplier(reference!, snapshot);
            if (id == null) return ([], arabic ? $"المورد \"{Clip(reference!)}\" غير موجود ضمن نطاقك المصرح به." : $"Supplier \"{Clip(reference!)}\" is not in your authorized scope.");
            ids.Add(id.Value);
        }
        return (ids.Distinct().Take(8).ToArray(), null);
    }

    public static int? ResolveSupplier(string reference, RadarSnapshot snapshot)
    {
        var wanted = Fold(reference.Trim().Replace("RADAR DEMO ", "", StringComparison.OrdinalIgnoreCase));
        if (wanted.Length == 0) return null;
        if (Regex.IsMatch(wanted, @"^#?\d+$") && int.TryParse(wanted.TrimStart('#'), out var id)) return snapshot.Supplier(id)?.Id;
        var code = Regex.IsMatch(wanted, @"^[a-z]\d{1,4}$");
        var matches = snapshot.Suppliers.Where(s => Fold(s.Name.Replace("RADAR DEMO ", "", StringComparison.OrdinalIgnoreCase)) is var name &&
            (code ? Regex.IsMatch(name, $@"\b{Regex.Escape(wanted)}\b") : name == wanted || name.Contains(wanted) && wanted.Length >= 3)).ToList();
        return matches.Count == 1 ? matches[0].Id : matches.FirstOrDefault(s => Fold(s.Name) == wanted)?.Id;
    }

    private static string Clip(string s) => s.Length <= 60 ? s : s[..60] + "…";

    // ---------- the page carries the pending intent between turns as plain JSON; it is untrusted and re-validated ----------
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };
    public static RadarIntent? Sanitize(RadarIntent? i)
    {
        if (i == null || !Kinds.Contains(i.Kind)) return null;
        static string? Short(string? s, int max = 80) => string.IsNullOrWhiteSpace(s) ? null : s.Trim().Length > max ? s.Trim()[..max] : s.Trim();
        return new RadarIntent(i.Kind, Formats.Contains(i.Format) ? i.Format : null, i.Report != null && Reports.ContainsKey(i.Report) ? i.Report : null,
            i.Destination != null && Destinations.ContainsKey(i.Destination) ? i.Destination : null, Short(i.Supplier), Short(i.SecondSupplier), Short(i.Tender),
            Math.Clamp(i.Months, 0, 36), Short(i.Category), i.AlsoAnswer);
    }
    public static string Serialize(RadarIntent intent) => JsonSerializer.Serialize(intent, Json);
}

// Completes a request the rules recognised as an action but could not fully read, using Ollama structured output: the reply must
// match a JSON schema of fixed enums, and is then sanitized and resolved exactly like a rule result. The model only classifies.
public sealed class RadarIntentRouter(RadarOllamaClient ollama, Microsoft.Extensions.Options.IOptions<RadarOptions> options)
{
    private static string[] WithNone(IEnumerable<string> values) => [.. values, "none"];
    public static object Schema => new
    {
        type = "object",
        properties = new
        {
            kind = new { type = "string", @enum = RadarIntents.Kinds },
            format = new { type = "string", @enum = WithNone(RadarIntents.Formats) },
            report = new { type = "string", @enum = WithNone(RadarIntents.Reports.Keys) },
            destination = new { type = "string", @enum = WithNone(RadarIntents.Destinations.Keys) },
            supplier = new { type = "string" }, secondSupplier = new { type = "string" }, tender = new { type = "string" },
            months = new { type = "integer" }, alsoAnswer = new { type = "boolean" }
        },
        required = new[] { "kind", "format", "report", "destination", "supplier", "secondSupplier", "tender", "months", "alsoAnswer" }
    };

    public static string Instructions(RadarPageContext context, RadarSnapshot snapshot) => $$"""
        You classify one request to Orange Intelligence, a procurement analytics page about suppliers, tenders, bidding cycles and offers.
        Reply with JSON only. Do not answer the request.
        kind: "report" = the user wants a downloadable file; "navigate" = the user wants to see part of the page; "question" = a question, analysis or chart in the chat;
        "unsupported" = the subject is not suppliers, tenders, bidding cycles or offers (e.g. invoices, salaries); "unclear" = a file or page is wanted but what it should contain cannot be determined.
        format: xlsx (Excel, spreadsheet, sheet), docx (Word), pdf, or none.
        report (for kind report): {{string.Join("; ", RadarIntents.Reports.Select(r => $"{r.Key} = {r.Value}"))}}.
        A general report covering all suppliers or all tenders never needs a tender. Only tender_summary, tender_lifecycle and tender_offers are about ONE specific tender.
        destination (for kind navigate): {{string.Join("; ", RadarIntents.Destinations.Select(d => $"{d.Key} = {d.Value}"))}}.
        supplier, secondSupplier, tender: copy the words the user used to name them (a name, a code like A21, or #id), "selected" if the user refers to the current selection, otherwise "".
        Never invent a name or id. months: a relative period in months the user stated, else 0. alsoAnswer: true if, besides opening the view, the user asks a question that needs an analysed answer.
        The text may come from speech recognition: a phonetically similar word may replace a product name (for example "exit sheet" for "Excel sheet"); read it that way only when the meaning is clear.
        Current selection: supplier {{(context.SelectedSupplierIds is { Length: > 0 } ids ? string.Join(", ", ids.Select(snapshot.SupplierName)) : "none")}}; tender {{(context.SelectedTenderId is { } t && snapshot.Tender(t) is { } tt ? $"#{tt.Id} {tt.Title}" : "none")}}.
        """;

    public async Task<(RadarIntent? Intent, double Ms)> RouteAsync(string message, RadarPageContext context, RadarSnapshot snapshot, IReadOnlyList<RadarTurn>? history,
        RadarIntent? pending, CancellationToken ct, string? model = null, bool? think = null)
    {
        var settings = options.Value.Models;
        var messages = new List<object> { new { role = "system", content = Instructions(context, snapshot) } };
        foreach (var turn in (history ?? []).TakeLast(2).Where(x => x.Role is "user" or "assistant" && !string.IsNullOrWhiteSpace(x.Text)))
            messages.Add(new { role = turn.Role, content = turn.Text.Length > 300 ? turn.Text[..300] : turn.Text });
        if (pending != null) messages.Add(new { role = "system", content = "Partly understood earlier request (complete it with the new message if it is an answer): " + RadarIntents.Serialize(pending) });
        messages.Add(new { role = "user", content = message });
        var (json, ms, _) = await ollama.ChatJsonAsync(model ?? settings.RouterModel ?? settings.ChatModel, messages, Schema, think ?? settings.RouterThink, ct);
        string? Str(string name) => json.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String && v.GetString() is { Length: > 0 } x && x != "none" ? x : null;
        var intent = new RadarIntent(Str("kind") ?? "question", Str("format"), Str("report"), Str("destination"), Str("supplier"), Str("secondSupplier"), Str("tender"),
            json.TryGetProperty("months", out var m) && m.TryGetInt32(out var months) ? months : 0, null, json.TryGetProperty("alsoAnswer", out var a) && a.ValueKind == JsonValueKind.True);
        return (RadarIntents.Sanitize(intent), ms);
    }

    // The rules' reading wins where it found something; the model fills only what the rules left open.
    // Only a completed action is taken from the model: refusing (unsupported) is decided by the rules' subject list, and a request
    // the rules saw as a file or view never silently becomes a chat answer - it gets the focused question instead.
    public static RadarIntent Merge(RadarIntent rules, RadarIntent? model)
    {
        if (model == null || model.Kind is not ("report" or "navigate")) return rules;
        var merged = model with
        {
            Format = rules.Format ?? model.Format, Report = rules.Report ?? model.Report, Destination = rules.Destination ?? model.Destination,
            Supplier = rules.Supplier ?? model.Supplier, SecondSupplier = rules.SecondSupplier ?? model.SecondSupplier, Tender = rules.Tender ?? model.Tender,
            Months = rules.Months > 0 ? rules.Months : model.Months, Category = rules.Category
        };
        if (rules.Format != null && merged.Kind == "navigate") merged = merged with { Kind = "report" };
        return merged;
    }
}
