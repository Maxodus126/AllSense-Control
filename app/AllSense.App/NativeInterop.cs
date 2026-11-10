using System;
using System.Runtime.InteropServices;

namespace AllSense;

/// <summary>P/Invoke utilitas Windows (power, hotkey).</summary>
public static class NativeInterop
{
    [StructLayout(LayoutKind.Sequential)]
    public struct SYSTEM_POWER_STATUS
    {
        public byte ACLineStatus;
        public byte BatteryFlag;
        public byte BatteryLifePercent;
        public byte Reserved1;
        public uint BatteryLifeTime;
        public uint BatteryFullLifeTime;
    }

    [DllImport("kernel32.dll")]
    public static extern bool GetSystemPowerStatus(out SYSTEM_POWER_STATUS status);

    public const int MOD_CONTROL = 0x0002;
    public const int MOD_ALT = 0x0001;
    public const int MOD_SHIFT = 0x0004;

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll")]
    public static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    public const int WM_HOTKEY = 0x0312;
    public const uint VK_A = 0x41, VK_M = 0x4D, VK_N = 0x4E;

    public static (bool onAc, int percent) GetPowerStatus()
    {
        if (!GetSystemPowerStatus(out var s)) return (true, -1);
        bool ac = s.ACLineStatus != 0;
        int pct = s.BatteryLifePercent == 255 ? -1 : s.BatteryLifePercent;
        return (ac, pct);
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool EnumDisplaySettings(string? lpszDeviceName, int iModeNum, ref DEVMODE lpDevMode);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DEVMODE
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmDeviceName;
        public short dmSpecVersion; public short dmDriverVersion; public short dmSize; public short dmDriverExtra;
        public uint dmFields;
        public int dmPositionX; public int dmPositionY;
        public uint dmDisplayOrientation; public uint dmDisplayFixedOutput;
        public short dmColor; public short dmDuplex; public short dmYResolution; public short dmTTOption; public short dmCollate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmFormName;
        public short dmLogPixels;
        public uint dmBitsPerPel; public uint dmPelsWidth; public uint dmPelsHeight; public uint dmDisplayFlags; public uint dmDisplayFrequency;
        public uint dmICMMethod; public uint dmICMIntent; public uint dmMediaType; public uint dmDitherType; public uint dmReserved1; public uint dmReserved2;
        public uint dmPanningWidth; public uint dmPanningHeight;
    }

    public static (int width, int height, int hz) GetDisplayMode()
    {
        try
        {
            const int ENUM_CURRENT_SETTINGS = -1;
            var d = new DEVMODE { dmSize = (short)Marshal.SizeOf<DEVMODE>() };
            if (EnumDisplaySettings(null, ENUM_CURRENT_SETTINGS, ref d))
                return ((int)d.dmPelsWidth, (int)d.dmPelsHeight, (int)d.dmDisplayFrequency);
        }
        catch { }
        return (0, 0, 0);
    }
}
