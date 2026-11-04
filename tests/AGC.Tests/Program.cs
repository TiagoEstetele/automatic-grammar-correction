using Agc;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

internal static class Program
{
    static int count, failed;
    static void Check<T>(string name, T expected, T actual)
    {
        count++;
        if (EqualityComparer<T>.Default.Equals(expected, actual)) return;
        failed++; Console.Error.WriteLine($"{name}\n  esperado: {expected}\n  obtido:  {actual}");
    }
    [STAThread]
    static int Main(string[] args)
    {
        var engine = new Corrector(Path.Combine(AppContext.BaseDirectory, "pt_BR", "pt_BR.dic"));
        if (args.Contains("--desktop-paste")) return DesktopPaste.Run(engine);
        var r = new RuleSettings();
        Check("início preguiçoso", false, engine.DictionaryLoaded);
        var diff = TextDiff.Compare("Eles tem tempo hoje.", "Eles têm tempo hoje.");
        Check("prévia destaca apenas tem → têm", 1, diff.Count(s => s.Changed));
        Check("motivo da revisão", "concordância com sujeito plural", TextDiff.Explain(diff.Single(s => s.Changed)));
        Check("destaques preservam a frase completa", "Eles têm tempo hoje.", string.Concat(diff.Select(s => s.After)));
        var yesterday = new Preferences { StatsDay = "2000-01-01", CheckedWords = 13, Corrections = 4, ElapsedMs = 40 };
        yesterday.Today(); Check("contadores viram no dia local", (0L, 0L, 0d), (yesterday.CheckedWords, yesterday.Corrections, yesterday.ElapsedMs));
        Corrector.WordChange? Word(string s) => engine.CompletedWord(s, s.Length, r);
        Check("resposta no primeiro espaço", "Você", Word("voce ")?.After);
        Check("acentos frequentes sem carregar Hunspell", false, engine.DictionaryLoaded);
        Check("palavra anterior com próxima já iniciada", "você", Word("eu vi voce ag")?.After);
        Check("palavra ainda aberta", null, Word("voce"));
        Check("nenhuma revisão de palavra antiga", null, Word("voce voltou "));
        Check("erro comum ao avançar", "Exceção", Word("excessão ")?.After);
        Check("URL intacta", null, Word("eu escrevi https://teste.br/voce "));
        string? Tail(string s)
        {
            var patch = engine.TrailingPunctuation(s, r);
            return patch is { } p ? s[..p.Start] + p.After + s[p.End..] : null;
        }
        Check("vírgula automática no cursor", "no entanto, ", Tail("no entanto "));
        Check("conector adicional", "além disso, ", Tail("além disso "));
        Check("remove espaço antes do sinal", "eu voltei!", Tail("eu voltei !"));
        Check("espaço automático depois de vírgula", "eu voltei, ", Tail("eu voltei,"));
        Check("não acrescenta espaço à vírgula decimal", null, Tail("eu paguei 1,"));
        Check("não repete vírgula", null, Tail("no entanto, "));
        Check("não muda letras automaticamente na UIA", null, Tail("eu vi voce "));
        Check("decimal permanece", null, Tail("eu paguei 1,50 "));
        Check("conector com regra desligada", null, engine.TrailingPunctuation("no entanto ", new RuleSettings { Commas = false }));
        Check("interrogativa direta recebe interrogação", "Por que você saiu?", engine.Correct("por que voce saiu", r, true));
        Check("pergunta indireta termina com ponto", "Eu sei por que você saiu.", engine.Correct("eu sei por que voce saiu", r, true));
        Check("edição automática não aceita letras", false, Corrector.OnlySeparators(" oi, "));
        var grammarOnly = ReviewOptions.Filter(r, ReviewKind.Grammar);
        Check("só gramática", "eles têm tempo", engine.Correct("eles tem tempo", grammarOnly, true));
        Check("só pontuação", "Voce vem?", engine.Correct("voce vem ?", ReviewOptions.Filter(r, ReviewKind.Punctuation), true));
        Check("pop-up acima da seleção", new Point(-900, 264), ReviewPopup.Place(new(-900, 370, 80, 20), new(300, 100), new(-1200, 0, 1200, 800)));
        Check("pop-up cabe abaixo no topo", new Point(0, 31), ReviewPopup.Place(new(-8, 5, 30, 20), new(300, 100), new(0, 0, 600, 400)));
        foreach (var (input, expected) in new (string, string)[]{
            ("voce  tambem vem ?","Você também vem?"),
            ("isso custa 1,50 reais","Isso custa 1,50 reais."),
            ("hoje eu fui. amanha eu volto","Hoje eu fui. Amanhã eu volto."),
            ("o dr. silva chegou hoje","O dr. silva chegou hoje."),
            ("no entanto eu nao fui","No entanto, eu não fui."),
            ("isso me da vontade de correr","Isso me dá vontade de correr."),
            ("eu comprei altos falantes","Eu comprei alto-falantes."),
            ("eles tem tempo hoje","Eles têm tempo hoje."),
            ("nós vai amanhã","Nós vamos amanhã."),
            ("a gente vamos hoje","A gente vai hoje."),
            ("voce usa https://exemplo.com/a?q=x","Você usa https://exemplo.com/a?q=x"),
            ("eu uso agc_test e falo com ana@example.org","Eu uso agc_test e falo com ana@example.org"),
            ("voce veio\r\neu tambem","Você veio.\r\nEu também."),
            ("Você vem?!","Você vem?!"),
            ("I wrote an English sentence","I wrote an English sentence"),
            ("const value = 'voce';","const value = 'voce';"),
            ("- voce vai","- voce vai"),
            ("",""),
            ("😀","😀")
        })
        {
            string actual = engine.Correct(input, r, true);
            Check(input, expected, actual);
            Check("idempotência " + input, actual, engine.Correct(actual, r, true));
        }
        Check("dicionário usado", true, engine.DictionaryLoaded);
        Check("sem ponto na pausa", "Você veio ", engine.Correct("voce veio ", r, false));
        Check("parágrafo concluído", "Você veio.", engine.Correct("voce veio", r, false, true));
        Check("trecho ambíguo", "do ", engine.Correct("do ", r, false));
        Check("regras desligadas", "voce  tambem vem ?", engine.Correct("voce  tambem vem ?", new RuleSettings { Accents = false, Capitals = false, Spacing = false, Commas = false, Grammar = false, FinalStop = false }, true));
        var m = new Rectangle(-1920, 0, 1920, 1080);
        Check("monitor secundário", true, Fullscreen.Covers(m, m, false, false));
        Check("maximizada com moldura", false, Fullscreen.Covers(m, m, false, true));
        Check("minimizada", false, Fullscreen.Covers(m, m, true, false));
        Check("barra de tarefas", false, Fullscreen.Covers(new(-1920, 0, 1920, 1040), m, false, false));
        Check("monitor errado", false, Fullscreen.Covers(new(0, 0, 1920, 1080), m, false, false));
        Check("DPI tolerância", true, Fullscreen.Covers(new(-1919, 1, 1918, 1078), m, false, false));
        Check("retângulo inválido", false, Fullscreen.Covers(Rectangle.Empty, m, false, false));
        var session = new FullscreenSessions();
        Check("primeiro aviso", true, session.First(5, 20));
        Check("pausa inicial", false, session.Allowed(5, 20));
        session.Answer(5, 20, true);
        Check("Alt+Tab", false, session.First(5, 20));
        Check("decisão preservada", true, session.Allowed(5, 20));
        Check("PID diferente", false, session.Allowed(5, 21));
        session.End(5);
        Check("reentrada pergunta novamente", true, session.First(5, 20));
        Check("sem permissão herdada", false, session.Allowed(5, 20));
        if (args.Contains("--windows")) { WindowsCases(engine); PanelCases(engine); }
        if (Array.IndexOf(args, "--preview") is int index && index >= 0 && index + 1 < args.Length)
        {
            using var settings = new SettingsWindow(new Preferences(), () => { });
            settings.ShowInTaskbar = false; settings.WindowStartupLocation = System.Windows.WindowStartupLocation.Manual;
            settings.Left = -30000; settings.Top = -30000; settings.Show();
            // Allow the real asynchronous preview to finish before recording the window.
            for (int n = 0; n < 30; n++) { Application.DoEvents(); Thread.Sleep(20); }
            settings.SavePreview(args[index + 1]); settings.Navigate("Digitação"); for (int n = 0; n < 12; n++) { Application.DoEvents(); Thread.Sleep(20); }
            settings.SavePreview(Path.ChangeExtension(args[index + 1], ".rules.png"));
            using var popup = new ReviewPopup();
            popup.Present(new(0, 0, "demo", "Eles tem tempo hoje.", new Rectangle(0, 0, 120, 20)));
            popup.Location = new Point(-30000, -30000);
            popup.ShowResult("Eles têm tempo hoje.", true);
            popup.Location = new Point(-30000, -30000);
            using var panel = new Bitmap(popup.Width, popup.Height);
            popup.DrawToBitmap(panel, new Rectangle(Point.Empty, panel.Size));
            panel.Save(Path.ChangeExtension(args[index + 1], ".popup.png"), System.Drawing.Imaging.ImageFormat.Png);
            using var menu = new TrayMenu();
            menu.SetStatus(false, 0);
            menu.Entry("Pausar por 15 minutos", "", () => { }); menu.Entry("Pausar até amanhã", "", () => { });
            menu.Items.Add(new ToolStripSeparator()); menu.Entry("Corrigir seleção", "", () => { }, "Ctrl Shift G");
            menu.Entry("Corrigir texto copiado", "", () => { }); menu.Entry("Ignorar neste aplicativo", "", () => { });
            var hover = menu.Entry("Abrir painel", "", () => { }); menu.Items.Add(new ToolStripSeparator()); menu.Entry("Sair do AGC", "", () => { });
            menu.Show(new Point(-30000, -30000)); hover.Select(); Application.DoEvents();
            using var trayImage = new Bitmap(menu.Width, menu.Height);
            menu.DrawToBitmap(trayImage, new Rectangle(Point.Empty, trayImage.Size));
            trayImage.Save(Path.ChangeExtension(args[index + 1], ".menu.png"), System.Drawing.Imaging.ImageFormat.Png);
            menu.Close();
        }
        Console.WriteLine($"{count - failed}/{count} verificações aprovadas.");
        return failed == 0 ? 0 : 1;
    }
    static IEnumerable<T> Descendants<T>(System.Windows.DependencyObject root) where T : System.Windows.DependencyObject
    {
        for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(root, i); if (child is T t) yield return t;
            foreach (var found in Descendants<T>(child)) yield return found;
        }
    }
    static void PanelCases(Corrector engine)
    {
        var prefs = new Preferences(); int saves = 0;
        using var window = new SettingsWindow(prefs, () => saves++, corrector: engine)
        {
            ShowActivated = false,
            ShowInTaskbar = false,
            WindowStartupLocation = System.Windows.WindowStartupLocation.Manual,
            Left = -30000,
            Top = -30000
        };
        window.Show(); window.Navigate("Digitação"); window.UpdateLayout();
        var accents = Descendants<System.Windows.Controls.CheckBox>(window).Single(c => System.Windows.Automation.AutomationProperties.GetName(c) == "Acentuação");
        accents.IsChecked = false; Check("regra editada pelo painel", false, prefs.Rules.Accents);
        var delay = Descendants<System.Windows.Controls.Button>(window).Single(b => Equals(b.Content, "300 ms"));
        delay.RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        Check("intervalo escolhido no painel", "300", prefs.Timing);
        Check("preferências notificam persistência", true, saves >= 2);
        window.Navigate("Aplicativos"); window.UpdateLayout();
        var search = Descendants<System.Windows.Controls.TextBox>(window).Single(); search.Text = "agc-fixture-unlisted.exe";
        window.UpdateLayout(); Check("busca sem resultado", true, Descendants<System.Windows.Controls.TextBlock>(window).Any(x => x.Text == "Nenhum aplicativo encontrado"));
    }
    static void WindowsCases(Corrector engine)
    {
        Application.EnableVisualStyles();
        using var form = new Form { Text = "AGC · teste de integração", ShowInTaskbar = false };
        using var box = new TextBox { Dock = DockStyle.Fill, Multiline = true };
        form.Controls.Add(box); _ = box.Handle;
        // Exercita mensagens reais do controle sem tomar o foco do usuário em CI.
        Check("janela sem foco nunca é editada", Outcome.Cancelled, Editor.Run(form.Handle, false, engine, new RuleSettings(), () => true).State);
        EditResult Run(bool manual, Func<bool>? valid = null) => Editor.Plain(box.Handle, manual, engine, new RuleSettings(), valid ?? (() => true));
        box.Text = "eu vi voce "; box.SelectionStart = box.TextLength;
        Check("automático no Edit", Outcome.Replaced, Run(false).State);
        Check("substituição", "eu vi você ", box.Text);
        Check("cursor no final", box.TextLength, box.SelectionStart);
        box.Text = "eu vi voce hoj"; box.SelectionStart = box.TextLength;
        Check("digitação contínua", Outcome.Replaced, Run(false).State);
        Check("palavra incompleta preservada", "eu vi você hoj", box.Text);
        Check("cursor após palavra incompleta", box.TextLength, box.SelectionStart);
        box.Text = "eu vi voce\r\n"; box.SelectionStart = box.TextLength;
        Check("Enter conclui parágrafo", Outcome.Replaced, Run(false).State);
        Check("quebra de linha mantida", "eu vi você.\r\n", box.Text);
        box.Text = "Prefixo — voce voltou — sufixo"; box.Select(10, 11);
        Check("seleção", Outcome.Replaced, Run(true).State);
        Check("sem alterar vizinhos", "Prefixo — você voltou. — sufixo", box.Text);
        box.Text = "eu vi voce "; box.SelectionStart = box.TextLength;
        Check("cancelamento", Outcome.Cancelled, Run(false, () => false).State);
        Check("sem alteração após cancelamento", "eu vi voce ", box.Text);
        // Interrompa precisamente quando o corretor abriu uma seleção temporária.
        // Na 1.0.0, a próxima tecla apagava essa seleção em vez de continuar a frase.
        Check("cancelamento com seleção temporária", Outcome.Cancelled,
            Run(false, () => box.SelectionLength == 0).State);
        box.SelectedText = "x";
        Check("tecla seguinte preserva o rascunho", "eu vi voce x", box.Text);
        box.Text = "eu vi voce "; box.SelectionStart = box.TextLength;
        Check("Codex ignora digitação mesmo num Edit", Outcome.Unchanged,
            Editor.ForControl(box.Handle, @"C:\Apps\Codex.exe", false, engine, new RuleSettings(), () => true).State);
        Check("Codex mantém o cursor", (box.TextLength, 0), (box.SelectionStart, box.SelectionLength));
        box.SelectedText = "x";
        Check("Codex mantém a frase", "eu vi voce x", box.Text);
        Check("controle sem edição nativa também ignora digitação", Outcome.Unchanged,
            Editor.ForControl(form.Handle, "browser.exe", false, engine, new RuleSettings(), () => true).State);
        box.Text = "eu vi voce "; box.SelectionStart = box.TextLength;
        bool CursorChanged()
        {
            if (box.SelectionLength == 0) return true;
            box.Select(2, 1);
            return false;
        }
        Check("cancelamento por nova seleção", Outcome.Cancelled, Run(false, CursorChanged).State);
        Check("preserva seleção feita pelo usuário", (2, 1), (box.SelectionStart, box.SelectionLength));
        var stale = new ReviewTarget(form.Handle, box.Handle, "0:3", "voce", Rectangle.Empty);
        Check("prévia antiga não substitui seleção nova", Outcome.Cancelled,
            Editor.Plain(box.Handle, true, engine, new RuleSettings(), () => true, stale, "Você").State);
        box.Text = "Prefixo: eles tem tempo. Fim."; box.Select(9, 14);
        var selected = Editor.SelectedTarget(form.Handle, box.Handle);
        Check("seleção nativa usada pelo popup", "eles tem tempo", selected?.Original);
        if (selected != null)
        {
            using var popup = new ReviewPopup();
            popup.Present(selected);
            popup.Location = new Point(-30000, -30000);
            string? preview = null;
            popup.Chosen += kind =>
            {
                preview = engine.Correct(selected.Original, ReviewOptions.Filter(new RuleSettings(), kind), true);
                popup.ShowResult(preview, true);
            };
            Button FindButton(Control parent, string label) => parent.Controls.OfType<Button>().FirstOrDefault(b => b.Text == label)
                ?? parent.Controls.OfType<Panel>().SelectMany(p => p.Controls.OfType<FlowLayoutPanel>())
                    .SelectMany(p => p.Controls.OfType<Button>()).Single(b => b.Text == label);
            FindButton(popup, "Gramática").PerformClick();
            Check("escolha no popup produz prévia", "eles têm tempo", preview);
            Check("prévia preserva editor", "Prefixo: eles tem tempo. Fim.", box.Text);
            Check("aplicar atua só na seleção aprovada", Outcome.Replaced,
                Editor.Plain(box.Handle, true, engine, new RuleSettings(), () => true, selected, preview).State);
            Check("vizinhos da seleção preservados", "Prefixo: eles têm tempo. Fim.", box.Text);
            popup.Dismiss();
        }
        box.Text = "no entanto eu "; box.SelectionStart = box.TextLength; box.MaxLength = box.TextLength;
        Check("limite impede truncamento", Outcome.Cancelled, Run(false).State);
        Check("texto inteiro em campo limitado", "no entanto eu ", box.Text);
        box.MaxLength = 32767;
        box.ReadOnly = true;
        Check("somente leitura", Outcome.Protected, Run(false).State);
        box.ReadOnly = false; box.Multiline = false; box.UseSystemPasswordChar = true; box.Focus();
        Check("senha", Outcome.Protected, Run(false).State);
        form.FormBorderStyle = FormBorderStyle.None;
        form.Bounds = Screen.FromHandle(form.Handle).Bounds;
        Check("área cliente de janela real cobre monitor", true, Fullscreen.IsFull(form.Handle));
        form.Width -= 60;
        Check("janela real deixa a tela cheia", false, Fullscreen.IsFull(form.Handle));
        form.Close();
    }
}
