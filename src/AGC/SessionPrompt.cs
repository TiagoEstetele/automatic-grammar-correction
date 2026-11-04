namespace Agc;

internal sealed class SessionPrompt : Form
{
    internal SessionPrompt(string app)
    {
        Text = "Correção em tela cheia"; BackColor = Theme.Bg; ForeColor = Theme.Text; Font = Theme.Font(10);
        FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; MinimizeBox = false; ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterScreen; ClientSize = new Size(435, 210); AutoScaleMode = AutoScaleMode.Dpi;
        var badge = Theme.Label("AUTOMATIC GRAMMAR CORRECTION", 8, Theme.Accent, true); badge.SetBounds(22, 17, 390, 22);
        var title = Theme.Label("Ativar nesta sessão?", 18, Theme.Text, true); title.SetBounds(20, 43, 392, 38);
        var text = Theme.Label(app + " está em tela cheia. A correção está pausada. Sua escolha vale até essa janela sair da tela cheia.", 10, Theme.Muted); text.SetBounds(22, 88, 390, 54);
        var no = Theme.Action("Manter pausado"); no.SetBounds(114, 158, 145, 34); no.DialogResult = DialogResult.No;
        var yes = Theme.Action("Ativar", true); yes.SetBounds(270, 158, 140, 34); yes.DialogResult = DialogResult.Yes;
        Controls.AddRange([badge, title, text, no, yes]); CancelButton = no; AcceptButton = no;
    }
}
