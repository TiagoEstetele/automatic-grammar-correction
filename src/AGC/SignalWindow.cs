using System.Runtime.InteropServices;
namespace Agc;

// Raw Input notifica limites de palavra, sem bloquear a digitação e sem converter
// teclas em texto. Os eventos WinEvent só acompanham foco/geometria das janelas.
internal sealed class SignalWindow : NativeWindow, IDisposable
{
    readonly Native.EventProc callback;
    readonly List<nint> hooks = new();
    internal event Action? Boundary, InputChanged, CursorMoved, HotkeyPressed;
    internal event Action<nint, uint>? WindowEvent;
    internal bool ShortcutReady { get; }
    internal SignalWindow()
    {
        CreateHandle(new CreateParams { Caption = "AGC.Events", Parent = (nint)(-3) });
        callback = (_, evt, h, obj, child, _, _) =>
        {
            if (h != 0 && obj == 0 && child == 0) WindowEvent?.Invoke(h, evt);
        };
        try
        {
            foreach (uint evt in new uint[] { 3, 0x8001, 0x800B, 0x8002, 0x8003 })
            {
                var hook = Native.SetWinEventHook(evt, evt, 0, callback, 0, 0, 2);
                if (hook == 0) throw new InvalidOperationException("O Windows recusou o monitor de janelas.");
                hooks.Add(hook);
            }
            if (!Native.RegisterRawInputDevices([new Native.Device { Page = 1, Usage = 6, Flags = 0x100, Target = Handle }], 1, (uint)Marshal.SizeOf<Native.Device>()))
                throw new InvalidOperationException("O Windows recusou os eventos de teclado.");
            ShortcutReady = Native.RegisterHotKey(Handle, 1, 0x4000 | 2 | 4, 0x47); // Ctrl+Shift+G, sem repetição
        }
        catch { Dispose(); throw; }
    }
    protected override void WndProc(ref Message m)
    {
        if (m.Msg == Native.Hotkey) HotkeyPressed?.Invoke();
        if (m.Msg == Native.Input)
        {
            uint size = (uint)Marshal.SizeOf<Native.RawKey>();
            if (Native.GetRawInputData(m.LParam, 0x10000003, out var key, ref size, (uint)Marshal.SizeOf<Native.Header>()) == size &&
                key.Header.Type == 1 && key.Header.Device != 0 && key.Message is 0x100 or 0x104)
            {
                if (key.Vk is >= 0x21 and <= 0x28 or 0x09 or 0x1B or 0x11 or 0x12) CursorMoved?.Invoke();
                else InputChanged?.Invoke();
                if (key.Vk is 0x20 or 0x0D or 0xBA or 0xBC or 0xBE or 0xBF or 0x31 || key.Vk == 0x6E) Boundary?.Invoke();
            }
        }
        base.WndProc(ref m);
    }
    public void Dispose()
    {
        foreach (var h in hooks) Native.UnhookWinEvent(h);
        if (Handle != 0)
        {
            Native.UnregisterHotKey(Handle, 1);
            Native.RegisterRawInputDevices([new Native.Device { Page = 1, Usage = 6, Flags = 1 }], 1, (uint)Marshal.SizeOf<Native.Device>());
            DestroyHandle();
        }
        GC.KeepAlive(callback);
    }
}
