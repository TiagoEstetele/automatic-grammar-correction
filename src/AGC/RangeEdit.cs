using System.Windows.Automation;
using System.Windows.Automation.Text;
namespace Agc;

internal enum RangeOutcome { Cancelled, Confirmed, Sent }

// Uma única inserção no intervalo verificado. Sem Delete, Backspace ou Enter.
// Em segundo plano só é permitido trocar separadores que TERMINAM no cursor.
internal static class RangeEdit
{
    internal static RangeOutcome Apply(ReviewTarget target, LiveRange live, string replacement, bool automatic, Func<bool> valid)
    {
        if (!target.Writable || replacement.Length == 0 || replacement.Length > 100000 ||
            automatic && (target.WordResult != replacement || !Corrector.OnlySeparators(target.Original) ||
                          !Corrector.OnlySeparators(replacement) || target.Original.Length > 8 || replacement.Length > 8))
            return RangeOutcome.Cancelled;
        bool focused() => Native.GetForegroundWindow() == target.Window && Native.Focus(target.Window) == target.Focus &&
            live.Element.Equals(AutomationElement.FocusedElement) && !live.Element.Current.IsPassword;
        bool owns(TextPatternRange r) => live.Pattern.GetSelection() is [var now] && now.Compare(r);
        bool original() => live.Target.GetText(100001) == target.Original;
        if (!valid() || Native.ModifiersDown() || !focused() || !owns(live.Selection) || !original()) return RangeOutcome.Cancelled;
        bool temporary = !live.Selection.Compare(live.Target);
        bool sent = false;
        try
        {
            if (automatic && live.Target.CompareEndpoints(TextPatternRangeEndpoint.End, live.Selection, TextPatternRangeEndpoint.End) != 0)
                return RangeOutcome.Cancelled;
            if (temporary) live.Target.Select();
            if (!valid() || !focused() || !owns(live.Target) || !original() || !valid()) return RangeOutcome.Cancelled;
            sent = automatic ? Native.InsertSeparators(replacement) : Native.PasteSelection();
            if (!sent) return RangeOutcome.Cancelled;
            // Leitura limitada depois da inserção. Nunca repetir uma colagem sem confirmação.
            string normalized = replacement.Replace("\r\n", "\n").Replace('\r', '\n');
            var suffix = normalized.Length > 96 ? normalized[^96..] : normalized;
            for (int i = 0; i < 10 && valid() && focused(); i++)
            {
                if (live.Pattern.GetSelection() is [var caret] && caret.CompareEndpoints(TextPatternRangeEndpoint.Start, caret, TextPatternRangeEndpoint.End) == 0)
                {
                    var tail = caret.Clone(); tail.MoveEndpointByUnit(TextPatternRangeEndpoint.Start, TextUnit.Character, -(suffix.Length + 2));
                    if (tail.GetText(512).Replace("\r\n", "\n").Replace('\r', '\n').EndsWith(suffix, StringComparison.Ordinal))
                    {
                        // Em F8 sobre palavra, devolva o cursor ao sufixo que já existia (ex.: espaço).
                        if (temporary && !automatic && valid() && owns(caret)) live.Selection.Select();
                        return RangeOutcome.Confirmed;
                    }
                }
                Thread.Sleep(12);
            }
            return RangeOutcome.Sent;
        }
        finally
        {
            if (temporary && focused() && owns(live.Target) && original()) live.Selection.Select();
        }
    }
}
