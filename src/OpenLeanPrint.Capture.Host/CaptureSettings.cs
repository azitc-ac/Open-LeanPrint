namespace OpenLeanPrint.Capture.Host;

/// <summary>What the capture host was asked to do.</summary>
public sealed record CaptureSettings
{
    /// <summary>The name the Windows service is registered under.</summary>
    public const string ServiceName = "OpenLeanPrintCapture";

    public string PrinterName { get; init; } = "OpenLeanPrint";
    public int Port { get; init; } = 6310;
    public required string OutputFolder { get; init; }

    /// <summary>Where the service records what it did, next to the captured jobs.</summary>
    public string LogPath => Path.Combine(
        Directory.GetParent(OutputFolder)?.FullName ?? OutputFolder, "service.log");

    /// <summary>
    /// Reads <c>--port</c>, <c>--name</c> and <c>--out</c>. Running as a service
    /// changes only the default output folder: LocalSystem's per-user folder is
    /// inside config\systemprofile, which no one can reach.
    /// </summary>
    public static CaptureSettings Parse(string[] args, bool asService)
    {
        string printerName = "OpenLeanPrint";
        int port = 6310;
        string? outputFolder = null;

        for (int i = 0; i < args.Length - 1; i++)
        {
            switch (args[i])
            {
                case "--port" when int.TryParse(args[i + 1], out int parsed): port = parsed; break;
                case "--name": printerName = args[i + 1]; break;
                case "--out": outputFolder = args[i + 1]; break;
            }
        }

        return new CaptureSettings
        {
            PrinterName = printerName,
            Port = port,
            OutputFolder = outputFolder
                ?? (asService ? CaptureLocations.SharedFolder : CaptureLocations.DefaultFolder),
        };
    }
}
