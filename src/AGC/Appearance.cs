using System.Drawing.Drawing2D;
namespace Agc;

internal static class Theme
{
    internal static readonly Color Bg = Color.FromArgb(22, 20, 29), Rail = Color.FromArgb(28, 25, 36), Panel = Color.FromArgb(36, 32, 47),
        Raised = Color.FromArgb(49, 43, 62), Border = Color.FromArgb(65, 57, 79), Text = Color.FromArgb(247, 244, 253),
        Muted = Color.FromArgb(187, 179, 203), Accent = Color.FromArgb(196, 170, 255), Active = Color.FromArgb(133, 221, 181);
    internal static Font Font(float size, bool bold = false) => new("Segoe UI", size, bold ? FontStyle.Bold : FontStyle.Regular);
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
            BackColor = primary ? Accent : Raised,
            ForeColor = primary ? Bg : Text,
            AutoSize = false,
            Padding = new Padding(6, 0, 6, 0)
        };
        b.FlatAppearance.BorderSize = 0;
        b.FlatAppearance.MouseOverBackColor = primary ? Color.FromArgb(215, 199, 255) : Border;
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

internal sealed class Surface : Panel
{
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    internal bool Hero { get; init; }
    internal Surface() { DoubleBuffered = true; BackColor = Theme.Bg; }
    protected override void OnPaintBackground(PaintEventArgs e)
    {
        e.Graphics.Clear(Theme.Bg);
        if (Width < 2 || Height < 2) return;
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var path = Theme.Round(new Rectangle(0, 0, Width - 1, Height - 1), 12);
        using Brush fill = Hero ? new LinearGradientBrush(ClientRectangle, Color.FromArgb(57, 43, 80), Color.FromArgb(37, 31, 54), 15f) : new SolidBrush(Theme.Panel);
        e.Graphics.FillPath(fill, path);
        using var pen = new Pen(Hero ? Color.FromArgb(93, 72, 128) : Theme.Border); e.Graphics.DrawPath(pen, path);
        if (Hero)
        {
            e.Graphics.SetClip(path);
            using var line = new Pen(Color.FromArgb(52, 179, 145, 240), 2f);
            for (int i = 0; i < 4; i++) e.Graphics.DrawArc(line, Width - 155 + i * 18, -80 + i * 10, 210, 235, 70, 180);
            e.Graphics.ResetClip();
        }
    }
}

internal sealed class SwitchRow : CheckBox
{
    readonly string detail;
    internal SwitchRow(string title, string description, bool value, Action<bool> set)
    {
        Text = title; detail = description; Checked = value; AccessibleName = title; AccessibleDescription = description;
        ForeColor = Theme.Text; BackColor = Theme.Panel; Cursor = Cursors.Hand;
        SetStyle(ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
        CheckedChanged += (_, _) => { set(Checked); Invalidate(); };
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        int d(int v) => LogicalToDeviceUnits(v);
        e.Graphics.Clear(Theme.Panel); e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var r = new Rectangle(Width - d(56), (Height - d(21)) / 2, d(38), d(21));
        using var path = Theme.Round(r, d(11)); using var brush = new SolidBrush(Checked ? Theme.Accent : Theme.Border);
        e.Graphics.FillPath(brush, path);
        int dot = d(15); var thumb = new Rectangle(Checked ? r.Right - dot - d(3) : r.Left + d(3), r.Top + d(3), dot, dot);
        using var knob = new SolidBrush(Checked ? Theme.Bg : Theme.Text); e.Graphics.FillEllipse(knob, thumb);
        using var title = Theme.Font(10, true); using var caption = Theme.Font(9);
        TextRenderer.DrawText(e.Graphics, Text, title, new Rectangle(d(16), d(5), r.Left - d(25), Height / 2), Theme.Text, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);
        TextRenderer.DrawText(e.Graphics, detail, caption, new Rectangle(d(16), Height / 2, r.Left - d(25), Height / 2 - d(3)), Theme.Muted, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
        if (Focused && ShowFocusCues) ControlPaint.DrawFocusRectangle(e.Graphics, Rectangle.Inflate(ClientRectangle, -2, -2), Theme.Text, Theme.Panel);
    }
}

internal sealed class TrayMenu : ContextMenuStrip
{
    internal TrayMenu()
    {
        BackColor = Theme.Panel; ForeColor = Theme.Text; ShowCheckMargin = false; ShowImageMargin = false;
        Renderer = new TrayRenderer(); Font = Theme.Font(10); Padding = new Padding(6);
        Items.Add(new ToolStripLabel("AGC   ·   Assistente de escrita") { ForeColor = Theme.Accent, Padding = new Padding(9, 5, 7, 6) });
        Items.Add(new ToolStripSeparator());
    }
    protected override void OnItemAdded(ToolStripItemEventArgs e)
    {
        base.OnItemAdded(e);
        if (e.Item is ToolStripMenuItem item) item.Padding = new Padding(9, 5, 9, 5);
    }
    sealed class TrayRenderer : ToolStripRenderer
    {
        static Color PanelColor => SystemInformation.HighContrast ? SystemColors.Menu : Theme.Panel;
        static Color ActiveColor => SystemInformation.HighContrast ? SystemColors.Highlight : Color.FromArgb(69, 51, 97);
        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e) => e.Graphics.Clear(PanelColor);
        protected override void OnRenderImageMargin(ToolStripRenderEventArgs e) { using var b = new SolidBrush(PanelColor); e.Graphics.FillRectangle(b, e.AffectedBounds); }
        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
        {
            using var p = new Pen(SystemInformation.HighContrast ? SystemColors.MenuText : Theme.Border);
            e.Graphics.DrawRectangle(p, 0, 0, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1);
        }
        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            var rect = new Rectangle(Point.Empty, e.Item.Size);
            using var b = new SolidBrush(e.Item.Selected && e.Item.Enabled ? ActiveColor : PanelColor); e.Graphics.FillRectangle(b, rect);
            if (e.Item.Selected && e.Item.Enabled)
            {
                using var accent = new SolidBrush(SystemInformation.HighContrast ? SystemColors.HighlightText : Theme.Accent);
                e.Graphics.FillRectangle(accent, 0, 3, 3, Math.Max(0, rect.Height - 6));
            }
        }
        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = SystemInformation.HighContrast ?
                (e.Item.Selected ? SystemColors.HighlightText : SystemColors.MenuText) :
                e.Item is ToolStripLabel ? Theme.Accent : e.Item.Enabled ? Theme.Text : Theme.Muted;
            base.OnRenderItemText(e);
        }
        protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
        {
            using var p = new Pen(SystemInformation.HighContrast ? SystemColors.MenuText : Theme.Border);
            e.Graphics.DrawLine(p, 5, e.Item.Height / 2, e.Item.Width - 5, e.Item.Height / 2);
        }
    }
}
