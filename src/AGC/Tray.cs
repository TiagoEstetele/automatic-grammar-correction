using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Automation;

namespace Agc;

internal sealed class Tray : ApplicationContext
{
    readonly Preferences prefs;
    readonly Corrector engine = new(Path.Combine(AppContext.BaseDirectory, "pt_BR", "pt_BR.dic"));
    readonly FullscreenSessions sessions = new();
    readonly System.Windows.Forms.Timer trayClick = new(), resume = new(), indicator = new() { Interval = 1000 };
    readonly SignalWindow signals;
    readonly NotifyIcon icon;
    readonly Control dispatcher = new();
    readonly ToolStripMenuItem sessionItem = new();
    readonly System.Windows.Forms.Timer debounce = new() { Interval = 25 };
    readonly System.Windows.Forms.Timer selectionTimer = new() { Interval = 55 };
    readonly ReviewPopup popup = new();
    bool probeBusy, probePending;
    readonly System.Windows.Forms.Timer saveTimer = new() { Interval = 1000 };
    SettingsWindow? settings;
    nint lastApp, prompted;
    uint promptedPid, lastPid;
    string? lastPath;
    int generation;
    bool busy, closed, paused;
    bool pendingBoundary;
    bool automaticInFlight;
    nint boundaryWindow, boundaryFocus;
    [DllImport("user32")] static extern bool SetForegroundWindow(nint hwnd);

