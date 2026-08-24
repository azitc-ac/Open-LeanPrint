using System.Globalization;
using Microsoft.Extensions.Hosting;
using OpenLeanPrint.Capture;
using OpenLeanPrint.Capture.Server;

namespace OpenLeanPrint.Capture.Host;

/// <summary>
/// The capture service: holds the loopback IPP listener for as long as the
/// machine is running, so the virtual printer works without anybody being
/// logged in and without the desktop app running.
/// <para>
/// It keeps a plain text log next to the captured jobs. A service that fails
/// silently is impossible to diagnose, and this project has already paid for
/// that lesson once.
/// </para>
/// </summary>
internal sealed class CaptureServiceWorker : BackgroundService
{
    private readonly CaptureSettings _settings;
    private IppPrinterServer? _server;

    public CaptureServiceWorker(CaptureSettings settings) => _settings = settings;

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        Directory.CreateDirectory(_settings.OutputFolder);
        // Which build is running should never again be a matter of comparing
        // file hashes by hand.
        string build = System.Diagnostics.FileVersionInfo
            .GetVersionInfo(System.Reflection.Assembly.GetExecutingAssembly().Location).FileVersion ?? "unknown";
        Log($"Starting build {build} on port {_settings.Port}, writing to {_settings.OutputFolder}");

        // Whoever creates this folder first decides who may delete from it, and
        // as a service that is LocalSystem - which would leave the person who
        // printed the job unable to remove it. Say so explicitly instead.
        if (OperatingSystem.IsWindows() &&
            string.Equals(_settings.OutputFolder, CaptureLocations.SharedFolder,
                          StringComparison.OrdinalIgnoreCase))
        {
            Log(CapturedFolder.AllowUsersToManage(_settings.OutputFolder)
                ? "Captured jobs can be managed by the users of this machine."
                : "Could not grant users access to the capture folder; they may not be able to delete their jobs.");
        }

        Prune();

        try
        {
            var server = new IppPrinterServer(new IppPrinterOptions
            {
                PrinterName = _settings.PrinterName,
                Port = _settings.Port,
            });

            server.JobCaptured += (_, job) =>
            {
                try
                {
                    string path = CapturedJobWriter.Save(job, _settings.OutputFolder);
                    Log($"Captured job #{job.JobId} from {job.UserName ?? "(unknown user)"}, " +
                        $"{job.Data.Length:N0} bytes, sides={job.Sides ?? "(unset)"}, " +
                        $"colour={job.ColorMode ?? "(unset)"} -> {Path.GetFileName(path)}");
                    Prune();
                }
                catch (Exception ex)
                {
                    // The print queue has already been told the job succeeded;
                    // losing one job must not take the service down.
                    Log($"Could not save job #{job.JobId}: {ex.Message}");
                }
            };

            server.Start();
            _server = server;
            Log("Listening.");
        }
        catch (Exception ex)
        {
            // Most likely something else already owns the port.
            Log($"Could not start: {ex.Message}");
            throw;
        }

        return Task.CompletedTask;
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_server is not null)
        {
            Log("Stopping.");
            try { await _server.StopAsync(); } catch (Exception) { /* already gone */ }
            _server.Dispose();
            _server = null;
        }
        await base.StopAsync(cancellationToken);
    }

    /// <summary>
    /// Keeps the folder from growing without bound. Captured jobs are a
    /// hand-over to the app, and nothing removed them before - so the folder
    /// grew with every page anyone printed, somewhere nobody looks.
    /// </summary>
    private void Prune()
    {
        var removed = CapturedFolder.Prune(_settings.OutputFolder, _settings.MaxAge, _settings.MaxBytes);
        if (removed.RemovedAnything)
            Log($"Removed {removed.Files} captured job(s), {removed.Bytes / (1024.0 * 1024.0):N1} MB.");
    }

    private void Log(string message)
    {
        string line = string.Create(CultureInfo.InvariantCulture,
            $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  {message}{Environment.NewLine}");
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_settings.LogPath)!);
            File.AppendAllText(_settings.LogPath, line);
        }
        catch (Exception)
        {
            // Logging must never be the reason the service fails.
        }
    }
}
