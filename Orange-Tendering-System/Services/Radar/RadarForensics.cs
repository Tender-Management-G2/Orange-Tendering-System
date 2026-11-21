using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Orange_Tendering_System.Models.Entities;

namespace Orange_Tendering_System.Services.Radar;

public sealed record EvidenceRef(string Type, int Id, string Label);

// Level: "Context" = factual relationship worth knowing; "ReviewSignal" = a statistical pattern a person may review.
// Neither level is an accusation: every finding carries its method, sample size and limitations.
public sealed record RadarFinding(string Id, string Detector, string Title, string Level, IReadOnlyList<int> SupplierIds, IReadOnlyList<int> TenderIds,
    IReadOnlyList<int> RoundIds, IReadOnlyList<EvidenceRef> Records, IReadOnlyDictionary<string, string> Metrics, int SampleSize, string Method,
    string Period, string Limitations, string Explanation, DateTime AvailableAt, string? AttributeKey = null, string? AttributeLabel = null);

public sealed record DetectorStatus(string Detector, string Label, string State, int Findings, string Note, string Threshold);

public sealed record DocFingerprint(int DocumentId, string FileName, string Owner, int? SupplierId, int? BidId, int? TenderId, int? RoundId,
    string? Sha256, string ExtractionState, DateTime AvailableAt);

public sealed record SemanticMatch(int DocumentA, int DocumentB, int SupplierA, int SupplierB, double Similarity, string ExcerptA, string ExcerptB,
    int ChunksCompared, int TemplateChunksExcluded, DateTime AvailableAt);

public sealed record ForensicResult(IReadOnlyList<RadarFinding> Findings, IReadOnlyList<DetectorStatus> Detectors);

public static class RadarForensics
{
    public const string SharedContact = "shared-contact", JointParticipation = "joint-participation", PriceGap = "price-gap", PriceSimilarity = "price-similarity",
        AwardRotation = "award-rotation", IdenticalDocument = "identical-document", SemanticDocument = "semantic-document", SubmissionTiming = "submission-timing";

    private static readonly HashSet<string> PublicMailDomains = new(StringComparer.OrdinalIgnoreCase)
    {
        "gmail.com", "googlemail.com", "outlook.com", "hotmail.com", "live.com", "yahoo.com", "icloud.com", "msn.com", "aol.com", "proton.me", "protonmail.com", "mail.com"
    };

