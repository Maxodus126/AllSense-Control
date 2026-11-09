using System;
using System.Runtime.InteropServices;

namespace AllSense;

/// <summary>P/Invoke utilitas Windows (power, hotkey).</summary>
public static class NativeInterop
{
    [StructLayout(LayoutKind.Sequential)]
    public struct SYSTEM_POWER_STATUS
    {
        public byte ACLineStatus;      // 0 = battery, 1 = AC
        public byte BatteryFlag;       // 1 high,2 low,4 critical,8 charging,128 no battery
        public byte BatteryLifePercent; // 0-100, 255 = unknown
        public byte Reserved1;
        public uint BatteryLifeTime;
        public uint BatteryFullLifeTime;
    }

    [DllImport("kernel32.dll")]
    public static extern bool GetSystemPowerStatus(out SYSTEM_POWER_STATUS status);

    public const int MOD_CONTROL = 0x0002;
    public const int MOD_ALT = 0x0001;

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll")]
    public static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    public const int WM_HOTKEY = 0x0312;
    public const uint VK_A = 0x41, VK_M = 0x4D;

    /// <summary>Doc AC/DC + level batterai. -1 = tidak tersedia.</summary>
    public static (bool onAc, int percent) GetPowerStatus()
    {
        if (!GetSystemPowerStatus(out var s)) return (true, -1);
        bool ac = s.ACLineStatus != 0;
        int pct = s.BatteryLifePercent == 255 ? -1 : s.BatteryLifePercent;
        return (ac, pct);
    }
}
