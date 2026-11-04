using System.Windows.Automation;
using System.Windows.Automation.Text;

namespace Agc;

internal sealed record ReviewTarget(nint Window, nint Focus, string Identity, string Original, Rectangle Anchor, string? WordResult = null, bool Writable=true);
internal sealed record LiveRange(AutomationElement Element,TextPattern Pattern,TextPatternRange Selection,TextPatternRange Target);

internal static class ReviewProbe
{
    // Métodos UIA são somente leitura. O popup não seleciona o trecho no editor.
    internal static ReviewTarget? Read(nint h, Corrector engine, RuleSettings rules, bool word, Func<bool> valid, bool punctuationOnly=false, Action<ReviewTarget,LiveRange>? onValidated=null)
    {
        if (!valid() || Native.GetForegroundWindow() != h) return null;
        nint focus = Native.Focus(h);
        if (focus == 0 || Native.GetAncestor(focus, 2) != h) return null;
        if (!word && onValidated==null && Editor.IsPlain(focus) && Editor.SelectedTarget(h, focus) is { } native)
            return valid() ? native : null;
        var el = AutomationElement.FocusedElement;
        if (el == null || el.Current.IsPassword || !el.Current.IsEnabled ||
            el.Current.ControlType != ControlType.Edit && el.Current.ControlType != ControlType.Document ||
            !el.TryGetCurrentPattern(TextPattern.Pattern, out var pattern)) return null;
        var tp = (TextPattern)pattern;
        if (tp.GetSelection() is not [var selection]) return null;
        bool empty = selection.CompareEndpoints(TextPatternRangeEndpoint.Start, selection, TextPatternRangeEndpoint.End) == 0;
        if (word != empty) return null;
        var range = selection.Clone();
        string original;
        string? result = null;
        var attr=range.GetAttributeValue(TextPattern.IsReadOnlyAttribute);
        bool writable=Equals(attr,false) || (!Equals(attr,true) && el.TryGetCurrentPattern(ValuePattern.Pattern,out var v) && !((ValuePattern)v).Current.IsReadOnly);
        if (word)
        {
            if(!writable) return null;
            int moved = range.MoveEndpointByUnit(TextPatternRangeEndpoint.Start, TextUnit.Character, -256);
            var before = range.GetText(1025);
            var change = punctuationOnly ? engine.TrailingPunctuation(before,rules,moved!=-256) : engine.CompletedWord(before, before.Length, rules, moved != -256);
            if (change is not { } w || before.Length > 1024) return null;
            range = selection.Clone();
            range.MoveEndpointByUnit(TextPatternRangeEndpoint.Start, TextUnit.Character, -(before.Length - w.Start));
            range.MoveEndpointByUnit(TextPatternRangeEndpoint.End, TextUnit.Character, -(before.Length - w.End));
            original = w.Before; result = w.After;
        }
        else original = range.GetText(100001);
        if ((!punctuationOnly && string.IsNullOrWhiteSpace(original)) || original.Length > 100000 || !valid()) return null;
        var rect = range.GetBoundingRectangles().FirstOrDefault(r => !r.IsEmpty && r.Width > 0 && r.Height > 0);
        if (rect.Width <= 0 || rect.Height <= 0 || !double.IsFinite(rect.Left) || !double.IsFinite(rect.Top)) return null;
        if (range.GetText(100001) != original || !valid() || Native.GetForegroundWindow() != h ||
            !el.Equals(AutomationElement.FocusedElement) || tp.GetSelection() is not [var now] || !now.Compare(selection)) return null;
        var target=new ReviewTarget(h, focus, string.Join(".", el.GetRuntimeId()), original,
            new Rectangle((int)rect.Left, (int)rect.Top, (int)Math.Ceiling(rect.Width), (int)Math.Ceiling(rect.Height)), result,writable);
        onValidated?.Invoke(target,new(el,tp,selection.Clone(),range.Clone()));
        return target;
    }
}

internal enum ReviewKind { All, Spelling, Grammar, Punctuation }
internal static class ReviewOptions
{
    internal static RuleSettings Filter(RuleSettings r, ReviewKind k) => new()
    {
        Accents = r.Accents && k is ReviewKind.All or ReviewKind.Spelling,
        Spelling = k == ReviewKind.Spelling || r.Spelling && k == ReviewKind.All,
        Grammar = r.Grammar && k is ReviewKind.All or ReviewKind.Grammar,
        Commas = r.Commas && k is ReviewKind.All or ReviewKind.Punctuation,
        Spacing = r.Spacing && k is ReviewKind.All or ReviewKind.Punctuation,
        Capitals = r.Capitals && k is ReviewKind.All or ReviewKind.Punctuation,
        FinalStop = r.FinalStop && k is ReviewKind.All or ReviewKind.Punctuation
    };
}