    public static ForensicResult Detect(RadarSnapshot s, RadarThresholds t, IReadOnlyList<DocFingerprint>? documents, IReadOnlyList<SemanticMatch>? semantic, string? semanticState = null)
    {
        var findings = new List<RadarFinding>();
        var statuses = new List<DetectorStatus>();
        void Add(string detector, string label, IEnumerable<RadarFinding> found, string threshold, Func<int, (string State, string Note)> state)
        {
            var list = found.ToList(); findings.AddRange(list);
            var (st, note) = state(list.Count);
            statuses.Add(new DetectorStatus(detector, label, st, list.Count, note, threshold));
        }

        Add(SharedContact, "Shared contact information", Contacts(s), "2+ suppliers with the same normalized phone, e-mail, tax number, company e-mail domain or address",
            n => s.Suppliers.Count < 2 ? ("insufficient", "Fewer than two suppliers are registered.") : ("ok", $"{s.Suppliers.Count} supplier records compared."));

        var joint = RadarAnalytics.JointRounds(s);
        Add(JointParticipation, "Repeated joint participation", Joint(s, t, joint), $"{t.JointParticipationMinRounds}+ shared closed rounds (context only, never a signal by itself)",
            n => joint.Count == 0 ? ("insufficient", "No closed round had two or more offers.") : ("ok", $"{joint.Count} supplier pairs met in at least one closed round."));

        var gapPairs = joint.Count(p => p.Value.Count >= t.PriceGapMinRounds);
        Add(PriceGap, "Repeated relative price gap", PriceGaps(s, t, joint), $"{t.PriceGapMinRounds}+ shared rounds, same direction, ratio {t.PriceGapMinRatio:0.###}-{t.PriceGapMaxRatio:0.##}, variation <= {t.PriceGapMaxVariation:P1}",
            n => gapPairs == 0 ? ("insufficient", $"No supplier pair shared {t.PriceGapMinRounds}+ priced rounds.") : ("ok", $"{gapPairs} pairs had enough shared rounds."));

        var items = RadarAnalytics.ItemComparisons(s);
        Add(PriceSimilarity, "Historical price similarity", PriceSimilarities(s, t, items), $"{t.PriceSimilarityMinLines}+ identical-item lines within {t.PriceSimilarityTolerance:P1} across 2+ rounds",
            n => items.Count == 0 ? ("insufficient", "No tender item was priced by two suppliers in the same round.") : ("ok", $"{items.Count} comparable item lines."));

        var awards = RadarAnalytics.Awards(s);
        Add(AwardRotation, "Repeated award rotation", Rotation(s, t, awards), $"{t.RotationMinAwards}+ awarded rounds where the same group bid and every member won in turn",
            n => awards.Count < t.RotationMinAwards ? ("insufficient", $"{awards.Count} official award(s) recorded; at least {t.RotationMinAwards} are needed.") : ("ok", $"{awards.Count} official awards examined."));

        if (documents == null)
            statuses.Add(new DetectorStatus(IdenticalDocument, "Identical uploaded documents (SHA-256)", "notRun", 0, "Document hashing has not run yet.", "Same SHA-256 from 2+ suppliers, tender templates excluded"));
        else
        {
            var hashed = documents.Count(d => d.Sha256 != null);
            Add(IdenticalDocument, "Identical uploaded documents (SHA-256)", Identical(s, documents), "Same SHA-256 from 2+ suppliers, tender templates excluded",
                n => hashed < 2 ? ("insufficient", $"{hashed} readable document file(s).") : ("ok", $"{hashed} files hashed; {documents.Count - hashed} unavailable."));
        }

        if (semantic == null)
            statuses.Add(new DetectorStatus(SemanticDocument, "Semantic document similarity", semanticState ?? "notRun", 0,
                semanticState == "unavailable" ? "The local embedding model is unavailable; other detectors still work." : "Run Document DNA to compute embeddings.", $"Cosine >= {t.SemanticSimilarity:0.00} after removing tender template passages"));
        else
            Add(SemanticDocument, "Semantic document similarity", Semantic(s, t, semantic), $"Cosine >= {t.SemanticSimilarity:0.00} after removing tender template passages (>= {t.TemplateSimilarity:0.00})",
                n => ("ok", $"{semantic.Sum(m => m.ChunksCompared)} passage comparisons over extractable bid documents."));

        Add(SubmissionTiming, "Submission-time proximity", Timing(s, t, joint), $"Submitted within {t.TimingWindowMinutes} min of each other in {t.TimingMinRounds}+ rounds",
            n => s.Bids.Count(b => b.SubmittedAt != null) < 2 ? ("insufficient", "Too few timestamped offers.") : ("ok", $"{s.Bids.Count(b => b.SubmittedAt != null)} timestamped offers."));

        return new ForensicResult(findings.OrderBy(f => f.Level == "ReviewSignal" ? 0 : 1).ThenByDescending(f => f.SampleSize).ToList(), statuses);
    }

