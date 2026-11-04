using System.IO;
using System.Windows.Automation;
using System.Windows.Automation.Text;

namespace Agc;

internal sealed record ReviewTarget(nint Window, nint Focus, string Identity, string Original, Rectangle Anchor, string? WordResult = null, bool Writable = true);
internal sealed record LiveRange(AutomationElement Element, TextPattern Pattern, TextPatternRange Selection, TextPatternRange Target);

internal static class ReviewProbe
{
    // Métodos UIA são somente leitura. O popup não seleciona o trecho no editor.
    internal static ReviewTarget? Read(nint h, Corrector engine, RuleSettings rules, bool word, Func<bool> valid, bool punctuationOnly = false, Action<ReviewTarget, LiveRange>? onValidated = null)
    {
        if (!valid() || Native.GetForegroundWindow() != h) return null;
        nint focus = Native.Focus(h);
        if (focus == 0 || Native.GetAncestor(focus, 2) != h) return null;
        if (!word && onValidated == null && Editor.IsPlain(focus) && Editor.SelectedTarget(h, focus) is { } native)
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
        var attr = range.GetAttributeValue(TextPattern.IsReadOnlyAttribute);
        bool writable = Equals(attr, false) || (!Equals(attr, true) && el.TryGetCurrentPattern(ValuePattern.Pattern, out var v) && !((ValuePattern)v).Current.IsReadOnly);
        if (word)
        {
            if (!writable) return null;
            int moved = range.MoveEndpointByUnit(TextPatternRangeEndpoint.Start, TextUnit.Character, -256);
            var before = range.GetText(1025);
            var change = punctuationOnly ? engine.TrailingPunctuation(before, rules, moved != -256) : engine.CompletedWord(before, before.Length, rules, moved != -256);
            if (change is not { } w || before.Length > 1024) return null;
            range = selection.Clone();
            range.MoveEndpointByUnit(TextPatternRangeEndpoint.Start, TextUnit.Character, -(before.Length - w.Start));
            range.MoveEndpointByUnit(TextPatternRangeEndpoint.End, TextUnit.Character, -(before.Length - w.End));
            original = w.Before; result = w.After;
        }
        else original = range.GetText(100001);
        if ((!punctuationOnly && string.IsNullOrWhiteSpace(original)) || original.Length > 100000 || !valid()) return null;
        var rect = range.GetBoundingRectangles().FirstOrDefault(r => !r.IsEmpty && r.Width > 0 && r.Height > 0);
        if (!punctuationOnly && (rect.Width <= 0 || rect.Height <= 0 || !double.IsFinite(rect.Left) || !double.IsFinite(rect.Top))) return null;
        if (range.GetText(100001) != original || !valid() || Native.GetForegroundWindow() != h ||
            !el.Equals(AutomationElement.FocusedElement) || tp.GetSelection() is not [var now] || !now.Compare(selection)) return null;
        var target = new ReviewTarget(h, focus, string.Join(".", el.GetRuntimeId()), original,
            new Rectangle((int)rect.Left, (int)rect.Top, (int)Math.Ceiling(rect.Width), (int)Math.Ceiling(rect.Height)), result, writable);
        onValidated?.Invoke(target, new(el, tp, selection.Clone(), range.Clone()));
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
    sealed class ActionButton : Button
    {
        bool pressing;
        internal ActionButton() { TabStop = false; }
        protected override void WndProc(ref Message m)
        {
            // O Button padrão chama Focus no mouse-down. Capture o clique sem
            // ativar esta janela, mantendo PerformClick e a interface acessível.
            if (m.Msg == 0x201) { pressing = true; Capture = true; return; }
            if (m.Msg == 0x202 && pressing)
            {
                bool inside = ClientRectangle.Contains(PointToClient(Cursor.Position));
                pressing = false; Capture = false; if (inside) PerformClick(); return;
            }
            if (m.Msg == 0x215) pressing = false;
            base.WndProc(ref m);
        }
    }
    [System.Runtime.InteropServices.DllImport("user32")] static extern bool SetWindowPos(nint h, nint after, int x, int y, int w, int height, uint flags);
    readonly FlowLayoutPanel body = new() { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(10, 7, 10, 6) };
    readonly Label heading = new() { AutoSize = true, ForeColor = Theme.Accent };
    readonly ChangePreview preview = new() { Width = 328, Height = 42 };
    readonly Label explanation = new() { AutoSize = false, AutoEllipsis = true, ForeColor = Theme.Muted, Font = Theme.Font(8), Height = 20 };
    readonly Label count = new() { AutoSize = true, ForeColor = Theme.Accent, Font = Theme.Font(8) };
    readonly Panel head = new() { Height = 25 };
    readonly Button close = new ActionButton { Text = "×", FlatStyle = FlatStyle.Flat, ForeColor = Theme.Muted, BackColor = Theme.Panel, Size = new Size(22, 22), AccessibleName = "Fechar prévia" };
    readonly System.Windows.Forms.Timer reveal = new() { Interval = 16 };
    int frame;
    internal Func<bool, bool>? ReviewKeys;
    bool enterReady;
    readonly Panel scroll = new() { Width = 345, Height = 44, AutoScroll = true };
    readonly FlowLayoutPanel buttons = new() { AutoSize = true, WrapContents = false };
    internal event Action<ReviewKind>? Chosen;
    internal event Action? CopyRequested;
    internal event Action? ApplyRequested;
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    internal Func<bool, bool>? BindAction { get; set; }
    bool hasActionKey;
    void ApplyButton(string label) => Button(label + (enterReady ? "   Enter" : hasActionKey ? "   F8" : ""), () => ApplyRequested?.Invoke());
    internal ReviewTarget? Target { get; private set; }
    internal string? Result { get; private set; }
    internal ReviewKind ActiveKind { get; private set; }
    void Pick(ReviewKind kind) { ActiveKind = kind; Result = null; Chosen?.Invoke(kind); }
    protected override bool ShowWithoutActivation => true;
    protected override CreateParams CreateParams
    {
        get { var cp = base.CreateParams; cp.ExStyle |= 0x08000000 | 0x80 | 0x8; return cp; }
    }
    protected override void WndProc(ref Message m)
    {
        if (m.Msg == 0x21) { m.Result = (nint)3; return; } // MA_NOACTIVATE: mantém seleção e foco do editor.
        base.WndProc(ref m);
    }
    internal ReviewPopup()
    {
        // Form.TopMost força FocusActiveControlInternal no Show(), mesmo com
        // ShowWithoutActivation. Posicione no topo via SWP_NOACTIVATE.
        ShowInTaskbar = false; StartPosition = FormStartPosition.Manual;
        FormBorderStyle = FormBorderStyle.None; BackColor = Theme.Panel; ForeColor = Theme.Text;
        Font = Theme.Font(9); AutoScaleMode = AutoScaleMode.Dpi; Padding = new Padding(2);
        body.BackColor = Theme.Panel; heading.Font = Theme.Font(9); heading.ForeColor = Theme.Muted; heading.Location = new Point(27, 2);
        using var stream = typeof(ReviewPopup).Assembly.GetManifestResourceStream("AGC.Brand.Icon")!;
        using var ico = new Icon(stream, new Size(18, 18));
        var logo = new PictureBox { Image = ico.ToBitmap(), Size = new Size(19, 19), SizeMode = PictureBoxSizeMode.Zoom };
        close.FlatAppearance.BorderSize = 0; close.Click += (_, _) => Dismiss();
        count.BackColor = Color.FromArgb(50, 46, 73); count.Padding = new Padding(4, 1, 4, 1);
        head.Controls.AddRange([logo, heading, count, close]); scroll.Controls.Add(preview);
        Controls.Add(body); body.Controls.Add(head); body.Controls.Add(scroll); body.Controls.Add(explanation); body.Controls.Add(buttons);
        reveal.Tick += (_, _) => { frame++; Opacity = Math.Min(1, 0.70 + frame * 0.05); if (frame >= 6) reveal.Stop(); };
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e); using var outline = Theme.Round(new Rectangle(0, 0, Width - 1, Height - 9), 12); using var pen = new Pen(Theme.Border);
        e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias; e.Graphics.DrawPath(pen, outline);
        int x = ArrowX(); e.Graphics.DrawLines(pen, [new Point(x - 7, Height - 9), new Point(x, Height - 2), new Point(x + 7, Height - 9)]);
    }
    int ArrowX() => Math.Clamp((Target?.Anchor.Left ?? Left) - Left + 12, 18, Math.Max(19, Width - 18));
    void Button(string label, Action click)
    {
        bool primary = label == "Tudo" || label.StartsWith("Aplicar") || label.StartsWith("Revisar e aplicar");
        var b = new ActionButton
        {
            FlatStyle = FlatStyle.Flat,
            Cursor = Cursors.Hand,
            Text = label,
            Font = Theme.Font(9, true),
            Padding = new Padding(7, 2, 7, 2),
            BackColor = primary ? Color.FromArgb(43, 41, 62) : Theme.Panel,
            ForeColor = primary ? Theme.Accent : Theme.Text
        };
        b.FlatAppearance.BorderSize = 1; b.FlatAppearance.BorderColor = primary ? Color.FromArgb(107, 97, 153) : Theme.Border;
        b.AutoSize = true; b.AutoSizeMode = AutoSizeMode.GrowAndShrink; b.TabStop = false; b.Margin = new Padding(0, 3, 5, 0); b.AccessibleName = label;
        b.Click += (_, _) => click(); buttons.Controls.Add(b);
    }
    void ClearButtons() { while (buttons.Controls.Count > 0) buttons.Controls[0].Dispose(); }
    internal void Present(ReviewTarget target)
    {
        if (Visible && Target == target) return;
        Target = target; Result = null; ActiveKind = ReviewKind.All; ClearButtons();
        enterReady = false; ReviewKeys?.Invoke(false); hasActionKey = BindAction?.Invoke(target.Writable) == true; count.Text = ""; explanation.Text = "";
        heading.Text = target.WordResult == null ? "Prévia da revisão" : "Sugestão de palavra";
        if (target.WordResult == null)
        {
            preview.Spans = [new(target.Original, target.Original, false)]; preview.Text = target.Original; preview.AccessibleDescription = target.Original;
            if (target.Writable) ApplyButton("Revisar e aplicar");
            Button("Ortografia", () => Pick(ReviewKind.Spelling));
            Button("Gramática", () => Pick(ReviewKind.Grammar));
            Button("Pontuação", () => Pick(ReviewKind.Punctuation));
            Button("Tudo", () => Pick(ReviewKind.All));
        }
        else
        {
            Result = target.WordResult; preview.Spans = TextDiff.Compare(target.Original, Result); preview.Text = target.Original.Trim() + "  →  " + Result.Trim();
            if (target.Writable) ApplyButton("Aplicar");
            Button("Copiar correção", () => CopyRequested?.Invoke());
        }
        LayoutAt(target.Anchor);
    }
    internal void ShowResult(string s, bool inline = false)
    {
        if (Target == null) return;
        Result = s; ClearButtons(); heading.Text = "Prévia da revisão"; preview.Spans = TextDiff.Compare(Target.Original, s);
        preview.Text = s; preview.AccessibleDescription = s; preview.Invalidate();
        var changes = preview.Spans.Where(x => x.Changed).ToList(); count.Text = changes.Count == 1 ? "1 correção" : $"{changes.Count} correções";
        explanation.Text = changes.Count == 0 ? "Nenhuma alteração pelas regras ativas." : string.Join(" · ", changes.Take(2).Select(x => $"{x.Before.Trim()} → {x.After.Trim()} · {TextDiff.Explain(x)}"));
        enterReady = ReviewKeys?.Invoke(inline && s != Target.Original) == true;
        if (inline && s != Target.Original) ApplyButton("Aplicar");
        Button("Copiar", () => CopyRequested?.Invoke());
        Button("Opções", () => { var t = Target; if (t == null) return; Target = null; Present(t); });
        buttons.Controls.Add(new Label { Text = "Esc para fechar", AutoSize = true, ForeColor = Theme.Muted, Margin = new Padding(5, 9, 0, 0), Font = Theme.Font(8) });
        LayoutAt(Target.Anchor);
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
        preview.Height = Math.Max(LogicalToDeviceUnits(27), preview.TextHeight(preview.Width) + 6);
        scroll.Height = Math.Min(LogicalToDeviceUnits(140), preview.Height); scroll.AutoScrollPosition = Point.Empty;
        head.Width = scroll.Width; close.Left = head.Width - close.Width; count.Location = new Point(close.Left - count.PreferredWidth - 8, 4);
        explanation.Width = scroll.Width; explanation.Height = explanation.Text.Length > 0 ? 24 : 2;
        ClientSize = new Size(width, Math.Min(work.Height, body.Padding.Vertical + head.Height + scroll.Height + explanation.Height + buttons.PreferredSize.Height + LogicalToDeviceUnits(24)));
        Location = Place(anchor, Size, work);
        int ax = ArrowX(); using var geometry = Theme.Round(new Rectangle(0, 0, Width, Height - 8), 12);
        geometry.AddPolygon([new Point(ax - 8, Height - 9), new Point(ax, Height), new Point(ax + 8, Height - 9)]);
        var region = Region; Region = new Region(geometry); region?.Dispose();
        if (!Visible) { Opacity = 0.70; frame = 0; Show(); reveal.Start(); }
        SetWindowPos(Handle, -1, 0, 0, 0, 0, 0x13);
    }
    protected override void Dispose(bool disposing) { if (disposing) reveal.Dispose(); base.Dispose(disposing); }
    internal void Dismiss() { if (Target != null) { BindAction?.Invoke(false); ReviewKeys?.Invoke(false); } reveal.Stop(); Hide(); preview.Spans = []; Target = null; Result = null; preview.Text = ""; preview.AccessibleDescription = null; }
}