internal sealed class ReviewPopup : Form
{
    readonly FlowLayoutPanel body = new() { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(10, 7, 10, 6) };
    readonly Label heading = new() { AutoSize = true, ForeColor = Theme.Accent };
    readonly Label preview = new() { AutoSize = false, Width = 335, Height = 42, ForeColor = Theme.Text, UseMnemonic = false };
    readonly Panel scroll = new() { Width = 345, Height = 44, AutoScroll = true };
    readonly FlowLayoutPanel buttons = new() { AutoSize = true, WrapContents = false };
    internal event Action<ReviewKind>? Chosen;
    internal event Action? CopyRequested;
    internal event Action? ApplyRequested;
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    internal Func<bool,bool>? BindAction { get; set; }
    bool hasActionKey;
    void ApplyButton(string label)=>Button(label+(hasActionKey?"   F8":""),()=>ApplyRequested?.Invoke());
    internal ReviewTarget? Target { get; private set; }
    internal string? Result { get; private set; }
    internal ReviewKind ActiveKind {get;private set;}
    void Pick(ReviewKind kind){ActiveKind=kind;Result=null;Chosen?.Invoke(kind);}
    protected override bool ShowWithoutActivation => true;
    protected override CreateParams CreateParams
    {
        get { var cp = base.CreateParams; cp.ExStyle |= 0x08000000 | 0x80; return cp; }
    }
    protected override void WndProc(ref Message m)
    {
        if (m.Msg == 0x21) { m.Result = (nint)3; return; } // MA_NOACTIVATE: mantém seleção e foco do editor.
        base.WndProc(ref m);
    }
    internal ReviewPopup()
    {
        ShowInTaskbar = false; TopMost = true; StartPosition = FormStartPosition.Manual;
        FormBorderStyle = FormBorderStyle.None; BackColor = Theme.Panel; ForeColor = Theme.Text;
        Font = Theme.Font(9); AutoScaleMode = AutoScaleMode.Dpi; Padding = new Padding(2);
        heading.Font = Theme.Font(10, true); preview.Font = Theme.Font(10);
        scroll.Controls.Add(preview);
        Controls.Add(body); body.Controls.Add(heading); body.Controls.Add(scroll); body.Controls.Add(buttons);
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e); using var pen = new Pen(Theme.Border); e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
        using var edge = new Pen(Theme.Accent, 2); e.Graphics.DrawLine(edge, 1, 1, Width - 2, 1);
    }
    void Button(string label, Action click)
    {
        var b = Theme.Action(label, label is "Tudo" or "Copiar resultado" or "Aplicar");
        b.AutoSize = true; b.AutoSizeMode = AutoSizeMode.GrowAndShrink; b.TabStop = false; b.Margin = new Padding(0, 3, 5, 0); b.AccessibleName = label;
        b.Click += (_, _) => click(); buttons.Controls.Add(b);
    }
    void ClearButtons() { while (buttons.Controls.Count > 0) buttons.Controls[0].Dispose(); }
    internal void Present(ReviewTarget target)
    {
        if (Visible && Target == target) return;
        Target = target; Result = null; ActiveKind=ReviewKind.All; ClearButtons();
        hasActionKey=BindAction?.Invoke(target.Writable)==true;
        heading.Text = target.WordResult == null ? "AGC  ·  Melhorar seleção" : "AGC  ·  Sugestão de palavra";
        if (target.WordResult == null)
        {
            preview.Text = target.Original; preview.AccessibleDescription = target.Original;
            if(target.Writable) ApplyButton("Revisar e aplicar");
            Button("Ortografia", () => Pick(ReviewKind.Spelling));
            Button("Gramática", () => Pick(ReviewKind.Grammar));
            Button("Pontuação", () => Pick(ReviewKind.Punctuation));
            Button("Tudo", () => Pick(ReviewKind.All));
        }
        else
        {
            Result = target.WordResult; preview.Text = target.Original.Trim() + "  →  " + Result.Trim();
            if(target.Writable) ApplyButton("Aplicar");
            Button("Copiar correção", () => CopyRequested?.Invoke());
        }
        Button("×", Dismiss); LayoutAt(target.Anchor);
    }
    internal void ShowResult(string s, bool inline = false)
    {
        if (Target == null) return;
        Result = s; ClearButtons(); heading.Text = s == Target.Original ? "Nenhuma alteração" : "Prévia da revisão";
        preview.Text = s; preview.AccessibleDescription = s;
        if (inline && s != Target.Original) ApplyButton("Aplicar");
        Button("Copiar resultado", () => CopyRequested?.Invoke()); Button("×", Dismiss); LayoutAt(Target.Anchor);
    }
    internal static Point Place(Rectangle anchor, Size size, Rectangle work)
    {
        int x = Math.Clamp(anchor.Left, work.Left, Math.Max(work.Left, work.Right - size.Width));
        int above = anchor.Top - size.Height - 6;
        int y = above >= work.Top ? above : Math.Clamp(anchor.Bottom + 6, work.Top, Math.Max(work.Top, work.Bottom - size.Height));
        return new(x, y);
    }
    void LayoutAt(Rectangle anchor)
    {
        var work = Screen.FromRectangle(anchor).WorkingArea;
        int width = Math.Min(work.Width, Math.Max(buttons.PreferredSize.Width + LogicalToDeviceUnits(38), LogicalToDeviceUnits(375)));
        scroll.Width = width - body.Padding.Horizontal - Padding.Horizontal - 8;
        preview.Width = Math.Max(80, scroll.Width - SystemInformation.VerticalScrollBarWidth - 4);
        var measured = TextRenderer.MeasureText(preview.Text, preview.Font, new Size(preview.Width, 0), TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
        preview.Height = Math.Max(LogicalToDeviceUnits(32), measured.Height + 5);
        scroll.Height = Math.Min(LogicalToDeviceUnits(140), preview.Height);
        scroll.AutoScrollPosition = Point.Empty;
        ClientSize = new Size(width, Math.Min(work.Height, body.Padding.Vertical + heading.PreferredHeight + scroll.Height + buttons.PreferredSize.Height + LogicalToDeviceUnits(17)));
        Location = Place(anchor, Size, work);
        if (!Visible) Show();
    }
    internal void Dismiss() { if(Target!=null)BindAction?.Invoke(false); Hide(); Target = null; Result = null; preview.Text = ""; preview.AccessibleDescription = null; }
}