    // 1. Shared normalized contact information.
    private static IEnumerable<RadarFinding> Contacts(RadarSnapshot s)
    {
        var keys = new List<(string Kind, string Key, string Masked, int SupplierId)>();
        foreach (var x in s.Suppliers)
        {
            var email = x.Email.Trim().ToLowerInvariant();
            if (email.Contains('@'))
            {
                keys.Add(("email", email, Mask(email), x.Id));
                var domain = email[(email.IndexOf('@') + 1)..];
                if (!PublicMailDomains.Contains(domain) && !domain.EndsWith(".test") && !domain.EndsWith(".local")) keys.Add(("domain", domain, "@" + domain, x.Id));
            }
            var phone = Digits(x.Phone);
            if (phone.Length >= 8) { var p = phone[^Math.Min(9, phone.Length)..]; keys.Add(("phone", p, "•••" + p[^3..], x.Id)); }
            var tax = Regex.Replace(x.TaxNumber ?? "", "[^A-Za-z0-9]", "").ToUpperInvariant();
            if (tax.Length >= 5) keys.Add(("tax", tax, "•••" + tax[^3..], x.Id));
            var address = NormalizeAddress(x.Address);
            if (address.Length >= 12) keys.Add(("address", address, address.Length > 28 ? address[..28] + "…" : address, x.Id));
        }
        var names = new Dictionary<string, string> { ["email"] = "e-mail address", ["domain"] = "company e-mail domain", ["phone"] = "phone number", ["tax"] = "tax number", ["address"] = "address" };
        foreach (var g in keys.GroupBy(k => (k.Kind, k.Key)).Where(g => g.Select(k => k.SupplierId).Distinct().Count() >= 2))
        {
            var ids = g.Select(k => k.SupplierId).Distinct().Order().ToList();
            var strong = g.Key.Kind is "email" or "phone" or "tax";
            var label = names[g.Key.Kind];
            yield return new RadarFinding(Id(SharedContact, g.Key.Kind, g.Key.Key), SharedContact, $"Shared {label}", strong ? "ReviewSignal" : "Context", ids, [], [],
                ids.Select(i => new EvidenceRef("Supplier", i, s.SupplierName(i))).ToList(),
                new Dictionary<string, string> { ["attribute"] = label, ["value"] = g.First().Masked, ["suppliers"] = ids.Count.ToString() },
                ids.Count, $"Exact match after normalization ({(g.Key.Kind == "address" ? "lower case, punctuation and spacing removed" : g.Key.Kind == "phone" ? "digits only, last 9 digits" : "case-insensitive")}).",
                "Registration records", strong
                    ? "Contact data is supplied by the suppliers and may be outdated; agents, consultants or group companies can legitimately share contacts."
                    : "Shared addresses or domains often reflect business centres, group companies or shared agents and are context, not a concern by themselves.",
                $"{string.Join(", ", ids.Select(s.SupplierName))} registered the same {label}.",
                ids.Select(i => s.Supplier(i)!.CreatedAt).Max(), $"{g.Key.Kind}:{g.Key.Key}", $"Shared {label} {g.First().Masked}");
        }
    }

    // 2. Repeated joint participation. In a small market ordinary competitors meet in most rounds, so this is always context:
    // it explains why suppliers are connected and feeds the price and timing detectors, but never becomes a signal on its own.
    private static IEnumerable<RadarFinding> Joint(RadarSnapshot s, RadarThresholds t, Dictionary<(int A, int B), List<int>> joint)
    {
        var roundsBySupplier = s.Bids.GroupBy(b => b.SupplierId).ToDictionary(g => g.Key, g => g.Select(b => b.RoundId).Distinct().Count());
        foreach (var ((a, b), rounds) in joint.Where(p => p.Value.Count >= t.JointParticipationMinRounds))
        {
            var union = roundsBySupplier[a] + roundsBySupplier[b] - rounds.Count;
            var overlap = union == 0 ? 0 : (double)rounds.Count / union;
            yield return new RadarFinding(Id(JointParticipation, a, b), JointParticipation, "Repeated joint participation", "Context",
                [a, b], TenderIds(s, rounds), rounds, Rounds(s, rounds),
                new Dictionary<string, string> { ["sharedRounds"] = rounds.Count.ToString(), ["roundsA"] = roundsBySupplier[a].ToString(), ["roundsB"] = roundsBySupplier[b].ToString(), ["overlap"] = overlap.ToString("P0") },
                rounds.Count, "Count of closed rounds in which both suppliers submitted an offer; overlap = shared rounds / rounds where either bid (Jaccard).",
                s.Period.Label, "Suppliers in the same category are expected to meet often; small markets inflate overlap.",
                $"{s.SupplierName(a)} and {s.SupplierName(b)} both submitted offers in {rounds.Count} closed rounds ({overlap:P0} of the rounds either of them bid in).",
                rounds.Select(r => s.Round(r)!.AvailableAt).Max());
        }
    }

