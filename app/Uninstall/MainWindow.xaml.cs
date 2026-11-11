using System.Diagnostics;
using System.IO;
using Microsoft.Win32;
using System.Windows;

namespace AllSenseUninstall;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();

    private void Go_Click(object sender, RoutedEventArgs e)
    {
        // cleanup sinkron, lalu self-delete via batch tertunda
        Uninstall();
        ScheduleSelfDelete();
        Close();
    }

    private static void Uninstall()
    {
        try
        {
            foreach (var p in Process.GetProcessesByName("AllSense"))
                p.Kill();
        }
        catch { }

        // pintasan desktop + start menu
        try
        {
            var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            var programs = Environment.GetFolderPath(Environment.SpecialFolder.Programs);
            foreach (var path in new[] { Path.Combine(desktop, "AllSense Control.lnk"), Path.Combine(programs, "AllSense Control.lnk") })
            {
                try { if (File.Exists(path)) File.Delete(path); } catch { }
            }
        }
        catch { }

        // autostart
        try
        {
            using var run = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Run", writable: true);
            run?.DeleteValue("AllSenseControl", throwOnMissingValue: false);
        }
        catch { }

        // key uninstall
        try
        {
            Registry.CurrentUser.DeleteSubKeyTree(
                @"Software\Microsoft\Windows\CurrentVersion\Uninstall\AllSense Control", throwOnMissingSubKey: false);
        }
        catch { }

        // data pengaturan
        try
        {
            var cfg = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AllSense");
            if (Directory.Exists(cfg)) Directory.Delete(cfg, recursive: true);
        }
        catch { }
    }

    /// <summary>Hapus folder instalasi (termasuk dirinya sendiri) setelah proses ini keluar.</summary>
    private static void ScheduleSelfDelete()
    {
        try
        {
            var dir = Path.GetDirectoryName(Environment.ProcessPath);
            if (string.IsNullOrEmpty(dir)) return;
            var script = Path.Combine(Path.GetTempPath(), "allsense-uninstall-" + Guid.NewGuid().ToString("N") + ".cmd");
            File.WriteAllText(script,
                "@echo off\r\n" +
                "ping -n 3 127.0.0.1 >nul\r\n" +
                "rmdir /s /q \"" + dir + "\"\r\n" +
                "del /q \"" + script + "\"\r\n");
            Process.Start(new ProcessStartInfo("cmd.exe", " /c start /b \"\" \"" + script + "\"")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
            });
        }
        catch { }
    }
}
