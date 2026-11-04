using System.Runtime.InteropServices;
namespace Agc;

public static class Fullscreen
{
    // Compara a área cliente (exclui moldura/barra de título) ao monitor físico,
    // nunca à área de trabalho. Coordenadas negativas são normais em múltiplos monitores.
    public static bool Covers(Rectangle client, Rectangle monitor, bool minimized, bool decoratedMaximized)
    {
        if (minimized || decoratedMaximized || client.Width <= 0 || client.Height <= 0 || monitor.Width <= 0 || monitor.Height <= 0) return false;
        const int tolerance = 2; // arredondamento por DPI
        return Math.Abs((long)client.Left - monitor.Left) <= tolerance && Math.Abs((long)client.Top - monitor.Top) <= tolerance &&
            Math.Abs((long)client.Right - monitor.Right) <= tolerance && Math.Abs((long)client.Bottom - monitor.Bottom) <= tolerance;
    }
    internal static bool IsFull(nint h)
    {
        if (h == 0 || Native.Class(h) is "Progman" or "WorkerW" or "Shell_TrayWnd" || !Native.GetClientRect(h, out var client)) return false;
        var p = new Point(0, 0);
        var info = new Native.MonitorInfo { Size = Marshal.SizeOf<Native.MonitorInfo>() };
        if (!Native.ClientToScreen(h, ref p) || !Native.GetMonitorInfo(Native.MonitorFromWindow(h, 2), ref info)) return false;
        var r = client.Bounds; r.Offset(p);
        return Covers(r, info.Monitor.Bounds, Native.IsIconic(h), Native.IsZoomed(h) && (Native.Style(h) & 0xc00000) == 0xc00000);
    }
}
// Uma decisão por janela/processo; Alt+Tab não encerra a sessão. Sair do modo
// tela cheia ou destruir a janela encerra inclusive uma resposta "não".
public sealed class FullscreenSessions
{
    readonly Dictionary<(nint, uint), bool> entries = new();
    public bool First(nint h, uint pid) => entries.TryAdd((h, pid), false);
    public void Answer(nint h, uint pid, bool allow) { if (entries.ContainsKey((h, pid))) entries[(h, pid)] = allow; }
    public bool Allowed(nint h, uint pid) => entries.TryGetValue((h, pid), out var allow) && allow;
    public void End(nint h) { foreach (var key in entries.Keys.Where(k => k.Item1 == h).ToArray()) entries.Remove(key); }
    public bool Tracks(nint h) => entries.Keys.Any(k => k.Item1 == h);
}
