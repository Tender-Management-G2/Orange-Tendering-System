using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using Orange_Tendering_System.Models.Entities;

namespace Orange_Tendering_System.Services.Radar;

// Document DNA: exact matching (SHA-256 of the stored bytes), text extraction through the existing ProtectedDocumentStore,
// and passage embeddings from the local qwen3-embedding model. Embeddings are cached on disk by text hash + model, so an
// unchanged document is never embedded twice. No vector database: cosine similarity is computed here in C#.
// Document text is untrusted data: it is compared numerically and shown as quoted excerpts, never executed or obeyed.
public sealed class RadarDocumentDna(IWebHostEnvironment environment, ProtectedDocumentStore store, RadarOllamaClient ollama,
    IOptions<RadarOptions> options, ILogger<RadarDocumentDna> logger)
{
    private static readonly ConcurrentDictionary<string, (long Length, DateTime Written, string Hash)> HashCache = new();
    private static readonly SemaphoreSlim EmbeddingGate = new(1, 1);
    private string CacheRoot => Path.Combine(environment.ContentRootPath, "App_Data", "RadarCache", "embeddings",
        Regex.Replace(options.Value.Models.EmbeddingModel, "[^A-Za-z0-9_.-]", "_"));

    public sealed record Passage(int Index, string Text, float[] Vector);
    public sealed record DocumentText(int DocumentId, string State, string? Text);
    public sealed record ComparedPassage(double Similarity, string TextA, string TextB, bool Template, IReadOnlyList<string> SharedPhrases);
    public sealed record Comparison(int DocumentA, int DocumentB, string FileA, string FileB, int SupplierA, int SupplierB, string? ShaA, string? ShaB, bool Identical,
        string StateA, string StateB, double? BestSimilarity, IReadOnlyList<ComparedPassage> Passages, int TemplatePassagesExcluded, string Notice);
    public sealed record SearchHit(int DocumentId, string FileName, int? SupplierId, string Supplier, int? TenderId, double Similarity, string Excerpt);

    public IReadOnlyList<DocFingerprint> Fingerprints(RadarSnapshot s)
    {
        var roundByBid = s.Bids.ToDictionary(b => b.Id, b => b.RoundId);
        return s.Documents.Select(d =>
        {
            var hash = Hash(d, out var state);
            var available = d.BidId is { } bid && s.Bids.FirstOrDefault(b => b.Id == bid) is { } b ? b.AvailableAt : d.UploadedAt;
            return new DocFingerprint(d.Id, d.FileName, d.Owner, d.SupplierId, d.BidId, d.TenderId, d.BidId is { } id ? roundByBid.GetValueOrDefault(id) : null, hash, state, available);
        }).ToList();
    }

    private string? Hash(RDocument d, out string state)
    {
        string path;
        try { path = PrivateDocumentPaths.Resolve(environment.ContentRootPath, d.StoragePath); }
        catch (WorkflowException) { state = "invalid path"; return null; }
        var info = new FileInfo(path);
        if (!info.Exists) { state = "file missing"; return null; }
        if (HashCache.TryGetValue(path, out var cached) && cached.Length == info.Length && cached.Written == info.LastWriteTimeUtc) { state = "ok"; return cached.Hash; }
        using var stream = info.OpenRead();
        var hash = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
        HashCache[path] = (info.Length, info.LastWriteTimeUtc, hash);
        state = "ok";
        return hash;
    }

    public async Task<DocumentText> TextAsync(RDocument d, CancellationToken ct)
    {
        var text = await store.Extract(new Document { Id = d.Id, FileName = d.FileName, StoragePath = d.StoragePath }, ct);
        // ProtectedDocumentStore marks unreadable content with a bracketed notice; never treat that notice as document text.
        if (text.StartsWith("[") && text.EndsWith("]")) return new DocumentText(d.Id, text.Trim('[', ']'), null);
        return string.IsNullOrWhiteSpace(text) ? new DocumentText(d.Id, "No text found", null) : new DocumentText(d.Id, "ok", text);
    }

    public static List<string> Chunk(string text, int minimum)
    {
        var paragraphs = Regex.Split(text.Replace("\r", ""), @"\n\s*\n|(?<=[.!?؟])\s+(?=[A-Z\p{IsArabic}])")
            .Select(p => Regex.Replace(p, @"\s+", " ").Trim()).Where(p => p.Length > 0).ToList();
        var chunks = new List<string>(); var current = new StringBuilder();
        foreach (var p in paragraphs)
        {
            if (current.Length > 0 && current.Length + p.Length > 600) { chunks.Add(current.ToString()); current.Clear(); }
            if (current.Length > 0) current.Append(' ');
            current.Append(p);
        }
        if (current.Length > 0) chunks.Add(current.ToString());
        return chunks.Where(c => c.Length >= minimum).Select(c => c.Length > 900 ? c[..900] : c).ToList();
    }

    public async Task<IReadOnlyList<Passage>> PassagesAsync(string text, CancellationToken ct)
    {
        var chunks = Chunk(text, options.Value.Thresholds.SemanticMinChunkCharacters);
        if (chunks.Count == 0) return [];
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\u0001", chunks)))).ToLowerInvariant();
        var file = Path.Combine(CacheRoot, key + ".json");
        if (File.Exists(file))
        {
            try
            {
                var cached = JsonSerializer.Deserialize<List<Passage>>(await File.ReadAllTextAsync(file, ct));
                if (cached != null && cached.Count == chunks.Count) return cached;
            }
            catch (JsonException) { logger.LogInformation("Ignoring an unreadable Orange Radar embedding cache entry."); }
        }
        await EmbeddingGate.WaitAsync(ct);
        try
        {
            var vectors = new List<float[]>();
            foreach (var batch in chunks.Chunk(16)) vectors.AddRange(await ollama.EmbedAsync(batch, ct));
            var passages = chunks.Select((c, i) => new Passage(i, c, Normalize(vectors[i]))).ToList();
            Directory.CreateDirectory(CacheRoot);
            await File.WriteAllTextAsync(file, JsonSerializer.Serialize(passages), ct);
            return passages;
        }
        finally { EmbeddingGate.Release(); }
    }

    // Tender-issued text (description, requirements and readable tender documents) defines what "template" wording is.
    private async Task<List<Passage>> TemplatePassagesAsync(RadarSnapshot s, int tenderId, CancellationToken ct)
    {
        var tender = s.Tender(tenderId);
        var result = new List<Passage>();
        if (tender == null) return result;
        result.AddRange(await PassagesAsync(tender.Description + "\n\n" + tender.Requirements, ct));
        foreach (var doc in s.Documents.Where(d => d.Owner == "Tender" && d.TenderId == tenderId))
            if ((await TextAsync(doc, ct)).Text is { } text) result.AddRange(await PassagesAsync(text, ct));
        return result;
    }

    public async Task<(IReadOnlyList<SemanticMatch> Matches, int Documents, int Readable)> SemanticMatchesAsync(RadarSnapshot s, CancellationToken ct)
    {
        var t = options.Value.Thresholds;
        var bidDocs = s.Documents.Where(d => d.Owner == "Bid" && d.SupplierId != null).ToList();
        var passages = new Dictionary<int, IReadOnlyList<Passage>>();
        foreach (var d in bidDocs)
            if ((await TextAsync(d, ct)).Text is { } text) passages[d.Id] = await PassagesAsync(text, ct);
        var templates = new Dictionary<int, List<Passage>>();
        foreach (var tenderId in bidDocs.Where(d => passages.ContainsKey(d.Id)).Select(d => d.TenderId!.Value).Distinct())
            templates[tenderId] = await TemplatePassagesAsync(s, tenderId, ct);
        var fingerprints = Fingerprints(s).ToDictionary(f => f.DocumentId);

        var matches = new List<SemanticMatch>();
        var readable = bidDocs.Where(d => passages.ContainsKey(d.Id)).ToList();
        for (var i = 0; i < readable.Count; i++)
            for (var j = i + 1; j < readable.Count; j++)
            {
                var (a, b) = (readable[i], readable[j]);
                if (a.SupplierId == b.SupplierId) continue;
                if (fingerprints[a.Id].Sha256 != null && fingerprints[a.Id].Sha256 == fingerprints[b.Id].Sha256) continue; // exact copies: detector 6
                var template = templates[a.TenderId!.Value].Concat(templates[b.TenderId!.Value]).ToList();
                var (best, compared, excluded) = BestPair(passages[a.Id], passages[b.Id], template, t.TemplateSimilarity);
                if (best == null) continue;
                matches.Add(new SemanticMatch(a.Id, b.Id, a.SupplierId!.Value, b.SupplierId!.Value, Math.Round(best.Value.Similarity, 4),
                    best.Value.A.Text, best.Value.B.Text, compared, excluded, new[] { fingerprints[a.Id].AvailableAt, fingerprints[b.Id].AvailableAt }.Max()));
            }
        return (matches, bidDocs.Count, readable.Count);
    }

    private static ((double Similarity, Passage A, Passage B)? Best, int Compared, int Excluded) BestPair(IReadOnlyList<Passage> a, IReadOnlyList<Passage> b, List<Passage> template, double templateThreshold)
    {
        bool IsTemplate(Passage p) => template.Any(t => Cosine(p.Vector, t.Vector) >= templateThreshold);
        var useA = a.Where(p => !IsTemplate(p)).ToList(); var useB = b.Where(p => !IsTemplate(p)).ToList();
        (double, Passage, Passage)? best = null; var compared = 0;
        foreach (var p in useA)
            foreach (var q in useB)
            {
                compared++;
                var sim = Cosine(p.Vector, q.Vector);
                if (best == null || sim > best.Value.Item1) best = (sim, p, q);
            }
        return (best, compared, a.Count - useA.Count + b.Count - useB.Count);
    }

    public async Task<Comparison> CompareAsync(RadarSnapshot s, int documentA, int documentB, bool semantic, CancellationToken ct)
    {
        var a = s.Documents.FirstOrDefault(d => d.Id == documentA) ?? throw new RadarNotFoundException("Document A is not available in this analysis scope.");
        var b = s.Documents.FirstOrDefault(d => d.Id == documentB) ?? throw new RadarNotFoundException("Document B is not available in this analysis scope.");
        var fa = Hash(a, out var hashStateA); var fb = Hash(b, out var hashStateB);
        var ta = await TextAsync(a, ct); var tb = await TextAsync(b, ct);
        var passages = new List<ComparedPassage>(); double? best = null; var excluded = 0;
        if (ta.Text != null && tb.Text != null && semantic)
        {
            var pa = await PassagesAsync(ta.Text, ct); var pb = await PassagesAsync(tb.Text, ct);
            var template = new List<Passage>();
            foreach (var tid in new[] { a.TenderId, b.TenderId }.Where(x => x != null).Distinct()) template.AddRange(await TemplatePassagesAsync(s, tid!.Value, ct));
            var th = options.Value.Thresholds.TemplateSimilarity;
            var pairs = pa.SelectMany(p => pb.Select(q => (Sim: Cosine(p.Vector, q.Vector), P: p, Q: q))).OrderByDescending(x => x.Sim).ToList();
            var usedA = new HashSet<int>(); var usedB = new HashSet<int>();
            foreach (var x in pairs)
            {
                if (passages.Count >= 4) break;
                if (!usedA.Add(x.P.Index) | !usedB.Add(x.Q.Index)) continue;
                var isTemplate = template.Any(t => Cosine(x.P.Vector, t.Vector) >= th || Cosine(x.Q.Vector, t.Vector) >= th);
                if (isTemplate) excluded++;
                passages.Add(new ComparedPassage(Math.Round(x.Sim, 4), x.P.Text, x.Q.Text, isTemplate, SharedPhrases(x.P.Text, x.Q.Text)));
            }
            best = passages.Where(p => !p.Template).Select(p => (double?)p.Similarity).Max();
        }
        else if (ta.Text != null && tb.Text != null)
            passages.Add(new ComparedPassage(0, Excerpt(ta.Text), Excerpt(tb.Text), false, SharedPhrases(ta.Text, tb.Text)));
        return new Comparison(a.Id, b.Id, a.FileName, b.FileName, a.SupplierId ?? 0, b.SupplierId ?? 0, fa, fb, fa != null && fa == fb,
            fa == null ? hashStateA : ta.State, fb == null ? hashStateB : tb.State, best, passages, excluded,
            "Similarity describes wording, not intent. Passages close to the tender's own text are marked as template and do not count.");
    }

    public async Task<IReadOnlyList<SearchHit>> SearchAsync(RadarSnapshot s, string query, int take, CancellationToken ct)
    {
        var vector = Normalize((await ollama.EmbedAsync([query], ct))[0]);
        var hits = new List<SearchHit>();
        foreach (var d in s.Documents.Where(d => d.Owner is "Bid" or "Tender"))
        {
            if ((await TextAsync(d, ct)).Text is not { } text) continue;
            var best = (await PassagesAsync(text, ct)).Select(p => (Sim: Cosine(vector, p.Vector), p.Text)).OrderByDescending(x => x.Sim).FirstOrDefault();
            if (best.Text == null) continue;
            hits.Add(new SearchHit(d.Id, d.FileName, d.SupplierId, d.SupplierId is { } sid ? s.SupplierName(sid) : "Tender document", d.TenderId, Math.Round(best.Sim, 4), Excerpt(best.Text)));
        }
        return hits.OrderByDescending(h => h.Similarity).Take(Math.Clamp(take, 1, 10)).ToList();
    }

    // Word sequences of 5+ words that appear in both passages (case-insensitive), longest first.
    public static IReadOnlyList<string> SharedPhrases(string a, string b)
    {
        static string[] Words(string x) => Regex.Matches(x.ToLowerInvariant(), @"[\p{L}\p{N}]+").Select(m => m.Value).ToArray();
        var wa = Words(a); var wb = Words(b);
        var grams = new HashSet<string>();
        for (var i = 0; i + 5 <= wb.Length; i++) grams.Add(string.Join(' ', wb[i..(i + 5)]));
        var phrases = new List<string>();
        var k = 0;
        while (k + 5 <= wa.Length)
        {
            if (!grams.Contains(string.Join(' ', wa[k..(k + 5)]))) { k++; continue; }
            var end = k + 5;
            while (end < wa.Length && grams.Contains(string.Join(' ', wa[(end - 4)..(end + 1)]))) end++;
            phrases.Add(string.Join(' ', wa[k..end])); k = end;
        }
        return phrases.OrderByDescending(p => p.Length).Take(6).ToList();
    }

    private static string Excerpt(string text) => text.Length <= 420 ? text : text[..420] + "…";
    private static float[] Normalize(float[] v) { var n = Math.Sqrt(v.Sum(x => (double)x * x)); return n == 0 ? v : v.Select(x => (float)(x / n)).ToArray(); }
    public static double Cosine(float[] a, float[] b)
    {
        if (a.Length != b.Length) return 0;
        double dot = 0; for (var i = 0; i < a.Length; i++) dot += a[i] * b[i];
        return dot; // vectors are normalized when stored
    }
}

public sealed class RadarNotFoundException(string message) : Exception(message);
