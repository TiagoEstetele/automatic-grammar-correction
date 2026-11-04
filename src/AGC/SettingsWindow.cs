using System.Diagnostics;
using System.IO;
namespace Agc;

internal static class Theme
{
    internal static readonly Color Bg = Color.FromArgb(24, 21, 31), Panel = Color.FromArgb(43, 37, 53), Text = Color.FromArgb(247, 244, 252), Muted = Color.FromArgb(204, 194, 219), Accent = Color.FromArgb(191, 158, 255);
}
internal sealed class SettingsWindow : Form
{
    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        int dark = 1;
        DwmSetWindowAttribute(Handle, 20, ref dark, sizeof(int));
    }
    [System.Runtime.InteropServices.DllImport("dwmapi")]
    static extern int DwmSetWindowAttribute(nint window, int attribute, ref int value, int length);
    readonly Preferences data;
    readonly Action changed;
    readonly FlowLayoutPanel list = new() { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, Padding = new Padding(2, 6, 12, 12) };
    bool apps;
    public SettingsWindow(Preferences prefs, Action changed)
    {
        data = prefs; this.changed = changed;
        Text = "automatic grammar correction"; Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        BackColor = Theme.Bg; ForeColor = Theme.Text; Font = new Font("Segoe UI", 10); AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(576, 580); MinimumSize = new Size(525, 480); StartPosition = FormStartPosition.CenterScreen;
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(26, 20, 26, 18), ColumnCount = 1, RowCount = 5 };
        foreach (int h in new[] { 32, 47, 46, 36 }) root.RowStyles.Add(new RowStyle(SizeType.Absolute, h));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        Controls.Add(root);
        root.Controls.Add(new Label { Text = "●   automatic grammar correction", ForeColor = Theme.Accent, AutoSize = true }, 0, 0);
        root.Controls.Add(new Label { Text = "Configurações", Font = new Font("Segoe UI", 21, FontStyle.Bold), AutoSize = true }, 0, 1);
        var tabs = new FlowLayoutPanel { Dock = DockStyle.Fill };
        var rules = Button("Regras");
        var programs = Button("Aplicativos");
        tabs.Controls.AddRange([rules, programs]); root.Controls.Add(tabs, 0, 2);
        var subtitle = new Label { Dock = DockStyle.Fill, ForeColor = Theme.Muted, TextAlign = ContentAlignment.MiddleLeft };
        root.Controls.Add(subtitle, 0, 3); root.Controls.Add(list, 0, 4);
        void Render(bool showApps)
        {
            apps = showApps;
            rules.BackColor = apps ? Theme.Panel : Theme.Accent; rules.ForeColor = apps ? Theme.Text : Theme.Bg;
            programs.BackColor = apps ? Theme.Accent : Theme.Panel; programs.ForeColor = apps ? Theme.Bg : Theme.Text;
            subtitle.Text = apps ? "Aplicativos abertos e já usados. Novos começam ativados." : "Ajustes locais. Desmarque uma regra para desativá-la.";
            Fill();
        }
        rules.Click += (_, _) => Render(false); programs.Click += (_, _) => Render(true);
        Render(false);
    }
    static Button Button(string text) => new() { Text = text, FlatStyle = FlatStyle.Flat, AutoSize = true, Padding = new Padding(12, 2, 12, 2), Margin = new Padding(0, 0, 8, 0), Cursor = Cursors.Hand };
    void Fill()
    {
        list.SuspendLayout();
        while (list.Controls.Count > 0) list.Controls[0].Dispose();
        if (apps)
        {
            foreach (var proc in Process.GetProcesses()) using (proc)
            {
                try { if (proc.Id != Environment.ProcessId && proc.MainWindowHandle != 0 && Preferences.ProcessPath((uint)proc.Id) is { } path) data.Apps.TryAdd(path, true); }
                catch (InvalidOperationException) { }
            }
            foreach (var entry in data.Apps.OrderBy(x => Path.GetFileName(x.Key), StringComparer.CurrentCultureIgnoreCase))
            {
                var box = Toggle(Path.GetFileName(entry.Key), entry.Value, b => data.Apps[entry.Key] = b);
                box.AccessibleDescription = entry.Key;
                var label = new Label { Text = entry.Key, AutoEllipsis = true, Width = 440, Height = 25, Margin = new Padding(26, 0, 0, 4), ForeColor = Theme.Muted, Font = new Font("Segoe UI", 8.5f) };
                list.Controls.Add(label);
            }
        }
        else
        {
            var r = data.Rules;
            Toggle("Normalizar espaços ao redor da pontuação", r.Spacing, b => r.Spacing = b);
            Toggle("Maiúscula no início de frases", r.Capitals, b => r.Capitals = b);
            Toggle("Corrigir acentos comuns", r.Accents, b => r.Accents = b);
            Toggle("Corrigir expressões gramaticais conhecidas", r.Grammar, b => r.Grammar = b);
            Toggle("Inserir vírgulas em «no entanto»", r.Commas, b => r.Commas = b);
            Toggle("Ponto final ao concluir parágrafo ou revisão", r.FinalStop, b => r.FinalStop = b);
            Toggle("Ortografia aproximada · só sob demanda", r.Spelling, b => r.Spelling = b);
        }
        list.ResumeLayout();
    }
    CheckBox Toggle(string text, bool value, Action<bool> setter)
    {
        var box = new CheckBox { Text = text, Checked = value, AutoSize = true, ForeColor = Theme.Text, Margin = new Padding(4, 6, 4, 7), FlatStyle = FlatStyle.Flat, AccessibleName = text };
        box.FlatAppearance.CheckedBackColor = Theme.Accent;
        box.CheckedChanged += (_, _) => { setter(box.Checked); changed(); };
        list.Controls.Add(box); return box;
    }
}
