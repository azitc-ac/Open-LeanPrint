using System.Windows;
using System.Windows.Threading;

namespace OpenLeanPrint.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // A crash in a UI handler would otherwise kill the app silently.
        DispatcherUnhandledException += OnUnhandledException;
    }

    private void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        MessageBox.Show(e.Exception.Message, "OpenLeanPrint", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }
}
