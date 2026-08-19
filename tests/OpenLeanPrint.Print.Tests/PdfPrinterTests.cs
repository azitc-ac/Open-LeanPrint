using System.Runtime.Versioning;
using PdfSharpCore.Pdf;
using Xunit;

namespace OpenLeanPrint.Print.Tests;

/// <summary>
/// Printer-facing tests. These deliberately do not spool a real job - that is
/// verified by hand against "Microsoft Print to PDF" (see docs/M3-PRINT.md), so
/// the suite stays fast and free of side effects.
/// </summary>
public class PdfPrinterTests
{
    private static byte[] OnePagePdf()
    {
        using var doc = new PdfDocument();
        doc.AddPage();
        using var ms = new MemoryStream();
        doc.Save(ms, false);
        return ms.ToArray();
    }

    [SupportedOSPlatform("windows")]
    [WindowsFact]
    public void InstalledPrinters_ContainsTheDefaultPrinter()
    {
        var printers = PdfPrinter.InstalledPrinters();
        string? defaultPrinter = PdfPrinter.DefaultPrinter();

        // A machine can legitimately have no printers at all; only the
        // relationship between the two calls has to hold.
        if (defaultPrinter is not null) Assert.Contains(defaultPrinter, printers);
    }

    [SupportedOSPlatform("windows")]
    [WindowsFact]
    public void Print_RejectsAnUnknownPrinter()
    {
        var ex = Assert.Throws<ArgumentException>(() =>
            PdfPrinter.Print(OnePagePdf(), "No Such Printer 4711"));

        Assert.Contains("not installed", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(null, DuplexMode.Default)]
    [InlineData("", DuplexMode.Default)]
    [InlineData("auto", DuplexMode.Default)]
    [InlineData("off", DuplexMode.Simplex)]
    [InlineData("none", DuplexMode.Simplex)]
    [InlineData("long", DuplexMode.LongEdge)]
    [InlineData("LONG-EDGE", DuplexMode.LongEdge)]
    [InlineData("book", DuplexMode.LongEdge)]
    [InlineData("short", DuplexMode.ShortEdge)]
    [InlineData(" booklet ", DuplexMode.ShortEdge)]
    public void DuplexModes_ParsesWhatPeopleType(string? text, DuplexMode expected)
    {
        Assert.True(DuplexModes.TryParse(text, out var mode));
        Assert.Equal(expected, mode);
    }

    [Fact]
    public void DuplexModes_RejectsNonsense()
    {
        Assert.False(DuplexModes.TryParse("sideways", out var mode));
        Assert.Equal(DuplexMode.Default, mode);
    }

    [Fact]
    public void DuplexModes_BookletsWantTheShortEdge()
    {
        // A booklet's sheets are landscape: flipping the long edge would print
        // every second side upside down.
        Assert.Equal(DuplexMode.ShortEdge, DuplexModes.PreferredFor(booklet: true));
        Assert.Equal(DuplexMode.LongEdge, DuplexModes.PreferredFor(booklet: false));
    }

    [SupportedOSPlatform("windows")]
    [WindowsFact]
    public void Print_RejectsInvalidOptions()
    {
        byte[] pdf = OnePagePdf();
        string printer = PdfPrinter.DefaultPrinter() ?? "Microsoft Print to PDF";

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            PdfPrinter.Print(pdf, printer, new PrintOptions { Copies = 0 }));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            PdfPrinter.Print(pdf, printer, new PrintOptions { Dpi = 10 }));
    }
}
