using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Win32;

namespace AllSenseSetup;

public partial class MainWindow : Window
{
    private const string ExeName = "AllSense.exe";

    public MainWindow()
    {
        InitializeComponent();
        // Lokasi default: per-user (tanpa admin), cocok untuk utility onboard.
        PathBox.Text = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Programs", "AllSense");
        UpdateInstallState();
    }

    private void UpdateInstallState() =>
        InstallBtn.IsEnabled = LicenseCheck.IsChecked == true;

    private void License_Changed(object sender, RoutedEventArgs e) => UpdateInstallState();

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog { Title = "Pilih lokasi instalasi" };
        if (dlg.ShowDialog(this) == true)
            PathBox.Text = dlg.FolderName;
    }

    private void Install_Click(object sender, RoutedEventArgs e)
    {
        var dest = PathBox.Text.Trim();
        if (dest.Length == 0)
        {
            Status.Text = "Pilih lokasi instalasi dulu.";
            return;
        }
        ConfirmPath.Text = dest;
        ConfirmOverlay.Visibility = Visibility.Visible;
    }

    private void CancelConfirm_Click(object sender, RoutedEventArgs e) =>
        ConfirmOverlay.Visibility = Visibility.Collapsed;

    private async void GoInstall_Click(object sender, RoutedEventArgs e)
    {
        ConfirmOverlay.Visibility = Visibility.Collapsed;
        await DoInstall(PathBox.Text.Trim());
    }

    private async Task DoInstall(string dest)
    {
        InstallBtn.IsEnabled = false;
        Progress.Visibility = Visibility.Visible;
        Progress.Value = 0;

        try
        {
            Directory.CreateDirectory(dest);
            var target = Path.Combine(dest, ExeName);

            // Jika AllSense sedang berjalan, matikan dulu agar file tidak terkunci.
            try
            {
                foreach (var p in Process.GetProcessesByName("AllSense"))
                    p.Kill();
            }
            catch { }

            Progress.Value = 25;
            File.WriteAllBytes(target, GetPayloadBytes());
            Progress.Value = 60;

            // shortcut desktop + start menu
            var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            var startMenu = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "AllSense Control.lnk");
            CreateShortcut(Path.Combine(desktop, "AllSense Control.lnk"), target);
            CreateShortcut(startMenu, target);
            Progress.Value = 80;

            // tulis uninstaller (berdiri sendiri, hapus semuanya termasuk dirinya)
            var uninstTarget = Path.Combine(dest, "Uninstall.exe");
            File.WriteAllBytes(uninstTarget, GetUninstallPayloadBytes());
            Progress.Value = 90;

            // registrasi Uninstall di HKCU agar tampil di Windows Apps + Revo
            RegisterUninstall(target, uninstTarget, dest);
            Progress.Value = 100;

            Status.Text = "Selesai. AllSense Control terpasang.";
            InstallBtn.Visibility = Visibility.Collapsed;
            CloseBtn.Visibility = Visibility.Visible;
            TryRun.Visibility = Visibility.Visible;
        }
        catch (Exception ex)
        {
            Status.Text = "Gagal: " + ex.Message;
            InstallBtn.IsEnabled = true;
            Progress.Visibility = Visibility.Collapsed;
        }
    }

    private static void RegisterUninstall(string target, string uninstTarget, string installDir)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Uninstall\AllSense Control");
            key?.SetValue("DisplayName", "AllSense Control");
            key?.SetValue("DisplayVersion", "1.0.6");
            key?.SetValue("Publisher", "Maxodus");
            key?.SetValue("DisplayIcon", target + ",0");
            key?.SetValue("UninstallString", "\"" + uninstTarget + "\"");
            key?.SetValue("InstallLocation", installDir);
            key?.SetValue("NoModify", 1, RegistryValueKind.DWord);
            key?.SetValue("NoRepair", 1, RegistryValueKind.DWord);
            key?.SetValue("EstimatedSize", 90000, RegistryValueKind.DWord);
        }
        catch { }
    }

    private static byte[] GetUninstallPayloadBytes()
    {
        var asm = Assembly.GetExecutingAssembly();
        var name = asm.GetManifestResourceNames().First(n => n.EndsWith("Uninstall.exe"));
        using var src = asm.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException("Payload Uninstall.exe tidak ditemukan.");
        using var ms = new MemoryStream();
        src.CopyTo(ms);
        return ms.ToArray();
    }

    private static byte[] GetPayloadBytes()
    {
        var asm = Assembly.GetExecutingAssembly();
        var name = asm.GetManifestResourceNames().First(n => n.EndsWith("AllSense.exe"));
        using var src = asm.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException("Payload AllSense.exe tidak ditemukan.");
        using var ms = new MemoryStream();
        src.CopyTo(ms);
        return ms.ToArray();
    }

    private static void CreateShortcut(string lnkPath, string target)
    {
        dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!)!;
        dynamic sc = shell.CreateShortcut(lnkPath);
        sc.TargetPath = target;
        sc.WorkingDirectory = Path.GetDirectoryName(target);
        sc.IconLocation = target + ",0";
        sc.Save();
    }

    private void Run_Click(object sender, RoutedEventArgs e)
    {
        try { Process.Start(new ProcessStartInfo(Path.Combine(PathBox.Text.Trim(), ExeName)) { UseShellExecute = true }); }
        catch { }
        Close();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
