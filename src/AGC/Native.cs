using System.Runtime.InteropServices;
using System.Text;
namespace Agc;

internal static class Native
{
    internal const uint Input = 0x00FF, Hotkey = 0x0312;
    [StructLayout(LayoutKind.Sequential)] internal struct Device { public ushort Page, Usage; public uint Flags; public nint Target; }
    [StructLayout(LayoutKind.Sequential)] internal struct Header { public uint Type, Size; public nint Device, Param; }
    [StructLayout(LayoutKind.Sequential)] internal struct RawKey { public Header Header; public ushort Scan, Flags, Reserved, Vk; public uint Message, Extra; }
    [StructLayout(LayoutKind.Sequential)] internal struct Rect { public int Left, Top, Right, Bottom; public Rectangle Bounds => Rectangle.FromLTRB(Left, Top, Right, Bottom); }
    [StructLayout(LayoutKind.Sequential)] internal struct GuiInfo { public int Size, Flags; public nint Active, Focus, Capture, Menu, Move, Caret; public Rect CaretBounds; }
    [StructLayout(LayoutKind.Sequential)] internal struct MonitorInfo { public int Size; public Rect Monitor, Work; public uint Flags; }
    [StructLayout(LayoutKind.Sequential)] struct Keyboard { public ushort Vk, Scan; public uint Flags, Time; public nuint Extra; }
    [StructLayout(LayoutKind.Sequential)] struct Mouse { public int X, Y; public uint Data, Flags, Time; public nuint Extra; }
    [StructLayout(LayoutKind.Explicit)] struct Payload { [FieldOffset(0)] public Keyboard Key; [FieldOffset(0)] public Mouse Mouse; }
    [StructLayout(LayoutKind.Sequential)] struct KeyInput { public uint Type; public Payload Data; }
    internal delegate void EventProc(nint hook, uint evt, nint hwnd, int obj, int child, uint thread, uint time);
    [DllImport("user32")] internal static extern nint SetWinEventHook(uint first, uint last, nint module, EventProc callback, uint process, uint thread, uint flags);
    [DllImport("user32")] internal static extern bool UnhookWinEvent(nint hook);
    [DllImport("user32")] internal static extern nint GetForegroundWindow();
    [DllImport("user32")] internal static extern uint GetWindowThreadProcessId(nint hwnd, out uint pid);
    [DllImport("user32")] static extern bool GetGUIThreadInfo(uint thread, ref GuiInfo info);
    [DllImport("user32")] internal static extern bool IsWindow(nint hwnd);
    [DllImport("user32")] internal static extern bool IsIconic(nint hwnd);
    [DllImport("user32")] internal static extern bool IsZoomed(nint hwnd);
    [DllImport("user32")] internal static extern nint GetAncestor(nint hwnd, uint kind);
    [DllImport("user32")] internal static extern bool GetClientRect(nint hwnd, out Rect rect);
    [DllImport("user32")] internal static extern bool ClientToScreen(nint hwnd, ref Point point);
    [DllImport("user32")] internal static extern nint MonitorFromWindow(nint hwnd, uint flags);
    [DllImport("user32", CharSet = CharSet.Unicode)] internal static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo info);
    [DllImport("user32", CharSet = CharSet.Unicode)] static extern int GetClassName(nint hwnd, StringBuilder buf, int count);
    [DllImport("user32", EntryPoint = "GetWindowLongW")] internal static extern int Style(nint hwnd, int index = -16);
    [DllImport("user32")] internal static extern bool RegisterRawInputDevices(Device[] devices, uint count, uint size);
    [DllImport("user32")] internal static extern uint GetRawInputData(nint handle, uint cmd, out RawKey key, ref uint size, uint headerSize);
    [DllImport("user32")] internal static extern bool RegisterHotKey(nint hwnd, int id, uint mods, uint key);
    [DllImport("user32")] internal static extern bool UnregisterHotKey(nint hwnd, int id);
    [DllImport("user32")] internal static extern uint GetClipboardSequenceNumber();
    [DllImport("user32")] internal static extern short GetAsyncKeyState(int key);
    [DllImport("user32", EntryPoint = "SendMessageTimeoutW", CharSet = CharSet.Unicode)] internal static extern nint Message(nint h, uint m, nint w, nint l, uint flags, uint ms, out nint result);
    [DllImport("user32", EntryPoint = "SendMessageTimeoutW", CharSet = CharSet.Unicode)] internal static extern nint Read(nint h, uint m, nint w, StringBuilder buf, uint flags, uint ms, out nint result);
    [DllImport("user32", EntryPoint = "SendMessageTimeoutW", CharSet = CharSet.Unicode)] internal static extern nint Write(nint h, uint m, nint w, string value, uint flags, uint ms, out nint result);
    [DllImport("user32")] static extern uint SendInput(uint count, KeyInput[] inputs, int size);
    internal static string Class(nint h) { var b = new StringBuilder(256); GetClassName(h, b, b.Capacity); return b.ToString(); }
    internal static nint Focus(nint h)
    {
        uint t = GetWindowThreadProcessId(h, out _);
        var info = new GuiInfo { Size = Marshal.SizeOf<GuiInfo>() };
        return t != 0 && GetGUIThreadInfo(t, ref info) ? info.Focus : 0;
    }
    internal static bool ModifiersDown() => new[] { 16, 17, 18, 91, 92 }.Any(k => GetAsyncKeyState(k) < 0);
    internal static bool TypeText(string s, bool stepOverBreak = false)
    {
        if (s.Length == 0 || s.Contains('\r') || s.Contains('\n') || ModifiersDown()) return false;
        var input = new KeyInput[s.Length * 2 + (stepOverBreak ? 2 : 0)];
        for (int i = 0; i < s.Length * 2; i++) input[i] = new KeyInput { Type = 1, Data = new Payload { Key = new Keyboard { Scan = s[i / 2], Flags = (uint)(4 + (i % 2) * 2) } } };
        // A quebra já existe no editor: volte ao início da linha seguinte sem
        // simular Enter (que poderia enviar uma mensagem em um aplicativo de chat).
        if (stepOverBreak) for (int i = 0; i < 2; i++) input[s.Length * 2 + i] = new KeyInput { Type = 1, Data = new Payload { Key = new Keyboard { Vk = 0x27, Flags = (uint)(1 + 2 * i) } } };
        return SendInput((uint)input.Length, input, Marshal.SizeOf<KeyInput>()) == input.Length;
    }
}
