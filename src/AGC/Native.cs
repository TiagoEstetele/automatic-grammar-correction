using System.Runtime.InteropServices;
using System.Text;
namespace Agc;

internal static class Native
{
    internal const uint Input = 0x00FF, Hotkey = 0x0312;
    [StructLayout(LayoutKind.Sequential)] internal struct Device { public ushort Page, Usage; public uint Flags; public nint Target; }
    [StructLayout(LayoutKind.Sequential)] internal struct Header { public uint Type, Size; public nint Device, Param; }
    // RAWKEYBOARD e RAWMOUSE compartilham o início deste payload.
    [StructLayout(LayoutKind.Explicit, Size = 24)]
    internal struct RawData
    {
        [FieldOffset(4)] public ushort MouseButtons;
        [FieldOffset(6)] public ushort Vk;
        [FieldOffset(8)] public uint Message;
    }
    [StructLayout(LayoutKind.Sequential)] internal struct RawPacket { public Header Header; public RawData Data; }
    [StructLayout(LayoutKind.Sequential)] internal struct Rect { public int Left, Top, Right, Bottom; public Rectangle Bounds => Rectangle.FromLTRB(Left, Top, Right, Bottom); }
    [StructLayout(LayoutKind.Sequential)] internal struct GuiInfo { public int Size, Flags; public nint Active, Focus, Capture, Menu, Move, Caret; public Rect CaretBounds; }
    [StructLayout(LayoutKind.Sequential)] internal struct MonitorInfo { public int Size; public Rect Monitor, Work; public uint Flags; }
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
    [DllImport("user32")] internal static extern uint GetRawInputData(nint handle, uint cmd, out RawPacket packet, ref uint size, uint headerSize);
    [DllImport("user32")] internal static extern bool RegisterHotKey(nint hwnd, int id, uint mods, uint key);
    [DllImport("user32")] internal static extern bool UnregisterHotKey(nint hwnd, int id);
    [DllImport("user32")] internal static extern uint GetClipboardSequenceNumber();
    [DllImport("user32")] internal static extern short GetAsyncKeyState(int key);
    [DllImport("user32", EntryPoint = "SendMessageTimeoutW", CharSet = CharSet.Unicode)] internal static extern nint Message(nint h, uint m, nint w, nint l, uint flags, uint ms, out nint result);
    [DllImport("user32", EntryPoint = "SendMessageTimeoutW", CharSet = CharSet.Unicode)] internal static extern nint Read(nint h, uint m, nint w, StringBuilder buf, uint flags, uint ms, out nint result);
    [DllImport("user32", EntryPoint = "SendMessageTimeoutW", CharSet = CharSet.Unicode)] internal static extern nint Write(nint h, uint m, nint w, string value, uint flags, uint ms, out nint result);
    internal static string Class(nint h) { var b = new StringBuilder(256); GetClassName(h, b, b.Capacity); return b.ToString(); }
    internal static nint Focus(nint h)
    {
        uint t = GetWindowThreadProcessId(h, out _);
        var info = new GuiInfo { Size = Marshal.SizeOf<GuiInfo>() };
        return t != 0 && GetGUIThreadInfo(t, ref info) ? info.Focus : 0;
    }
    internal static bool ModifiersDown() => new[] { 16, 17, 18, 91, 92 }.Any(k => GetAsyncKeyState(k) < 0);
    [DllImport("user32",CharSet=CharSet.Unicode)] static extern nint FindWindowEx(nint parent,nint after,string? cls,string title);
    [DllImport("user32")] static extern bool AllowSetForegroundWindow(uint processId);
    internal static void GrantForegroundToTray(){
        var h=FindWindowEx(-3,0,null,"AGC.Events");
        if(h!=0 && GetWindowThreadProcessId(h,out var pid)!=0 && pid!=0) AllowSetForegroundWindow(pid);
    }
    [StructLayout(LayoutKind.Sequential)] struct Key { public ushort Code, Scan; public uint Flags,Time; public nuint Extra; }
    [StructLayout(LayoutKind.Sequential)] struct Mouse { public int X,Y; public uint Data,Flags,Time; public nuint Extra; }
    [StructLayout(LayoutKind.Explicit)] struct Payload { [FieldOffset(0)] public Key Key; [FieldOffset(0)] public Mouse Mouse; }
    [StructLayout(LayoutKind.Sequential)] struct InputEvent { public uint Type; public Payload Payload; }
    [DllImport("user32")] static extern uint SendInput(uint count,InputEvent[] events,int size);
    static InputEvent Stroke(ushort code,ushort scan,uint flags)=>new(){Type=1,Payload=new(){Key=new(){Code=code,Scan=scan,Flags=flags}}};
    static bool Send(InputEvent[] events)=>!ModifiersDown() && SendInput((uint)events.Length,events,Marshal.SizeOf<InputEvent>())==events.Length;
    internal static bool PasteSelection()=>Send([Stroke(17,0,0),Stroke(86,0,0),Stroke(86,0,2),Stroke(17,0,2)]);
    internal static bool InsertSeparators(string text){
        if(text.Length is 0 or >8 || !Corrector.OnlySeparators(text)) return false;
        var events=new InputEvent[text.Length*2];
        for(int i=0;i<text.Length;i++){events[i*2]=Stroke(0,text[i],4);events[i*2+1]=Stroke(0,text[i],6);}
        return Send(events);
    }
}
