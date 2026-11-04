namespace Agc;

internal sealed class SessionPrompt : Form
{
    internal SessionPrompt(string app)
    {
        Text = "Correção em tela cheia"; BackColor = Theme.Bg; ForeColor = Theme.Text; Font = Theme.Font(10);
        FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; MinimizeBox = false; ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterScreen; ClientSize = new Size(435, 210); AutoScaleMode = AutoScaleMode.Dpi;
        using var markStream = typeof(SessionPrompt).Assembly.GetManifestResourceStream("AGC.Brand.Icon")!;
        using var brand = new Icon(markStream, 32, 32);
        var logo = new PictureBox { Image = brand.ToBitmap(), SizeMode = PictureBoxSizeMode.Zoom }; logo.SetBounds(20, 14, 28, 28); Controls.Add(logo);
        var badge = Theme.Label("AGC   /   Tela cheia", 9, Theme.Muted); badge.SetBounds(58, 17, 340, 22);
        var title = Theme.Label("Ativar nesta sessão?", 18, Theme.Text, true); title.SetBounds(20, 43, 392, 38);
        var text = Theme.Label(app + " está em tela cheia. A correção está pausada. Sua escolha vale até essa janela sair da tela cheia.", 10, Theme.Muted); text.SetBounds(22, 88, 390, 54);
        var no = Theme.Action("Manter pausado"); no.SetBounds(114, 158, 145, 34); no.DialogResult = DialogResult.No;
        var yes = Theme.Action("Ativar", true); yes.SetBounds(270, 158, 140, 34); yes.DialogResult = DialogResult.Yes;
        Controls.AddRange([badge, title, text, no, yes]); CancelButton = no; AcceptButton = no;
    }
}
