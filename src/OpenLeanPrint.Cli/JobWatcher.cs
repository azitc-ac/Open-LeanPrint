using OpenLeanPrint.Capture;
using OpenLeanPrint.Print;

namespace OpenLeanPrint.Cli;

/// <summary>
/// Imposes — and optionally prints — every PDF that lands in a folder, typically
/// the capture host's output folder. That makes "print → auto 4-up → printer"
/// usable before the GUI exists.
/// <para>
/// Arrival detection lives in <see cref="CapturedFolderWatcher"/>; this class is
/// just what happens to a job once it is there.
/// </para>
/// </summary>
internal sealed class JobWatcher : IDisposable
{
    private readonly string _outputFolder;
    private readonly ImposeOptions _impose;
    private readonly string? _printer;
    private readonly int _dpi;
    private readonly DuplexMode _duplex;
    private readonly CapturedFolderWatcher _watcher;

    public JobWatcher(string folder, string outputFolder, ImposeOptions impose, string? printer, int dpi,
                      DuplexMode duplex = DuplexMode.Default)
    {
        _outputFolder = Path.GetFullPath(outputFolder);
        _impose = impose;
        _printer = printer;
        _dpi = dpi;
        _duplex = duplex;

        _watcher = new CapturedFolderWatcher(folder)
        {
            // Never pick up our own output (possible when --out-dir is the watched folder).
            Accept = path => !string.Equals(Path.GetFullPath(Path.GetDirectoryName(path) ?? string.Empty),
                                            _outputFolder, StringComparison.OrdinalIgnoreCase),
        };
        _watcher.JobArrived += (_, path) => Process(path);
        _watcher.JobTimedOut += (_, path) =>
            Console.Error.WriteLine($"  ! {Path.GetFileName(path)}: still being written - skipped.");
    }

    /// <summary>Watches until <paramref name="token"/> is cancelled.</summary>
    public void Run(bool includeExisting, CancellationToken token)
    {
        Directory.CreateDirectory(_outputFolder);
        _watcher.Start(includeExisting);
        token.WaitHandle.WaitOne(); // jobs are handled on the watcher's thread
        _watcher.Stop();
    }

    private void Process(string path)
    {
        string name = Path.GetFileName(path);
        try
        {
            byte[] imposed = ImposeRunner.Run(File.ReadAllBytes(path), _impose);

            string output = Path.Combine(_outputFolder,
                $"{Path.GetFileNameWithoutExtension(name)}-{_impose.FileTag()}.pdf");
            File.WriteAllBytes(output, imposed);
            Console.WriteLine($"  + {name} -> {Path.GetFileName(output)} ({_impose.Describe()}, {imposed.Length:N0} bytes)");

            if (_printer is not null && OperatingSystem.IsWindows())
            {
                var report = PdfPrinter.Print(imposed, _printer, new PrintOptions
                {
                    Dpi = _dpi,
                    Duplex = _duplex,
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

    public void Dispose() => _watcher.Dispose();
}
