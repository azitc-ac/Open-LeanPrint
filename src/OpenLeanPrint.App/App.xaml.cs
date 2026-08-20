using System.Windows;
using System.Windows.Threading;

namespace OpenLeanPrint.App;

public partial class App : Application
{
    /// <summary>Run only the loopback IPP service, with no window at all.</summary>
    public const string CaptureServiceSwitch = "--capture-service";

    /// <summary>Start hidden in the tray, already collecting.</summary>
    public const string TraySwitch = "--tray";

    private CaptureService? _headlessService;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // A crash in a UI handler would otherwise kill the app silently.
        DispatcherUnhandledException += OnUnhandledException;

        if (e.Args.Contains(CaptureServiceSwitch, StringComparer.OrdinalIgnoreCase))
        {
            StartHeadlessService();
            return;
        }

        // The window is created here rather than by StartupUri, so the headless
        // mode above can skip it.
        var window = new MainWindow();
        if (e.Args.Contains(TraySwitch, StringComparer.OrdinalIgnoreCase))
        {
            window.StartInTray();
            return;
        }

        window.Show();
    }

    /// <summary>
    /// Used by the installer: the printer queue can only be created while the
    /// IPP service is answering, so setup starts this, adds the printer, and
    /// stops it again.
    /// </summary>
    private void StartHeadlessService()
    {
        try
        {
            _headlessService = new CaptureService();
            _headlessService.Start();
        }
        catch (Exception)
        {
            // Nothing can be reported without a window; exiting non-zero is the
            // signal that matters to whoever started us.
            Shutdown(1);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _headlessService?.Dispose();
        base.OnExit(e);
    }

    private void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        MessageBox.Show(e.Exception.Message, "OpenLeanPrint", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }
}
