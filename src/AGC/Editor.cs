using System.Text;
using System.Windows.Automation;
using System.Windows.Automation.Text;

namespace Agc;

internal enum Outcome { NoSelection, Protected, Unchanged, Replaced, Cancelled, CopyResult }
internal readonly record struct EditResult(Outcome State, string? Text = null);

internal static class Editor
{
    // Não registramos caracteres de teclado. Só consultamos o trecho visível em
    // um editor identificável, depois de confirmar que é gravável e não protegido.
    internal static EditResult Run(nint window, bool manual, Corrector engine, RuleSettings rules, Func<bool> valid)
    {
        bool Current() => valid() && Native.GetForegroundWindow() == window;
        if (!Current()) return new(Outcome.Cancelled);
        nint focus = Native.Focus(window);
        if (focus == 0 || Native.GetAncestor(focus, 2) != window) return new(Outcome.NoSelection);
        if (Native.Class(focus).Equals("Edit", StringComparison.OrdinalIgnoreCase))
            return Plain(focus, manual, engine, rules, () => Current() && Native.Focus(window) == focus);
        return Accessible(window, manual, engine, rules, Current);
    }

    static bool Msg(nint h, uint code, nint w, nint l, out nint v) => Native.Message(h, code, w, l, 2, 150, out v) != 0;
    static (int start, int end, string text)? Snapshot(nint h)
    {
        // EM_GETSEL's packed result is valid below 65535. No pointers into another process.
        if (!Msg(h, 0xE, 0, 0, out var length) || length < 0 || length > 60_000) return null;
        if (!Msg(h, 0xB0, 0, 0, out var sel) || sel == -1) return null;
        var buffer = new StringBuilder((int)length + 1);
        if (Native.Read(h, 0xD, buffer.Capacity, buffer, 2, 150, out var actual) == 0 || actual != length) return null;
        var text = buffer.ToString();
        int start = (int)((long)sel & 65535), end = (int)(((long)sel >> 16) & 65535);
        return end >= start && end <= text.Length ? (start, end, text) : null;
    }
    internal static EditResult Plain(nint h, bool manual, Corrector engine, RuleSettings rules, Func<bool> valid)
    {
        // ES_PASSWORD, ES_READONLY e caracteres de máscara definidos dinamicamente.
        if (!valid()) return new(Outcome.Cancelled);
        if ((Native.Style(h) & 0x820) != 0 || !Msg(h, 0xD2, 0, 0, out var mask) || mask != 0) return new(Outcome.Protected);
        var snap = Snapshot(h); if (snap is not { } s) return new(Outcome.NoSelection);
        int start = s.start, end = s.end;
        bool paragraph = false;
        if (manual && start == end) return new(Outcome.NoSelection);
        if (!manual)
        {
            if (start != end || end == 0) return new(Outcome.Unchanged);
            if (end < s.text.Length && char.IsLetterOrDigit(s.text[end])) return new(Outcome.Unchanged);
            while (end > 0 && s.text[end - 1] is '\r' or '\n') { end--; paragraph = true; }
            if (!paragraph) while (end > 0 && !Boundary(s.text[end - 1])) end--;
            if (end == 0 || (!paragraph && s.text[end - 1] is '\n' or '\r')) return new(Outcome.Unchanged);
            start = s.text.LastIndexOfAny(new[] { '\r', '\n' }, end - 1) + 1;
            if (end - start > 1024) return new(Outcome.Unchanged);
        }
        string before = s.text[start..end], after = engine.Correct(before, rules, manual, paragraph, start == 0 || s.text[start - 1] is '\r' or '\n');
        if (before == after) return new(Outcome.Unchanged);
        if (!valid() || Snapshot(h) != snap || !valid()) return new(Outcome.Cancelled);
        int prefix = manual ? 0 : CommonPrefix(before, after);
        if (!Msg(h, 0xB1, start + prefix, end, out _) || !valid()) return new(Outcome.Cancelled);
        if (Native.Write(h, 0xC2, 1, after[prefix..], 2, 150, out _) == 0) return new(Outcome.Cancelled);
        int caret = manual ? start + after.Length : s.end + after.Length - before.Length;
        if (valid()) Msg(h, 0xB1, caret, caret, out _);
        return new(Outcome.Replaced);
    }
    static int CommonPrefix(string a, string b)
    {
        int n = 0; while (n < a.Length && n < b.Length && a[n] == b[n]) n++;
        if (n > 0 && char.IsHighSurrogate(a[n - 1])) n--;
        return n;
    }
    static bool Boundary(char c) => char.IsWhiteSpace(c) || ",.;:!?".Contains(c);

