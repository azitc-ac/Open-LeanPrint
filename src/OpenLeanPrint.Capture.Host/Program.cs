using System.Globalization;
using System.Runtime.Versioning;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.WindowsServices;
using OpenLeanPrint.Capture;
using OpenLeanPrint.Capture.Host;
using OpenLeanPrint.Capture.Server;

// Two ways to run: as a console host for trying things out and for development,
// or as a Windows service so the printer works whether or not anybody is logged
// in. A printer that only accepts jobs while a user application happens to be
// running is not really a printer.
bool asService = args.Contains("--service", StringComparer.OrdinalIgnoreCase) ||
                 (OperatingSystem.IsWindows() && WindowsServiceHelpers.IsWindowsService());

var settings = CaptureSettings.Parse(args, asService);

// The guard is spelled out here rather than hidden in the flag above, because
// that is what lets the platform analyser prove the Windows-only call is safe.
if (asService && OperatingSystem.IsWindows())
{
    RunService(settings);
    return;
}

RunConsole(settings);

// ---------------------------------------------------------------------------

static void RunConsole(CaptureSettings settings)
{
    Directory.CreateDirectory(settings.OutputFolder);

    var options = new IppPrinterOptions { PrinterName = settings.PrinterName, Port = settings.Port };
    using var server = new IppPrinterServer(options);

    server.RequestLog += (_, line) =>
        Console.WriteLine($"[{DateTime.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture)}] {line}");

    server.JobCaptured += (_, job) =>
    {
        Console.WriteLine();
        Console.WriteLine($"[{DateTime.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture)}] Captured job #{job.JobId}");
        Console.WriteLine($"    name   : {job.JobName ?? "(none)"}");
        Console.WriteLine($"    user   : {job.UserName ?? "(none)"}");
        Console.WriteLine($"    format : {job.DocumentFormat}");
        Console.WriteLine($"    bytes  : {job.Data.Length:N0}");

        if (job.Document is { } doc)
        {
            Console.WriteLine($"    pages  : {doc.PageCount}");
            for (int i = 0; i < doc.PageCount; i++)
            {
                var size = doc.PageSizes[i];
                Console.WriteLine($"      p{i + 1}: {size.Width:0.#} x {size.Height:0.#} pt");
            }
        }
        else if (job.ParseError is { } error)
        {
            Console.WriteLine($"    parse  : FAILED ({error})");
        }

        Console.WriteLine($"    saved  : {CapturedJobWriter.Save(job, settings.OutputFolder)}");
    };

    server.Start();

    Console.WriteLine("OpenLeanPrint capture host");
    Console.WriteLine($"  printer name : {options.PrinterName}");
    Console.WriteLine($"  printer URI  : {options.PrinterUri}");
    Console.WriteLine($"  http prefix  : {options.HttpPrefix}");
    Console.WriteLine($"  output dir   : {settings.OutputFolder}");
    Console.WriteLine();
    Console.WriteLine("On Windows: register the printer (scripts/Register-Printer.ps1),");
    Console.WriteLine("then print to it. Press Ctrl+C to stop.");

    var stop = new ManualResetEventSlim(false);
    Console.CancelKeyPress += (_, e) => { e.Cancel = true; stop.Set(); };
    stop.Wait();

    Console.WriteLine("Stopping...");
    server.StopAsync().GetAwaiter().GetResult();
}

[SupportedOSPlatform("windows")]
static void RunService(CaptureSettings settings)
{
    var builder = Host.CreateApplicationBuilder();
    builder.Services.AddSingleton(settings);
    builder.Services.AddHostedService<CaptureServiceWorker>();
    builder.Services.AddWindowsService(options => options.ServiceName = CaptureSettings.ServiceName);

    builder.Build().Run();
}