    // 3. Repeated relative price gap: one supplier's total sits a near-constant small margin above the other's.
    private static IEnumerable<RadarFinding> PriceGaps(RadarSnapshot s, RadarThresholds t, Dictionary<(int A, int B), List<int>> joint)
    {
        foreach (var ((a, b), rounds) in joint.Where(p => p.Value.Count >= t.PriceGapMinRounds))
        {
            var samples = rounds.Select(r => (Round: r, A: s.Bids.First(x => x.RoundId == r && x.SupplierId == a), B: s.Bids.First(x => x.RoundId == r && x.SupplierId == b)))
                .Where(x => x.A.Currency == x.B.Currency && x.A.Total > 0 && x.B.Total > 0).ToList();
            if (samples.Count < t.PriceGapMinRounds) continue;
            var ratios = samples.Select(x => (double)(x.B.Total / x.A.Total)).ToList();
            bool higherB = ratios.All(r => r > 1), higherA = ratios.All(r => r < 1);
            if (!higherA && !higherB) continue;
            var oriented = higherB ? ratios : ratios.Select(r => 1 / r).ToList();
            var mean = oriented.Average();
            var cv = Math.Sqrt(oriented.Sum(r => (r - mean) * (r - mean)) / oriented.Count) / mean;
            if (mean < t.PriceGapMinRatio || mean > t.PriceGapMaxRatio || cv > t.PriceGapMaxVariation) continue;
            var (low, high) = higherB ? (a, b) : (b, a);
            var ids = samples.Select(x => x.Round).ToList();
            yield return new RadarFinding(Id(PriceGap, a, b), PriceGap, "Repeated relative price gap", "ReviewSignal", [a, b], TenderIds(s, ids), ids,
                samples.SelectMany(x => new[] { new EvidenceRef("Bid", x.A.Id, $"{s.SupplierName(a)} {x.A.Currency} {x.A.Total:N2}"), new EvidenceRef("Bid", x.B.Id, $"{s.SupplierName(b)} {x.B.Currency} {x.B.Total:N2}") }).ToList(),
                new Dictionary<string, string> { ["meanGap"] = (mean - 1).ToString("P2"), ["variation"] = cv.ToString("P2"), ["rounds"] = samples.Count.ToString(), ["higher"] = s.SupplierName(high) },
                samples.Count, "Ratio of the two offer totals per shared round (same round, same currency, latest revision); flagged when the direction never changes and the ratio barely varies.",
                s.Period.Label, "Similar cost structures, shared distributors or price lists can produce stable gaps. Offer totals include extra costs.",
                $"In {samples.Count} shared rounds {s.SupplierName(high)} priced {mean - 1:P1} above {s.SupplierName(low)} on average, with only {cv:P1} variation.",
                ids.Select(r => s.Round(r)!.AvailableAt).Max());
        }
    }

    // 4. Historical price similarity: near-identical unit prices for the same tender item in the same round.
    private static IEnumerable<RadarFinding> PriceSimilarities(RadarSnapshot s, RadarThresholds t, List<RadarAnalytics.ItemComparison> items)
    {
        var pairs = new Dictionary<(int, int), List<(RadarAnalytics.ItemComparison Item, RadarAnalytics.ItemPrice A, RadarAnalytics.ItemPrice B)>>();
        var compared = new Dictionary<(int, int), int>();
        foreach (var item in items)
            foreach (var p in item.Prices)
                foreach (var q in item.Prices.Where(q => q.SupplierId > p.SupplierId))
                {
                    var key = (p.SupplierId, q.SupplierId);
                    compared[key] = compared.GetValueOrDefault(key) + 1;
                    var min = Math.Min(p.UnitPrice, q.UnitPrice);
                    if (min > 0 && Math.Abs(p.UnitPrice - q.UnitPrice) / min <= (decimal)t.PriceSimilarityTolerance)
                    {
                        if (!pairs.TryGetValue(key, out var list)) pairs[key] = list = [];
                        list.Add((item, p, q));
                    }
                }
        foreach (var ((a, b), lines) in pairs.Where(p => p.Value.Count >= t.PriceSimilarityMinLines && p.Value.Select(x => x.Item.RoundId).Distinct().Count() >= 2))
        {
            var rounds = lines.Select(x => x.Item.RoundId).Distinct().ToList();
            yield return new RadarFinding(Id(PriceSimilarity, a, b), PriceSimilarity, "Near-identical unit prices", "ReviewSignal", [a, b], TenderIds(s, rounds), rounds,
                lines.Select(x => new EvidenceRef("BidItem", x.A.BidId, $"{x.Item.Item}: {x.A.UnitPrice:N2} vs {x.B.UnitPrice:N2} {x.Item.Currency} ({x.Item.Unit})")).ToList(),
                new Dictionary<string, string> { ["matchingLines"] = lines.Count.ToString(), ["comparedLines"] = compared[(a, b)].ToString(), ["rounds"] = rounds.Count.ToString(), ["tolerance"] = t.PriceSimilarityTolerance.ToString("P1") },
                compared[(a, b)], "Unit prices for the same tender item, quantity, unit, currency and round; a line matches when the prices differ by at most the tolerance.",
                s.Period.Label, "Regulated prices, published list prices or commodity items can be legitimately identical.",
                $"{lines.Count} of {compared[(a, b)]} commonly priced item lines from {s.SupplierName(a)} and {s.SupplierName(b)} were within {t.PriceSimilarityTolerance:P1} of each other across {rounds.Count} rounds.",
                rounds.Select(r => s.Round(r)!.AvailableAt).Max());
        }
    }

