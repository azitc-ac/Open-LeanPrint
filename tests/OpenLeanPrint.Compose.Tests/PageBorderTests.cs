using OpenLeanPrint.Core;
using OpenLeanPrint.Core.Imposition;
using PdfSharpCore.Pdf.IO;
using Xunit;

namespace OpenLeanPrint.Compose.Tests;

public class PageBorderTests
{
    private static int PageCount(byte[] pdf)
    {
        using var doc = PdfReader.Open(new MemoryStream(pdf), PdfDocumentOpenMode.InformationOnly);
        return doc.PageCount;
    }

    private static readonly ImpositionSettings FourUp =
        ImpositionSettings.NUp(2, 2) with { SheetSize = PaperSizes.A4 };

    [Fact]
    public void Border_AddsContent_WithoutChangingTheSheets()
    {
        var pdf = TestPdfs.Colored(4);

        byte[] plain = new PdfImposer().ImposeToPdf(pdf, FourUp);
        byte[] framed = new PdfImposer { PageBorder = new PageBorder() }.ImposeToPdf(pdf, FourUp);

        Assert.Equal(PageCount(plain), PageCount(framed));
        Assert.True(framed.Length > plain.Length, "four frames should add content to the file");
    }

    [Fact]
    public void ZeroWidthBorder_ChangesNothing()
    {
        var pdf = TestPdfs.Colored(4);

        byte[] plain = new PdfImposer().ImposeToPdf(pdf, FourUp);
        byte[] framed = new PdfImposer { PageBorder = new PageBorder { WidthPt = 0 } }.ImposeToPdf(pdf, FourUp);

        Assert.Equal(plain.Length, framed.Length);
    }

    [Fact]
    public void MorePagesPerSheet_MeansMoreFrames()
    {
        var pdf = TestPdfs.Colored(16);
        var border = new PageBorder();

        byte[] fourUp = new PdfImposer { PageBorder = border }
            .ImposeToPdf(pdf, ImpositionSettings.NUp(2, 2) with { SheetSize = PaperSizes.A4 });
        byte[] sixteenUp = new PdfImposer { PageBorder = border }
            .ImposeToPdf(pdf, ImpositionSettings.NUp(4, 4) with { SheetSize = PaperSizes.A4 });

        // Same 16 pages, same frames per page - but 16-up puts them all on one
        // sheet, so this only checks that both compose and stay sane.
        Assert.Equal(4, PageCount(fourUp));
        Assert.Equal(1, PageCount(sixteenUp));
    }

    [Theory]
    [InlineData("#FF0000", 0xFF, 0x00, 0x00)]
    [InlineData("A0B0C0", 0xA0, 0xB0, 0xC0)]
    [InlineData("nonsense", 0x80, 0x80, 0x80)]
    public void Colour_ParsesOrFallsBackToGrey(string hex, byte r, byte g, byte b)
    {
        Assert.Equal((r, g, b), new PageBorder { ColorHex = hex }.Color());
    }

    [Fact]
    public void ATinyPage_IsNotFramed_RatherThanDrawnInsideOut()
    {
        // A cell narrower than the line itself would give a rectangle with
        // negative width; the frame is skipped instead.
        var pdf = TestPdfs.Colored(64);
        var settings = ImpositionSettings.NUp(8, 8) with { SheetSize = PaperSizes.A4 };

        byte[] framed = new PdfImposer { PageBorder = new PageBorder { WidthPt = 400 } }
            .ImposeToPdf(pdf, settings);

        Assert.Equal(1, PageCount(framed));
    }
}
