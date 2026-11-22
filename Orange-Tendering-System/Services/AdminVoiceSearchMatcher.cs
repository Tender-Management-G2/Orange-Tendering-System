using System.Text;
using System.Text.RegularExpressions;

namespace Orange_Tendering_System.Services;

internal sealed class AdminVoiceSearchMatcher(string term, bool forDepartments = false)
{
    private static readonly Regex Words = new(@"[\p{L}\p{Nd}][\p{L}\p{Nd}\p{M}]*", RegexOptions.CultureInvariant);
    private readonly string[] spoken = Tokens(Regex.Replace(forDepartments ? Regex.Replace(term, @"^\s*قسم\s+", "") : term, @"(?<!\p{L})دي\s+[اآأإ]ر(?!\p{L})", "dr"));
    private string? best;
    private int bestScore;
    private bool ambiguous;

    public string Resolve() => ambiguous ? term : best ?? term;

    public void Consider(string? label)
    {
        if (string.IsNullOrWhiteSpace(label) || spoken.Length == 0) return;
        var matches = Words.Matches(label);
        for (var start = 0; start + spoken.Length <= matches.Count; start++)
        {
            var score = 0;
            for (var offset = 0; offset < spoken.Length; offset++)
            {
                var rank = Rank(spoken[offset], matches[start + offset].Value, forDepartments);
                if (rank == 0) { score = 0; break; }
                score += rank;
            }

            if (score == 0 || score < bestScore) continue;
            var first = matches[start];
            var last = matches[start + spoken.Length - 1];
            var match = label[first.Index..(last.Index + last.Length)];
            if (score > bestScore)
            {
                bestScore = score;
                best = match;
                ambiguous = false;
            }
            else if (!string.Equals(best, match, StringComparison.OrdinalIgnoreCase))
            {
                ambiguous = true;
            }
        }
    }

    private static string[] Tokens(string value) => Words.Matches(value).Select(match => match.Value).ToArray();

    private static int Rank(string spoken, string recorded, bool forDepartments)
    {
        if (string.Equals(spoken, recorded, StringComparison.OrdinalIgnoreCase)) return 3;
        var left = Fold(spoken);
        var right = Fold(recorded);
        if (left == right) return 2;
        if (forDepartments && right == "marketing" && left is ("tswyq" or "altswyq")) return 2;
        var leftKey = Consonants(left);
        var rightKey = Consonants(right);
        // English -ing loanwords can be transcribed with a final ج or غ.
        if (forDepartments)
        {
            leftKey = NormalizeDepartmentEnding(leftKey);
            rightKey = NormalizeDepartmentEnding(rightKey);
        }
        return leftKey.Length >= 2 && leftKey == rightKey ? 1 : 0;
    }

    private static string Fold(string value)
    {
        var folded = new StringBuilder();
        foreach (var letter in value.ToLowerInvariant().Normalize(NormalizationForm.FormD))
        {
            if (char.GetUnicodeCategory(letter) is System.Globalization.UnicodeCategory.NonSpacingMark || letter == 'ـ') continue;
            folded.Append(letter switch
            {
                'ا' => "a", 'ب' => "b", 'ت' or 'ط' => "t", 'ث' => "th",
                'ج' => "j", 'ح' or 'ه' or 'ة' => "h", 'خ' => "kh",
                'د' or 'ض' => "d", 'ذ' => "dh", 'ر' => "r", 'ز' or 'ظ' => "z",
                'س' or 'ص' => "s", 'ش' => "sh", 'ع' => "a", 'غ' => "gh",
                'ف' => "f", 'ق' => "q", 'ك' => "k", 'ل' => "l", 'م' => "m",
                'ن' => "n", 'و' => "w", 'ي' or 'ى' => "y", 'ء' => "",
                _ => letter.ToString()
            });
        }
        var normalized = folded.ToString();
        return normalized switch { "dktwr" or "dktwrh" => "dr", _ => normalized };
    }

    private static string NormalizeDepartmentEnding(string key) =>
        key.EndsWith("ngh", StringComparison.Ordinal) ? key[..^1] :
        key.EndsWith("nj", StringComparison.Ordinal) ? key[..^1] + "g" : key;

    private static string Consonants(string value)
    {
        // Latin spelling often supplies vowels that are implicit in an Arabic name.
        value = value.TrimEnd('h');
        return new string(value.Where(letter => !"aeiouwy".Contains(letter)).ToArray());
    }
}
