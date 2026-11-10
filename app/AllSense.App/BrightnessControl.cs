using System.Management;
using System.Runtime.InteropServices;

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
        // Fallback: DDC/CI lewat dxva2 (berguna untuk monitor/sistem yang
        // tidak mengekspos WMI brightness).
        var ddc = DdcGetBrightness();
        if (ddc.HasValue) return ddc.Value;
        return null;
    }

    public static bool CanSet
    {
        get { Ensure(); return (_setter is not null && _levels > 1) || DdcAvailable(); }
    }

    public static bool Set(int value)
    {
        var v = Math.Clamp(value, 0, 100);
        Ensure();
        if (_setter is not null)
        {
            try
            {
                // WmiSetBrightness wajib dipanggil pada kelas WmiMonitorBrightnessMethods.
                _setter.InvokeMethod("WmiSetBrightness",
                    new object[] { 1u, (byte)v });
                return true;
            }
            catch { }
        }
        return DdcSetBrightness(v);
    }

    // ---------- Fallback DDC/CI (dxva2.dll) ----------

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct PhysicalMonitor
    {
        public IntPtr hPhysicalMonitor;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szPhysicalMonitorDescription;
    }

    [DllImport("dxva2.dll")]
    private static extern bool GetNumberOfPhysicalMonitorsFromHMONITOR(IntPtr hMonitor, out uint dwNumberOfPhysicalMonitors);

    [DllImport("dxva2.dll")]
    private static extern bool GetPhysicalMonitorsFromHMONITOR(IntPtr hMonitor, uint dwArraySize, [Out] PhysicalMonitor[] pPhysicalMonitorArray);

    [DllImport("dxva2.dll")]
    private static extern bool DestroyPhysicalMonitors(uint dwArraySize, [In] PhysicalMonitor[] pPhysicalMonitorArray);

    [DllImport("dxva2.dll")]
    private static extern bool GetMonitorBrightness(IntPtr hMonitor, out uint pdwMinimumBrightness, out uint pdwCurrentBrightness, out uint pdwMaximumBrightness);

    [DllImport("dxva2.dll")]
    private static extern bool SetMonitorBrightness(IntPtr hMonitor, uint dwMinimumBrightness, uint dwBrightness, uint dwMaximumBrightness);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

    private const uint MONITOR_DEFAULTTOPRIMARY = 1;

    private static IntPtr PrimaryMonitorHandle() => MonitorFromWindow(IntPtr.Zero, MONITOR_DEFAULTTOPRIMARY);

    private static PhysicalMonitor[]? GetMonitors()
    {
        var hMon = PrimaryMonitorHandle();
        if (hMon == IntPtr.Zero) return null;
        if (!GetNumberOfPhysicalMonitorsFromHMONITOR(hMon, out var count) || count == 0) return null;
        var arr = new PhysicalMonitor[count];
        if (!GetPhysicalMonitorsFromHMONITOR(hMon, count, arr)) return null;
        return arr;
    }

    private static int? DdcGetBrightness()
    {
        try
        {
            var arr = GetMonitors();
            if (arr is null) return null;
            try
            {
                foreach (var m in arr)
                {
                    if (GetMonitorBrightness(m.hPhysicalMonitor, out var min, out var cur, out var max) && max > min)
                        return (int)Math.Round((cur - min) * 100d / (max - min));
                }
            }
            finally { DestroyPhysicalMonitors((uint)arr.Length, arr); }
        }
        catch { }
        return null;
    }

    private static bool DdcAvailable()
    {
        try
        {
            var arr = GetMonitors();
            if (arr is null) return false;
            var ok = false;
            try
            {
                if (arr.Length > 0) ok = GetMonitorBrightness(arr[0].hPhysicalMonitor, out _, out _, out _);
            }
            finally { DestroyPhysicalMonitors((uint)arr.Length, arr); }
            return ok;
        }
        catch { return false; }
    }

    private static bool DdcSetBrightness(int percent)
    {
        try
        {
            var arr = GetMonitors();
            if (arr is null) return false;
            try
            {
                foreach (var m in arr)
                {
                    if (!GetMonitorBrightness(m.hPhysicalMonitor, out var min, out _, out var max)) continue;
                    if (max <= min) continue;
                    var b = min + (uint)Math.Round((max - min) * percent / 100d);
                    if (SetMonitorBrightness(m.hPhysicalMonitor, min, b, max)) return true;
                }
            }
            finally { DestroyPhysicalMonitors((uint)arr.Length, arr); }
        }
        catch { }
        return false;
    }
}
