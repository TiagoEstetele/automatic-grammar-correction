using System.Text.RegularExpressions;
using WeCantSpell.Hunspell;
using System.IO;

namespace Agc;

public sealed class RuleSettings
{
    public bool Spacing { get; set; } = true;
    public bool Capitals { get; set; } = true;
    public bool Accents { get; set; } = true;
    public bool Grammar { get; set; } = true;
    public bool Commas { get; set; } = true;
    public bool FinalStop { get; set; } = true;
    public bool Spelling { get; set; } // Sugestões aproximadas podem trocar nomes legítimos.
    public bool WordSpelling { get; set; } = true;
}

public sealed class Corrector
{
    readonly Lazy<WordList> dictionary;
    public bool DictionaryLoaded => dictionary.IsValueCreated;
    public Corrector(string path) => dictionary = new(() => WordList.CreateFromFiles(path));
    static readonly Regex Words = Rx(@"[\p{L}\p{M}]+");
    static readonly Regex Protected = Rx(@"(?im)^\s*(?:[#>|]|[-*]\s|\d+[.)]\s).*?$|(?:\x60[^\r\n\x60]*\x60)|(?:https?://|www\.)[^\s]+|[\w.+-]+@[\w.-]+\.\w+|(?:[a-z]:\\|\\\\)[^\s]+|\b\d+(?:[.,:/-]\d+)+\b|\b[\w-]+\.(?:com|org|br|net|exe|md|json)\b|[\w]+_[\w]+");
    static readonly Regex CodeLine = Rx(@"(?i)^\s*(?:function|const|using|class|import|git|SELECT)\s|(?:=>|[{};]\s*$|</?\w+>|:=)");
    static Regex Rx(string pattern) => new(pattern, RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(180));
    static readonly HashSet<string> Markers = new("eu meu minha você voce voces vocês não nao também tambem estou hoje nós nos para uma são sao com está esta isso essa que obrigado obrigada olá ola entanto amanhã amanha relatório preciso atenção atenção temos português portugues vontade eles elas gente do da dos das não".Split(' '), StringComparer.OrdinalIgnoreCase);
    static readonly Dictionary<string, string> AccentMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["voce"] = "você",
        ["voces"] = "vocês",
        ["nao"] = "não",
        ["tambem"] = "também",
        ["amanha"] = "amanhã",
        ["portugues"] = "português",
        ["ninguem"] = "ninguém",
        ["alguem"] = "alguém",
        ["parabens"] = "parabéns",
        ["informacao"] = "informação",
        ["informacoes"] = "informações",
        ["correcao"] = "correção",
        ["relatorio"] = "relatório",
        ["entao"] = "então",
        ["ola"] = "olá"
    };
    static readonly Regex Abbrev = Rx(@"(?i)\b(?:sr|sra|dr|dra|prof|etc|p\.ex|ex|art|av|pág)\.$|(?:\b\p{L}\.){2,}$");
    static readonly Regex SentenceStart = Rx(@"(^[ \t""“(]*|[.!?][ \t]+[""“(]*)(\p{Ll})");
    static readonly Regex RepeatedSpaces = Rx(@"(?<=\S)[ \t]{2,}(?=\S)");
    static readonly Regex BeforeMark = Rx(@"(?<=\S)[ \t]+([,;:!?.])");
    static readonly Regex AfterMark = Rx(@"([,;:!?])(?=\p{L})|(?<=\p{Ll})(\.)(?=\p{Ll}{2,}\b)");
    static readonly Regex EligibleEnd = Rx(@"\p{L}[\p{L}\p{M}]*[ \t]*$");
    static readonly Regex PluralTer = Rx(@"(?i)\b(eles|elas|vocês) (tem)\b");
    static readonly Regex NosVai = Rx(@"(?i)\b(nós) (vai)\b");
    static readonly Regex GenteVamos = Rx(@"(?i)\b(a gente) (vamos)\b");
    static readonly Regex LastWord = Rx(@"[ \t,;:.!?]*(?<word>[\p{L}\p{M}]+)[ \t,;:.!?]*\z");
    // Conectores no início de uma oração. Não inferir pausas entre sujeito e verbo.
    static readonly Regex Intro = Rx(@"(?i)(^|[.!?][ \t]+)([ \t]*(?:no entanto|além disso|por outro lado|ou seja|isto é))[ \t]+(?=\p{L}|$)");
    static readonly Regex IsIntro = Rx(@"(?i)^\s*(?:no entanto|além disso|por outro lado|ou seja|isto é)\b");
    static readonly Regex Question = Rx(@"(?i)^\s*(?:por que|como faço para|onde (?:fica|posso)|quando (?:posso|será)|qual (?:é|seria)|você (?:pode|poderia|consegue))\b");
    static readonly Dictionary<string, string> FrequentTypos = new(StringComparer.OrdinalIgnoreCase)
    {
        ["excessão"] = "exceção",
        ["excessões"] = "exceções",
        ["concerteza"] = "com certeza",
        ["precionar"] = "pressionar",
        ["resposivo"] = "responsivo",
        ["problmea"] = "problema",
        ["nescessário"] = "necessário",
        ["nescessario"] = "necessário",
        ["menas"] = "menos"
    };
    public readonly record struct WordChange(int Start, int End, string Before, string After);
    internal static bool OnlySeparators(string text) => text.All(c => " ,;:.!?".Contains(c));
    // Em um editor acessível, só a pontuação/espaços imediatamente anteriores ao
    // cursor podem ser substituídos sem ação do usuário. Nunca selecionar letras.
    internal WordChange? TrailingPunctuation(string prefix, RuleSettings rules, bool beginsLine = true)
    {
        int start = prefix.LastIndexOfAny(['\r', '\n']) + 1;
        string line = prefix[start..];
        if (line.Length == 0 || line.Length > 256 || !OnlySeparators(line[^1..]) || !rules.Spacing && !rules.Commas) return null;
        var only = ReviewOptions.Filter(rules, ReviewKind.Punctuation);
        only.Capitals = false; only.FinalStop = false;
        var after = Correct(line, only, false, startsAtBoundary: beginsLine || start > 0);
        after = SpaceAfterTypedComma(line, after, rules);
        if (line == after) return null;
        int common = 0; while (common < line.Length && common < after.Length && line[common] == after[common]) common++;
        string before = line[common..], replacement = after[common..];
        if (before.Length > 8 || replacement.Length is 0 or > 8 || !OnlySeparators(before) || !OnlySeparators(replacement)) return null;
        return new(start + common, prefix.Length, before, replacement);
    }
    static string SpaceAfterTypedComma(string source, string corrected, RuleSettings rules)
    {
        if (!rules.Spacing || !Words.Matches(source).Any(m => Markers.Contains(m.Value)) || CodeLine.IsMatch(source)) return corrected;
        int comma = corrected.TrimEnd(' ').Length - 1;
        if (comma < 1 || corrected[comma] != ',' || !char.IsLetter(corrected[comma - 1]) ||
            Protected.Matches(source).Any(m => m.Index + m.Length >= source.TrimEnd(' ').Length)) return corrected;
        return corrected[..(comma + 1)] + " ";
    }

    // O contexto determina idioma/concordância, mas a edição só abrange a última
    // palavra concluída e sua pontuação. Nunca reescreva o começo do parágrafo.
    public WordChange? CompletedWord(string text, int caret, RuleSettings rules, bool startsAtBoundary = true)
    {
        var stamp = System.Diagnostics.Stopwatch.GetTimestamp();
        var result = FindWord(text, caret, rules, startsAtBoundary, out bool checkedWord);
        if (checkedWord) WordChecked?.Invoke(System.Diagnostics.Stopwatch.GetElapsedTime(stamp).TotalMilliseconds);
        return result;
    }
    internal event Action<double>? WordChecked;
    WordChange? FindWord(string text, int caret, RuleSettings rules, bool startsAtBoundary, out bool checkedWord)
    {
        checkedWord = false;
        if (caret <= 0 || caret > text.Length) return null;
        int end = caret;
        while (end > 0 && text[end - 1] is '\n' or '\r') end--;
        bool paragraph = end != caret;
        if (!paragraph)
        {
            while (end > 0 && char.IsLetterOrDigit(text[end - 1])) end--;
            if (end == 0 || !(" \t,;:.!?".Contains(text[end - 1]))) return null;
        }
        if (end == 0) return null;
        int start = text.LastIndexOfAny(['\r', '\n'], end - 1) + 1;
        if (end - start > 256)
        {
            start = text.IndexOf(' ', end - 256, 256);
            if (start < 0) return null;
            start++;
            startsAtBoundary = false;
        }
        var context = text[start..end];
        var last = LastWord.Match(context);
        if (!last.Success || CodeLine.IsMatch(context) || Protected.Matches(context).Any(m =>
            m.Index < last.Groups["word"].Index + last.Groups["word"].Length && m.Index + m.Length > last.Groups["word"].Index)) return null;
        var token = last.Groups["word"];
        checkedWord = true;
        // Formas revisadas não precisam esperar a carga inicial do dicionário.
        // Use o caminho contextual quando a pontuação do entorno também precisa mudar.
        string? exact = null;
        if (rules.Accents) AccentMap.TryGetValue(token.Value, out exact);
        if (exact == null && rules.WordSpelling) FrequentTypos.TryGetValue(token.Value, out exact);
        if (exact != null && !paragraph && last.Value.Trim() == token.Value && !context.Contains("  ") && !context.Contains("no entanto", StringComparison.OrdinalIgnoreCase))
        {
            string replacement = MatchCase(token.Value, exact);
            var left = context[..token.Index].TrimEnd();
            bool sentence = left.Length == 0 ? startsAtBoundary && (start == 0 || text[start - 1] is '\r' or '\n')
                : left[^1] is '!' or '?' || (left[^1] == '.' && !Abbrev.IsMatch(left));
            if (rules.Capitals && sentence) replacement = char.ToUpperInvariant(replacement[0]) + replacement[1..];
            if (replacement != token.Value) return new(start + token.Index, start + token.Index + token.Length, token.Value, replacement);
        }
        string corrected = Correct(context, rules, false, paragraph, start == 0 ? startsAtBoundary : text[start - 1] is '\r' or '\n');
        if (!paragraph && end == caret) corrected = SpaceAfterTypedComma(context, corrected, rules);
        var next = LastWord.Match(corrected);
        if (!next.Success) return null;
        string updated = next.Value;
        if (rules.WordSpelling && token.Length is >= 5 and <= 26 && token.Value.All(char.IsLower) &&
            Words.Matches(context).Any(m => Markers.Contains(m.Value)) && !dictionary.Value.Check(token.Value))
        {
            var plausible = dictionary.Value.Suggest(token.Value).Where(s => s.All(char.IsLetter) && OneTypo(token.Value, s)).Take(2).ToArray();
            if (plausible.Length == 1) updated = updated.Remove(next.Groups["word"].Index - next.Index, next.Groups["word"].Length)
                .Insert(next.Groups["word"].Index - next.Index, MatchCase(next.Groups["word"].Value, plausible[0]));
        }
        return updated == last.Value ? null : new(start + last.Index, end, last.Value, updated);
    }
    static bool OneTypo(string a, string b)
    {
        if (a == b || Math.Abs(a.Length - b.Length) > 1) return false;
        int i = 0;
        while (i < Math.Min(a.Length, b.Length) && a[i] == b[i]) i++;
        if (a.Length != b.Length)
        {
            var longer = a.Length > b.Length ? a : b;
            var shorter = a.Length > b.Length ? b : a;
            return longer.AsSpan(i + 1).SequenceEqual(shorter.AsSpan(i));
        }
        return a.AsSpan(i + 1).SequenceEqual(b.AsSpan(i + 1)) ||
            (i + 1 < a.Length && a[i] == b[i + 1] && a[i + 1] == b[i] && a.AsSpan(i + 2).SequenceEqual(b.AsSpan(i + 2)));
    }
    static string MatchCase(string old, string value) =>
        old.All(char.IsUpper) ? value.ToUpperInvariant() : char.IsUpper(old[0]) ? char.ToUpperInvariant(value[0]) + value[1..] : value;

    public string Correct(string text, RuleSettings rules, bool manual, bool paragraphComplete = false, bool startsAtBoundary = true)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length > 100_000) return text;
        // Preserva as quebras originais (inclusive CRLF) e não entra em blocos Markdown.
        bool fenced = false;
        var parts = Regex.Split(text, @"(\r\n|\r|\n)");
        for (int i = 0; i < parts.Length; i += 2)
        {
            var line = parts[i];
            if (line.TrimStart().StartsWith("\x60\x60\x60") || line.TrimStart().StartsWith("~~~")) { fenced = !fenced; continue; }
            if (fenced || line.Length > 8192 || CodeLine.IsMatch(line)) continue;
            var protectedTokens = new List<string>();
            if (line.Contains('\uE000') || line.Contains('\uE001')) continue;
            line = Protected.Replace(line, m => { protectedTokens.Add(m.Value); return "\uE000" + (protectedTokens.Count - 1) + "\uE001"; });
            var words = Words.Matches(line).Select(m => m.Value).ToArray();
            // Evidência lexical, nunca o layout do teclado. Trechos ambíguos ficam intactos.
            int markers = words.Count(Markers.Contains) + (IsIntro.IsMatch(line) || Question.IsMatch(line) ? 1 : 0);
            if (!manual && markers == 1 && words.Length == 1 && words[0].ToLowerInvariant() is "do" or "da" or "nos" or "que" or "para" or "ola" or "com") continue;
            if (words.Length == 0 || (markers == 0 && (!manual || words.Length < 2))) continue;
            var lexicon = dictionary.Value;
            int known = words.Count(w => Markers.Contains(w) || AccentMap.ContainsKey(w) || lexicon.Check(w.ToLowerInvariant()));
            if (known * 2 < words.Length || (markers == 0 && known != words.Length)) continue;
            if (rules.Accents || (manual && rules.Spelling))
                line = Words.Replace(line, m =>
                {
                    if (rules.Accents && AccentMap.TryGetValue(m.Value, out var accent) && lexicon.Check(accent))
                        return MatchCase(m.Value, accent);
                    // Opt-in, sob demanda: só uma sugestão, nenhuma para siglas e nomes capitalizados.
                    if (!manual || !rules.Spelling || m.Length < 4 || m.Length > 32 || char.IsUpper(m.Value[0]) || lexicon.Check(m.Value)) return m.Value;
                    var options = lexicon.Suggest(m.Value).Take(2).ToArray();
                    return options.Length == 1 ? options[0] : m.Value;
                });
            if (rules.Grammar)
            {
                // Concordância apenas com sujeito explícito. Há risco em citações ou
                // transcrições coloquiais; desabilite Gramática para preservá-las.
                line = Lightproof.Grammar(line);
                line = PluralTer.Replace(line, m => m.Groups[1].Value + " " + MatchCase(m.Groups[2].Value, "têm"));
                line = NosVai.Replace(line, m => m.Groups[1].Value + " " + MatchCase(m.Groups[2].Value, "vamos"));
                line = GenteVamos.Replace(line, m => m.Groups[1].Value + " " + MatchCase(m.Groups[2].Value, "vai"));
            }
            if (rules.Commas)
            {
                line = Lightproof.Commas(line);
                line = Intro.Replace(line, m => m.Index == 0 && i == 0 && !startsAtBoundary ? m.Value : m.Groups[1].Value + m.Groups[2].Value + ", ");
            }
            if (rules.Spacing)
            {
                line = RepeatedSpaces.Replace(line, " ");
                line = BeforeMark.Replace(line, "$1");
                line = AfterMark.Replace(line, m => m.Value + " ");
            }
            if (rules.Capitals) line = SentenceStart.Replace(line, m =>
            {
                if (m.Index == 0 && i == 0 && !startsAtBoundary) return m.Value;
                if (m.Value.StartsWith('.') && Abbrev.IsMatch(line[..(m.Index + 1)])) return m.Value;
                return m.Groups[1].Value + m.Groups[2].Value.ToUpperInvariant();
            });
            // Só ao concluir um parágrafo ou por solicitação. Uma pausa ao digitar não encerra a frase.
            if (rules.FinalStop && (manual || paragraphComplete || i + 2 < parts.Length) && words.Length >= 2 && EligibleEnd.IsMatch(line))
                line = line.TrimEnd(' ', '\t') + (Question.IsMatch(line) ? "?" : ".") + line[line.TrimEnd(' ', '\t').Length..];
            parts[i] = Regex.Replace(line, "\uE000([0-9]+)\uE001", m => protectedTokens[int.Parse(m.Groups[1].Value)]);
        }
        return string.Concat(parts);
    }
}
