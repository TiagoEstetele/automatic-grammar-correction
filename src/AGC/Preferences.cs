using System.Diagnostics;
using System.IO;
using System.Text.Json;
namespace Agc;

internal sealed class Preferences
{
    internal static readonly string Folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AutomaticGrammarCorrection");
    static string FilePath => Path.Combine(Folder, "settings.json");
    public RuleSettings Rules { get; set; } = new();
    public Dictionary<string, bool> Apps { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    internal static Preferences Load(out bool recovered)
    {
        recovered = false;
        try
        {
            if (!File.Exists(FilePath)) return new();
            var data = JsonSerializer.Deserialize<Preferences>(File.ReadAllText(FilePath)) ?? throw new JsonException();
            data.Rules ??= new();
            data.Apps = new(data.Apps ?? new(), StringComparer.OrdinalIgnoreCase);
            return data;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            recovered = true;
            try { File.Copy(FilePath, FilePath + ".bak", true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            return new();
        }
    }
    internal void Save()
    {
        Directory.CreateDirectory(Folder);
        File.WriteAllText(FilePath + ".tmp", JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(FilePath + ".tmp", FilePath, true);
    }
    internal static string? ProcessPath(uint pid)
    {
        try { using var p = Process.GetProcessById((int)pid); return p.MainModule?.FileName; }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException) { return null; }
    }
}
