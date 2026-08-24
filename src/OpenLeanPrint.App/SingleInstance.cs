using System.IO;
using System.Threading;
using Path = System.IO.Path;

namespace OpenLeanPrint.App;

/// <summary>
/// Keeps one copy of the app per logon session, and turns a second start into
/// "show the one that is already running".
/// <para>
/// Without this, the login shortcut and the Start-menu entry produce two
/// windows, two folder watchers - every captured job collected twice - and two
/// attempts on the same IPP port.
/// </para>
/// <para>
/// The names are session-local on purpose. Every logged-in user gets their own
/// instance, and a copy started somewhere without a desktop cannot take the
/// name away from a real one.
/// </para>
/// </summary>
internal sealed class SingleInstance : IDisposable
{
    private const string MutexName = @"Local\OpenLeanPrint.App.Instance";
    private const string SignalName = @"Local\OpenLeanPrint.App.Show";

    /// <summary>
    /// How the uninstaller asks the app to stop. Killing it instead leaves its
    /// tray icon behind: the notification area only drops an icon when the owner
    /// takes it away, and a process that is terminated never gets to.
    /// </summary>
    public const string QuitSignalName = @"Local\OpenLeanPrint.App.Quit";

    private readonly Mutex _mutex;
    private readonly EventWaitHandle _signal;
    private readonly EventWaitHandle _quit;
    private RegisteredWaitHandle? _registration;
    private RegisteredWaitHandle? _quitRegistration;

    private SingleInstance(Mutex mutex, EventWaitHandle signal, EventWaitHandle quit)
    {
        _mutex = mutex;
        _signal = signal;
        _quit = quit;
    }

    /// <summary>
    /// The instance to keep, or <c>null</c> when another copy already has the
    /// name — it has then been handed <paramref name="files"/> and asked to come
    /// to the front, and this copy should just exit.
    /// </summary>
    public static SingleInstance? Claim(IReadOnlyList<string> files)
    {
        var mutex = new Mutex(initiallyOwned: true, MutexName, out bool weAreTheFirst);
        var signal = new EventWaitHandle(false, EventResetMode.AutoReset, SignalName);
        var quit = new EventWaitHandle(false, EventResetMode.ManualReset, QuitSignalName);

        if (weAreTheFirst) return new SingleInstance(mutex, signal, quit);

        // Written before the signal, so the other copy finds it there.
        if (files.Count > 0) Handoff(files);
        signal.Set();

        mutex.Dispose();
        signal.Dispose();
        quit.Dispose();
        return null;
    }

    /// <summary>Runs <paramref name="show"/> whenever another copy is started.</summary>
    public void OnShowRequested(Action<IReadOnlyList<string>> show)
    {
        _registration = ThreadPool.RegisterWaitForSingleObject(
            _signal, (_, _) => show(TakeHandoff()), state: null,
            Timeout.Infinite, executeOnlyOnce: false);
    }

    /// <summary>Runs <paramref name="quit"/> when something asks the app to stop.</summary>
    public void OnQuitRequested(Action quit)
    {
        _quitRegistration = ThreadPool.RegisterWaitForSingleObject(
            _quit, (_, _) => quit(), state: null, Timeout.Infinite, executeOnlyOnce: true);
    }

    /// <summary>Where a second copy leaves the files it was asked to open.</summary>
    private static string HandoffPath =>
        Path.Combine(Path.GetDirectoryName(AppSettings.FilePath)!, "open-request.txt");

    private static void Handoff(IEnumerable<string> files)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(HandoffPath)!);
            File.AppendAllLines(HandoffPath, files);
        }
        catch (Exception)
        {
            // Worst case the running copy just comes to the front empty-handed;
            // failing to open a file must not stop it being shown.
        }
    }

    private static IReadOnlyList<string> TakeHandoff()
    {
        try
        {
            if (!File.Exists(HandoffPath)) return Array.Empty<string>();

            string[] wanted = File.ReadAllLines(HandoffPath);
            File.Delete(HandoffPath);
            return wanted.Where(File.Exists).ToArray();
        }
        catch (Exception)
        {
            return Array.Empty<string>();
        }
    }

    public void Dispose()
    {
        _registration?.Unregister(waitObject: null);
        _quitRegistration?.Unregister(waitObject: null);
        _quit.Dispose();
        try { _mutex.ReleaseMutex(); }
        catch (ApplicationException) { /* never owned it */ }
        _mutex.Dispose();
        _signal.Dispose();
    }
}
