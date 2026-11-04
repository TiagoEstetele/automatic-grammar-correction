using System.Text.RegularExpressions;
namespace Agc;

internal readonly record struct ChangeSpan(string Before, string After, bool Changed);
internal static class TextDiff
{
    // Compare tokens, including whitespace. Bound the LCS matrix for large selections.
    static readonly Regex Tokens = new(@"[\p{L}\p{M}\p{N}]+|\s+|.", RegexOptions.CultureInvariant);
    internal static List<ChangeSpan> Compare(string a, string b)
    {
        if (a == b) return [new(a, b, false)];
        var x = Tokens.Matches(a).Select(t => t.Value).ToArray(); var y = Tokens.Matches(b).Select(t => t.Value).ToArray();
        if ((long)(x.Length + 1) * (y.Length + 1) > 180000)
        {
            int left = 0, right = 0; while (left < x.Length && left < y.Length && x[left] == y[left]) left++;
            while (right < x.Length - left && right < y.Length - left && x[^(1 + right)] == y[^(1 + right)]) right++;
            string old = string.Concat(x[left..(x.Length - right)]), result = string.Concat(y[left..(y.Length - right)]);
            return [new(string.Concat(x[..left]), string.Concat(x[..left]), false), new(old, result, true), new(string.Concat(x[(x.Length - right)..]), string.Concat(y[(y.Length - right)..]), false)];
        }
        var l = new int[x.Length + 1, y.Length + 1];
        for (int i = x.Length - 1; i >= 0; i--) for (int j = y.Length - 1; j >= 0; j--) l[i, j] = x[i] == y[j] ? 1 + l[i + 1, j + 1] : Math.Max(l[i + 1, j], l[i, j + 1]);
        List<ChangeSpan> spans = []; var oldBuf = new System.Text.StringBuilder(); var newBuf = new System.Text.StringBuilder();
        void Flush() { if (oldBuf.Length + newBuf.Length > 0) { spans.Add(new(oldBuf.ToString(), newBuf.ToString(), true)); oldBuf.Clear(); newBuf.Clear(); } }
        int m = 0, n = 0;
        while (m < x.Length || n < y.Length)
        {
            if (m < x.Length && n < y.Length && x[m] == y[n]) { Flush(); spans.Add(new(x[m], y[n], false)); m++; n++; }
            else if (n < y.Length && (m == x.Length || l[m, n + 1] >= l[m + 1, n])) newBuf.Append(y[n++]); else oldBuf.Append(x[m++]);
        }
        Flush(); return spans;
    }
    internal static string Explain(ChangeSpan s)
    {
        var a = s.Before.Trim(); var b = s.After.Trim();
        if (a.Equals("tem", StringComparison.OrdinalIgnoreCase) && b.Equals("têm", StringComparison.OrdinalIgnoreCase)) return "concordância com sujeito plural";
        if (a.Equals(b, StringComparison.OrdinalIgnoreCase) && a != b) return "maiúscula da frase";
        if (a.Length + b.Length == 0) return "espaçamento";
        if ((a + b).All(c => char.IsPunctuation(c) || char.IsWhiteSpace(c))) return "pontuação";
        return "revisão em português";
    }
}
