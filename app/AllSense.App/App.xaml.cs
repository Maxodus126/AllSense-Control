using System.IO;

namespace AllSense;

public partial class App : System.Windows.Application
{
    public static readonly string LocalDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AllSense");

    protected override void OnStartup(System.Windows.StartupEventArgs e)
    {
        try { Directory.CreateDirectory(LocalDir); }
        catch { /* abaikan */ }
        base.OnStartup(e);
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
