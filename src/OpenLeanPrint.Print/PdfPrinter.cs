using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Printing;
using System.Runtime.Versioning;

namespace OpenLeanPrint.Print;

/// <summary>
/// Sends an imposed PDF to a Windows printer: each sheet is rasterised with
/// PDFium and drawn onto the printer's <see cref="Graphics"/>. Works with any
/// installed printer/driver and needs no external tools.
/// <para>
/// Windows-only (the print spooler and GDI+ are), hence the platform
/// annotation — callers must guard with <see cref="OperatingSystem.IsWindows"/>.
/// </para>
/// </summary>
[SupportedOSPlatform("windows")]
public static class PdfPrinter
{
    /// <summary>Installed printer names, sorted for display.</summary>
    public static IReadOnlyList<string> InstalledPrinters()
    {
        var names = new List<string>();
        foreach (string? name in PrinterSettings.InstalledPrinters)
            if (!string.IsNullOrWhiteSpace(name)) names.Add(name);
        names.Sort(StringComparer.CurrentCultureIgnoreCase);
        return names;
    }

    /// <summary>The Windows default printer, or null if there is none.</summary>
    public static string? DefaultPrinter()
    {
        var settings = new PrinterSettings(); // defaults to the system default printer
        return settings.IsValid ? settings.PrinterName : null;
    }

    /// <summary>
    /// Prints every page of <paramref name="imposedPdf"/> to
    /// <paramref name="printerName"/> at its true size.
    /// </summary>
    /// <exception cref="ArgumentException">The PDF has no pages, or the printer is unknown.</exception>
    public static PrintReport Print(byte[] imposedPdf, string printerName, PrintOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(imposedPdf);
        ArgumentException.ThrowIfNullOrWhiteSpace(printerName);
        var opts = options ?? new PrintOptions();
        ArgumentOutOfRangeException.ThrowIfLessThan(opts.Copies, 1, nameof(options));
        ArgumentOutOfRangeException.ThrowIfGreaterThan(opts.Copies, short.MaxValue, nameof(options));
        ArgumentOutOfRangeException.ThrowIfLessThan(opts.Dpi, 36, nameof(options));
        ArgumentOutOfRangeException.ThrowIfGreaterThan(opts.Dpi, 1200, nameof(options));

        var sheets = PdfRasterizer.PageSizes(imposedPdf);
        if (sheets.Count == 0)
            throw new ArgumentException("The PDF contains no pages.", nameof(imposedPdf));

        using var doc = new PrintDocument();
        doc.PrinterSettings.PrinterName = printerName;
        if (!doc.PrinterSettings.IsValid)
            throw new ArgumentException(
                $"Printer '{printerName}' is not installed. Installed: {string.Join(", ", InstalledPrinters())}.",
                nameof(printerName));

        doc.DocumentName = opts.JobName ?? "OpenLeanPrint";
        doc.PrinterSettings.Copies = (short)opts.Copies;
        // StandardPrintController prints without any progress dialog, so this
        // works from a console app or a service.
        doc.PrintController = new StandardPrintController();
        // The imposed sheet already *is* the page layout: no margins, and we map
        // it onto the physical page ourselves.
        doc.OriginAtMargins = false;
        doc.DefaultPageSettings.Margins = new Margins(0, 0, 0, 0);

        string? outputFile = null;
        if (!string.IsNullOrWhiteSpace(opts.OutputFile))
        {
            outputFile = Path.GetFullPath(opts.OutputFile);
            string? directory = Path.GetDirectoryName(outputFile);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            doc.PrinterSettings.PrintToFile = true;
            doc.PrinterSettings.PrintFileName = outputFile;
        }

        var candidates = new List<PaperCandidate>();
        var paperSizes = new List<PaperSize>();
        foreach (PaperSize paper in doc.PrinterSettings.PaperSizes)
        {
            paperSizes.Add(paper);
            candidates.Add(new PaperCandidate(paper.PaperName, paper.Width, paper.Height));
        }

        var resolvedPaper = new string[sheets.Count];
        int index = 0;

        doc.QueryPageSettings += (_, e) =>
        {
            var sheet = sheets[Math.Min(index, sheets.Count - 1)];
            // Orientation is a page setting; paper sizes are always compared in
            // portrait terms.
            e.PageSettings.Landscape = sheet.Width > sheet.Height;
            int match = PaperMatch.BestIndex(candidates, sheet.Width, sheet.Height);
            if (match >= 0) e.PageSettings.PaperSize = paperSizes[match];
            e.PageSettings.Margins = new Margins(0, 0, 0, 0);
            resolvedPaper[Math.Min(index, sheets.Count - 1)] = e.PageSettings.PaperSize.PaperName;
        };

        doc.PrintPage += (_, e) =>
        {
            var graphics = e.Graphics
                ?? throw new InvalidOperationException("The printer provided no drawing surface.");
            graphics.PageUnit = GraphicsUnit.Display; // for printers: 1/100 inch
            graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;

            using var bitmap = PdfRasterizer.RenderPage(imposedPdf, index, opts.Dpi);

            // PageBounds is the whole physical sheet, in the same 1/100 inch unit
            // as the graphics. Filling it (rather than MarginBounds) is what makes
            // the print WYSIWYG; the printer's non-printable border clips the very
            // edges, which is unavoidable on hardware that cannot print full bleed.
            var page = e.PageBounds;
            var fit = PagePlacement.Fit(bitmap.Width, bitmap.Height, page.Width, page.Height);
            // With OriginAtMargins = false the origin sits at the top-left of the
            // printable area, so shift by the hardware margin to line the sheet up
            // with the paper edge.
            float offsetX = -e.PageSettings.HardMarginX;
            float offsetY = -e.PageSettings.HardMarginY;
            graphics.DrawImage(bitmap, new RectangleF(
                (float)fit.X + offsetX, (float)fit.Y + offsetY, (float)fit.Width, (float)fit.Height));

            index++;
            e.HasMorePages = index < sheets.Count;
        };

        doc.Print();

        return new PrintReport
        {
            PrinterName = doc.PrinterSettings.PrinterName,
            Sheets = sheets.Count,
            Dpi = opts.Dpi,
            Copies = opts.Copies,
            PaperNames = resolvedPaper.Where(p => !string.IsNullOrEmpty(p)).Distinct().ToList(),
            OutputFile = outputFile,
        };
    }
}
