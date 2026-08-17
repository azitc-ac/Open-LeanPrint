using System.Collections.Concurrent;

namespace OpenLeanPrint.Capture;

/// <summary>
/// Raises <see cref="JobArrived"/> for every PDF that lands in a folder — the
/// bridge between the capture host (which writes files) and anything that wants
/// to act on them: the CLI's <c>watch</c> command and the desktop app's job pool.
/// <para>
/// The subtlety this class exists for: a file-creation event fires as soon as
/// the file exists, long before the writer is finished with it. Events are
/// therefore queued and only handed on once the file has stopped growing and can
/// be opened exclusively.
/// </para>
/// </summary>
public sealed class CapturedFolderWatcher : IDisposable
{
    private readonly FileSystemWatcher _watcher;
    private readonly BlockingCollection<string> _queue = new();
    private readonly HashSet<string> _seen = new(StringComparer.OrdinalIgnoreCase);
    private CancellationTokenSource? _cancellation;
    private Thread? _consumer;

    public CapturedFolderWatcher(string folder, string filter = "*.pdf")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folder);

        Folder = Path.GetFullPath(folder);
        _watcher = new FileSystemWatcher(Folder, filter)
        {
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.Size,
            IncludeSubdirectories = false,
        };
        _watcher.Created += (_, e) => Enqueue(e.FullPath);
        // Some writers create a temp file and rename it into place.
        _watcher.Renamed += (_, e) => Enqueue(e.FullPath);
    }

    /// <summary>The folder being watched (absolute).</summary>
    public string Folder { get; }

    /// <summary>How long to wait for a file to finish being written.</summary>
    public TimeSpan CompletionTimeout { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>Optional veto, e.g. to ignore a nested output folder.</summary>
    public Func<string, bool>? Accept { get; init; }

    /// <summary>A file finished arriving. Raised on a background thread.</summary>
    public event EventHandler<string>? JobArrived;

    /// <summary>A file never stopped changing within <see cref="CompletionTimeout"/>.</summary>
    public event EventHandler<string>? JobTimedOut;

    /// <summary>Starts watching. <paramref name="includeExisting"/> also picks up what is already there.</summary>
    public void Start(bool includeExisting = false)
    {
        if (_consumer is not null) return;

        Directory.CreateDirectory(Folder);
        _cancellation = new CancellationTokenSource();
        var token = _cancellation.Token;

        _consumer = new Thread(() => Consume(token))
        {
            IsBackground = true,
            Name = "OpenLeanPrint capture watcher",
        };
        _consumer.Start();

        _watcher.EnableRaisingEvents = true;

        if (includeExisting)
            foreach (string path in Directory.EnumerateFiles(Folder, _watcher.Filter)) Enqueue(path);
    }

    /// <summary>Stops watching. Safe to call more than once.</summary>
    public void Stop()
    {
        _watcher.EnableRaisingEvents = false;
        _cancellation?.Cancel();
        _consumer?.Join(TimeSpan.FromSeconds(2));
        _consumer = null;
        _cancellation?.Dispose();
        _cancellation = null;
    }

    private void Enqueue(string path)
    {
        if (Accept is not null && !Accept(path)) return;

        lock (_seen)
        {
            if (!_seen.Add(path)) return; // one file can raise several events
        }

        // The queue is only closed on Dispose; losing a late event is harmless.
        try { _queue.Add(path); }
        catch (InvalidOperationException) { }
    }

    private void Consume(CancellationToken token)
    {
        try
        {
            foreach (string path in _queue.GetConsumingEnumerable(token))
            {
                if (WaitUntilComplete(path, CompletionTimeout))
                    JobArrived?.Invoke(this, path);
                else
                    JobTimedOut?.Invoke(this, path);
            }
        }
        catch (OperationCanceledException)
        {
            // Stop() - a clean shutdown.
        }
    }

    /// <summary>
    /// Waits until a file has stopped growing and is no longer locked by its
    /// writer. Returns false if it never settled.
    /// </summary>
    private static bool WaitUntilComplete(string path, TimeSpan timeout)
    {
        DateTime deadline = DateTime.UtcNow + timeout;
        long lastLength = -1;
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                var info = new FileInfo(path);
                if (!info.Exists) return false;
                if (info.Length > 0 && info.Length == lastLength)
                {
                    // Opening exclusively proves the writer has let go.
                    using var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.None);
                    return true;
                }
                lastLength = info.Length;
            }
            catch (IOException)
            {
                // Still locked by the writer - keep waiting.
            }
            Thread.Sleep(150);
        }
        return false;
    }

    public void Dispose()
    {
        Stop();
        _watcher.Dispose();
        _queue.Dispose();
    }
}
