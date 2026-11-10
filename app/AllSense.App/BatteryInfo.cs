using System;
using System.Management;
using System.Threading.Tasks;

namespace AllSense;

/// <summary>Query WMI Win32_Battery untuk DesignCapacity & FullChargeCapacity (health %).</summary>
public static class BatteryInfo
{
    public sealed record BatteryHealth(int DesignCapacity, int FullChargeCapacity, double HealthPercent);

    /// <summary>Baca sekali; cache internal biar tidak query berulang.</summary>
    private static BatteryHealth? _cached;

    public static async Task<BatteryHealth?> GetAsync()
    {
        if (_cached is not null) return _cached;

        try
        {
            var h = await Task.Run(() =>
            {
                using var searcher = new ManagementObjectSearcher("SELECT DesignCapacity, FullChargeCapacity FROM Win32_Battery");
                foreach (ManagementObject obj in searcher.Get())
                {
                    var design = Convert.ToInt64(obj["DesignCapacity"] ?? 0);
                    var full = Convert.ToInt64(obj["FullChargeCapacity"] ?? 0);
                    if (design <= 0) return null; // tidak didukung / 0 = tidak valid
                    var pct = (double)full / design * 100.0;
                    return new BatteryHealth((int)design, (int)full, Math.Round(pct, 1));
                }
                return null;
            });
            _cached = h;
            return h;
        }
        catch { return null; }
    }

    /// <summary>Format singkat untuk UI: "90% · 45.0 / 50.0 Wh"</summary>
    public static string Format(BatteryHealth h) =>
        $"{h.HealthPercent}% · {h.FullChargeCapacity / 1000.0:F1} / {h.DesignCapacity / 1000.0:F1} Wh";

    /// <summary>Clear cache (testing / kalau hardware berubah — jarang).</summary>
    public static void Invalidate() => _cached = null;
}