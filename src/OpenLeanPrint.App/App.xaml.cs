using System.Diagnostics;
using System.IO;
using System.Security.Principal;
using System.Windows;
using System.Windows.Threading;
using Path = System.IO.Path;

namespace OpenLeanPrint.App;

public partial class App : Application
{
    /// <summary>Run only the loopback IPP service, with no window at all.</summary>
    public const string CaptureServiceSwitch = "--capture-service";

    /// <summary>Start hidden in the tray, already collecting.</summary>
    public const string TraySwitch = "--tray";

    private CaptureService? _headlessService;
    private SingleInstance? _instance;

    /// <summary>
    /// Whether this copy is running with administrator rights.
    /// <para>
    /// It should not be. Nothing the app does needs them - creating the printer
    /// is the installer's job, and the button that offers to do it asks for
    /// them separately. An elevated window with a file dialog in it is a way to
    /// reach anything on the machine as administrator, so this is worth saying
    /// out loud rather than leaving for somebody to notice.
    /// </para>
    /// </summary>
    internal static bool RunningElevated { get; } = CheckElevation();

    private static bool CheckElevation()
    {
        try
        {
            if (!OperatingSystem.IsWindows()) return false;
            using var identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch (Exception)
        {
            return false;
        }
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // A crash in a UI handler would otherwise kill the app silently.
        DispatcherUnhandledException += OnUnhandledException;
        Log($"start, args: {(e.Args.Length == 0 ? "(none)" : string.Join(" ", e.Args))}");

        if (e.Args.Contains(CaptureServiceSwitch, StringComparer.OrdinalIgnoreCase))
        {
            StartHeadlessService();
            return;
        }

        // A second copy is a request, not a new app: it hands over whatever it
        // was asked to open, raises the copy already running, and leaves.
        var files = e.Args.Where(File.Exists).ToArray();
        _instance = SingleInstance.Claim(files);
        if (_instance is null)
        {
            Log("another copy is already running; asked it to show itself");
            Shutdown();
            return;
        }

        // The window is created here rather than by StartupUri, so the headless
        // mode above can skip it.
        var window = new MainWindow();
        _instance.OnShowRequested(wanted =>
            window.Dispatcher.Invoke(() => window.BringToFront(wanted)));

        // The uninstaller asks rather than kills, so the tray icon goes with it.
        _instance.OnQuitRequested(() =>
        {
            Log("asked to stop");
            window.Dispatcher.Invoke(window.QuitForGood);
        });

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
        _instance?.Dispose();
        base.OnExit(e);
    }

    /// <summary>
    /// One line per start, in <c>%APPDATA%\OpenLeanPrint\app.log</c>.
    /// <para>
    /// It exists because "the app is not running" is otherwise an unanswerable
    /// question: whether the installer ever started it, under which account and
    /// in which session, cannot be reconstructed after the fact. That cost a
    /// round trip once.
    /// </para>
    /// </summary>
    internal static void Log(string message)
    {
        try
        {
            string path = Path.Combine(Path.GetDirectoryName(AppSettings.FilePath)!, "app.log");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);

            // A log nobody rotates eventually becomes the problem it documents.
            if (File.Exists(path) && new FileInfo(path).Length > 64 * 1024) File.Delete(path);

            using var process = Process.GetCurrentProcess();
            File.AppendAllText(path, string.Format("{0:yyyy-MM-dd HH:mm:ss}  {1}{2}  session {3}  {4}{5}",
                DateTime.Now, Environment.UserName, RunningElevated ? " (elevated)" : string.Empty,
                process.SessionId, message, Environment.NewLine));
        }
        catch (Exception)
        {
            // Logging must never be the reason the app fails to start.
        }
    }

    private void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log($"unhandled: {e.Exception}");
        MessageBox.Show(e.Exception.Message, "Open-LeanPrint", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }
}
