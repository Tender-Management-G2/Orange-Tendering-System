using System.Text.Json;
using System.Text.RegularExpressions;
using Orange_Tendering_System.Models.Entities;

namespace Orange_Tendering_System.Services.Assistant;

internal sealed record DocumentAnalysisSection(string Heading, string Body);
internal sealed record SavedDocumentAnalysis(Guid[] UploadIds, bool Arabic, string Title, string Sources, DocumentAnalysisSection[] Sections);
internal sealed record DocumentExcerpt(int Source, AiChatFile File, string Text, bool Limited);

internal static class DocumentAnalysis
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private sealed record Finding(int Source, string? Explanation, string? Quote);
    private sealed record Findings(Finding?[]? Items);
    private static readonly Regex Number = new(@"\d+(?:[.,٫٬]\d+)*", RegexOptions.CultureInvariant);

    public static bool Requested(string message)
    {
        var text = AssistantIntentParser.Normalize(AssistantIntentParser.Strip(message));
        return Regex.IsMatch(text, @"\b(analy[sz]e|analysis|summari[sz]e|explain|review)\b|حلل|تحليل|لخص|تلخيص|اشرح(?:لي)?|فهمني");
    }

    public static ReportFormat DownloadChoice(string message) =>
        AssistantIntentParser.Normalize(AssistantIntentParser.Strip(message)).Trim(' ', '.', '!', '؟', '?') switch
        {
            "excel" or "exel" or "xlsx" or "اكسل" or "اكسيل" => ReportFormat.Xlsx,
            "pdf" or "بي دي اف" or "بدف" => ReportFormat.Pdf,
            "word" or "docx" or "وورد" or "ورد" => ReportFormat.Docx,
            _ => ReportFormat.None
        };

    public static DocumentAnalysisSection[] ReadFindings(string json, IReadOnlyList<DocumentExcerpt> sources, bool ar)
    {
        if (json.Length > 24_000) return [];
        Findings? output;
        try { output = JsonSerializer.Deserialize<Findings>(json, Json); }
        catch (JsonException) { return []; }
        var sections = new List<DocumentAnalysisSection>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var point in (output?.Items ?? []).Take(10))
        {
            if (point?.Explanation?.Trim() is not { Length: > 0 and <= 650 } explanation ||
                point.Quote?.Trim() is not { Length: >= 8 and <= 350 } quote) continue;
            var source = sources.FirstOrDefault(s => s.Source == point.Source);
            if (source == null || !source.Text.Contains(quote, StringComparison.Ordinal)) continue;
            var quotedNumbers = Number.Matches(AssistantIntentParser.Normalize(quote)).Select(m => m.Value).ToHashSet(StringComparer.Ordinal);
            if (Number.Matches(AssistantIntentParser.Normalize(explanation)).Any(m => !quotedNumbers.Contains(m.Value))) continue;
            if (!seen.Add(explanation)) continue;
            var offset = source.Text.IndexOf(quote, StringComparison.Ordinal);
            var location = Regex.Matches(source.Text[..offset], @"(?m)^\[(?:Page \d+|Sheet: [^\]\r\n]+)\]$").LastOrDefault()?.Value;
            var heading = ar ? $"النتيجة {sections.Count + 1}" : $"Finding {sections.Count + 1}";
            sections.Add(new(heading, $"{explanation}\n\n{(ar ? "الدليل" : "Evidence")} — {source.File.FileName} {location}\n“{quote}”"));
        }
        return [..sections];
    }
}
