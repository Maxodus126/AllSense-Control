using System.IO;
using System.Text.Json;

namespace AllSense;

public sealed class UserSettings
{
    public string PowerMode { get; set; } = "balanced";
    public int Brightness { get; set; } = 75;
    public int Volume { get; set; } = 65;
    public bool Mute { get; set; }
    public string UpdateUrl { get; set; } = "";
    public string HotkeyToggle { get; set; } = "Ctrl+Alt+A";
    public string HotkeyCycle { get; set; } = "Ctrl+Alt+M";

    private static string Path => System.IO.Path.Combine(App.LocalDir, "config.json");

    public static UserSettings Load()
    {
        try
        {
            if (File.Exists(Path))
                return JsonSerializer.Deserialize<UserSettings>(File.ReadAllText(Path)) ?? new UserSettings();
        }
        catch { }
        return new UserSettings();
    }

    public void Save()
    {
        try { File.WriteAllText(Path, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true })); }
        catch { }
    }
}
