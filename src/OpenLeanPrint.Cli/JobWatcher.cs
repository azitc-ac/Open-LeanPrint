using System.Collections.Concurrent;
using OpenLeanPrint.Print;

namespace OpenLeanPrint.Cli;

/// <summary>
/// Watches a folder for new PDFs — typically the capture host's <c>captured/</c>
/// folder — imposes each one and optionally prints it. That makes
/// "print → auto 4-up → printer" usable before the GUI exists.
/// </summary>
internal sealed class JobWatcher : IDisposable
{
    private readonly string _folder;
    private readonly string _outputFolder;
    private readonly ImposeOptions _impose;
    private readonly string? _printer;
    private readonly int _dpi;

    private readonly FileSystemWatcher _watcher;
    private readonly BlockingCollection<string> _queue = new();
    private readonly HashSet<string> _handled = new(StringComparer.OrdinalIgnoreCase);

    public JobWatcher(string folder, string outputFolder, ImposeOptions impose, string? printer, int dpi)
    {
        _folder = Path.GetFullPath(folder);
        _outputFolder = Path.GetFullPath(outputFolder);
        _impose = impose;
        _printer = printer;
        _dpi = dpi;

        _watcher = new FileSystemWatcher(_folder, "*.pdf")
        {
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.Size,
            IncludeSubdirectories = false,
        };
        _watcher.Created += (_, e) => Enqueue(e.FullPath);
        // Some writers create a temp file and rename it into place.
        _watcher.Renamed += (_, e) => Enqueue(e.FullPath);
    }

    /// <summary>Processes queued jobs until <paramref name="token"/> is cancelled.</summary>
    public void Run(bool includeExisting, CancellationToken token)
    {
        Directory.CreateDirectory(_outputFolder);
        _watcher.EnableRaisingEvents = true;

        if (includeExisting)
            foreach (string path in Directory.EnumerateFiles(_folder, "*.pdf")) Enqueue(path);

        try
        {
            foreach (string path in _queue.GetConsumingEnumerable(token)) Process(path);
        }
        catch (OperationCanceledException)
        {
            // Ctrl+C: a clean stop, not a failure.
        }
    }

    private void Enqueue(string path)
    {
        // Never pick up our own output (possible when --out-dir is the watched folder).
        if (string.Equals(Path.GetFullPath(Path.GetDirectoryName(path) ?? _folder), _outputFolder,
                          StringComparison.OrdinalIgnoreCase)) return;

        lock (_handled)
        {
            if (!_handled.Add(path)) return; // one file can raise several events
        }
        _queue.Add(path);
    }

    private void Process(string path)
    {
        string name = Path.GetFileName(path);
        if (!WaitUntilComplete(path, TimeSpan.FromSeconds(30)))
        {
            Console.Error.WriteLine($"  ! {name}: still being written after 30 s - skipped.");
            return;
        }

        try
        {
            byte[] source = File.ReadAllBytes(path);
            byte[] imposed = ImposeRunner.Run(source, _impose);

            string output = Path.Combine(_outputFolder,
                $"{Path.GetFileNameWithoutExtension(name)}-{_impose.FileTag()}.pdf");
            File.WriteAllBytes(output, imposed);
            Console.WriteLine($"  + {name} -> {Path.GetFileName(output)} ({_impose.Describe()}, {imposed.Length:N0} bytes)");

            if (_printer is not null && OperatingSystem.IsWindows())
            {
                var report = PdfPrinter.Print(imposed, _printer, new PrintOptions
                {
                    Dpi = _dpi,
                    JobName = $"OpenLeanPrint - {name}",
                });
                Console.WriteLine($"    printed {report.Sheets} sheet(s) to \"{report.PrinterName}\".");
            }
        }
        catch (Exception ex)
        {
            // One bad job must not take the watcher down.
            Console.Error.WriteLine($"  ! {name}: {ex.Message}");
        }
    }

    /// <summary>
    /// Waits until a file has stopped growing and is no longer locked. The
    /// creation event fires as soon as the file exists, long before the writer
    /// is done with it.
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
            Thread.Sleep(300);
        }
        return false;
    }

    public void Dispose()
    {
        _watcher.EnableRaisingEvents = false;
        _watcher.Dispose();
        _queue.Dispose();
    }
}
