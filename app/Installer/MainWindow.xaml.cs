using System.IO;
using Microsoft.Win32;

namespace AllSense.Installer;

public partial class MainWindow : System.Windows.Window
{
    private const string AppVersion = "1.0.0";
    private bool _done;

    public MainWindow()
    {
        InitializeComponent();
        string installDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "AllSense");
        LocText.Text = Path.Combine(installDir, "AllSense.exe");
    }

    private async void InstallBtn_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        InstallBtn.IsEnabled = false;
        StatusText.Text = "Menginstal…";

        string payload = Path.Combine(AppContext.BaseDirectory, "payload", "AllSense.exe");
        if (!File.Exists(payload))
        {
            StatusText.Text = "File payload tidak ditemukan.";
            InstallBtn.IsEnabled = true;
            return;
        }

        string installDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "AllSense");
        string destExe = Path.Combine(installDir, "AllSense.exe");

        // cegah menimpa app yang sedang berjalan
        var running = System.Diagnostics.Process.GetProcessesByName("AllSense");
        if (running.Length > 0)
        {
            StatusText.Text = "AllSense sedang berjalan. Tutup dulu.";
            InstallBtn.IsEnabled = true;
            return;
        }

        try
        {
            await Task.Run(() =>
            {
                Directory.CreateDirectory(installDir);
                File.Copy(payload, destExe, true);
            });
            Progress.Value = 45;

            await Task.Run(() => CreateShortcuts(destExe, installDir));
            Progress.Value = 80;

            WriteUninstall(installDir, destExe);
            Progress.Value = 100;

            StatusText.Text = "Instalasi selesai.";
            InstallBtn.Content = "✓ Terpasang";
            _done = true;
        }
        catch (Exception ex)
        {
            StatusText.Text = "Gagal: " + ex.Message;
            InstallBtn.IsEnabled = true;
        }
    }

    private static void CreateShortcuts(string exe, string dir)
    {
        var shellType = Type.GetTypeFromProgID("WScript.Shell");
        if (shellType is null) return;
        dynamic shell = Activator.CreateInstance(shellType);

        string[] folders =
        {
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            Environment.GetFolderPath(Environment.SpecialFolder.Programs)
        };
        foreach (var folder in folders)
        {
            if (string.IsNullOrEmpty(folder)) continue;
            string lnk = Path.Combine(folder, "AllSense.lnk");
            dynamic sc = shell.CreateShortcut(lnk);
            sc.TargetPath = exe;
            sc.WorkingDirectory = dir;
            sc.Description = "AllSense Control — universal power control";
            sc.Save();
        }
    }

    private static void WriteUninstall(string dir, string exe)
    {
        using var key = Registry.CurrentUser.CreateSubKey(
            @"Software\Microsoft\Windows\CurrentVersion\Uninstall\AllSense");
        key.SetValue("DisplayName", "AllSense Control");
        key.SetValue("DisplayVersion", AppVersion);
        key.SetValue("Publisher", "AllSense");
        key.SetValue("InstallLocation", dir);
        key.SetValue("DisplayIcon", exe);
        key.SetValue("UninstallString", $"cmd /c rmdir /s /q \"{dir}\"");
        key.SetValue("NoModify", 1);
        key.SetValue("NoRepair", 1);
    }

    private void CloseBtn_Click(object sender, System.Windows.RoutedEventArgs e) => Close();
}
