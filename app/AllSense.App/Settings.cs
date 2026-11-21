using System.IO;
using System.Text.Json;

namespace AllSense;

public sealed class UserSettings
{
    /// <summary>Versi skema config; memungkinkan migrasi di masa depan.</summary>
    public int ConfigVersion { get; set; } = 1;

    public string PowerMode { get; set; } = "balanced";
    public int Brightness { get; set; } = 75;
    public int Volume { get; set; } = 65;
    public bool Mute { get; set; }
    public bool TrayMode { get; set; } = true;
    public bool AutoPowerOnBattery { get; set; } = true;
    public bool RunAtStartup { get; set; }
    public int LowBatteryThreshold { get; set; } = 20;
    public string UpdateUrl { get; set; } = "";
    public string HotkeyToggle { get; set; } = "Ctrl+Alt+A";
    public string HotkeyCycle { get; set; } = "Ctrl+Alt+M";

    private static string Path => System.IO.Path.Combine(App.LocalDir, "config.json");

    public static UserSettings Load()
    {
        UserSettings s;
        try
        {
            if (!File.Exists(Path)) return new UserSettings();
            s = JsonSerializer.Deserialize<UserSettings>(File.ReadAllText(Path)) ?? new UserSettings();
        }
        catch { return new UserSettings(); }

        // Sanitasi: config rusak/aneh tidak boleh membuat app crash atau nilai mustahil.
        s.ConfigVersion = Math.Max(1, s.ConfigVersion);
        s.Brightness = Math.Clamp(s.Brightness, 0, 100);
        s.Volume = Math.Clamp(s.Volume, 0, 100);
        s.LowBatteryThreshold = Math.Clamp(s.LowBatteryThreshold, 5, 100);
        if (string.IsNullOrWhiteSpace(s.PowerMode)) s.PowerMode = "balanced";
        if (string.IsNullOrWhiteSpace(s.HotkeyToggle)) s.HotkeyToggle = "Ctrl+Alt+A";
        if (string.IsNullOrWhiteSpace(s.HotkeyCycle)) s.HotkeyCycle = "Ctrl+Alt+M";
        return s;
    }

    public void Save()
    {
        try
        {
            var dir = System.IO.Path.GetDirectoryName(Path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            var tmp = Path + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
            // File.Move(overwrite) = atomic pada NTFS: config tak pernah setengah tertulis
            // walaupun proses mati di tengah write.
            File.Move(tmp, Path, overwrite: true);
        }
        catch { }
    }
}
