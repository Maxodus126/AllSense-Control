using System.Management;

namespace AllSense;

public static class BrightnessControl
{
    private static ManagementObject? _setter;
    private static int _levels = -1;

    private static void Ensure()
    {
        if (_setter is not null) return;
        try
        {
            using var searcher = new ManagementObjectSearcher(
                @"root\wmi", "SELECT * FROM WmiMonitorBrightness");
            foreach (ManagementBaseObject o in searcher.Get())
            {
                _levels = Convert.ToInt32(o["Levels"]);
                break;
            }
            using var setSearcher = new ManagementObjectSearcher(
                @"root\wmi", "SELECT * FROM WmiMonitorBrightnessMethods");
            foreach (ManagementBaseObject o in setSearcher.Get())
            {
                _setter = (ManagementObject)o;
                break;
            }
        }
        catch { _setter = null; _levels = -1; }
    }

    /// <summary>Baca kecerahan saat ini dengan query segar (hindari cache basi).</summary>
    public static int? Current()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                @"root\wmi", "SELECT * FROM WmiMonitorBrightness");
            foreach (ManagementBaseObject o in searcher.Get())
                return Convert.ToInt32(o["CurrentBrightness"]);
        }
        catch { }
        return null;
    }

    public static bool CanSet
    {
        get { Ensure(); return _setter is not null && _levels > 1; }
    }

    public static bool Set(int value)
    {
        Ensure();
        if (_setter is null) return false;
        try
        {
            // WmiSetBrightness wajib dipanggil pada kelas WmiMonitorBrightnessMethods.
            _setter.InvokeMethod("WmiSetBrightness",
                new object[] { 1u, (byte)Math.Clamp(value, 0, 100) });
            return true;
        }
        catch { return false; }
    }
}
