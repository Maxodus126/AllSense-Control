using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

namespace AllSense;

public partial class App : System.Windows.Application
{
    public static readonly string LocalDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AllSense");

    private static Mutex? _mutex;

    internal const string SmokeEnv = "ALLSENSE_SMOKE_TEST";
    internal static bool IsSmokeTest => Environment.GetEnvironmentVariable(SmokeEnv) == "1";

    protected override void OnStartup(System.Windows.StartupEventArgs e)
    {
        // Mode smoke-test (dipakai Updater sebelum menimpa exe):
        // verifikasi bootstrap WPF (font cache/ICU) sembuh TANPA side-effect
        // hardware/tray/hotkey. Keluar 0 bila window bisa dibangun, 3 bila crash.
        if (IsSmokeTest)
        {
            DispatcherUnhandledException += (_, a) => { a.Handled = true; Shutdown(3); };
            var w = new System.Windows.Window
            {
                Width = 320, Height = 40, ShowInTaskbar = false,
                WindowStyle = System.Windows.WindowStyle.None,
                ResizeMode = System.Windows.ResizeMode.NoResize,
                Content = new System.Windows.Controls.TextBlock
                    { Text = "AllSense smoke-test", FontSize = 12 },
            };
            w.Show();
            w.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            w.Close();
            Shutdown(0);
            return;
        }

        // Single-instance: cegah dua proses berjalan (hotkey global saling rebut).
        _mutex = new Mutex(true, "AllSense.SingleInstance", out var fresh);
        if (!fresh)
        {
            ActivateExisting();
            Shutdown();
            return;
        }
        try { Directory.CreateDirectory(LocalDir); }
        catch { /* abaikan */ }
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        base.OnStartup(e);
    }

    protected override void OnExit(System.Windows.ExitEventArgs e)
    {
        try { _mutex?.ReleaseMutex(); } catch { }
        base.OnExit(e);
    }

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    private static void ActivateExisting()
    {
        // Sinyaikan event: instance pertama akan menampilkan window walau berada di tray.
        try
        {
            using var evt = EventWaitHandle.OpenExisting("AllSense.ShowWindow");
            evt.Set();
            return;
        }
        catch { }
        // Fallback: cari window terlihat.
        try
        {
            foreach (var p in Process.GetProcessesByName("AllSense"))
            {
                if (p.Id == Environment.ProcessId) continue;
                var h = p.MainWindowHandle;
                if (h == IntPtr.Zero) continue;
                ShowWindow(h, 9); // SW_RESTORE
                SetForegroundWindow(h);
            }
        }
        catch { }
    }

    private static bool _warned;

    private void OnDispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
    {
        try
        {
            File.AppendAllText(Path.Combine(LocalDir, "crash.log"),
                $"[{DateTime.Now:u}] {e.Exception}\n\n");
        }
        catch { }
        // Jangan telan hening: kegagalan dahulu (font-cache) tertutup oleh Handled=true
        // sehingga app "hidup" tanpa window. Sekarang tampilkan peringatan sekali per sesi,
        // supaya kelainan terlihat oleh pengguna sementara aplikasi tetap berjalan.
        if (!_warned)
        {
            _warned = true;
            try
            {
                System.Windows.MessageBox.Show(
                    "Terjadi kesalahan tak terduga.\nDetail tercatat di crash.log:\n" + LocalDir,
                    "AllSense – Error", System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Warning);
            }
            catch { }
        }
        e.Handled = true;
    }
}
