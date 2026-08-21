using OpenLeanPrint.Capture;
using OpenLeanPrint.Cli;
using OpenLeanPrint.Compose;
using OpenLeanPrint.Core;
using OpenLeanPrint.Core.Imposition;
using OpenLeanPrint.Print;

if (args.Length == 0)
{
    PrintUsage();
    return 1;
}

try
{
    switch (args[0].ToLowerInvariant())
    {
        case "impose": return Impose(args[1..]);
        case "sample": return Sample(args[1..]);
        case "print": return PrintPdf(args[1..]);
        case "list-printers" or "printers": return ListPrinters();
        case "watch": return Watch(args[1..]);
        case "-h" or "--help" or "help": PrintUsage(); return 0;
        default:
            Console.Error.WriteLine($"Unknown command '{args[0]}'.");
            PrintUsage();
            return 1;
    }
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Error: {ex.Message}");
    return 1;
}

static int Impose(string[] a)
{
    if (a.Length < 2)
    {
        Console.Error.WriteLine("Usage: openleanprint impose <in.pdf> <out.pdf> [--nup RxC] [--paper A4] [--booklet] [--margin MM] [--gutter PT] [--pages 1-4,7]");
        return 1;
    }

    string input = a[0];
    string output = a[1];
    var settings = ImposeRunner.Parse(ArgMap.Parse(a[2..]));

    byte[] outPdf = ImposeRunner.Run(File.ReadAllBytes(input), settings);
    File.WriteAllBytes(output, outPdf);

    Console.WriteLine($"{settings.Describe()}: {input} -> {output}");
    Console.WriteLine($"Wrote {outPdf.Length:N0} bytes.");
    return 0;
}

static int Sample(string[] a)
{
    if (a.Length < 1)
    {
        Console.Error.WriteLine("Usage: openleanprint sample <out.pdf> [--pages N]");
        return 1;
    }
    string output = a[0];
    int pages = (int)ArgMap.Parse(a[1..]).GetDouble("pages", 8);
    byte[] pdf = SamplePdf.Colored(pages);
    File.WriteAllBytes(output, pdf);
    Console.WriteLine($"Wrote {pages}-page sample to {output} ({pdf.Length:N0} bytes).");
    return 0;
}

static int PrintPdf(string[] a)
{
    if (a.Length < 1)
    {
        Console.Error.WriteLine("Usage: openleanprint print <in.pdf> [--printer NAME] [--out FILE] [--copies N] [--dpi N]");
        return 1;
    }
    if (!OperatingSystem.IsWindows())
    {
        Console.Error.WriteLine("'print' needs Windows - it prints through the Windows spooler.");
        return 1;
    }

    string input = a[0];
    if (!File.Exists(input))
    {
        Console.Error.WriteLine($"File not found: {input}");
        return 1;
    }

    var opts = ArgMap.Parse(a[1..]);
    string? printer = opts.Has("printer") ? opts.Get("printer", string.Empty) : PdfPrinter.DefaultPrinter();
    if (string.IsNullOrWhiteSpace(printer))
    {
        Console.Error.WriteLine("No --printer given and no Windows default printer is set. See 'list-printers'.");
        return 1;
    }

    string? outputFile = opts.Has("out") ? opts.Get("out", string.Empty) : null;
    if (!DuplexModes.TryParse(opts.Get("duplex", "auto"), out var duplex))
    {
        Console.Error.WriteLine($"Unknown --duplex value '{opts.Get("duplex", string.Empty)}'. " +
                                "Use off, long, short or auto.");
        return 1;
    }

    var settings = new PrintOptions
    {
        Copies = (int)opts.GetDouble("copies", 1),
        Dpi = (int)opts.GetDouble("dpi", 200),
        OutputFile = outputFile,
        Duplex = duplex,
        JobName = $"OpenLeanPrint - {Path.GetFileName(input)}",
    };

    byte[] pdf = File.ReadAllBytes(input);
    // Note the time before spooling so a stale file at --out cannot be mistaken
    // for this job's output.
    DateTime startedUtc = DateTime.UtcNow;
    var report = PdfPrinter.Print(pdf, printer, settings);

    string paper = report.PaperNames.Count > 0 ? string.Join("/", report.PaperNames) : "driver default";
    string copies = report.Copies == 1 ? "1 copy" : $"{report.Copies} copies";
    string sides = report.Duplex switch
    {
        DuplexMode.Simplex => ", single-sided",
        DuplexMode.LongEdge => ", double-sided (long edge)",
        DuplexMode.ShortEdge => ", double-sided (short edge)",
        _ => string.Empty,
    };
    Console.WriteLine($"Sent {report.Sheets} sheet(s) of {input} to \"{report.PrinterName}\" " +
                      $"({paper}, {report.Dpi} dpi, {copies}{sides}).");
    if (report.DuplexUnsupported)
        Console.WriteLine("Note: this printer reports no duplex support, so it printed single-sided.");

    if (report.OutputFile is not null)
    {
        long bytes = WaitForSpooledFile(report.OutputFile, startedUtc, TimeSpan.FromSeconds(60));
        if (bytes > 0)
            Console.WriteLine($"Wrote {report.OutputFile} ({bytes:N0} bytes).");
        else
            Console.WriteLine($"Job spooled, but {report.OutputFile} has not appeared yet - check the print queue.");
    }
    return 0;
}

