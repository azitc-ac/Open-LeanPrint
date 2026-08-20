using System.IO;
using OpenLeanPrint.Capture;
using OpenLeanPrint.Capture.Server;
using Path = System.IO.Path;

namespace OpenLeanPrint.App;

/// <summary>
/// Runs the loopback IPP service inside the app, so an installed copy can
/// capture print jobs on its own. Before this existed, capturing meant running
/// a separate console host from a source checkout — which an installed package
/// has no way to do.
/// <para>
/// Captured jobs are written to <see cref="CaptureLocations.DefaultFolder"/>,
/// exactly where the console host writes them, so both routes look identical to
/// everything downstream and a job survives the app being closed.
/// </para>
/// </summary>
internal sealed class CaptureService : IDisposable
{
    private IppPrinterServer? _server;

    /// <summary>The port the loopback service listens on.</summary>
    public int Port { get; private set; } = DefaultPort;

    /// <summary>The default port, matching the scripts and the console host.</summary>
    public const int DefaultPort = 6310;

    public bool IsRunning => _server is not null;

    /// <summary>Where a captured job landed. Raised on a background thread.</summary>
    public event EventHandler<string>? JobCaptured;

    /// <summary>
    /// Starts listening. Throws if the port is taken — usually because the
    /// console host is already running, which is worth telling the user rather
    /// than silently doing nothing.
    /// </summary>
    public void Start(int port = DefaultPort)
    {
        if (_server is not null) return;

        var server = new IppPrinterServer(new IppPrinterOptions { Port = port });
        server.JobCaptured += (_, job) => Save(job);
        server.Start();

        _server = server;
        Port = port;
    }

    public void Stop()
    {
        var server = _server;
        _server = null;
        if (server is null) return;

        try
        {
            server.StopAsync().GetAwaiter().GetResult();
        }
        catch (Exception)
        {
            // Shutting down a listener that is already gone is not a problem
            // worth surfacing.
        }
        server.Dispose();
    }

    private void Save(CapturedJob job)
    {
        try
        {
            Directory.CreateDirectory(CaptureLocations.DefaultFolder);
            string extension = job.IsPdf ? "pdf" : "bin";
            string path = Path.Combine(CaptureLocations.DefaultFolder, $"job-{job.JobId:D4}.{extension}");

            // A job id repeats after a restart, so never overwrite one.
            int suffix = 1;
            while (File.Exists(path))
            {
                path = Path.Combine(CaptureLocations.DefaultFolder,
                                    $"job-{job.JobId:D4}-{suffix++}.{extension}");
            }

            File.WriteAllBytes(path, job.Data);
            JobCaptured?.Invoke(this, path);
        }
        catch (Exception)
        {
            // Losing one job must not take the service down; the print queue
            // has already been told the job succeeded.
        }
    }

    public void Dispose() => Stop();
}
