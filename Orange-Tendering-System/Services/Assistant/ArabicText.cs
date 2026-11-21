using System.Text;

namespace Orange_Tendering_System.Services.Assistant;

// PDFsharp/MigraDoc place glyphs left-to-right and do not apply Arabic joining. For PDF output only, this converts a logical
// Arabic line into its visual form: contextual presentation forms (Unicode Arabic Presentation Forms-B, present in Arial and
// DejaVu Sans), lam-alef ligatures, and right-to-left run order with embedded Latin/digit runs kept left-to-right.
// Excel, Word and PowerPoint receive the original logical text because those applications shape Arabic themselves.
public static class ArabicText
{
    // isolated, final, initial, medial (0 = form does not exist)
    private static readonly Dictionary<char, int[]> Forms = new()
    {
        ['ء'] = [0xFE80, 0, 0, 0], ['آ'] = [0xFE81, 0xFE82, 0, 0], ['أ'] = [0xFE83, 0xFE84, 0, 0], ['ؤ'] = [0xFE85, 0xFE86, 0, 0],
        ['إ'] = [0xFE87, 0xFE88, 0, 0], ['ئ'] = [0xFE89, 0xFE8A, 0xFE8B, 0xFE8C], ['ا'] = [0xFE8D, 0xFE8E, 0, 0], ['ب'] = [0xFE8F, 0xFE90, 0xFE91, 0xFE92],
        ['ة'] = [0xFE93, 0xFE94, 0, 0], ['ت'] = [0xFE95, 0xFE96, 0xFE97, 0xFE98], ['ث'] = [0xFE99, 0xFE9A, 0xFE9B, 0xFE9C], ['ج'] = [0xFE9D, 0xFE9E, 0xFE9F, 0xFEA0],
        ['ح'] = [0xFEA1, 0xFEA2, 0xFEA3, 0xFEA4], ['خ'] = [0xFEA5, 0xFEA6, 0xFEA7, 0xFEA8], ['د'] = [0xFEA9, 0xFEAA, 0, 0], ['ذ'] = [0xFEAB, 0xFEAC, 0, 0],
        ['ر'] = [0xFEAD, 0xFEAE, 0, 0], ['ز'] = [0xFEAF, 0xFEB0, 0, 0], ['س'] = [0xFEB1, 0xFEB2, 0xFEB3, 0xFEB4], ['ش'] = [0xFEB5, 0xFEB6, 0xFEB7, 0xFEB8],
        ['ص'] = [0xFEB9, 0xFEBA, 0xFEBB, 0xFEBC], ['ض'] = [0xFEBD, 0xFEBE, 0xFEBF, 0xFEC0], ['ط'] = [0xFEC1, 0xFEC2, 0xFEC3, 0xFEC4], ['ظ'] = [0xFEC5, 0xFEC6, 0xFEC7, 0xFEC8],
        ['ع'] = [0xFEC9, 0xFECA, 0xFECB, 0xFECC], ['غ'] = [0xFECD, 0xFECE, 0xFECF, 0xFED0], ['ف'] = [0xFED1, 0xFED2, 0xFED3, 0xFED4], ['ق'] = [0xFED5, 0xFED6, 0xFED7, 0xFED8],
        ['ك'] = [0xFED9, 0xFEDA, 0xFEDB, 0xFEDC], ['ل'] = [0xFEDD, 0xFEDE, 0xFEDF, 0xFEE0], ['م'] = [0xFEE1, 0xFEE2, 0xFEE3, 0xFEE4], ['ن'] = [0xFEE5, 0xFEE6, 0xFEE7, 0xFEE8],
        ['ه'] = [0xFEE9, 0xFEEA, 0xFEEB, 0xFEEC], ['و'] = [0xFEED, 0xFEEE, 0, 0], ['ى'] = [0xFEEF, 0xFEF0, 0, 0], ['ي'] = [0xFEF1, 0xFEF2, 0xFEF3, 0xFEF4],
    };
    private static readonly Dictionary<char, (int Isolated, int Final)> LamAlef = new()
    {
        ['آ'] = (0xFEF5, 0xFEF6), ['أ'] = (0xFEF7, 0xFEF8), ['إ'] = (0xFEF9, 0xFEFA), ['ا'] = (0xFEFB, 0xFEFC)
    };

