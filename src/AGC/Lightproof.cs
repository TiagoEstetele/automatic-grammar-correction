// SPDX-License-Identifier: MPL-1.1
// Subconjunto adaptado do Lightproof pt_BR de Raimundo Santos Moura (2013).
// Origem e alterações em licenses/THIRD-PARTY.md. Os padrões são deliberadamente estreitos.
using System.Text.RegularExpressions;
namespace Agc;

internal static class Lightproof
{
    static Regex Pattern(string p) => new(p, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
    static readonly Regex Vontade = Pattern(@"\b(assim|lhe|lhes|me|não|nos|que|te|vezes|vos) da vontade de (?=\p{L}+r\b)");
    static readonly Regex Alto = Pattern(@"\b([Aa])ltos falantes\b");
    static readonly Regex Initial = Pattern(@"^([ \t]*no entanto)[ \t]+(?=\p{L})");
    static readonly Regex Medial = Pattern(@"(\p{L}) no entanto (?=\p{L})");
    // Risco contextual: discurso citado ou uso metalinguístico. Desativável em Gramática.
    internal static string Grammar(string s) => Alto.Replace(Vontade.Replace(s, "$1 dá vontade de "), "$1lto-falantes");
    // Só o conector "no entanto". Uma vírgula arbitrária pode mudar o sentido de uma frase.
    internal static string Commas(string s) => Medial.Replace(Initial.Replace(s, "$1, "), "$1, no entanto, ");
}