    internal Tray()
    {
        prefs = Preferences.Load(out var recovered);
        _ = dispatcher.Handle;
        var menu = new TrayMenu();
        signals = new();
        indicator.Tick += (_, _) => { indicator.Stop(); if (popup.Target?.WordResult != null) popup.Dismiss(); };
        icon = new NotifyIcon { Icon = LoadTrayIcon(), Text = "automatic grammar correction", ContextMenuStrip = menu, Visible = true };
        paused = !prefs.AutoEnabled;
        menu.ToggleRequested += () => SetPause(!paused);
        menu.Entry("Pausar por 15 minutos", "", () => SetPause(true, DateTime.Now.AddMinutes(15)));
        menu.Entry("Pausar até amanhã", "", () => SetPause(true, DateTime.Today.AddDays(1)));
        menu.Items.Add(new ToolStripSeparator());
        menu.Entry("Corrigir seleção", "", () => _ = Manual(true), "Ctrl Shift G");
        menu.Entry("Corrigir texto copiado", "", () => _ = Manual(true, true));
        var ignore = menu.Entry("Ignorar neste aplicativo", "", () =>
        {
            if (lastPath != null) { prefs.Apps[lastPath] = !prefs.Apps.GetValueOrDefault(lastPath, true); InvalidateWork(); Save(); settings?.RefreshStatus(); }
        });
        menu.Entry("Abrir painel", "", OpenSettings);
        sessionItem.Click += (_, _) => SessionChoice(); menu.Items.Add(sessionItem);
        menu.Items.Add(new ToolStripSeparator()); menu.Entry("Sair do AGC", "", ExitThread);
        menu.Opening += (_, _) =>
        {
            prefs.Today(); menu.SetStatus(paused, prefs.Corrections);
            ignore.Enabled = lastPath != null;
            ignore.Text = lastPath == null ? "Ignorar neste aplicativo" : (prefs.Apps.GetValueOrDefault(lastPath, true) ? "Ignorar em " : "Ativar em ") + Path.GetFileNameWithoutExtension(lastPath);
            sessionItem.Visible = prompted != 0 && Native.IsWindow(prompted) && Fullscreen.IsFull(prompted);
            sessionItem.Text = "Correção nesta tela cheia…";
        };
        long doubleClickTime = -SystemInformation.DoubleClickTime;
        icon.MouseClick += (_, e) => { if (e.Button == MouseButtons.Left && Environment.TickCount64 - doubleClickTime > SystemInformation.DoubleClickTime) { trayClick.Stop(); trayClick.Start(); } };
        icon.MouseDoubleClick += (_, e) => { if (e.Button == MouseButtons.Left) { doubleClickTime = Environment.TickCount64; trayClick.Stop(); menu.Close(); OpenSettings(); } };
        trayClick.Interval = SystemInformation.DoubleClickTime + 20;
        trayClick.Tick += (_, _) =>
        {
            trayClick.Stop(); var pt = Cursor.Position; var area = Screen.FromPoint(pt).WorkingArea;
            menu.Show(new Point(Math.Clamp(pt.X - menu.PreferredSize.Width / 2, area.Left, Math.Max(area.Left, area.Right - menu.PreferredSize.Width)), Math.Max(area.Top, pt.Y - menu.PreferredSize.Height - 8)));
        };
        signals.PausePressed += () => SetPause(!paused);
        resume.Tick += (_, _) => { resume.Stop(); if (prefs.PausedUntil <= DateTime.Now) SetPause(false); else ScheduleResume(); }; ScheduleResume();
        engine.WordChecked += ms => { try { if (!closed) dispatcher.BeginInvoke((Action)(() => { if (closed) return; prefs.Today(); prefs.CheckedWords++; prefs.ElapsedMs += ms; settings?.RefreshStatus(); saveTimer.Start(); })); } catch (InvalidOperationException) when (closed) { } };
        icon.BalloonTipClicked += (_, _) => { if (prompted != 0) SessionChoice(); };
        signals.InputChanged += () =>
        {
            if (popup.Target?.WordResult == null) popup.Dismiss();
            selectionTimer.Stop();
            Interlocked.Increment(ref generation);
            if (automaticInFlight) pendingBoundary = true;
            if (pendingBoundary) { if (prefs.Timing == "300") debounce.Stop(); debounce.Start(); }
        };
        signals.CursorMoved += InvalidateWork;
        signals.SelectionGesture += () =>
        {
            if (popup.Visible && popup.Bounds.Contains(Cursor.Position) && Native.GetAsyncKeyState(0x10) >= 0) return;
            popup.Dismiss();
            Interlocked.Increment(ref generation);
            selectionTimer.Stop(); selectionTimer.Start();
        };
        selectionTimer.Tick += async (_, _) =>
        {
            selectionTimer.Stop();
            await ProbeSelection();
        };
        popup.Chosen += async k => await Preview(k);
        popup.CopyRequested += async () => await CopyPreview();
        popup.ApplyRequested += async () => await ApplyPreview();
        popup.BindAction = signals.CaptureApply; popup.ReviewKeys = signals.CaptureReview; signals.CopyPressed += async () => await CopyPreview();
        signals.ApplyPressed += async () => await ApplyPreview();
        signals.Boundary += () => { if (!paused && prefs.Timing != "ponto") QueueBoundary(); };
        signals.PunctuationBoundary += () => { if (!paused && prefs.Timing == "ponto") QueueBoundary(); };
        signals.WindowEvent += Changed;
        signals.HotkeyPressed += async () => await Manual(false);
        debounce.Tick += async (_, _) => { debounce.Stop(); if (pendingBoundary) { pendingBoundary = false; await Automatic(); } };
        saveTimer.Tick += (_, _) => { saveTimer.Stop(); Save(); };
        Track(Native.GetForegroundWindow());
        if (recovered) Notify("Preferências recuperadas", "Os padrões foram restaurados. Verifique a pasta de configurações.");
        if (!signals.ShortcutReady) Notify("Atalho indisponível", "Ctrl+Shift+G já está em uso. Use Melhorar texto na bandeja.");
    }
    void QueueBoundary()
    {
        popup.Dismiss(); boundaryWindow = Native.GetForegroundWindow(); boundaryFocus = Native.Focus(boundaryWindow);
        pendingBoundary = true; debounce.Interval = prefs.Timing == "300" ? 300 : 20; debounce.Start();
    }
    static Icon LoadTrayIcon()
    {
        object? light = Microsoft.Win32.Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "SystemUsesLightTheme", 0);
        return new Icon(Path.Combine(AppContext.BaseDirectory, light is int v && v != 0 ? "tray-dark.ico" : "tray.ico"));
    }
    void SetPause(bool value, DateTime? until = null)
    {
        paused = value; prefs.AutoEnabled = !value; prefs.PausedUntil = value ? until : null; ScheduleResume(); InvalidateWork(); Save(); settings?.RefreshStatus();
    }
    void ScheduleResume()
    {
        resume.Stop(); if (prefs.PausedUntil is { } end) { resume.Interval = (int)Math.Clamp((end - DateTime.Now).TotalMilliseconds, 1, int.MaxValue); resume.Start(); }
    }
    void Applied()
    {
        prefs.Today(); prefs.Corrections++; if (prefs.Sound) System.Media.SystemSounds.Asterisk.Play(); settings?.RefreshStatus(); saveTimer.Start();
    }
    void InvalidateWork() { Interlocked.Increment(ref generation); pendingBoundary = false; debounce.Stop(); selectionTimer.Stop(); popup.Dismiss(); }
    void Notify(string title, string message) => icon.ShowBalloonTip(4500, title, message, ToolTipIcon.Info);
    void Changed(nint h, uint evt)
    {
        if (closed) return;
        if (evt == 0x8001) { sessions.End(h); if (h == prompted) prompted = 0; return; }
        if (evt == 3) { InvalidateWork(); Track(h); }
        if (h != Native.GetForegroundWindow() && !sessions.Tracks(h)) return;
        UpdateSession(h);
    }
    void Track(nint h)
    {
        if (h == 0 || Native.Class(h) is "Shell_TrayWnd" or "Shell_SecondaryTrayWnd" or "NotifyIconOverflowWindow" or "TopLevelWindowForOverflowXamlIsland" or "#32768" or "Progman" or "WorkerW") return;
        Native.GetWindowThreadProcessId(h, out var pid);
        if (pid == Environment.ProcessId) return;
        lastApp = h;
        if (pid != lastPid || lastPath == null) { lastPath = Preferences.ProcessPath(pid); lastPid = pid; }
        if (lastPath != null && prefs.Apps.TryAdd(lastPath, true)) saveTimer.Start();
        UpdateSession(h);
    }
    void UpdateSession(nint h)
    {
        Native.GetWindowThreadProcessId(h, out var pid);
        if (!Fullscreen.IsFull(h)) { sessions.End(h); if (prompted == h) prompted = 0; return; }
        if (h != Native.GetForegroundWindow() || h != lastApp || lastPath == null || !prefs.Apps.GetValueOrDefault(lastPath, true)) return;
        prompted = h; promptedPid = pid;
        if (sessions.First(h, pid)) Notify("Ativar correção nesta tela cheia?", Path.GetFileNameWithoutExtension(lastPath) + " está pausado. Clique para escolher ou use o menu da bandeja.");
    }
    void SessionChoice()
    {
        var h = prompted; var pid = promptedPid;
        if (h == 0 || !Native.IsWindow(h) || !Fullscreen.IsFull(h)) return;
        using var dialog = new SessionPrompt(Path.GetFileNameWithoutExtension(Preferences.ProcessPath(pid)) ?? "O aplicativo");
        var answer = dialog.ShowDialog();
        if (Fullscreen.IsFull(h)) sessions.Answer(h, pid, answer == DialogResult.Yes);
        InvalidateWork();
    }
    bool Eligible(nint h, bool automatic)
    {
        Track(h);
        if (h == 0 || h != lastApp || lastPath == null || !prefs.Apps.GetValueOrDefault(lastPath, true)) return false;
        return !automatic || (!paused && (!Fullscreen.IsFull(h) || sessions.Allowed(h, lastPid)));
    }
    async Task Automatic()
    {
        if (closed) return;
        if (busy) { pendingBoundary = true; return; }
        var h = Native.GetForegroundWindow();
        if (h != boundaryWindow || Native.Focus(h) != boundaryFocus) return;
        if (!Eligible(h, true) || Native.ModifiersDown()) return;
        if (Editor.IsPlain(boundaryFocus) && !string.Equals(Path.GetFileNameWithoutExtension(lastPath), "Codex", StringComparison.OrdinalIgnoreCase))
        { if ((await Edit(h, false)).State == Outcome.Replaced) Applied(); }
        else
        {
            busy = true; automaticInFlight = true;
            try
            {
                int stamp = Volatile.Read(ref generation);
                bool allowed = sessions.Allowed(h, lastPid);
                bool safe() => !closed && Volatile.Read(ref generation) == stamp && Native.GetForegroundWindow() == h &&
                    Native.Focus(h) == boundaryFocus && (allowed || !Fullscreen.IsFull(h));
                var punctuation = await EditRange(h, null, null, true, safe);
                if (punctuation == RangeOutcome.Confirmed) Applied();
                if (prefs.Indicator && punctuation == RangeOutcome.Cancelled && safe() && await Probe(h, true) is { } word && Eligible(h, true) && Native.GetForegroundWindow() == h) { popup.Present(word); indicator.Stop(); indicator.Start(); }
            }
            finally { busy = false; automaticInFlight = false; if (!closed && pendingBoundary) debounce.Start(); }
        }
    }
    async Task<ReviewTarget?> Probe(nint h, bool word)
    {
        int stamp = Volatile.Read(ref generation);
        bool Current() => !closed && Volatile.Read(ref generation) == stamp && Native.GetForegroundWindow() == h;
        try
        {
            var target = await Task.Run(() => ReviewProbe.Read(h, engine, prefs.Rules, word, Current));
            return Current() ? target : null;
        }
        catch (Exception ex) when (ex is ElementNotAvailableException or InvalidOperationException or COMException or IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException or System.Text.RegularExpressions.RegexMatchTimeoutException) { return null; }
    }
    async Task ProbeSelection()
    {
        if (closed) return;
        if (probeBusy) { probePending = true; return; }
        var h = Native.GetForegroundWindow();
        if (!Eligible(h, true)) return;
        probeBusy = true;
        try
        {
            if (await Probe(h, false) is { } target && Native.GetForegroundWindow() == h && Eligible(h, true)) { popup.Present(target); await Preview(ReviewKind.All); }
        }
        finally { probeBusy = false; if (probePending && !closed) { probePending = false; selectionTimer.Start(); } }
    }
    async Task Preview(ReviewKind kind)
    {
        var target = popup.Target;
        if (target == null || !Eligible(target.Window, true)) { popup.Dismiss(); return; }
        var rules = ReviewOptions.Filter(prefs.Rules, kind);
        try
        {
            var result = await Task.Run(() => engine.Correct(target.Original, rules, true));
            if (popup.Target == target && await Probe(target.Window, false) == target && popup.Target == target && popup.ActiveKind == kind)
                popup.ShowResult(result, target.Writable);
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or System.Text.RegularExpressions.RegexMatchTimeoutException) { popup.Dismiss(); Notify("Revisão indisponível", "Tente novamente pela bandeja."); }
    }
    async Task<RangeOutcome> EditRange(nint h, ReviewTarget? expected, string? result, bool automatic, Func<bool> valid)
    {
        try
        {
            return await Task.Run(() =>
            {
                var outcome = RangeOutcome.Cancelled;
                ReviewProbe.Read(h, engine, prefs.Rules, automatic || expected?.WordResult != null, valid, automatic, (target, live) =>
                {
                    if (!automatic && (expected != target || result == null)) return;
                    outcome = RangeEdit.Apply(target, live, automatic ? target.WordResult! : result!, automatic, valid);
                });
                return outcome;
            });
        }
        catch (Exception ex) when (ex is ElementNotAvailableException or InvalidOperationException or COMException or IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException or System.Text.RegularExpressions.RegexMatchTimeoutException) { return RangeOutcome.Cancelled; }
    }
    async Task ApplyPreview()
    {
        var target = popup.Target; var result = popup.Result;
        if (busy || target == null || !target.Writable || Native.GetForegroundWindow() != target.Window || !Eligible(target.Window, true)) return;
        int stamp = Volatile.Read(ref generation);
        bool allowed = sessions.Allowed(target.Window, lastPid);
        bool Valid() => !closed && Volatile.Read(ref generation) == stamp && Native.GetForegroundWindow() == target.Window && Native.Focus(target.Window) == target.Focus && (allowed || !Fullscreen.IsFull(target.Window));
        busy = true;
        try
        {
            var kind = popup.ActiveKind;
            uint sequence = Native.GetClipboardSequenceNumber();
            result ??= await Task.Run(() => engine.Correct(target.Original, ReviewOptions.Filter(prefs.Rules, kind), true));
            if (!Valid() || popup.Target != target || popup.ActiveKind != kind || await Probe(target.Window, target.WordResult != null) != target || !Valid()) return;
            if (result == target.Original) { popup.ShowResult(result); return; }
            if (Editor.IsPlain(target.Focus) && target.WordResult == null)
            {
                var edit = await Task.Run(() => Editor.Plain(target.Focus, true, engine, prefs.Rules, Valid, target, result));
                if (edit.State == Outcome.Replaced) Applied();
                if (edit.State == Outcome.CopyResult) Notify("Campo com limite", "A revisão não cabe neste campo.");
                else popup.Dismiss();
                return;
            }
            if (sequence != Native.GetClipboardSequenceNumber() || Native.ModifiersDown()) return;
            Clipboard.SetText(result); // Uma colagem substitui a seleção. Nunca excluir o texto antes.
            uint owned = Native.GetClipboardSequenceNumber();
            var outcome = await EditRange(target.Window, target, result, false, () => Valid() && owned == Native.GetClipboardSequenceNumber());
            popup.Dismiss();
            if (outcome == RangeOutcome.Confirmed) Applied();
            if (outcome != RangeOutcome.Confirmed) Notify("Revisão na área de transferência", outcome == RangeOutcome.Sent ? "A colagem foi enviada, mas o editor não confirmou o resultado." : "A seleção mudou ou o editor não aceitou a colagem.");
        }
        catch (Exception ex) when (ex is ExternalException or IOException or InvalidOperationException or System.Text.RegularExpressions.RegexMatchTimeoutException) { Notify("Não foi possível aplicar", "A revisão foi interrompida. Confira a seleção e tente novamente."); }
        finally { busy = false; if (!closed && pendingBoundary) debounce.Start(); }
    }
    async Task CopyPreview()
    {
        var target = popup.Target; var result = popup.Result;
        if (target == null || result == null) return;
        uint seq = Native.GetClipboardSequenceNumber();
        if (await Probe(target.Window, target.WordResult != null) != target || popup.Target != target || seq != Native.GetClipboardSequenceNumber()) return;
        try { Clipboard.SetText(result); popup.Dismiss(); Notify("Resultado copiado", "Cole no trecho desejado com Ctrl+V."); }
        catch (ExternalException) { Notify("Área de transferência ocupada", "Tente copiar novamente."); }
    }
    async Task<EditResult> Edit(nint h, bool manual)
    {
        if (busy || closed) return new(Outcome.Cancelled);
        busy = true; automaticInFlight = !manual; int ticket = Volatile.Read(ref generation);
        bool permittedFull = sessions.Allowed(h, lastPid);
        var rules = prefs.Rules;
        string? executable = lastPath;
        bool Valid() => Volatile.Read(ref generation) == ticket && (manual || permittedFull || !Fullscreen.IsFull(h));
        try { return await Task.Run(() => Editor.Run(h, manual, engine, rules, Valid, executable)); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or ElementNotAvailableException or InvalidOperationException or COMException or System.Text.RegularExpressions.RegexMatchTimeoutException or NotSupportedException or ArgumentException)
        {
            if (manual || ex is IOException) Notify("Correção indisponível", ex is IOException ? "Verifique o dicionário na pasta pt_BR do aplicativo." : "O editor não disponibilizou o texto para correção.");
            return new(Outcome.Cancelled);
        }
        finally { busy = false; automaticInFlight = false; if (!closed && pendingBoundary) debounce.Start(); }
    }
    async Task Manual(bool menu, bool clipboardOnly = false)
    {
        if (closed || busy) return;
        var h = clipboardOnly ? 0 : menu ? lastApp : Native.GetForegroundWindow();
        if (h != 0 && Native.Class(h) is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd") h = 0;
        uint sequence = Native.GetClipboardSequenceNumber();
        if (h != 0 && !Eligible(h, false)) { Notify("Aplicativo desativado", "Verifique a lista de aplicativos nas configurações."); return; }
        if (menu && h != 0 && h != Native.GetForegroundWindow())
        {
            var fg = Native.GetForegroundWindow();
            Native.GetWindowThreadProcessId(fg, out var pid);
            if (pid == Environment.ProcessId || Native.Class(fg) is "Shell_TrayWnd" or "Shell_SecondaryTrayWnd" or "#32768") SetForegroundWindow(h);
        }
        for (int n = 0; n < 20 && Native.ModifiersDown(); n++) await Task.Delay(35);
        if (closed || Native.ModifiersDown()) return;
        if (h != 0 && h != Native.GetForegroundWindow())
        {
            if (!menu) return;
            h = 0; // Um menu sem editor recuperável ainda pode melhorar o texto copiado.
        }
        if (h != 0 && await Probe(h, false) is { } selection && selection.Writable)
        {
            selectionTimer.Stop();
            popup.Present(selection);
            await Preview(ReviewKind.All);
            return;
        }
        var result = h == 0 ? new EditResult(Outcome.NoSelection) : await Edit(h, true);
        if (closed) return;
        try
        {
            if (result.State == Outcome.CopyResult)
            {
                if (sequence != Native.GetClipboardSequenceNumber()) return;
                Clipboard.SetText(result.Text!); Notify("Texto melhorado", "Resultado copiado para a área de transferência."); return;
            }
            if (result.State == Outcome.NoSelection)
            {
                if (sequence != Native.GetClipboardSequenceNumber() || !Clipboard.ContainsText()) { Notify("Nenhum texto", "Selecione um trecho ou copie texto e tente novamente."); return; }
                string original = Clipboard.GetText();
                if (original.Length > 100_000) { Notify("Trecho muito longo", "Copie até 100 mil caracteres por revisão."); return; }
                var after = await Task.Run(() => engine.Correct(original, prefs.Rules, true));
                if (closed || sequence != Native.GetClipboardSequenceNumber()) return;
                if (after != original) Clipboard.SetText(after);
                Notify("Área de transferência", after == original ? "O texto já está de acordo com as regras ativas." : "Texto corrigido e copiado."); return;
            }
            if (result.State == Outcome.Unchanged) Notify("Texto selecionado", "Nenhuma alteração pelas regras ativas.");
            if (result.State == Outcome.Cancelled) Notify("Seleção indisponível", "Copie o texto e escolha Melhorar texto copiado na bandeja.");
        }
        catch (Exception ex) when (ex is ExternalException or IOException or InvalidOperationException or System.Text.RegularExpressions.RegexMatchTimeoutException)
        {
            Notify("Não foi possível melhorar o texto", "Verifique o dicionário e tente novamente com a área de transferência disponível.");
        }
    }
    internal void RequestSettings()
    {
        try { if (!closed) dispatcher.BeginInvoke((Action)OpenSettings); }
        catch (InvalidOperationException) when (closed || dispatcher.IsDisposed) { }
    }
    void OpenSettings()
    {
        if (closed) return;
        if (settings is { IsDisposed: false })
        {
            if (settings.WindowState == System.Windows.WindowState.Minimized) settings.WindowState = System.Windows.WindowState.Normal;
            settings.Show(); settings.Activate(); SetForegroundWindow(new System.Windows.Interop.WindowInteropHelper(settings).Handle); return;
        }
        settings = new SettingsWindow(prefs, () => { InvalidateWork(); Save(); }, () => paused, b => SetPause(b), engine);
        System.Windows.Forms.Integration.ElementHost.EnableModelessKeyboardInterop(settings);
        settings.Show();
    }
    void Save() { try { prefs.Save(); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Notify("Preferências não salvas", "Verifique o acesso à pasta de dados locais."); } }
    protected override void Dispose(bool disposing)
    {
        if (disposing && !closed) { closed = true; InvalidateWork(); saveTimer.Stop(); Save(); signals.Dispose(); trayClick.Dispose(); resume.Dispose(); indicator.Dispose(); debounce.Dispose(); selectionTimer.Dispose(); popup.Dispose(); saveTimer.Dispose(); settings?.Dispose(); icon.Visible = false; icon.ContextMenuStrip?.Dispose(); icon.Icon?.Dispose(); icon.Dispose(); dispatcher.Dispose(); }
        base.Dispose(disposing);
    }
}
