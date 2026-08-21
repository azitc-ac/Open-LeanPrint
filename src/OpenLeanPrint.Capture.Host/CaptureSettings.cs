using System.Globalization;
using OpenLeanPrint.Capture;

namespace OpenLeanPrint.Capture.Host;

/// <summary>What the capture host was asked to do.</summary>
public sealed record CaptureSettings
{
    /// <summary>The name the Windows service is registered under.</summary>
    public const string ServiceName = "OpenLeanPrintCapture";

    public string PrinterName { get; init; } = "OpenLeanPrint";
    public int Port { get; init; } = 6310;
    public required string OutputFolder { get; init; }

    /// <summary>
    /// How long a captured job is kept. It is a hand-over to the app, not an
    /// archive; a week is long enough to survive a holiday weekend and short
    /// enough that the folder does not quietly become a record of everything
    /// anyone ever printed. Zero keeps them for ever.
    /// </summary>
    public TimeSpan MaxAge { get; init; } = CapturedFolder.DefaultMaxAge;

    /// <summary>Size the folder may reach before the oldest jobs go. Zero means no limit.</summary>
    public long MaxBytes { get; init; } = CapturedFolder.DefaultMaxBytes;

    /// <summary>Where the service records what it did, next to the captured jobs.</summary>
    public string LogPath => Path.Combine(
        Directory.GetParent(OutputFolder)?.FullName ?? OutputFolder, "service.log");

    /// <summary>
    /// Reads <c>--port</c>, <c>--name</c>, <c>--out</c>, <c>--keep-days</c> and
    /// <c>--keep-mb</c>. Running as a service changes only the default output
    /// folder: LocalSystem's per-user folder is inside config\systemprofile,
    /// which no one can reach.
    /// </summary>
    public static CaptureSettings Parse(string[] args, bool asService)
    {
        string printerName = "OpenLeanPrint";
        int port = 6310;
        string? outputFolder = null;
        TimeSpan maxAge = CapturedFolder.DefaultMaxAge;
        long maxBytes = CapturedFolder.DefaultMaxBytes;

        for (int i = 0; i < args.Length - 1; i++)
        {
            switch (args[i])
            {
                case "--port" when int.TryParse(args[i + 1], out int parsed): port = parsed; break;
                case "--name": printerName = args[i + 1]; break;
                case "--out": outputFolder = args[i + 1]; break;
                case "--keep-days" when double.TryParse(args[i + 1], NumberStyles.Float,
                                                        CultureInfo.InvariantCulture, out double days):
                    maxAge = TimeSpan.FromDays(Math.Max(0, days));
                    break;
                case "--keep-mb" when long.TryParse(args[i + 1], NumberStyles.Integer,
                                                    CultureInfo.InvariantCulture, out long megabytes):
                    maxBytes = Math.Max(0, megabytes) * 1024 * 1024;
                    break;
            }
        }

        return new CaptureSettings
        {
            PrinterName = printerName,
            Port = port,
            OutputFolder = outputFolder
                ?? (asService ? CaptureLocations.SharedFolder : CaptureLocations.DefaultFolder),
            MaxAge = maxAge,
            MaxBytes = maxBytes,
        };
    }
}
