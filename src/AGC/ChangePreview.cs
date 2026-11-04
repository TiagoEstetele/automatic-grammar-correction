using System.Text.RegularExpressions;
namespace Agc;
// Owner-drawn text keeps the preview unselectable so the editor retains focus.
internal sealed class ChangePreview : Control
{
    internal IReadOnlyList<ChangeSpan> Spans = [];
    internal ChangePreview() { SetStyle(ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true); TabStop = false; Font = Theme.Font(11); ForeColor = Theme.Text; BackColor = Theme.Panel; }
    internal int TextHeight(int width) { using var g = CreateGraphics(); return PaintText(g, width, false); }
    protected override void OnPaint(PaintEventArgs e) { PaintText(e.Graphics, ClientSize.Width, true); }
    int PaintText(Graphics g, int width, bool draw)
    {
        var flags = TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine;
        int x = 0, y = 0, line = Font.Height + 4, seen = 0;
        foreach (var span in Spans) foreach (Match m in Regex.Matches(span.After, @"\r\n|\n|\r|[^\S\r\n]+|[^\s]+"))
        {
            if (seen > 3000) return y + line;
            string token = m.Value; seen += token.Length;
            if (token.Contains('\r') || token.Contains('\n')) { y += line; x = 0; continue; }
            int w = TextRenderer.MeasureText(g, token, Font, new Size(int.MaxValue, line), flags).Width;
            if (x + w > width && x > 0) { x = 0; y += line; }
            if (draw)
            {
                if (span.Changed) { using var b = new SolidBrush(Color.FromArgb(64, 59, 88)); g.FillRectangle(b, x, y, Math.Min(w, width), line - 1); using var p = new Pen(Theme.Accent); g.DrawLine(p, x, y + line - 2, x + Math.Min(w, width), y + line - 2); }
                TextRenderer.DrawText(g, token, Font, new Point(x, y), Theme.Text, flags);
            }
            x += w;
        }
        return y + line;
    }
}
