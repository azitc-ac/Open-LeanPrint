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
        Console.Error.WriteLine("Usage: openleanprint impose <in.pdf> <out.pdf> [--nup RxC] [--paper A4] [--booklet] [--margin MM] [--gutter PT]");
        return 1;
    }

    string input = a[0];
    string output = a[1];
    var opts = ArgMap.Parse(a[2..]);

    var paper = PaperSizes.ByName(opts.Get("paper", "A4")) ?? PaperSizes.A4;
    double marginMm = opts.GetDouble("margin", 0);
    double gutter = opts.GetDouble("gutter", 0);
    byte[] src = File.ReadAllBytes(input);
    var imposer = new PdfImposer();

    byte[] outPdf;
    if (opts.Has("booklet"))
    {
        outPdf = imposer.ImposeBookletToPdf(src, paper, PtMargins.UniformMm(marginMm), gutter);
        Console.WriteLine($"Booklet: {input} -> {output} (sheet {opts.Get("paper", "A4")})");
    }
    else
    {
        var (rows, cols) = ParseNUp(opts.Get("nup", "2x2"));
        var settings = ImpositionSettings.NUp(rows, cols) with
        {
            SheetSize = paper,
            Margins = PtMargins.UniformMm(marginMm),
            GutterX = gutter,
            GutterY = gutter,
        };
        outPdf = imposer.ImposeToPdf(src, settings);
        Console.WriteLine($"{rows}x{cols}-up: {input} -> {output} (sheet {opts.Get("paper", "A4")})");
    }

    File.WriteAllBytes(output, outPdf);
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
    var settings = new PrintOptions
    {
        Copies = (int)opts.GetDouble("copies", 1),
        Dpi = (int)opts.GetDouble("dpi", 200),
        OutputFile = outputFile,
        JobName = $"OpenLeanPrint - {Path.GetFileName(input)}",
    };

    byte[] pdf = File.ReadAllBytes(input);
    // Note the time before spooling so a stale file at --out cannot be mistaken
    // for this job's output.
    DateTime startedUtc = DateTime.UtcNow;
    var report = PdfPrinter.Print(pdf, printer, settings);

    string paper = report.PaperNames.Count > 0 ? string.Join("/", report.PaperNames) : "driver default";
    string copies = report.Copies == 1 ? "1 copy" : $"{report.Copies} copies";
    Console.WriteLine($"Sent {report.Sheets} sheet(s) of {input} to \"{report.PrinterName}\" " +
                      $"({paper}, {report.Dpi} dpi, {copies}).");

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

static (int Rows, int Cols) ParseNUp(string s)
{
    var parts = s.ToLowerInvariant().Split('x', 'X');
    if (parts.Length == 2 && int.TryParse(parts[0], out int r) && int.TryParse(parts[1], out int c) && r > 0 && c > 0)
        return (r, c);
    // Allow a single number: 4 -> 2x2, 2 -> 1x2, 9 -> 3x3, 6 -> 2x3.
    if (int.TryParse(s, out int n) && n > 0)
        return n switch { 1 => (1, 1), 2 => (1, 2), 4 => (2, 2), 6 => (2, 3), 9 => (3, 3), 16 => (4, 4), _ => (1, n) };
    throw new ArgumentException($"Invalid --nup value '{s}'. Use RxC (e.g. 2x2) or a count (e.g. 4).");
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
    Console.WriteLine();
    Console.WriteLine("  sample <out.pdf> [--pages N]           Write a colored sample PDF to try");
    Console.WriteLine();
    Console.WriteLine("  print <in.pdf> [options]               Print a PDF to a Windows printer (Windows only)");
    Console.WriteLine("     --printer NAME  target printer                       default: Windows default");
    Console.WriteLine("     --out FILE      write to a file instead of paper (for \"Microsoft Print to PDF\")");
    Console.WriteLine("     --copies N      copies to request from the driver     default 1");
    Console.WriteLine("     --dpi N         rasterisation resolution              default 200");
    Console.WriteLine();
    Console.WriteLine("  list-printers                          List installed printers (Windows only)");
    Console.WriteLine();
    Console.WriteLine("Examples:");
    Console.WriteLine("  openleanprint sample sample.pdf --pages 8");
    Console.WriteLine("  openleanprint impose captured/job-0002.pdf out-4up.pdf --nup 2x2 --paper A4 --margin 8 --gutter 6");
    Console.WriteLine("  openleanprint impose report.pdf booklet.pdf --booklet --paper A4");
    Console.WriteLine("  openleanprint print out-4up.pdf --printer \"Microsoft Print to PDF\" --out proof.pdf");
}
