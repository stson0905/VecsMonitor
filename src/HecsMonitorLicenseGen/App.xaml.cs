using System.Windows;
using System.Windows.Threading;

namespace HecsMonitorLicenseGen;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        DispatcherUnhandledException += OnUnhandled;
        base.OnStartup(e);
    }

    static void OnUnhandled(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        MessageBox.Show(e.Exception.Message, $"{HecsMonitor.Services.Brand.Name} License Studio 오류", MessageBoxButton.OK, MessageBoxImage.Warning);
        e.Handled = true;
    }
}
