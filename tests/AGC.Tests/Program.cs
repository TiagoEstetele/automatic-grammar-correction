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
        var r = new RuleSettings();
        Check("início preguiçoso", false, engine.DictionaryLoaded);
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
        if (args.Contains("--windows")) WindowsCases(engine);
        if (Array.IndexOf(args, "--preview") is int index && index >= 0 && index + 1 < args.Length)
        {
            using var settings = new SettingsWindow(new Preferences(), () => { });
            settings.ShowInTaskbar = false;
            settings.StartPosition = FormStartPosition.Manual;
            settings.Location = new Point(-30000, -30000);
            settings.Show();
            Application.DoEvents();
            using var frame = new Bitmap(settings.Width, settings.Height);
            settings.DrawToBitmap(frame, new Rectangle(Point.Empty, frame.Size));
            frame.Save(args[index + 1], System.Drawing.Imaging.ImageFormat.Png);
        }
        Console.WriteLine($"{count - failed}/{count} verificações aprovadas.");
        return failed == 0 ? 0 : 1;
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
        box.Text = "voce voltou "; box.SelectionStart = box.TextLength;
        Check("automático no Edit", Outcome.Replaced, Run(false).State);
        Check("substituição", "Você voltou ", box.Text);
        Check("cursor no final", box.TextLength, box.SelectionStart);
        box.Text = "voce voltou hoj"; box.SelectionStart = box.TextLength;
        Check("digitação contínua", Outcome.Replaced, Run(false).State);
        Check("palavra incompleta preservada", "Você voltou hoj", box.Text);
        Check("cursor após palavra incompleta", box.TextLength, box.SelectionStart);
        box.Text = "voce voltou\r\n"; box.SelectionStart = box.TextLength;
        Check("Enter conclui parágrafo", Outcome.Replaced, Run(false).State);
        Check("quebra de linha mantida", "Você voltou.\r\n", box.Text);
        box.Text = "Prefixo — voce voltou — sufixo"; box.Select(10, 11);
        Check("seleção", Outcome.Replaced, Run(true).State);
        Check("sem alterar vizinhos", "Prefixo — você voltou. — sufixo", box.Text);
        box.Text = "voce voltou "; box.SelectionStart = box.TextLength;
        Check("cancelamento", Outcome.Cancelled, Run(false, () => false).State);
        Check("sem alteração após cancelamento", "voce voltou ", box.Text);
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