    // 5. Repeated award rotation inside a stable group of bidders (pairs and triples).
    private static IEnumerable<RadarFinding> Rotation(RadarSnapshot s, RadarThresholds t, List<RadarAnalytics.Award> awards)
    {
        if (awards.Count < t.RotationMinAwards) yield break;
        var awarded = awards.Select(a => (Award: a, Bidders: s.Bids.Where(b => b.RoundId == a.RoundId).Select(b => b.SupplierId).ToHashSet())).ToList();
        var winners = awards.Select(a => a.SupplierId).Distinct().Order().ToList();
        var groups = new List<int[]>();
        for (var i = 0; i < winners.Count; i++)
            for (var j = i + 1; j < winners.Count; j++)
            {
                groups.Add([winners[i], winners[j]]);
                for (var k = j + 1; k < winners.Count; k++) groups.Add([winners[i], winners[j], winners[k]]);
            }
        var reported = new List<int[]>();
        foreach (var group in groups.OrderByDescending(g => g.Length))
        {
            var rounds = awarded.Where(x => group.All(x.Bidders.Contains)).OrderBy(x => x.Award.AwardedAt).ToList();
            if (rounds.Count < t.RotationMinAwards || rounds.Any(x => !group.Contains(x.Award.SupplierId))) continue;
            if (group.Any(g => rounds.All(x => x.Award.SupplierId != g))) continue;
            var switches = rounds.Zip(rounds.Skip(1)).Count(p => p.First.Award.SupplierId != p.Second.Award.SupplierId);
            if (switches < rounds.Count - 1) continue;
            if (reported.Any(r => group.All(r.Contains))) continue;
            reported.Add(group);
            var ids = rounds.Select(x => x.Award.RoundId).ToList();
            yield return new RadarFinding(Id(AwardRotation, group), AwardRotation, "Awards rotate within a stable group", "ReviewSignal", group.ToList(), rounds.Select(x => x.Award.TenderId).ToList(), ids,
                rounds.Select(x => new EvidenceRef("Award", x.Award.BidId, $"{x.Award.Tender}: {x.Award.Supplier} ({x.Award.Currency} {x.Award.Value:N2})")).ToList(),
                new Dictionary<string, string> { ["awards"] = rounds.Count.ToString(), ["groupSize"] = group.Length.ToString(), ["winnerChanges"] = switches.ToString(), ["sequence"] = string.Join(" → ", rounds.Select(x => x.Award.Supplier)) },
                rounds.Count, "Official awards in chronological order where every group member bid; flagged when only group members won and the winner changed every time.",
                s.Period.Label, "With few qualified suppliers, alternating winners can be ordinary competition. Official awards only; recommendations are not counted.",
                $"Across {rounds.Count} awarded rounds where {string.Join(", ", group.Select(s.SupplierName))} all bid, the award changed hands every time: {string.Join(" → ", rounds.Select(x => x.Award.Supplier))}.",
                rounds.Max(x => x.Award.AwardedAt ?? s.Round(x.Award.RoundId)!.AvailableAt));
        }
    }

