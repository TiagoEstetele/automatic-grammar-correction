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
    internal static EditResult Run(nint window, bool manual, Corrector engine, RuleSettings rules, Func<bool> valid, string? executable = null)
    {
        bool Current() => valid() && Native.GetForegroundWindow() == window;
        if (!Current()) return new(Outcome.Cancelled);
        nint focus = Native.Focus(window);
        if (focus == 0 || Native.GetAncestor(focus, 2) != window) return new(Outcome.NoSelection);
        return ForControl(focus, executable, manual, engine, rules, () => Current() && Native.Focus(window) == focus);
    }

    internal static EditResult ForControl(nint focus, string? executable, bool manual, Corrector engine, RuleSettings rules, Func<bool> valid)
    {
        // O compositor do Codex e editores via acessibilidade não oferecem uma
        // substituição atômica. Nunca abra seleção nem simule teclas nesses campos.
        bool codex = string.Equals(System.IO.Path.GetFileNameWithoutExtension(executable), "Codex", StringComparison.OrdinalIgnoreCase);
        if (!codex && IsPlain(focus))
            return Plain(focus, manual, engine, rules, valid);
        return manual ? ReadSelected(engine, rules, valid) : new(Outcome.Unchanged);
    }
    internal static bool IsPlain(nint h) => Native.Class(h) is var name &&
        (name.Equals("Edit", StringComparison.OrdinalIgnoreCase) || name.StartsWith("WindowsForms10.EDIT.", StringComparison.OrdinalIgnoreCase));
    internal static ReviewTarget? SelectedTarget(nint window, nint focus)
    {
        if ((Native.Style(focus) & 0x20) != 0 || !Msg(focus, 0xD2, 0, 0, out var mask) || mask != 0) return null;
        if (Snapshot(focus) is not { } s || s.start == s.end || !Msg(focus, 0xD6, s.start, 0, out var pos) || pos == -1) return null;
        var point = new Point((short)((long)pos & 65535), (short)((long)pos >> 16));
        if (!Native.ClientToScreen(focus, ref point)) return null;
        return new(window, focus, s.start + ":" + s.end, s.text[s.start..s.end], new Rectangle(point, new Size(1, 20)),Writable:(Native.Style(focus)&0x800)==0);
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
    internal static EditResult Plain(nint h, bool manual, Corrector engine, RuleSettings rules, Func<bool> valid, ReviewTarget? expected = null, string? reviewed = null)
    {
        // ES_PASSWORD, ES_READONLY e caracteres de máscara definidos dinamicamente.
        if (!valid()) return new(Outcome.Cancelled);
        if ((Native.Style(h) & 0x820) != 0 || !Msg(h, 0xD2, 0, 0, out var mask) || mask != 0) return new(Outcome.Protected);
        var snap = Snapshot(h); if (snap is not { } s) return new(Outcome.NoSelection);
        int start = s.start, end = s.end;
        string before, after;
        if (manual)
        {
            if (start == end) return new(Outcome.NoSelection);
            before = s.text[start..end];
            if (expected != null && (expected.Focus != h || expected.Identity != start + ":" + end || before != expected.Original)) return new(Outcome.Cancelled);
            after = reviewed ?? engine.Correct(before, rules, true, startsAtBoundary: start == 0 || s.text[start - 1] is '\r' or '\n');
        }
        else
        {
            if (start != end || (end < s.text.Length && char.IsLetterOrDigit(s.text[end]))) return new(Outcome.Unchanged);
            var word = engine.CompletedWord(s.text, end, rules);
            if (word is not { } w) return new(Outcome.Unchanged);
            (start, end, before, after) = (w.Start, w.End, w.Before, w.After);
        }
        if (before == after) return new(Outcome.Unchanged);
        if (!valid() || Snapshot(h) != snap || !valid()) return new(Outcome.Cancelled);
        // Não exceda o limite do controle: ele pode aceitar só parte do texto.
        if (!Msg(h, 0xD5, 0, 0, out var limit) || (long)s.text.Length - before.Length + after.Length > (long)limit)
            return manual ? new(Outcome.CopyResult, after) : new(Outcome.Cancelled);
        int prefix = manual ? 0 : CommonPrefix(before, after);
        int suffix = 0;
        if (!manual)
        {
            while (suffix < Math.Min(before.Length, after.Length) - prefix && before[^(suffix + 1)] == after[^(suffix + 1)]) suffix++;
            if (suffix > 0 && char.IsLowSurrogate(before[^suffix])) suffix--;
        }
        int from = start + prefix, to = end - suffix;
        bool temporary = from != s.start || to != s.end;
        if (temporary && !Msg(h, 0xB1, from, to, out _)) return new(Outcome.Cancelled);
        bool StillOurs() => Snapshot(h) == (from, to, s.text);
        void ReleaseSelection()
        {
            // Mesmo com o trabalho cancelado, libere a seleção criada pelo AGC.
            // Se o usuário já moveu o cursor ou digitou, respeite o estado novo.
            if (temporary && StillOurs()) Msg(h, 0xB1, s.start, s.end, out _);
        }
        if (!valid() || !StillOurs() || !valid())
        {
            ReleaseSelection();
            return new(Outcome.Cancelled);
        }
        if (Native.Write(h, 0xC2, 1, after[prefix..(after.Length - suffix)], 2, 150, out _) == 0)
        {
            ReleaseSelection();
            return new(Outcome.Cancelled);
        }
        int caret = manual ? start + after.Length : s.end + after.Length - before.Length;
        int insertedEnd = start + after.Length - suffix;
        if (valid() && Snapshot(h) == (insertedEnd, insertedEnd, s.text[..start] + after + s.text[end..]))
            Msg(h, 0xB1, caret, caret, out _);
        return new(Outcome.Replaced);
    }
    static int CommonPrefix(string a, string b)
    {
        int n = 0; while (n < a.Length && n < b.Length && a[n] == b[n]) n++;
        if (n > 0 && char.IsHighSurrogate(a[n - 1])) n--;
        return n;
    }


    static EditResult ReadSelected(Corrector engine, RuleSettings rules, Func<bool> valid)
    {
        if (!valid()) return new(Outcome.Cancelled);
        var el = AutomationElement.FocusedElement;
        if (el == null || !valid()) return new(Outcome.Cancelled);
        if (el.Current.IsPassword) return new(Outcome.Protected);
        if (el.Current.ControlType != ControlType.Edit && el.Current.ControlType != ControlType.Document)
            return new(Outcome.NoSelection);
        if (!el.TryGetCurrentPattern(TextPattern.Pattern, out var provider)) return new(Outcome.NoSelection);
        var tp = (TextPattern)provider;
        if (tp.GetSelection() is not [var range] ||
            range.CompareEndpoints(TextPatternRangeEndpoint.Start, range, TextPatternRangeEndpoint.End) == 0)
            return new(Outcome.NoSelection);
        string before = range.GetText(100001);
        if (before.Length == 0) return new(Outcome.NoSelection);
        if (before.Length > 100000) return new(Outcome.Cancelled);
        string after = engine.Correct(before, rules, true);
        if (!valid() || !el.Equals(AutomationElement.FocusedElement) || tp.GetSelection() is not [var current] ||
            !current.Compare(range) || current.GetText(100001) != before) return new(Outcome.Cancelled);
        return new(Outcome.CopyResult, after);
    }
}