// Waits for a print-to-file job to land on disk. The spooler writes it
// asynchronously, so the file usually does not exist yet when Print() returns.
// Returns the final size, or 0 if it never appeared.
static long WaitForSpooledFile(string path, DateTime notBeforeUtc, TimeSpan timeout)
{
    DateTime deadline = DateTime.UtcNow + timeout;
    long lastSize = -1;
    while (DateTime.UtcNow < deadline)
    {
        var info = new FileInfo(path);
        // Two seconds of slack: file timestamps are coarser than DateTime.
        if (info.Exists && info.Length > 0 && info.LastWriteTimeUtc >= notBeforeUtc.AddSeconds(-2))
        {
            if (info.Length == lastSize) return info.Length; // stopped growing
            lastSize = info.Length;
        }
        Thread.Sleep(250);
    }
    return lastSize > 0 ? lastSize : 0;
}

static int ListPrinters()
{
    if (!OperatingSystem.IsWindows())
    {
        Console.Error.WriteLine("'list-printers' needs Windows.");
        return 1;
    }

    string? defaultPrinter = PdfPrinter.DefaultPrinter();
    var printers = PdfPrinter.InstalledPrinters();
    if (printers.Count == 0)
    {
        Console.WriteLine("No printers installed.");
        return 0;
    }

    Console.WriteLine("Installed printers (* = default):");
    foreach (string name in printers)
        Console.WriteLine($"  {(name == defaultPrinter ? "*" : " ")} {name}");
    return 0;
}

static int Watch(string[] a)
{
    // With no folder given, watch where the capture host writes by default.
    bool hasFolder = a.Length > 0 && !a[0].StartsWith("--", StringComparison.Ordinal);
    string folder = hasFolder ? a[0] : CaptureLocations.DefaultFolder;
    if (!Directory.Exists(folder))
    {
        if (hasFolder)
        {
            Console.Error.WriteLine($"Folder not found: {folder}");
            return 1;
        }
        Directory.CreateDirectory(folder); // the host would create it too
    }

    var opts = ArgMap.Parse(hasFolder ? a[1..] : a);
    var impose = ImposeRunner.Parse(opts);
    string? printer = opts.Has("printer") ? opts.Get("printer", string.Empty) : null;
    if (printer is not null && !OperatingSystem.IsWindows())
    {
        Console.Error.WriteLine("--printer needs Windows; without it the watcher only writes imposed PDFs.");
        return 1;
    }

    string outDir = opts.Get("out-dir", Path.Combine(folder, "imposed"));
    int dpi = (int)opts.GetDouble("dpi", 200);
    if (!DuplexModes.TryParse(opts.Get("duplex", "auto"), out var watchDuplex))
    {
        Console.Error.WriteLine($"Unknown --duplex value '{opts.Get("duplex", string.Empty)}'. " +
                                "Use off, long, short or auto.");
        return 1;
    }

    using var watcher = new JobWatcher(folder, outDir, impose, printer, dpi, watchDuplex);
    using var cancellation = new CancellationTokenSource();
    Console.CancelKeyPress += (_, e) =>
    {
        e.Cancel = true; // stop cleanly instead of killing the process
        cancellation.Cancel();
    };

    Console.WriteLine($"Watching {Path.GetFullPath(folder)} for new PDFs");
    Console.WriteLine($"  layout: {impose.Describe()}");
    Console.WriteLine($"  output: {Path.GetFullPath(outDir)}");
    if (printer is not null)
    {
        Console.WriteLine($"  printing to: \"{printer}\" at {dpi} dpi" +
                          (watchDuplex == DuplexMode.Default ? string.Empty : $", duplex {watchDuplex}"));
    }
    Console.WriteLine("Press Ctrl+C to stop.");

    watcher.Run(opts.Has("existing"), cancellation.Token);
    Console.WriteLine("Stopped.");
    return 0;
}