    // 6. Identical uploaded files (SHA-256), excluding files the tender itself issued (templates / requirement forms).
    private static IEnumerable<RadarFinding> Identical(RadarSnapshot s, IReadOnlyList<DocFingerprint> documents)
    {
        var templates = documents.Where(d => d.Owner == "Tender" && d.Sha256 != null).Select(d => d.Sha256!).ToHashSet();
        foreach (var g in documents.Where(d => d.Owner != "Tender" && d.Sha256 != null && d.SupplierId != null && !templates.Contains(d.Sha256)).GroupBy(d => d.Sha256!))
        {
            var suppliers = g.Select(d => d.SupplierId!.Value).Distinct().Order().ToList();
            if (suppliers.Count < 2) continue;
            var docs = g.ToList();
            var tenders = docs.Where(d => d.TenderId != null).Select(d => d.TenderId!.Value).Distinct().ToList();
            yield return new RadarFinding(Id(IdenticalDocument, g.Key), IdenticalDocument, "Byte-identical uploaded document", "ReviewSignal", suppliers, tenders,
                docs.Where(d => d.RoundId != null).Select(d => d.RoundId!.Value).Distinct().ToList(),
                docs.Select(d => new EvidenceRef("Document", d.DocumentId, $"{d.FileName} ({s.SupplierName(d.SupplierId!.Value)}, {d.Owner.ToLowerInvariant()} document)")).ToList(),
                new Dictionary<string, string> { ["sha256"] = g.Key[..Math.Min(16, g.Key.Length)] + "…", ["files"] = docs.Count.ToString(), ["suppliers"] = suppliers.Count.ToString() },
                docs.Count, "SHA-256 of the stored file bytes. Files whose hash equals a document issued with the tender are treated as templates and skipped.",
                s.Period.Label, "Identical files can be legitimately shared, e.g. a manufacturer's datasheet, a public certificate or a form provided outside the system.",
                $"{docs.Count} files uploaded by {string.Join(", ", suppliers.Select(s.SupplierName))} have exactly the same content (SHA-256).",
                docs.Max(d => d.AvailableAt), $"sha:{g.Key}", docs[0].FileName);
        }
    }

    // 7. Semantic similarity of bid documents (embeddings), template passages already removed by RadarDocumentDna.
    private static IEnumerable<RadarFinding> Semantic(RadarSnapshot s, RadarThresholds t, IReadOnlyList<SemanticMatch> matches)
    {
        foreach (var g in matches.Where(m => m.Similarity >= t.SemanticSimilarity).GroupBy(m => (Math.Min(m.SupplierA, m.SupplierB), Math.Max(m.SupplierA, m.SupplierB))))
        {
            var best = g.OrderByDescending(m => m.Similarity).First();
            var docs = g.SelectMany(m => new[] { m.DocumentA, m.DocumentB }).Distinct().ToList();
            yield return new RadarFinding(Id(SemanticDocument, g.Key.Item1, g.Key.Item2), SemanticDocument, "Semantically similar document passages", "ReviewSignal",
                [g.Key.Item1, g.Key.Item2], [], [], docs.Select(d => s.Documents.FirstOrDefault(x => x.Id == d) is { } doc
                    ? new EvidenceRef("Document", d, $"{doc.FileName} ({s.SupplierName(doc.SupplierId ?? 0)}, bid #{doc.BidId})") : new EvidenceRef("Document", d, $"Document #{d}")).ToList(),
                new Dictionary<string, string> { ["bestSimilarity"] = best.Similarity.ToString("0.000"), ["documentPairs"] = g.Count().ToString(), ["templatePassagesExcluded"] = g.Sum(m => m.TemplateChunksExcluded).ToString() },
                g.Sum(m => m.ChunksCompared), "Cosine similarity of qwen3-embedding passage vectors; passages close to the tender's own text are removed first.",
                s.Period.Label, "Similar meaning is not copying: standard technical language, common standards and shared consultants produce similar text. Only TXT, CSV and DOCX can be read.",
                $"The most similar passages of {s.SupplierName(g.Key.Item1)} and {s.SupplierName(g.Key.Item2)} reach cosine {best.Similarity:0.000} after template text was removed.",
                g.Max(m => m.AvailableAt), $"sem:{g.Key.Item1}-{g.Key.Item2}", null);
        }
    }

