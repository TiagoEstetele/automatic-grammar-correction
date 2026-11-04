using System.Diagnostics;
using System.Globalization;
using System.IO;
using W = System.Windows;
using C = System.Windows.Controls;
using M = System.Windows.Media;
using D = System.Windows.Documents;
using I = System.Windows.Input;
using System.Windows.Shell;

namespace Agc;

internal sealed class SettingsWindow : W.Window, IDisposable
{
    readonly Preferences prefs;
    readonly Action changed;
    readonly Func<bool> paused;
    readonly Action<bool> pause;
    readonly Corrector engine;
    readonly C.StackPanel content = new() { MaxWidth = 720, HorizontalAlignment = W.HorizontalAlignment.Left };
    readonly C.ScrollViewer scroll = new() { HorizontalScrollBarVisibility = C.ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = C.ScrollBarVisibility.Auto };
    readonly Dictionary<string, C.Button> nav = [];
    readonly C.TextBlock crumb = Txt("", 12), status = Txt("", 13), badge = Txt("", 12);
    C.CheckBox statusSwitch = null!;
    C.TextBox? search;
    C.TextBlock? heroTitle, heroSub;
    C.Button? heroPause;
    readonly List<C.TextBlock> metrics = [];
    string current = "", inputText = "voce nao chegou. eles tem tempo amanha tambem.", query = "", filter = "Todos";
    readonly Dictionary<string, string> friendly = new(StringComparer.OrdinalIgnoreCase);
    int revision;
    bool closed, refreshing;
    internal bool IsDisposed => closed;
    static M.SolidColorBrush Brush(string hex) { var b = new M.SolidColorBrush((M.Color)M.ColorConverter.ConvertFromString(hex)); b.Freeze(); return b; }
    static readonly M.Brush Ink = Brush("#161826"), Rail = Brush("#1E202D"), Surface = Brush("#1D1F2C"), Line = Brush("#303241"), Fg = Brush("#E9E9ED"), Muted = Brush("#A2A4B8"), Soft = Brush("#787B90"), Accent = Brush("#B5ABFC");
    static W.Thickness T(double n) => new(n);
    static C.TextBlock Txt(string s, double size, M.Brush? color = null, bool medium = false) => new() { Text = s, FontSize = size, Foreground = color ?? Fg, FontWeight = medium ? W.FontWeights.Medium : W.FontWeights.Normal, VerticalAlignment = W.VerticalAlignment.Center, TextWrapping = W.TextWrapping.Wrap };
    static C.Button Btn(object label, Action click, string? access = null)
    {
        var b = new C.Button { Content = label, HorizontalContentAlignment = W.HorizontalAlignment.Center };
        if (access != null) System.Windows.Automation.AutomationProperties.SetName(b, access);
        b.Click += (_, _) => click(); return b;
    }
    static C.Border Card(W.UIElement child, double radius = 14) => new() { CornerRadius = new(radius), Background = Surface, BorderBrush = Line, BorderThickness = T(1), Child = child };
    static C.Grid Grid2(double left = 0)
    {
        var g = new C.Grid(); g.ColumnDefinitions.Add(new() { Width = left > 0 ? new W.GridLength(left) : new W.GridLength(1, W.GridUnitType.Star) });
        g.ColumnDefinitions.Add(new() { Width = left > 0 ? new W.GridLength(1, W.GridUnitType.Star) : W.GridLength.Auto }); return g;
    }
    static void Add(C.Grid g, W.UIElement e, int col = 0, int row = 0) { C.Grid.SetColumn(e, col); C.Grid.SetRow(e, row); g.Children.Add(e); }
    static C.TextBlock Glyph(string s, double size = 16) => new() { Text = s, FontSize = size, FontFamily = new M.FontFamily("Segoe MDL2 Assets"), Foreground = Muted, VerticalAlignment = W.VerticalAlignment.Center };
    static C.StackPanel Lines(string title, string detail, double headline = 14)
    {
        var p = new C.StackPanel { VerticalAlignment = W.VerticalAlignment.Center }; p.Children.Add(Txt(title, headline, Fg, true)); var sub = Txt(detail, 12, Muted); sub.Margin = new(0, 4, 0, 0); p.Children.Add(sub); return p;
    }
    public SettingsWindow(Preferences prefs, Action changed, Func<bool>? isPaused = null, Action<bool>? setPause = null, Corrector? corrector = null)
    {
        this.prefs = prefs; this.changed = changed; paused = isPaused ?? (() => !prefs.AutoEnabled); pause = setPause ?? (v => prefs.AutoEnabled = !v);
        engine = corrector ?? new(Path.Combine(AppContext.BaseDirectory, "pt_BR", "pt_BR.dic"));
        Title = "automatic grammar correction"; Width = 1120; Height = 720; MinWidth = 740; MinHeight = 490;
        Width = Math.Min(Width, W.SystemParameters.WorkArea.Width); Height = Math.Min(Height, W.SystemParameters.WorkArea.Height);
        WindowStartupLocation = W.WindowStartupLocation.CenterScreen; Background = Ink; Foreground = Fg; FontSize = 14;
        FontFamily = new M.FontFamily(new Uri("pack://application:,,,/AutomaticGrammarCorrection;component/"), "./Fonts/#Inter Variable");
        Icon = new System.Windows.Media.Imaging.BitmapImage(new Uri("pack://application:,,,/AutomaticGrammarCorrection;component/Brand/app.ico"));
        Resources.MergedDictionaries.Add(new W.ResourceDictionary { Source = new Uri("/AutomaticGrammarCorrection;component/Nocturne.xaml", UriKind.Relative) });
        WindowChrome.SetWindowChrome(this, new WindowChrome { CaptionHeight = 0, CornerRadius = new(12), GlassFrameThickness = T(0), ResizeBorderThickness = T(5), UseAeroCaptionButtons = false });
        var root = Grid2(232); root.Background = Ink; Content = root; BuildRail(root);
        var main = new C.Grid(); main.RowDefinitions.Add(new() { Height = new W.GridLength(44) }); main.RowDefinitions.Add(new()); Add(root, main, 1);
        var top = Grid2(); top.Background = Ink; var trail = new C.StackPanel { Orientation = C.Orientation.Horizontal, Margin = new(28, 0, 0, 0) };
        trail.Children.Add(Txt("AGC   /   ", 12, Soft)); trail.Children.Add(crumb); Add(top, trail);
        top.MouseLeftButtonDown += (_, e) => { if (e.OriginalSource is C.Button) return; if (e.ClickCount == 2) Maximize(); else if (e.LeftButton == I.MouseButtonState.Pressed) DragMove(); };
        var buttons = new C.StackPanel { Orientation = C.Orientation.Horizontal }; Add(top, buttons, 1);
        foreach (var (icon, label, action) in new (string, string, Action)[] { ("", "Minimizar", () => WindowState = W.WindowState.Minimized), ("", "Maximizar ou restaurar", Maximize), ("", "Fechar painel", Close) })
        {
            var b = Btn(Glyph(icon, 12), action, label); b.Width = 46; b.BorderThickness = T(0); buttons.Children.Add(b);
        }
        Add(main, top);
        content.Margin = new(48, 18, 36, 32); scroll.Content = content; Add(main, scroll, 0, 1);
        scroll.SizeChanged += (_, _) => content.Width = Math.Max(220, Math.Min(720, scroll.ActualWidth - 100));
        PreviewKeyDown += (_, e) => { if (current == "Aplicativos" && e.Key == I.Key.F && I.Keyboard.Modifiers == I.ModifierKeys.Control) { search?.Focus(); search?.SelectAll(); e.Handled = true; } };
        Closed += (_, _) => { closed = true; revision++; }; Navigate("Visão geral");
    }
    void Maximize() => WindowState = WindowState == W.WindowState.Maximized ? W.WindowState.Normal : W.WindowState.Maximized;
    void BuildRail(C.Grid root)
    {
        var dock = new C.DockPanel { LastChildFill = true }; Add(root, new C.Border { Background = Rail, BorderBrush = Line, BorderThickness = new(0, 0, 1, 0), Child = dock });
        var brand = Grid2(42); brand.Margin = new(20, 20, 10, 0);
        var mark = new C.Border
        {
            Width = 31,
            Height = 31,
            CornerRadius = new(8),
            Background = Brush("#2B2741"),
            BorderBrush = Brush("#5D5294"),
            BorderThickness = T(1),
            HorizontalAlignment = W.HorizontalAlignment.Left,
            Child = new System.Windows.Shapes.Path { Data = M.Geometry.Parse("M8,10.5 L22,10.5 M8,16 L13.5,16 L17,19.5 L22.5,13"), Stroke = Accent, StrokeThickness = 2.1, StrokeStartLineCap = M.PenLineCap.Round, StrokeEndLineCap = M.PenLineCap.Round, StrokeLineJoin = M.PenLineJoin.Round }
        };
        Add(brand, mark);
        var logo = new C.StackPanel(); logo.Children.Add(Txt("AGC", 14, Fg, true)); logo.Children.Add(Txt("automatic grammar correction", 10, Muted)); Add(brand, logo, 1); C.DockPanel.SetDock(brand, C.Dock.Top); dock.Children.Add(brand);
        var bottom = new C.StackPanel { Margin = T(12) }; var top = Grid2(); var serial = new C.StackPanel { Orientation = C.Orientation.Horizontal };
        serial.Children.Add(Txt("●  ", 12, Accent)); serial.Children.Add(status); Add(top, serial);
        statusSwitch = new C.CheckBox { VerticalAlignment = W.VerticalAlignment.Center }; System.Windows.Automation.AutomationProperties.SetName(statusSwitch, "Correção ativa");
        statusSwitch.Click += (_, _) => { if (!refreshing) { pause(statusSwitch.IsChecked != true); RefreshStatus(); } }; Add(top, statusSwitch, 1);
        var box = new C.StackPanel { Margin = T(12) }; box.Children.Add(top); var version = Txt("Modelo local · pt-BR · v" + typeof(SettingsWindow).Assembly.GetName().Version!.ToString(3), 10, Soft); version.Margin = new(0, 10, 0, 0); box.Children.Add(version);
        var frame = Card(box, 10); frame.Background = Ink; bottom.Children.Add(frame); C.DockPanel.SetDock(bottom, C.Dock.Bottom); dock.Children.Add(bottom);
        var items = new C.StackPanel { Margin = new(12, 26, 12, 0) }; var label = Txt("GERAL", 10, Soft); label.Margin = new(10, 0, 0, 8); items.Children.Add(label);
        foreach (var (name, glyph) in new[] { ("Visão geral", ""), ("Digitação", ""), ("Aplicativos", "") })
        {
            var row = Grid2(); var inner = new C.StackPanel { Orientation = C.Orientation.Horizontal }; inner.Children.Add(Glyph(glyph, 14)); var t = Txt(name, 13); t.Margin = new(10, 0, 0, 0); inner.Children.Add(t); Add(row, inner);
            if (name == "Aplicativos") Add(row, badge, 1);
            var b = Btn(row, () => Navigate(name), name); b.HorizontalContentAlignment = W.HorizontalAlignment.Stretch; b.BorderThickness = T(0); b.Height = 35; b.Margin = new(0, 1, 0, 1); items.Children.Add(b); nav[name] = b;
        }
        dock.Children.Add(items);
    }
    void Block(W.FrameworkElement e, double gap = 24) { e.Margin = new(0, 0, 0, gap); content.Children.Add(e); }
    void Section(string s) { var t = Txt(s.ToUpperInvariant(), 11, Soft); t.Margin = new(4, 0, 0, 10); content.Children.Add(t); }
    internal void Navigate(string name)
    {
        current = name; revision++; metrics.Clear(); heroTitle = heroSub = null; heroPause = null; search = null; content.Children.Clear(); scroll.ScrollToTop();
        foreach (var (key, b) in nav) b.Background = key == name ? Brush("#2E303D") : M.Brushes.Transparent;
        crumb.Text = name; crumb.Foreground = Muted;
        Block(Txt(name, 28, Fg, true), 5);
        Block(Txt(name switch { "Visão geral" => "Tudo é processado no seu computador. Nenhum texto sai daqui.", "Digitação" => "Defina o que o AGC corrige e como ele se comporta enquanto você digita.", _ => "Escolha onde o AGC corrige automaticamente. Apps novos entram ativos." }, 14, Muted), 28);
        if (name == "Visão geral") Home(); else if (name == "Digitação") Typing(); else Apps(); RefreshStatus();
    }
    internal void RefreshStatus()
    {
        if (closed) return; refreshing = true; bool off = paused(); status.Text = off ? "Pausado" : "Corrigindo"; statusSwitch.IsChecked = !off;
        int active = prefs.Apps.Count(a => a.Value); badge.Text = active.ToString();
        if (heroTitle != null) { heroTitle.Text = off ? "Correção pausada" : "Correção ativa"; heroSub!.Text = $"Monitorando {active} de {prefs.Apps.Count} aplicativos · modelo local pt-BR"; heroPause!.Content = off ? "▷   Retomar" : "Ⅱ   Pausar       Ctrl Alt P"; }
        prefs.Today();
        if (metrics.Count == 3)
        {
            metrics[0].Text = prefs.Corrections.ToString("N0", CultureInfo.GetCultureInfo("pt-BR"));
            metrics[1].Text = prefs.CheckedWords.ToString("N0", CultureInfo.GetCultureInfo("pt-BR"));
            metrics[2].Text = prefs.CheckedWords == 0 ? "—" : (prefs.ElapsedMs / prefs.CheckedWords).ToString("0.#", CultureInfo.GetCultureInfo("pt-BR")) + " ms";
        }
        refreshing = false;
    }
    void Home()
    {
        var h = Grid2(); h.Margin = new(24, 0, 23, 0); h.Height = 95; var left = new C.StackPanel { Orientation = C.Orientation.Horizontal };
        var orb = new C.Border
        {
            Width = 44,
            Height = 44,
            CornerRadius = new(12),
            Background = Ink,
            BorderThickness = T(1),
            BorderBrush = Line,
            VerticalAlignment = W.VerticalAlignment.Center,
            Child = new C.TextBlock { Text = "●", FontSize = 21, HorizontalAlignment = W.HorizontalAlignment.Center, VerticalAlignment = W.VerticalAlignment.Center, Foreground = Accent }
        };
        left.Children.Add(orb); var heads = new C.StackPanel { Margin = new(20, 0, 0, 0), VerticalAlignment = W.VerticalAlignment.Center };
        heroTitle = Txt("", 19, Fg, true); heroSub = Txt("", 12, Muted); heroSub.Margin = new(0, 5, 0, 0); heads.Children.Add(heroTitle); heads.Children.Add(heroSub); left.Children.Add(heads); Add(h, left);
        heroPause = Btn("", () => { pause(!paused()); RefreshStatus(); }, "Pausar ou retomar correção"); heroPause.VerticalAlignment = W.VerticalAlignment.Center; Add(h, heroPause, 1);
        var hero = Card(h, 15); hero.BorderBrush = Brush("#50476F"); hero.Background = new M.LinearGradientBrush(M.Color.FromRgb(40, 37, 62), M.Color.FromRgb(32, 34, 46), 0); Block(hero, 27);
        var row = new C.Grid(); foreach (string label in new[] { "Correções hoje", "Palavras verificadas", "Latência média" })
        {
            row.ColumnDefinitions.Add(new()); int col = row.ColumnDefinitions.Count - 1;
            var group = new C.StackPanel { Margin = new(20, 16, 12, 16), VerticalAlignment = W.VerticalAlignment.Center }; group.Children.Add(Txt(label, 12, Muted)); var val = Txt("—", 25, Fg, true); val.Margin = new(0, 9, 0, 0); group.Children.Add(val); metrics.Add(val);
            Add(row, new C.Border { BorderThickness = new(col == 0 ? 0 : 1, 0, 0, 0), BorderBrush = Line, Child = group }, col);
        }
        row.Height = 99; Block(Card(row), 28);
        var tester = new C.StackPanel(); var top = Grid2(); top.Margin = new(20, 13, 14, 0); Add(top, Txt("✎   Teste de escrita", 14, Fg, true));
        var input = new C.TextBox { Text = inputText, AcceptsReturn = true, TextWrapping = W.TextWrapping.Wrap, MinHeight = 76, FontSize = 16, Margin = new(20, 10, 20, 12) };
        System.Windows.Automation.AutomationProperties.SetName(input, "Teste de escrita");
        var clear = Btn("Limpar", () => { input.Clear(); input.Focus(); }); clear.BorderThickness = T(0); Add(top, clear, 1); tester.Children.Add(top); tester.Children.Add(input);
        tester.Children.Add(new C.Border { Height = 1, Background = Line }); var heading = Grid2(); heading.Margin = new(20, 16, 20, 8); Add(heading, Txt("RESULTADO", 10, Soft)); var count = Txt("", 11, Accent, true); Add(heading, count, 1); tester.Children.Add(heading);
        var result = Txt("", 16, Fg, true); result.Margin = new(20, 0, 20, 18); result.MinHeight = 54; result.LineHeight = 26; tester.Children.Add(result); Block(Card(tester), 0);
        async void Update()
        {
            inputText = input.Text; var value = inputText; int ticket = ++revision;
            try
            {
                await Task.Delay(110); if (ticket != revision || closed) return;
                string fixedText = await Task.Run(() => engine.Correct(value, prefs.Rules, true));
                if (ticket != revision || closed) return;
                var spans = TextDiff.Compare(value, fixedText); result.Inlines.Clear();
                foreach (var s in spans) { var run = new D.Run(s.After); if (s.Changed) { run.Background = Brush("#3B3851"); run.TextDecorations = W.TextDecorations.Underline; } result.Inlines.Add(run); }
                int n = spans.Count(s => s.Changed); count.Text = n == 1 ? "1 correção" : $"{n} correções";
            }
            catch (Exception ex) when (ex is IOException or InvalidOperationException or System.Text.RegularExpressions.RegexMatchTimeoutException) { if (ticket == revision) count.Text = "Revisão indisponível"; }
        }
        input.TextChanged += (_, _) => Update(); Update();
    }
    static C.CheckBox Toggle(bool value, string name, Action<bool> action)
    {
        var sw = new C.CheckBox { IsChecked = value, VerticalAlignment = W.VerticalAlignment.Center }; System.Windows.Automation.AutomationProperties.SetName(sw, name);
        sw.Checked += (_, _) => action(true); sw.Unchecked += (_, _) => action(false); return sw;
    }
    void Row(C.StackPanel list, string icon, string title, string sub, W.UIElement end)
    {
        if (list.Children.Count > 0) list.Children.Add(new C.Border { Height = 1, Background = Line });
        var g = Grid2(); g.MinHeight = 74; g.Margin = new(20, 0, 20, 0); var l = Grid2(35); l.VerticalAlignment = W.VerticalAlignment.Center; Add(l, Glyph(icon)); Add(l, Lines(title, sub), 1);
        Add(g, l); Add(g, end, 1); list.Children.Add(g);
    }
    void Typing()
    {
        var r = prefs.Rules; Section("Correção"); var list = new C.StackPanel(); Block(Card(list));
        void R(string i, string t, string d, bool v, Action<bool> set) => Row(list, i, t, d, Toggle(v, t, b => { set(b); changed(); }));
        Row(list, "", "Correção automática", "Substitui erros enquanto você digita.", Toggle(!paused(), "Correção automática", b => { pause(!b); RefreshStatus(); }));
        R("", "Acentuação", "voce → você, nao → não, amanha → amanhã.", r.Accents, b => r.Accents = b);
        R("", "Maiúscula no início da frase", "Capitaliza após ponto final, interrogação e exclamação.", r.Capitals, b => r.Capitals = b);
        R("", "Espaços duplos", "Remove espaços repetidos entre palavras.", r.Spacing, b => r.Spacing = b);
        Section("Comportamento"); var behavior = new C.StackPanel(); Block(Card(behavior));
        Row(behavior, "", "Quando corrigir", "Escolha quando revisar a palavra concluída.", Segments([("now", "Imediato"), ("300", "300 ms"), ("ponto", "Ao pontuar")], prefs.Timing, v => { prefs.Timing = v; changed(); Navigate(current); }));
        Row(behavior, "", "Corrigir seleção", "Selecione um trecho em qualquer app e use o atalho.", Txt("Ctrl   Shift   G", 12, Muted));
        Row(behavior, "", "Som ao corrigir", "Um som discreto a cada substituição confirmada.", Toggle(prefs.Sound, "Som ao corrigir", b => { prefs.Sound = b; changed(); }));
        Row(behavior, "", "Indicador junto ao cursor", "Exibe sugestões de palavra perto do cursor.", Toggle(prefs.Indicator, "Indicador junto ao cursor", b => { prefs.Indicator = b; changed(); }));
        Section("Revisão de português"); list = new C.StackPanel(); Block(Card(list));
        R("", "Ortografia durante a digitação", "Erros conhecidos e sugestões sem ambiguidade.", r.WordSpelling, b => r.WordSpelling = b);
        R("", "Gramática e concordância", "Revisa construções como “eles tem”.", r.Grammar, b => r.Grammar = b);
        R("", "Vírgulas", "Pontua conectores como “no entanto” e “ou seja”.", r.Commas, b => r.Commas = b);
        R("", "Pontuação final", "Conclui parágrafos e identifica perguntas diretas.", r.FinalStop, b => r.FinalStop = b);
        R("", "Ortografia aproximada", "Inclui sugestões aproximadas na revisão completa.", r.Spelling, b => r.Spelling = b);
    }
    static C.Border Segments((string key, string label)[] opts, string active, Action<string> pick)
    {
        var p = new C.StackPanel { Orientation = C.Orientation.Horizontal }; foreach (var (k, t) in opts) { var b = Btn(t, () => pick(k)); b.FontSize = 12; b.BorderThickness = T(0); b.Padding = new(9, 6, 9, 6); b.Background = k == active ? Brush("#40424C") : M.Brushes.Transparent; b.Foreground = k == active ? Fg : Muted; p.Children.Add(b); }
        var box = Card(p, 9); box.Background = Ink; box.Padding = T(3); box.VerticalAlignment = W.VerticalAlignment.Center; return box;
    }
    void Apps()
    {
        foreach (var proc in Process.GetProcesses()) using (proc) try { if (proc.Id != Environment.ProcessId && proc.MainWindowHandle != 0 && Preferences.ProcessPath((uint)proc.Id) is { } path) prefs.Apps.TryAdd(path, true); } catch (InvalidOperationException) { }
        var toolbar = Grid2(); var searchWrap = Grid2(30); searchWrap.Margin = new(10, 0, 8, 0); Add(searchWrap, Glyph("")); search = new C.TextBox { Text = query, VerticalContentAlignment = W.VerticalAlignment.Center, FontSize = 14, Margin = new(0, 0, 43, 0) };
        System.Windows.Automation.AutomationProperties.SetName(search, "Buscar aplicativo ou processo"); Add(searchWrap, search, 1);
        var placeholder = Txt("Buscar aplicativo ou processo", 13, Soft); placeholder.IsHitTestVisible = false; Add(searchWrap, placeholder, 1);
        var key = Txt("Ctrl F", 11, Soft); key.HorizontalAlignment = W.HorizontalAlignment.Right; key.IsHitTestVisible = false; Add(searchWrap, key, 1);
        var searchCard = Card(searchWrap, 9); searchCard.Height = 40; searchCard.Margin = new(0, 0, 10, 0); Add(toolbar, searchCard);
        Add(toolbar, Segments([("Todos", "Todos"), ("Ativos", "Ativos"), ("Ignorados", "Ignorados")], filter, v => { filter = v; Navigate(current); }), 1); Block(toolbar, 24);
        var table = new C.StackPanel(); Block(Card(table), 15);
        void Filter()
        {
            query = search!.Text; placeholder.Visibility = query.Length == 0 ? W.Visibility.Visible : W.Visibility.Hidden;
            string Name(string path)
            {
                if (!friendly.TryGetValue(path, out var value))
                {
                    value = Path.GetFileNameWithoutExtension(path);
                    try { if (File.Exists(path) && FileVersionInfo.GetVersionInfo(path).FileDescription is { Length: > 0 } desc) value = desc; }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception) { }
                    friendly[path] = value;
                }
                return value;
            }
            table.Children.Clear(); var apps = prefs.Apps.Where(kv => (kv.Key.Contains(query, StringComparison.OrdinalIgnoreCase) || Name(kv.Key).Contains(query, StringComparison.OrdinalIgnoreCase)) && (filter == "Todos" || kv.Value == (filter == "Ativos"))).OrderBy(k => Name(k.Key), StringComparer.OrdinalIgnoreCase).ToArray();
            var th = Grid2(); th.Margin = new(20, 11, 20, 11); Add(th, Txt($"{apps.Length} APLICATIVOS", 11, Soft)); Add(th, Txt("CORREÇÃO", 11, Soft), 1); table.Children.Add(th);
            foreach (var (path, on) in apps)
            {
                table.Children.Add(new C.Border { Height = 1, Background = Line }); string exe = Path.GetFileName(path), name = Name(path);
                var row = Grid2(); row.Height = 55; row.Margin = new(20, 0, 20, 0); var left = Grid2(46);
                var tile = Card(Txt(name.Length > 0 ? name[..1].ToUpperInvariant() : "·", 14, Muted, true), 8); tile.Width = 32; tile.Height = 32; tile.Padding = new(9, 0, 0, 0); tile.HorizontalAlignment = W.HorizontalAlignment.Left; tile.Background = Brush("#292B33"); Add(left, tile); Add(left, Lines(name, exe), 1); left.ToolTip = path; Add(row, left);
                var end = new C.StackPanel { Orientation = C.Orientation.Horizontal }; if (!on) { var ignored = Txt("Ignorado", 12, Soft); ignored.Margin = new(8, 0, 14, 0); end.Children.Add(ignored); }
                end.Children.Add(Toggle(on, name, b => { prefs.Apps[path] = b; changed(); Filter(); RefreshStatus(); })); Add(row, end, 1); table.Children.Add(row);
            }
            if (apps.Length == 0) { var none = Lines("Nenhum aplicativo encontrado", "Tente outro nome ou adicione pelo executável."); none.Margin = T(20); table.Children.Add(none); }
        }
        search.TextChanged += (_, _) => Filter(); Filter();
        var add = Btn("+   Adicionar aplicativo", () =>
        {
            var dialog = new Microsoft.Win32.OpenFileDialog { Filter = "Aplicativos Windows (*.exe)|*.exe", CheckFileExists = true };
            if (dialog.ShowDialog(this) == true) { prefs.Apps.TryAdd(dialog.FileName, true); changed(); query = ""; filter = "Todos"; Navigate(current); }
        });
        add.HorizontalAlignment = W.HorizontalAlignment.Left; Block(add);
    }
    internal void SavePreview(string path)
    {
        UpdateLayout(); var root = (W.FrameworkElement)Content;
        var image = new System.Windows.Media.Imaging.RenderTargetBitmap((int)Math.Ceiling(root.ActualWidth), (int)Math.Ceiling(root.ActualHeight), 96, 96, M.PixelFormats.Pbgra32);
        image.Render(root); var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder(); encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(image)); using var f = File.Create(path); encoder.Save(f);
    }
    public void Dispose() { if (!closed) Close(); }
}
