using System.Globalization;
using OpenLeanPrint.Capture;
using OpenLeanPrint.Capture.Server;

// Simple argument parsing: --port N, --name NAME, --out DIR.
string printerName = "OpenLeanPrint";
int port = 6310;
string outDir = Path.Combine(Directory.GetCurrentDirectory(), "captured");

for (int i = 0; i < args.Length - 1; i++)
{
    switch (args[i])
    {
        case "--port" when int.TryParse(args[i + 1], out var p): port = p; break;
        case "--name": printerName = args[i + 1]; break;
        case "--out": outDir = args[i + 1]; break;
    }
}

Directory.CreateDirectory(outDir);

var options = new IppPrinterOptions { PrinterName = printerName, Port = port };
using var server = new IppPrinterServer(options);

server.JobCaptured += (_, job) =>
{
    string stamp = DateTime.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
    Console.WriteLine();
    Console.WriteLine($"[{stamp}] Captured job #{job.JobId}");
    Console.WriteLine($"    name   : {job.JobName ?? "(none)"}");
    Console.WriteLine($"    user   : {job.UserName ?? "(none)"}");
    Console.WriteLine($"    format : {job.DocumentFormat}");
    Console.WriteLine($"    bytes  : {job.Data.Length:N0}");

    if (job.Document is { } doc)
    {
        Console.WriteLine($"    pages  : {doc.PageCount}");
        for (int i = 0; i < doc.PageCount; i++)
        {
            var s = doc.PageSizes[i];
            Console.WriteLine($"      p{i + 1}: {s.Width:0.#} x {s.Height:0.#} pt");
        }
    }
    else if (job.ParseError is { } err)
    {
        Console.WriteLine($"    parse  : FAILED ({err})");
    }

    string ext = job.IsPdf ? "pdf" : "bin";
    string path = Path.Combine(outDir, $"job-{job.JobId:D4}.{ext}");
    File.WriteAllBytes(path, job.Data);
    Console.WriteLine($"    saved  : {path}");
};

server.Start();

Console.WriteLine("OpenLeanPrint capture host");
Console.WriteLine($"  printer name : {options.PrinterName}");
Console.WriteLine($"  printer URI  : {options.PrinterUri}");
Console.WriteLine($"  http prefix  : {options.HttpPrefix}");
Console.WriteLine($"  output dir   : {outDir}");
Console.WriteLine();
Console.WriteLine("On Windows: register the printer (scripts/Register-Printer.ps1),");
Console.WriteLine("then print to it. Press Ctrl+C to stop.");

var stop = new ManualResetEventSlim(false);
Console.CancelKeyPress += (_, e) => { e.Cancel = true; stop.Set(); };
stop.Wait();

Console.WriteLine("Stopping...");
await server.StopAsync();