    // 8. Repeated submission-time proximity.
    private static IEnumerable<RadarFinding> Timing(RadarSnapshot s, RadarThresholds t, Dictionary<(int A, int B), List<int>> joint)
    {
        foreach (var ((a, b), rounds) in joint.Where(p => p.Value.Count >= t.TimingMinRounds))
        {
            var close = rounds.Select(r => (Round: r, A: s.Bids.First(x => x.RoundId == r && x.SupplierId == a), B: s.Bids.First(x => x.RoundId == r && x.SupplierId == b)))
                .Where(x => x.A.SubmittedAt != null && x.B.SubmittedAt != null)
                .Select(x => (x.Round, x.A, x.B, Minutes: Math.Abs((x.A.SubmittedAt!.Value - x.B.SubmittedAt!.Value).TotalMinutes)))
                .Where(x => x.Minutes <= t.TimingWindowMinutes).ToList();
            if (close.Count < t.TimingMinRounds) continue;
            var ids = close.Select(x => x.Round).ToList();
            yield return new RadarFinding(Id(SubmissionTiming, a, b), SubmissionTiming, "Offers repeatedly submitted minutes apart", "ReviewSignal", [a, b], TenderIds(s, ids), ids,
                close.SelectMany(x => new[] { new EvidenceRef("Bid", x.A.Id, $"{s.SupplierName(a)} {x.A.SubmittedAt:yyyy-MM-dd HH:mm}"), new EvidenceRef("Bid", x.B.Id, $"{s.SupplierName(b)} {x.B.SubmittedAt:yyyy-MM-dd HH:mm}") }).ToList(),
                new Dictionary<string, string> { ["closeRounds"] = close.Count.ToString(), ["sharedRounds"] = rounds.Count.ToString(), ["medianGapMinutes"] = Median(close.Select(x => x.Minutes)).ToString("0.0") },
                rounds.Count, $"Absolute difference between the submission times of the latest revisions; counted when within {t.TimingWindowMinutes} minutes.",
                s.Period.Label, "Many suppliers submit just before a deadline, so proximity near a deadline is common and weak evidence on its own.",
                $"In {close.Count} of {rounds.Count} shared rounds {s.SupplierName(a)} and {s.SupplierName(b)} submitted within {t.TimingWindowMinutes} minutes of each other.",
                ids.Select(r => s.Round(r)!.AvailableAt).Max());
        }
    }

    private static List<int> TenderIds(RadarSnapshot s, IEnumerable<int> rounds) => rounds.Select(r => s.Round(r)!.TenderId).Distinct().ToList();
    private static List<EvidenceRef> Rounds(RadarSnapshot s, IEnumerable<int> rounds) =>
        rounds.Select(r => { var round = s.Round(r)!; return new EvidenceRef("Round", r, $"{s.Tender(round.TenderId)?.Title} · round {round.RoundNumber}"); }).ToList();
    private static double Median(IEnumerable<double> values) { var v = values.Order().ToArray(); return v.Length == 0 ? 0 : v.Length % 2 == 1 ? v[v.Length / 2] : (v[v.Length / 2 - 1] + v[v.Length / 2]) / 2; }
    private static string Digits(string? value) => new((value ?? "").Where(char.IsDigit).ToArray());
    private static string Mask(string email) { var at = email.IndexOf('@'); return at <= 1 ? "•••" + email[at..] : email[0] + "•••" + email[at..]; }
    public static string NormalizeAddress(string? value) => Regex.Replace(Regex.Replace((value ?? "").ToLowerInvariant(), @"[^\p{L}\p{N}]+", " "), @"\s+", " ").Trim();

    // Stable identifiers: the same evidence always produces the same finding id, so links, briefings and reports stay valid.
    public static string Id(string detector, params object[] key)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(detector + "|" + string.Join("|", key.Select(k => k is int[] a ? string.Join(",", a) : k.ToString()))));
        var code = detector switch { SharedContact => "SC", JointParticipation => "JP", PriceGap => "PG", PriceSimilarity => "PS", AwardRotation => "AR", IdenticalDocument => "ID", SemanticDocument => "SD", _ => "ST" };
        return $"RF-{code}-{Convert.ToHexString(hash)[..8]}";
    }
}
