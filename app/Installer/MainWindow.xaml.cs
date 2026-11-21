using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
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
        ShowStep(StepLicense);
    }

    // ---------- navigasi wizard ----------
    private void ShowStep(UIElement step)
    {
        StepLicense.Visibility = ReferenceEquals(step, StepLicense) ? Visibility.Visible : Visibility.Collapsed;
        StepConfirm.Visibility = ReferenceEquals(step, StepConfirm) ? Visibility.Visible : Visibility.Collapsed;
        StepProgress.Visibility = ReferenceEquals(step, StepProgress) ? Visibility.Visible : Visibility.Collapsed;
        StepDone.Visibility = ReferenceEquals(step, StepDone) ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UpdateInstallState()
    {
        StepMsg.Text = "";
        UpStatus.Text = "";
        NextBtn.IsEnabled = LicenseCheck.IsChecked == true && !string.IsNullOrWhiteSpace(PathBox.Text);
    }

    private void License_Changed(object sender, RoutedEventArgs e) => UpdateInstallState();
    private void PathBox_TextChanged(object sender, TextChangedEventArgs e) => UpdateInstallState();

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog { Title = "Pilih lokasi instalasi" };
        if (dlg.ShowDialog(this) == true)
            PathBox.Text = dlg.FolderName;
    }

    private void Next_Click(object sender, RoutedEventArgs e)
    {
        var dest = PathBox.Text.Trim();
        if (dest.Length == 0)
        {
            UpStatus.Text = "Pilih lokasi instalasi dulu.";
            return;
        }
        ConfirmPath.Text = dest;
        ShowStep(StepConfirm);
    }

    private void Back_Click(object sender, RoutedEventArgs e) => ShowStep(StepLicense);
    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();

    private async void GoInstall_Click(object sender, RoutedEventArgs e)
    {
        ShowStep(StepProgress);
        var ok = await DoInstall(PathBox.Text.Trim());
        ShowStep(ok ? StepDone : StepConfirm);
    }

    private async Task<bool> DoInstall(string dest)
    {
        InstallProgress.Value = 0;
        InstallStatus.Text = "";
        try
        {
            Directory.CreateDirectory(dest);
            var target = Path.Combine(dest, ExeName);

            // Jika AllSense sedang berjalan, matikan dan TUNGGU benar-benar keluar
            // sebelum menimpa file. Tanpa WaitForExit, File.WriteAllBytes bisa
            // gagal dengan "file sedang dipakai" (race yang intermittent).
            try
            {
                foreach (var p in Process.GetProcessesByName("AllSense"))
                {
                    try
                    {
                        p.Kill();
                        if (!p.WaitForExit(5000)) continue;
                    }
                    catch { }
                    finally { p.Dispose(); }
                }
            }
            catch { }

            InstallProgress.Value = 25;
            InstallStatus.Text = "Menyalin file aplikasi…";
            await Task.Run(() => File.WriteAllBytes(target, GetPayloadBytes()));
            InstallProgress.Value = 55;

            // shortcut desktop + start menu
            var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            var startMenu = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "AllSense Control.lnk");
            await Task.Run(() =>
            {
                CreateShortcut(Path.Combine(desktop, "AllSense Control.lnk"), target);
                CreateShortcut(startMenu, target);
            });
            InstallProgress.Value = 75;
            InstallStatus.Text = "Menyertakan uninstaller…";

            // tulis uninstaller (berdiri sendiri, hapus semuanya termasuk dirinya)
            var uninstTarget = Path.Combine(dest, "Uninstall.exe");
            await Task.Run(() => File.WriteAllBytes(uninstTarget, GetUninstallPayloadBytes()));
            InstallProgress.Value = 88;

            // registrasi Uninstall di HKCU agar tampil di Windows Apps + Revo
            RegisterUninstall(target, uninstTarget, dest);
            InstallProgress.Value = 100;
            InstallStatus.Text = "Selesai.";
            DoneStatus.Text = $"AllSense Control terpasang di\n{dest}";
            return true;
        }
        catch (Exception ex)
        {
            InstallProgress.Value = 0;
            InstallStatus.Text = "";
            StepMsg.Text = "Gagal memasang: " + ex.Message;
            return false;
        }
    }

    private static void RegisterUninstall(string target, string uninstTarget, string installDir)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Uninstall\AllSense Control");
            key?.SetValue("DisplayName", "AllSense Control");
            key?.SetValue("DisplayVersion", "2.0.1");
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
