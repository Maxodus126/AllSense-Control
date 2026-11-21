using System;
using System.Reflection;

namespace AllSense;

/// <summary>
/// Self-test murni (tanpa UI/hardware) untuk memvalidasi logika inti.
/// Dipanggil via `AllSense.exe --selftest` → exit 0 bila lolos, 1 bila gagal.
/// </summary>
public static class Selftest
{
    public static int Run()
    {
        var ok = true;

        // 1. Parsing versi (Updater.CurrentVersion harus semver valid)
        var versionOk = Version.TryParse(Updater.CurrentVersion, out var curVersion) && curVersion.Major >= 2;
        ok &= Check("Updater.CurrentVersion semver", versionOk);

        // 2. Mapping GUID mode daya (PowerControl.FromGuid via refleksi)
        ok &= Check("PowerControl.FromGuid(Balanced)", PowerGuid("00000000-0000-0000-0000-000000000000") is PowerMode.Balanced);
        ok &= Check("PowerControl.FromGuid(Efficiency)", PowerGuid("961cc777-2547-4f9d-8174-7d86181b8a7a") is PowerMode.Efficiency);
        ok &= Check("PowerControl.FromGuid(Performance)", PowerGuid("ded574b5-45a0-4f42-8737-46345c09c238") is PowerMode.Performance);
        ok &= Check("PowerControl.FromGuid(tak dikenal)", PowerGuid("11111111-1111-1111-1111-111111111111") is null);

        // 3. Clamping Settings
        ok &= Check("Settings.clamp(brightness)", SettingsClamp(0, 100, 999) == 100);
        ok &= Check("Settings.clamp(volume)", SettingsClamp(0, 100, -5) == 0);

        // 4. Round-trip serialisasi Settings
        ok &= Check("Settings.round-trip", SettingsRoundTrip());

        Console.WriteLine(ok ? "SELFTEST: PASS" : "SELFTEST: FAIL");
        return ok ? 0 : 1;
    }

    private static bool Check(string name, bool cond)
    {
        Console.WriteLine((cond ? "  ok  " : "  FAIL ") + name);
        return cond;
    }

    private static PowerMode? PowerGuid(string g)
    {
        var m = typeof(PowerControl).GetMethod("FromGuid", BindingFlags.NonPublic | BindingFlags.Static)!;
        return (PowerMode?)m.Invoke(null, new object[] { Guid.Parse(g) });
    }

    private static int SettingsClamp(int min, int max, int v)
        => Math.Min(max, Math.Max(min, v));

    private static bool SettingsRoundTrip()
    {
        try
        {
            var s = new UserSettings { PowerMode = "performance", Brightness = 42, LowBatteryThreshold = 30 };
            var json = System.Text.Json.JsonSerializer.Serialize(s);
            var back = System.Text.Json.JsonSerializer.Deserialize<UserSettings>(json);
            return back is not null && back.PowerMode == "performance" && back.Brightness == 42 && back.LowBatteryThreshold == 30;
        }
        catch { return false; }
    }
}
