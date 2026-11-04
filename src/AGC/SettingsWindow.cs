using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
namespace Agc;

internal sealed class SettingsWindow : Form
{
    readonly Preferences data;
    readonly Action changed;
    readonly Func<bool> isPaused;
    readonly Action<bool> setPaused;
    readonly Corrector engine;
    readonly Panel rail = new() { Dock = DockStyle.Left, Width = 176, BackColor = Theme.Rail };
    readonly Panel page = new() { Dock = DockStyle.Fill, BackColor = Theme.Bg, AutoScroll = true };
    readonly Dictionary<string, Button> nav = new();
    readonly List<Control> blocks = [];
    readonly Label status = Theme.Label("", 9, Theme.Active, true);
    int nextY;
    string current = "Visão geral";
    const int Pad = 24;
    [DllImport("dwmapi")] static extern int DwmSetWindowAttribute(nint h, int attribute, ref int value, int size);
    protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); int dark = 1; DwmSetWindowAttribute(Handle, 20, ref dark, 4); }
    internal SettingsWindow(Preferences prefs, Action changed, Func<bool>? paused = null, Action<bool>? setPause = null, Corrector? corrector = null)
    {
        data = prefs; this.changed = changed; isPaused = paused ?? (() => false); setPaused = setPause ?? (_ => { });
        engine = corrector ?? new Corrector(Path.Combine(AppContext.BaseDirectory, "pt_BR", "pt_BR.dic"));
        Text = "automatic grammar correction"; Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        Font = Theme.Font(10); BackColor = Theme.Bg; ForeColor = Theme.Text; AutoScaleMode = AutoScaleMode.Dpi;
        StartPosition = FormStartPosition.CenterScreen; MinimumSize = new Size(690, 480); ClientSize = new Size(866, 624);
        Controls.Add(page); Controls.Add(rail);
        using var brand = new Icon(Path.Combine(AppContext.BaseDirectory, "tray.ico"), new Size(48, 48));
        var logo = new PictureBox { Image = brand.ToBitmap(), SizeMode = PictureBoxSizeMode.Zoom };
        Put(rail, logo, 19, 25, 41, 41);
        Put(rail, Theme.Label("automatic", 12, Theme.Text, true), 66, 23, 107, 23);
        Put(rail, Theme.Label("grammar correction", 8, Theme.Muted), 67, 46, 108, 17);
        Put(rail, Theme.Label("PAINEL", 8, Theme.Muted, true), 19, 104, 130, 23);
        int top = 138;
        foreach (string name in new[] { "Visão geral", "Digitação", "Aplicativos" })
        {
            var b = Theme.Action(name); b.TextAlign = ContentAlignment.MiddleLeft; b.Padding = new Padding(10, 0, 0, 0);
            b.Image = Theme.NavIcon(name); b.ImageAlign = ContentAlignment.MiddleLeft; b.TextImageRelation = TextImageRelation.ImageBeforeText;
            nav[name] = b; Put(rail, b, 12, top, 152, 38); b.Click += (_, _) => Navigate(name); top += 46;
        }
        Put(rail, status, 18, rail.Height - 62, 144, 22); status.Anchor = AnchorStyles.Left | AnchorStyles.Bottom;
        var version = Theme.Label("v" + Application.ProductVersion.Split('+')[0] + "  ·  Windows", 8, Theme.Muted);
        Put(rail, version, 18, rail.Height - 38, 150, 20); version.Anchor = AnchorStyles.Left | AnchorStyles.Bottom;
        page.SizeChanged += (_, _) => SizeBlocks();
        Navigate(current);
    }
    static void Put(Control parent, Control child, int x, int y, int width, int height, bool stretch = false)
    {
        child.SetBounds(x, y, width, height); parent.Controls.Add(child);
        if (stretch) child.Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top;
    }
    int PageWidth => Math.Max(300, page.ClientSize.Width - 2 * Pad - SystemInformation.VerticalScrollBarWidth);
    void SizeBlocks() { foreach (var b in blocks) b.Width = PageWidth; }
    void Block(Control c, int height, int gap = 12)
    {
        c.SetBounds(Pad, nextY, PageWidth, height); page.Controls.Add(c); blocks.Add(c); nextY += height + gap;
    }
    Surface Card(int height, bool hero = false) { var c = new Surface { Hero = hero }; Block(c, height); return c; }
    static Label TextAt(Control c, string txt, int y, int height, float size, bool bold = false, Color? color = null, int inset = 18)
    {
        var l = Theme.Label(txt, size, color ?? Theme.Text, bold);
        Put(c, l, inset, y, c.Width - inset * 2, height, true); return l;
    }
    void Section(string text) { Block(Theme.Label(text, 10, Theme.Accent, true), 24, 5); }
    internal void Navigate(string name)
    {
        current = name; page.SuspendLayout(); page.AutoScrollPosition = Point.Empty; blocks.Clear();
        while (page.Controls.Count > 0) page.Controls[0].Dispose();
        nextY = 20; status.Text = isPaused() ? "○   Correção pausada" : "●   Correção ativa"; status.ForeColor = isPaused() ? Theme.Muted : Theme.Active;
        foreach (var (key, b) in nav) { b.BackColor = key == name ? Theme.Raised : Theme.Rail; b.ForeColor = key == name ? Theme.Accent : Theme.Muted; }
        Block(Theme.Label("ASSISTENTE DE ESCRITA   /   PT-BR", 8, Theme.Accent, true), 19, 1);
        Block(Theme.Label(name, 23, Theme.Text, true), 44, 3);
        Block(Theme.Label(name switch { "Digitação" => "Escolha quais ajustes podem mudar suas palavras.", "Aplicativos" => "Defina onde a correção pode atuar.", "Visão geral" => "Controle a escrita automática e experimente uma revisão local.", _ => "" }, 10, Theme.Muted), 28, 15);
        if (name == "Digitação") Rules(); else if (name == "Aplicativos") Apps(); else Home();
        page.AutoScrollMinSize = new Size(0, nextY + 4); page.ResumeLayout(true); SizeBlocks();
    }
    void Home()
    {
        var hero = Card(122, true);
        TextAt(hero, "MODO DE DIGITAÇÃO", 11, 20, 8, true, Theme.Accent);
        TextAt(hero, isPaused() ? "Correção pausada" : "Correção palavra por palavra", 32, 36, 17, true);
        var line = Theme.Label("Ao avançar para a próxima palavra, o AGC revisa a anterior.", 9, Theme.Muted);
        Put(hero, line, 18, 72, hero.Width - 165, 36, true);
        var pause = Theme.Action(isPaused() ? "Retomar" : "Pausar"); Put(hero, pause, hero.Width - 109, 74, 90, 30); pause.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        pause.Click += (_, _) => { setPaused(!isPaused()); Navigate(current); };
        Section("Teste de escrita");
        var test = Card(147);
        TextAt(test, "Experimente uma frase", 12, 26, 12, true);
        var input = new TextBox { Text = "voce tambem vem ?", BorderStyle = BorderStyle.FixedSingle, BackColor = Theme.Bg, ForeColor = Theme.Text, Font = Theme.Font(11), AccessibleName = "Texto para testar" };
        Put(test, input, 18, 53, test.Width - 153, 27, true);
        var check = Theme.Action("Revisar", true); Put(test, check, test.Width - 119, 49, 101, 34); check.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        var output = TextAt(test, "A prévia aparece aqui. Seu texto original fica no campo acima.", 89, 45, 10, false, Theme.Muted);
        check.Click += async (_, _) =>
        {
            string original = input.Text; check.Enabled = false;
            try
            {
                string result = await Task.Run(() => engine.Correct(original, data.Rules, true));
                if (!output.IsDisposed && input.Text == original) { output.Text = result.Length == 0 ? "Digite uma frase para revisar." : result; output.ForeColor = Theme.Accent; }
            }
            catch (Exception ex) when (ex is IOException or InvalidOperationException or System.Text.RegularExpressions.RegexMatchTimeoutException)
            {
                if (!output.IsDisposed) output.Text = "Não foi possível carregar a revisão.";
            }
            finally { if (!check.IsDisposed) check.Enabled = true; }
        };
        Section("Seleção e revisão");
        var help = Card(95);
        TextAt(help, "Selecione o trecho que quer melhorar", 10, 26, 11, true);
        TextAt(help, "O pop-up oferece ortografia, gramática e pontuação. Ctrl+Shift+G revisa a seleção inteira. No Codex, cole o resultado com Ctrl+V.", 40, 45, 9, false, Theme.Muted);
    }
    void Rules()
    {
        var r = data.Rules;
        void Row(string title, string detail, bool value, Action<bool> setter)
        {
            var c = Card(62);
            var row = new SwitchRow(title, detail, value, b => { setter(b); changed(); });
            Put(c, row, 7, 2, c.Width - 14, 58, true);
        }
        Section("Ao concluir uma palavra");
        Row("Acentuação", "Restaura acentos frequentes, como em “você” e “também”.", r.Accents, b => r.Accents = b);
        Row("Ortografia durante a digitação", "Corrige erros conhecidos e sugestões próximas sem ambiguidade.", r.WordSpelling, b => r.WordSpelling = b);
        Row("Gramática e concordância", "Revisa construções como “eles tem” e “nós vai”.", r.Grammar, b => r.Grammar = b);
        Section("Formatação da frase");
        Row("Espaçamento", "Normaliza espaços entre palavras e sinais de pontuação.", r.Spacing, b => r.Spacing = b);
        Row("Maiúsculas", "Usa maiúscula no início da frase.", r.Capitals, b => r.Capitals = b);
        Row("Vírgulas", "Ajusta o conector “no entanto” no contexto da frase.", r.Commas, b => r.Commas = b);
        Row("Ponto final", "Conclui parágrafos e revisões solicitadas.", r.FinalStop, b => r.FinalStop = b);
        Section("Revisão completa");
        Row("Sugestões aproximadas", "Inclui ortografia aproximada ao pedir a revisão completa.", r.Spelling, b => r.Spelling = b);
    }
    void Apps()
    {
        foreach (var proc in Process.GetProcesses()) using (proc)
        {
            try { if (proc.Id != Environment.ProcessId && proc.MainWindowHandle != 0 && Preferences.ProcessPath((uint)proc.Id) is { } path) data.Apps.TryAdd(path, true); }
            catch (InvalidOperationException) { }
        }
        var find = Card(65);
        var box = new TextBox
        {
            PlaceholderText = "Buscar por nome ou caminho",
            BackColor = Theme.Bg,
            ForeColor = Theme.Text,
            BorderStyle = BorderStyle.FixedSingle,
            Font = Theme.Font(11),
            AccessibleName = "Buscar aplicativo"
        };
        Put(find, box, 18, 19, find.Width - 36, 27, true);
        var count = Theme.Label("", 9, Theme.Muted, true); Block(count, 25, 8);
        int start = nextY, fixedCount = blocks.Count;
        void Filter()
        {
            page.SuspendLayout();
            while (blocks.Count > fixedCount) { var old = blocks[^1]; blocks.RemoveAt(blocks.Count - 1); old.Dispose(); }
            nextY = start;
            var matches = data.Apps.Where(p => p.Key.Contains(box.Text, StringComparison.OrdinalIgnoreCase)).OrderBy(p => Path.GetFileName(p.Key), StringComparer.OrdinalIgnoreCase).ToArray();
            count.Text = matches.Length == 0 ? "Nenhum aplicativo encontrado" : $"{matches.Length} aplicativo(s)  ·  Novos começam ativados";
            foreach (var entry in matches)
            {
                var c = Card(62);
                var row = new SwitchRow(Path.GetFileName(entry.Key), entry.Key, entry.Value, b => { data.Apps[entry.Key] = b; changed(); });
                Put(c, row, 7, 2, c.Width - 14, 58, true);
            }
            page.AutoScrollMinSize = new Size(0, nextY + 4); page.ResumeLayout(true); SizeBlocks();
        }
        box.TextChanged += (_, _) => Filter(); Filter();
    }
}
