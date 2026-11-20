using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Orange_Tendering_System.Services.Radar;

// Numeric grounding guard. Every figure the model writes must appear in the verified facts it was given (tool results, page
// context or the user's own words), allowing only display rounding and percent/fraction conversion. Sentences with an
// unverifiable figure are flagged in the transcript and are not read aloud.
public sealed class RadarGrounding
{
    private static readonly Regex NumberPattern = new(@"(?<![\p{L}])[-+]?\d{1,3}(?:[,٬]\d{3})+(?:[.٫]\d+)?|[-+]?\d+(?:[.٫]\d+)?", RegexOptions.Compiled);
    private readonly List<double> facts = [];

    public void AddFacts(string? text)
    {
        if (string.IsNullOrEmpty(text)) return;
        foreach (var n in Numbers(text)) facts.Add(n);
    }

    public static string NormalizeDigits(string text)
    {
        var sb = new StringBuilder(text.Length);
        foreach (var c in text)
            sb.Append(c switch { >= '٠' and <= '٩' => (char)('0' + (c - '٠')), >= '۰' and <= '۹' => (char)('0' + (c - '۰')), _ => c });
        return sb.ToString();
    }

    private static string Clean(string raw) => raw.Replace(",", "").Replace("٬", "").Replace('٫', '.');

    public static IEnumerable<double> Numbers(string text)
    {
        foreach (Match m in NumberPattern.Matches(NormalizeDigits(text)))
            if (double.TryParse(Clean(m.Value), NumberStyles.Float, CultureInfo.InvariantCulture, out var n)) yield return Math.Abs(n);
    }

    public IReadOnlyList<string> Unverified(string sentence)
    {
        // List markers ("1." / "2)") at line starts are numbering, not figures.
        var text = Regex.Replace(NormalizeDigits(sentence), @"(^|\n)\s*\d{1,2}[.)]\s", "$1");
        var missing = new List<string>();
        foreach (Match m in NumberPattern.Matches(text))
        {
            var raw = Clean(m.Value);
            if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var n)) continue;
            n = Math.Abs(n);
            var decimals = raw.Contains('.') ? raw.Length - raw.IndexOf('.') - 1 : 0;
            var tolerance = 0.5 * Math.Pow(10, -decimals) + 1e-9;
            bool Close(double a, double b) => Math.Abs(a - b) <= tolerance || (b >= 1000 && Math.Abs(a - b) / b <= 0.005);
            // "1.2 million" / "1.2M" are spoken shorthands of a verified total.
            var after = text[(m.Index + m.Length)..].TrimStart();
            double? scaled = Regex.IsMatch(after, @"^(million|m\b|مليون)", RegexOptions.IgnoreCase) ? n * 1_000_000
                : Regex.IsMatch(after, @"^(thousand|k\b|ألف|الف)", RegexOptions.IgnoreCase) ? n * 1_000 : null;
            // Percent <-> fraction conversions keep the precision of the written figure (33.3% ~ 0.333, never 12 ~ 0.333).
            var ok = facts.Any(f => Close(n, f) || Math.Abs(n - f * 100) <= tolerance || Math.Abs(n / 100 - f) <= tolerance / 100
                || (scaled is { } s && Math.Abs(s - f) / Math.Max(f, 1) <= 0.05));
            if (!ok) missing.Add(m.Value);
        }
        return missing;
    }

    // Splits streamed text into complete sentences for the speech queue; returns the unfinished remainder.
    public static (List<string> Sentences, string Remainder) SplitSentences(string buffer)
    {
        var list = new List<string>();
        var start = 0;
        for (var i = 0; i < buffer.Length; i++)
        {
            var c = buffer[i];
            var boundary = c == '\n' || (c is '.' or '!' or '?' or '؟' or '؛' && i + 1 < buffer.Length && char.IsWhiteSpace(buffer[i + 1]));
            if (!boundary) continue;
            var sentence = buffer[start..(i + 1)].Trim();
            if (sentence.Length > 0) list.Add(sentence);
            start = i + 1;
        }
        return (list, buffer[start..]);
    }

    // Text sent to Piper: no markdown, no finding codes, readable currency names.
    public static string Speakable(string text, bool arabic)
    {
        var t = Regex.Replace(text, @"[*_#`>|]+", " ");
        t = Regex.Replace(t, @"\bRF-[A-Z]{2}-[0-9A-F]{8}\b", arabic ? "هذا النمط" : "this finding");
        t = Regex.Replace(t, @"\bJOD\b", arabic ? "دينار" : "Jordanian dinars");
        t = Regex.Replace(t, @"\bUSD\b", arabic ? "دولار" : "US dollars");
        t = Regex.Replace(t, @"\bEUR\b", arabic ? "يورو" : "euros");
        t = Regex.Replace(t, @"\(\s*id\s*\d+\s*\)", "", RegexOptions.IgnoreCase);
        return Regex.Replace(t, @"\s+", " ").Trim();
    }

    public static bool IsArabic(string? text) => !string.IsNullOrEmpty(text) && text.Count(c => c is >= '؀' and <= 'ۿ') > text.Count(char.IsLetter) / 3;
}
