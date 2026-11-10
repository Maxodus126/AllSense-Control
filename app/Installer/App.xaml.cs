using System.Windows;

namespace AllSenseSetup;

public partial class App : System.Windows.Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += (_, ev) =>
        {
            try
            {
                System.IO.File.AppendAllText(
                    System.IO.Path.Combine(System.IO.Path.GetTempPath(), "AllSenseSetup.log"),
                    $"[{DateTime.Now:u}] {ev.Exception}\n\n");
            }
            catch { }
            System.Windows.MessageBox.Show(ev.Exception.Message, "AllSense Setup",
                MessageBoxButton.OK, MessageBoxImage.Error);
            ev.Handled = true;
        };
    }
}