static void PrintUsage()
{
    Console.WriteLine("OpenLeanPrint CLI");
    Console.WriteLine();
    Console.WriteLine("  impose <in.pdf> <out.pdf> [options]   Impose a PDF N-up or as a booklet");
    Console.WriteLine("     --nup RxC     grid, e.g. 2x2 (or a count: 2, 4, 9)   default 2x2");
    Console.WriteLine("     --paper NAME  A4 | A5 | A3 | Letter | Legal | Tabloid  default A4");
    Console.WriteLine("     --booklet     saddle-stitch booklet (overrides --nup)");
    Console.WriteLine("     --margin MM   outer margin in millimetres            default 0");
    Console.WriteLine("     --gutter PT   spacing between cells in points        default 0");
    Console.WriteLine("     --pages LIST  which source pages to keep, e.g. 1-4,7  default all");
    Console.WriteLine("     --rotate DEG  turn every page 90 | 180 | 270          default 0");
    Console.WriteLine("     --border                draw a thin frame around every page");
    Console.WriteLine("     --border-width PT       line width, implies --border      default 0.75");
    Console.WriteLine("     --border-color HEX      e.g. #202020                      default #9A9AA2");
    Console.WriteLine("     --watermark TEXT        draw TEXT across every sheet");
    Console.WriteLine("     --watermark-opacity N   0..1                          default 0.18");
    Console.WriteLine("     --watermark-color HEX   e.g. #C00000                  default #808080");
    Console.WriteLine("     --watermark-size PT     0 = fit to the sheet          default 0");
    Console.WriteLine();
    Console.WriteLine("  sample <out.pdf> [--pages N]           Write a colored sample PDF to try");
    Console.WriteLine();
    Console.WriteLine("  print <in.pdf> [options]               Print a PDF to a Windows printer (Windows only)");
    Console.WriteLine("     --printer NAME  target printer                       default: Windows default");
    Console.WriteLine("     --out FILE      write to a file instead of paper (for \"Microsoft Print to PDF\")");
    Console.WriteLine("     --copies N      copies to request from the driver     default 1");
    Console.WriteLine("     --duplex MODE   off | long | short | auto             default auto");
    Console.WriteLine("     --dpi N         rasterisation resolution              default 200");
    Console.WriteLine();
    Console.WriteLine("  list-printers                          List installed printers (Windows only)");
    Console.WriteLine();
    Console.WriteLine("  watch [folder] [options]               Impose (and optionally print) every new PDF in a folder");
    Console.WriteLine("                                         default folder: where the capture host writes");
    Console.WriteLine("     --printer NAME  also print each imposed result      default: only write files");
    Console.WriteLine("     --out-dir DIR   where imposed PDFs go               default <folder>/imposed");
    Console.WriteLine("     --existing      also process PDFs already in there  default: only new ones");
    Console.WriteLine("     --duplex MODE   off | long | short | auto             default auto");
    Console.WriteLine("     plus the layout options of 'impose' (--nup, --booklet, --paper, --margin, --gutter)");
    Console.WriteLine();
    Console.WriteLine("Examples:");
    Console.WriteLine("  openleanprint sample sample.pdf --pages 8");
    Console.WriteLine("  openleanprint impose captured/job-0002.pdf out-4up.pdf --nup 2x2 --paper A4 --margin 8 --gutter 6");
    Console.WriteLine("  openleanprint impose report.pdf booklet.pdf --booklet --paper A4");
    Console.WriteLine("  openleanprint print out-4up.pdf --printer \"Microsoft Print to PDF\" --out proof.pdf");
    Console.WriteLine("  openleanprint watch captured --nup 2x2 --paper A4 --margin 8 --printer \"Brother MFC-9332CDW Printer\"");
}
