namespace Agc;

internal static class Program
{
    [STAThread]
    static void Main()
    {
        using var mutex = new Mutex(true, @"Local\AutomaticGrammarCorrection.Tray", out bool first);
        if (!first) return;
        ApplicationConfiguration.Initialize();
        try { using var app = new Tray(); Application.Run(app); }
        catch (Exception ex) { MessageBox.Show("Não foi possível iniciar o corretor.\n" + ex.Message, "automatic grammar correction", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }
}
