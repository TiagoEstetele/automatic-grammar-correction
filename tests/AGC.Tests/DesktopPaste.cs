using Agc;
using System.Runtime.InteropServices;
using System.Windows.Forms;

// Prova da colagem real em uma janela de teste, no desktop isolado do runner.
// Só envia entrada se o HWND em primeiro plano é exatamente o deste teste.
internal static class DesktopPaste
{
    [DllImport("user32")] static extern bool SetForegroundWindow(nint h);
    internal static int Run(Corrector engine)
    {
        if(Environment.GetEnvironmentVariable("GITHUB_ACTIONS")!="true") throw new InvalidOperationException("Este teste usa a área de transferência do runner.");
        Application.EnableVisualStyles();
        using var form=new Form{Text="AGC · teste de colagem",Width=600,Height=240,TopMost=true};
        using var field=new RichTextBox{Dock=DockStyle.Fill};
        form.Controls.Add(field);
        string? error=null;
        form.Shown+=async (_,_)=>{
            try{
                form.Activate(); SetForegroundWindow(form.Handle); field.Focus();
                await Task.Delay(180);
                var frame=form.Handle; var focus=field.Handle;
                bool Valid()=>Native.GetForegroundWindow()==frame && Native.Focus(frame)==focus;
                if(!Valid())throw new Exception("O desktop não concedeu foco à janela de teste.");
                async Task<RangeOutcome> Apply(bool auto,string? result){
                    return await Task.Run(()=>{
                        var state=RangeOutcome.Cancelled;
                        ReviewProbe.Read(frame,engine,new(),auto,Valid,auto,(target,live)=>{
                            state=RangeEdit.Apply(target,live,auto?target.WordResult!:result!,auto,Valid);
                        });
                        return state;
                    });
                }
                field.Text="Prefixo | por que voce saiu | Fim";
                field.Select(9,"por que voce saiu".Length);
                var fixedText=engine.Correct("por que voce saiu",new(),true);
                Clipboard.SetText(fixedText);
                var sent=await Apply(false,fixedText);
                if(field.Text!="Prefixo | Por que você saiu? | Fim" || sent==RangeOutcome.Cancelled)
                    throw new Exception("A seleção não foi substituída corretamente. Estado: "+sent+"; texto: "+field.Text);
                field.Text="eu voltei !";field.Select(field.TextLength,0);
                var punct=await Apply(true,null);
                if(field.Text!="eu voltei!" || punct==RangeOutcome.Cancelled || field.SelectionLength!=0)
                    throw new Exception("Pontuação no cursor falhou: "+punct+"; texto: "+field.Text);
            }catch(Exception ex){error=ex.Message;}
            finally{form.Close();}
        };
        Application.Run(form);
        if(error!=null){Console.Error.WriteLine(error);return 1;}
        Console.WriteLine("Colagem e pontuação confirmadas em um editor real do Windows.");return 0;
    }
}
