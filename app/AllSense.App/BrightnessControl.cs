using System.Management;

namespace AllSense;

/// <summary>Brightness panel internal via WMI root\wmi.</summary>
public static class BrightnessControl
{
    private static ManagementObject? _monitor;
    private static int _levels = -1;

    private static void Ensure()
    {
        if (_monitor is not null) return;
        try
        {
            using var searcher = new ManagementObjectSearcher(
                @"root\wmi", "SELECT * FROM WmiMonitorBrightness");
            foreach (ManagementBaseObject o in searcher.Get())
            {
                _monitor = (ManagementObject)o;
                _levels = Convert.ToInt32(o["Levels"]);
                break;
            }
        }
        catch { _monitor = null; _levels = -1; }
    }

    public static int? Current()
    {
        Ensure();
        if (_monitor is null) return null;
        try { return Convert.ToInt32(_monitor["CurrentBrightness"]); }
        catch { return null; }
    }

    public static bool CanSet
    {
        get { Ensure(); return _monitor is not null && _levels > 1; }
    }

    public static bool Set(int value)
    {
        Ensure();
        if (_monitor is null) return false;
        try
        {
            // WmiSetBrightness(Timeout, Brightness)
            _monitor.InvokeMethod("WmiSetBrightness",
                new object[] { 0u, (byte)Math.Clamp(value, 0, 100) });
            return true;
        }
        catch { return false; }
    }
}