    public static bool HasArabic(string? s) => s != null && s.Any(c => c is >= '؀' and <= 'ۿ');
    private static bool IsTransparent(char c) => c is >= 'ً' and <= 'ٟ' or 'ٰ';
    private static bool JoinsForward(char c) => c == 'ـ' || (Forms.TryGetValue(c, out var f) && f[2] != 0);
    private static bool JoinsBackward(char c) => c == 'ـ' || (Forms.TryGetValue(c, out var f) && f[1] != 0);

    // Contextual shaping in logical order (diacritics are dropped: the presentation forms carry the letter shapes).
    public static string Shape(string text)
    {
        var chars = text.Where(c => !IsTransparent(c)).ToArray();
        var sb = new StringBuilder(chars.Length);
        for (var i = 0; i < chars.Length; i++)
        {
            var c = chars[i];
            var prevJoins = i > 0 && JoinsForward(chars[i - 1]) && JoinsBackward(c);
            if (c == 'ل' && i + 1 < chars.Length && LamAlef.TryGetValue(chars[i + 1], out var ligature))
            {
                sb.Append((char)(prevJoins ? ligature.Final : ligature.Isolated)); i++; continue;
            }
            if (!Forms.TryGetValue(c, out var forms)) { sb.Append(c); continue; }
            var nextJoins = i + 1 < chars.Length && JoinsForward(c) && JoinsBackward(chars[i + 1]);
            var form = prevJoins && nextJoins ? forms[3] : prevJoins ? forms[1] : nextJoins ? forms[2] : forms[0];
            sb.Append((char)(form == 0 ? forms[0] : form));
        }
        return sb.ToString();
    }

    private enum Dir { L, R, N }
    private static Dir Class(char c) =>
        c is >= '؀' and <= 'ۿ' or >= 'ﭐ' and <= '﷿' or >= 'ﹰ' and <= '﻿' ? Dir.R
        : char.IsLetterOrDigit(c) ? Dir.L : Dir.N;
    private static char Mirror(char c) => c switch { '(' => ')', ')' => '(', '[' => ']', ']' => '[', '{' => '}', '}' => '{', '<' => '>', '>' => '<', '«' => '»', '»' => '«', _ => c };

    // One line in visual order for a right-to-left paragraph. Neutral characters between two Latin/digit runs stay with them.
    public static string Visual(string line)
    {
        if (!HasArabic(line)) return line;
        var shaped = Shape(line);
        var classes = shaped.Select(Class).ToArray();
        for (var i = 0; i < classes.Length; i++)
        {
            if (classes[i] != Dir.N) continue;
            var before = Enumerable.Range(0, i).Reverse().Select(j => classes[j]).FirstOrDefault(d => d != Dir.N, Dir.R);
            var after = Enumerable.Range(i + 1, classes.Length - i - 1).Select(j => classes[j]).FirstOrDefault(d => d != Dir.N, Dir.R);
            classes[i] = before == Dir.L && after == Dir.L ? Dir.L : Dir.R;
        }
        var runs = new List<(Dir Dir, string Text)>();
        var start = 0;
        for (var i = 1; i <= shaped.Length; i++)
            if (i == shaped.Length || classes[i] != classes[start]) { runs.Add((classes[start], shaped[start..i])); start = i; }
        var sb = new StringBuilder(shaped.Length);
        for (var r = runs.Count - 1; r >= 0; r--)
            if (runs[r].Dir == Dir.L) sb.Append(runs[r].Text);
            else for (var i = runs[r].Text.Length - 1; i >= 0; i--) sb.Append(Mirror(runs[r].Text[i]));
        return sb.ToString();
    }

    // Wraps logical text to lines of at most maxChars (by words) and returns each line in visual order, first line first.
    public static List<string> VisualLines(string text, int maxChars)
    {
        var lines = new List<string>();
        foreach (var paragraph in text.Replace("\r", "").Split('\n'))
        {
            var current = new StringBuilder();
            foreach (var word in paragraph.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                if (current.Length > 0 && current.Length + 1 + word.Length > maxChars) { lines.Add(current.ToString()); current.Clear(); }
                if (current.Length > 0) current.Append(' ');
                current.Append(word);
            }
            lines.Add(current.ToString());
        }
        return lines.Select(Visual).ToList();
    }
}
