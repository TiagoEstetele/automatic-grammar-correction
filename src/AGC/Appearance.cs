using System.IO;
using System.Drawing.Drawing2D;
namespace Agc;

internal static class Theme
{
    internal static readonly Color Bg = Color.FromArgb(22, 24, 38), Rail = Color.FromArgb(30, 32, 45), Panel = Color.FromArgb(35, 37, 50),
        Raised = Color.FromArgb(47, 49, 63), Border = Color.FromArgb(64, 66, 82), Text = Color.FromArgb(233, 233, 237),
        Muted = Color.FromArgb(162, 164, 184), Accent = Color.FromArgb(181, 171, 252), Active = Color.FromArgb(145, 132, 217);
    [System.Runtime.InteropServices.DllImport("gdi32", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    static extern int AddFontResourceEx(string file, uint flags, nint reserved);
    static readonly Lazy<string> Typeface = new(() => AddFontResourceEx(Path.Combine(AppContext.BaseDirectory, "Fonts", "InterVariable.ttf"), 0x10, 0) > 0 ? "Inter Variable" : "Segoe UI");
    internal static Font Font(float size, bool bold = false) => new(Typeface.Value, size, bold ? FontStyle.Bold : FontStyle.Regular);
    internal static GraphicsPath Round(Rectangle r, int radius)
    {
        var p = new GraphicsPath(); int d = Math.Max(2, Math.Min(2 * radius, Math.Min(r.Width, r.Height)));
        p.AddArc(r.Left, r.Top, d, d, 180, 90); p.AddArc(r.Right - d, r.Top, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90); p.AddArc(r.Left, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure(); return p;
    }
    internal static Label Label(string text, float size, Color color, bool bold = false) => new()
    {
        Text = text,
        AutoSize = false,
        AutoEllipsis = true,
        Font = Font(size, bold),
        ForeColor = color,
        BackColor = Color.Transparent,
        TextAlign = ContentAlignment.MiddleLeft
    };
    internal static Button Action(string label, bool primary = false)
    {
        var b = new Button
        {
            Text = label,
            FlatStyle = FlatStyle.Flat,
            Cursor = Cursors.Hand,
            Font = Font(9, true),
            BackColor = primary ? Color.FromArgb(43, 41, 62) : Panel,
            ForeColor = primary ? Accent : Text,
            AutoSize = false,
            Padding = new Padding(6, 0, 6, 0)
        };
        b.FlatAppearance.BorderSize = 1;
        b.FlatAppearance.BorderColor = primary ? Accent : Border;
        b.FlatAppearance.MouseOverBackColor = Raised;
        return b;
    }
    internal static Image NavIcon(string name)
    {
        var image = new Bitmap(20, 20);
        using var g = Graphics.FromImage(image); g.SmoothingMode = SmoothingMode.AntiAlias;
        using var p = new Pen(Accent, 1.5f);
        if (name == "Visão geral")
        {
            foreach (var r in new[] { new Rectangle(2, 2, 6, 6), new Rectangle(12, 2, 6, 6), new Rectangle(2, 12, 6, 6), new Rectangle(12, 12, 6, 6) })
            { using var outline = Round(r, 2); g.DrawPath(p, outline); }
        }
        else if (name == "Digitação")
        {
            for (int i = 0; i < 3; i++) { int y = 4 + i * 6, x = i == 1 ? 12 : 5; g.DrawLine(p, 1, y, x - 2, y); g.DrawEllipse(p, x - 2, y - 2, 4, 4); g.DrawLine(p, x + 2, y, 18, y); }
        }
        else
        {
            g.DrawRectangle(p, 2, 5, 13, 12); g.DrawLine(p, 2, 9, 15, 9); g.DrawLine(p, 6, 2, 18, 2); g.DrawLine(p, 18, 2, 18, 13);
        }
        return image;
    }
}

internal sealed class TrayMenu : ContextMenuStrip
{
    readonly ToolStripMenuItem header = new() { AutoSize = false, Size = new Size(282, 53), AccessibleName = "Alternar correção" };
    internal bool Paused { get; private set; }
    internal long Count { get; private set; }
    internal event Action? ToggleRequested;
    readonly Bitmap brand;
    internal TrayMenu()
    {
        using var source = typeof(TrayMenu).Assembly.GetManifestResourceStream("AGC.Brand.Icon")!;
        using var ico = new Icon(source, 32, 32);
        brand = ico.ToBitmap();
        ShowCheckMargin = false; ShowImageMargin = false; BackColor = Theme.Panel; ForeColor = Theme.Text; Font = Theme.Font(10); Padding = new Padding(6);
        MinimumSize = new Size(LogicalToDeviceUnits(288), 0); Renderer = new RendererImpl(this); Items.Add(header); Items.Add(new ToolStripSeparator()); header.Click += (_, _) => ToggleRequested?.Invoke();
    }
    internal void SetStatus(bool off, long count) { Paused = off; Count = count; header.Text = off ? "AGC · Pausado" : "AGC · Ativo"; Invalidate(); }
    internal ToolStripMenuItem Entry(string text, string glyph, Action action, string shortcut = "")
    {
        var row = new ToolStripMenuItem(text, null, (_, _) => action()) { Tag = glyph, ShortcutKeyDisplayString = shortcut };
        Items.Add(row); return row;
    }
    protected override void OnItemAdded(ToolStripItemEventArgs e) { base.OnItemAdded(e); if (e.Item is ToolStripMenuItem item && item != header) item.Padding = new Padding(27, 5, 7, 5); }
    protected override void OnOpened(EventArgs e) { base.OnOpened(e); using var path = Theme.Round(new Rectangle(0, 0, Width, Height), 13); var old = Region; Region = new Region(path); old?.Dispose(); }
    sealed class RendererImpl(TrayMenu menu) : ToolStripRenderer
    {
        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e) => e.Graphics.Clear(Theme.Panel);
        protected override void OnRenderImageMargin(ToolStripRenderEventArgs e) { }
        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e) { e.Graphics.SmoothingMode = SmoothingMode.AntiAlias; using var outline = Theme.Round(new Rectangle(0, 0, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1), 13); using var p = new Pen(Theme.Border); e.Graphics.DrawPath(p, outline); }
        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias; var r = new Rectangle(0, 0, e.Item.Width - 1, e.Item.Height - 1);
            if (e.Item.Selected) { using var bg = new SolidBrush(Theme.Raised); using var path = Theme.Round(r, 7); g.FillPath(bg, path); }
            if (e.Item == menu.header)
            {
                g.DrawImage(menu.brand, 9, 10, 24, 25);
                using var title = Theme.Font(11, true); using var sub = Theme.Font(8);
                TextRenderer.DrawText(g, "AGC", title, new Rectangle(41, 4, 160, 22), Theme.Text, TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
                TextRenderer.DrawText(g, menu.Paused ? "○  Pausado" : $"●  Ativo · {menu.Count} correções hoje", sub, new Rectangle(41, 25, r.Width - 97, 20), Theme.Muted, TextFormatFlags.EndEllipsis);
                var toggle = new Rectangle(r.Width - 47, 15, 36, 20); using var tp = Theme.Round(toggle, 10); using var fill = new SolidBrush(menu.Paused ? Theme.Raised : Color.FromArgb(80, 69, 117)); using var pen = new Pen(menu.Paused ? Theme.Border : Theme.Accent);
                g.FillPath(fill, tp); g.DrawPath(pen, tp); using var dot = new SolidBrush(menu.Paused ? Theme.Muted : Theme.Text); g.FillEllipse(dot, toggle.X + (menu.Paused ? 3 : 19), toggle.Y + 3, 14, 14);
            }
            else if (e.Item.Tag is string icon)
            {
                var state = g.Save(); g.TranslateTransform(8, (r.Height - 16) / 2f);
                using var pen = new Pen(Theme.Muted, 1.25f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
                switch (icon)
                {
                    case "": g.DrawEllipse(pen, 1, 1, 13, 13); g.DrawLines(pen, [new Point(7, 3), new Point(7, 8), new Point(11, 8)]); break;
                    case "": g.DrawArc(pen, 1, 1, 13, 13, 0, 255); g.DrawBezier(pen, 5, 1, 3, 9, 8, 13, 14, 8); break;
                    case "": g.DrawEllipse(pen, 1, 1, 13, 13); g.DrawLine(pen, 3, 3, 12, 12); break;
                    case "": g.DrawRectangle(pen, 1, 2, 14, 12); g.DrawLine(pen, 1, 5, 15, 5); break;
                    case "": g.DrawArc(pen, 2, 2, 12, 12, -55, 290); g.DrawLine(pen, 8, 0, 8, 7); break;
                    case "": g.DrawLines(pen, [new Point(2, 13), new Point(10, 5), new Point(12, 7), new Point(4, 15), new Point(2, 13)]); g.DrawLine(pen, 3, 1, 3, 5); g.DrawLine(pen, 1, 3, 5, 3); g.DrawLine(pen, 11, 1, 14, 1); break;
                    default: g.DrawRectangle(pen, 2, 2, 9, 10); g.DrawLines(pen, [new Point(12, 5), new Point(14, 5), new Point(14, 14), new Point(5, 14)]); break;
                }
                g.Restore(state);
            }
        }
        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            if (e.Item == menu.header) return;
            bool shortcut = e.Item is ToolStripMenuItem row && !string.IsNullOrEmpty(row.ShortcutKeyDisplayString) && e.Text == row.ShortcutKeyDisplayString;
            int left = menu.LogicalToDeviceUnits(32); var r = new Rectangle(left, 0, e.Item.Width - left - menu.LogicalToDeviceUnits(9), e.Item.Height);
            TextRenderer.DrawText(e.Graphics, e.Text, e.TextFont, r, shortcut || !e.Item.Enabled ? Theme.Muted : Theme.Text,
                TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | (shortcut ? TextFormatFlags.Right : TextFormatFlags.Left));
        }
        protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e) { using var p = new Pen(Theme.Border); e.Graphics.DrawLine(p, 7, e.Item.Height / 2, e.Item.Width - 7, e.Item.Height / 2); }
    }
    protected override void Dispose(bool disposing) { if (disposing) brand.Dispose(); base.Dispose(disposing); }
}
