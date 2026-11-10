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

    protected override void OnStartup(System.Windows.StartupEventArgs e)
    {
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

    private void OnDispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
    {
        try
        {
            File.AppendAllText(Path.Combine(LocalDir, "crash.log"),
                $"[{DateTime.Now:u}] {e.Exception}\n\n");
        }
        catch { }
        e.Handled = true;
    }
}
