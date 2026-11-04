namespace Agc;

internal static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        bool openPanel=!args.Contains("--tray",StringComparer.OrdinalIgnoreCase);
        // Crie o sinal antes da eleição. Um segundo lançamento durante o início
        // fica pendente até a janela principal começar a atender mensagens.
        using var open=new EventWaitHandle(false,EventResetMode.AutoReset,@"Local\AutomaticGrammarCorrection.Settings");
        using var mutex = new Mutex(true, @"Local\AutomaticGrammarCorrection.Tray", out bool first);
        if (!first) { if(openPanel) { Native.GrantForegroundToTray(); open.Set(); } return; }
        ApplicationConfiguration.Initialize();
        try {
            using var app = new Tray();
            var wait=ThreadPool.RegisterWaitForSingleObject(open,(_,_)=>app.RequestSettings(),null,Timeout.Infinite,false);
            try { if(openPanel)app.RequestSettings(); Application.Run(app); }
            finally {wait.Unregister(null);}
        }
        catch (Exception ex) { MessageBox.Show("Não foi possível iniciar o corretor.\n" + ex.Message, "automatic grammar correction", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }
}
