using System;
using System.Runtime.InteropServices;

namespace AllSense;

/// <summary>Power Mode & skema daya Windows.</summary>
public enum PowerMode { Efficiency = 0, Balanced = 1, Performance = 2 }

public static class PowerControl
{
    // Mode slider (Win11) — GUID, jangan tertukar dengan skema.
    private static readonly Guid Efficiency = Guid.Parse("961cc777-2547-4f9d-8174-7d86181b8a7a");
    private static readonly Guid Balanced = Guid.Empty;
    private static readonly Guid Performance = Guid.Parse("ded574b5-45a0-4f42-8737-46345c09c238");

    // Skema (legacy Win10).
    private static readonly Guid SchemeEfficiency = Guid.Parse("a4f06079-c92a-4e1f-8cfa-9b02a2a0b8d8");
    private static readonly Guid SchemeBalanced = Guid.Parse("381b4222-f694-41f0-9685-ff5bb260df2e");
    private static readonly Guid SchemePerformance = Guid.Parse("8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c");

    [DllImport("powrprof.dll")]
    private static extern int PowerSetUserConfiguredACPowerMode(Guid mode);

    [DllImport("powrprof.dll")]
    private static extern int PowerSetUserConfiguredDCPowerMode(Guid mode);

    [DllImport("powrprof.dll")]
    private static extern int PowerSetActiveScheme(IntPtr rootPowerKey, ref Guid schemeGuid);

    [DllImport("powrprof.dll")]
    private static extern int PowerGetActiveScheme(IntPtr rootPowerKey, out IntPtr activePolicyGuid);

    private static Guid ModeGuid(PowerMode m) => m switch
    {
        PowerMode.Efficiency => Efficiency,
        PowerMode.Performance => Performance,
        _ => Balanced
    };

    private static Guid SchemeGuid(PowerMode m) => m switch
    {
        PowerMode.Efficiency => SchemeEfficiency,
        PowerMode.Performance => SchemePerformance,
        _ => SchemeBalanced
    };

    public static bool ModernApiAvailable { get; }
        = NativeMethods.IsExportPresent("powrprof.dll", "PowerSetUserConfiguredACPowerMode");

    /// <summary>Terapkan mode untuk AC dan DC. Kembali false bila kedua jalur gagal.</summary>
    public static bool Apply(PowerMode mode)
    {
        var g = ModeGuid(mode);
        if (ModernApiAvailable)
        {
            // coba slider modern (Win11)
            if (PowerSetUserConfiguredACPowerMode(g) == 0 &&
                PowerSetUserConfiguredDCPowerMode(g) == 0)
                return true;
            // gagal → jatuh ke skema
        }
        // fallback legacy
        var sg = SchemeGuid(mode);
        return PowerSetActiveScheme(IntPtr.Zero, ref sg) == 0;
    }

    public static PowerMode? Current()
    {
        try
        {
            if (PowerGetActiveScheme(IntPtr.Zero, out var p) != 0) return null;
            var g = Marshal.PtrToStructure<Guid>(p);
            Marshal.FreeHGlobal(p);
            if (g == Balanced || g == SchemeBalanced) return PowerMode.Balanced;
            if (g == Efficiency || g == SchemeEfficiency) return PowerMode.Efficiency;
            if (g == Performance || g == SchemePerformance) return PowerMode.Performance;
            return null;
        }
        catch { return null; }
    }
}

internal static class NativeMethods
{
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr LoadLibrary(string name);

    [DllImport("kernel32.dll", CharSet = CharSet.Ansi)]
    private static extern IntPtr GetProcAddress(IntPtr hModule, string procName);

    public static bool IsExportPresent(string library, string proc)
    {
        var h = LoadLibrary(library);
        if (h == IntPtr.Zero) return false;
        return GetProcAddress(h, proc) != IntPtr.Zero;
    }
}