    static EditResult Accessible(nint window, bool manual, Corrector engine, RuleSettings rules, Func<bool> valid)
    {
        var el = AutomationElement.FocusedElement;
        if (el == null || !valid()) return new(Outcome.Cancelled);
        if (el.Current.IsPassword) return new(Outcome.Protected);
        var kind = el.Current.ControlType;
        if (kind != ControlType.Edit && kind != ControlType.Document) return new(Outcome.NoSelection);
        if (!el.TryGetCurrentPattern(TextPattern.Pattern, out var provider)) return new(Outcome.NoSelection);
        var tp = (TextPattern)provider;
        var sels = tp.GetSelection();
        if (sels.Length != 1) return new(Outcome.NoSelection);
        var range = sels[0].Clone();
        bool selected = range.CompareEndpoints(TextPatternRangeEndpoint.Start, range, TextPatternRangeEndpoint.End) != 0;
        if (selected != manual) return new(Outcome.NoSelection);
        if (!manual && (el.Current.IsEnabled == false || !Equals(range.GetAttributeValue(TextPattern.IsReadOnlyAttribute), false))) return new(Outcome.Protected);
        int moved = 0;
        if (!manual) moved = range.MoveEndpointByUnit(TextPatternRangeEndpoint.Start, TextUnit.Character, -512);
        string whole = range.GetText(manual ? 100001 : 2049);
        if (whole.Length == 0 || whole.Length > 100000) return new(Outcome.Unchanged);
        string before = whole;
        int trailingBreak = 0;
        if (!manual)
        {
            trailingBreak = whole.EndsWith("\r\n") ? 2 : whole[^1] is '\r' or '\n' ? 1 : 0;
            var paragraph = whole[..(whole.Length - trailingBreak)];
            int line = paragraph.LastIndexOfAny(new[] { '\n', '\r' }) + 1;
            if (line == 0 && moved == -512) return new(Outcome.Unchanged);
            before = paragraph[line..];
        }
        int complete = before.Length;
        if (!manual && trailingBreak == 0) while (complete > 0 && !Boundary(before[complete - 1])) complete--;
        string after = engine.Correct(before[..complete], rules, manual, trailingBreak != 0) + before[complete..];
        if (before == after) return new(Outcome.Unchanged);
        bool StillSelected()
        {
            if (!valid() || !el.Equals(AutomationElement.FocusedElement)) return false;
            var now = tp.GetSelection();
            return now.Length == 1 && now[0].Compare(sels[0]) && range.GetText(100001) == whole;
        }
        if (!StillSelected()) return new(Outcome.Cancelled);
        if (manual && (after.Contains('\r') || after.Contains('\n') || !Equals(range.GetAttributeValue(TextPattern.IsReadOnlyAttribute), false)))
            return new(Outcome.CopyResult, after);
        int prefix = manual ? 0 : CommonPrefix(before, after);
        var change = range.Clone();
        var skip = whole.Length - trailingBreak - before.Length + prefix;
        // Contagem UTF-16 pode diferir das unidades UIA (emoji/CRLF); confirme o texto exato.
        change.MoveEndpointByUnit(TextPatternRangeEndpoint.Start, TextUnit.Character, skip);
        if (trailingBreak != 0) change.MoveEndpointByUnit(TextPatternRangeEndpoint.End, TextUnit.Character, -1);
        if (change.GetText(100001) != before[prefix..] || Native.ModifiersDown() || !StillSelected()) return new(Outcome.Cancelled);
        change.Select();
        var check = tp.GetSelection();
        if (!valid() || !el.Equals(AutomationElement.FocusedElement) || check.Length != 1 || !check[0].Compare(change)) return new(Outcome.Cancelled);
        if (Native.TypeText(after[prefix..], trailingBreak != 0)) return new(Outcome.Replaced);
        // Uma falha de injeção não deve deixar o trecho selecionado para a próxima tecla.
        if (valid() && el.Equals(AutomationElement.FocusedElement) && tp.GetSelection() is [var current] && current.Compare(change))
            sels[0].Select();
        return new(Outcome.Cancelled);
    }
}
