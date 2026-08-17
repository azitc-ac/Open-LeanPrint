using OpenLeanPrint.Core;
using OpenLeanPrint.Core.Imposition;
using PdfSharpCore.Pdf.IO;
using Xunit;

namespace OpenLeanPrint.Compose.Tests;

public class PdfImposerTests
{
    private static (int PageCount, double W, double H) Inspect(byte[] pdf)
    {
        using var doc = PdfReader.Open(new MemoryStream(pdf), PdfDocumentOpenMode.InformationOnly);
        var first = doc.Pages[0];
        return (doc.PageCount, first.Width.Point, first.Height.Point);
    }

    [Fact]
    public void ReadPageSizes_ReturnsOnePagePerSourcePage()
    {
        var pdf = TestPdfs.Colored(3);
        var pages = PdfImposer.ReadPageSizes(pdf);
        Assert.Equal(3, pages.Count);
        Assert.All(pages, p => Assert.True(p.Size.Width > 0 && p.Size.Height > 0));
    }

    [Fact]
    public void FourUp_SixPages_ProducesTwoA4Sheets()
    {
        var pdf = TestPdfs.Colored(6);
        var settings = ImpositionSettings.NUp(2, 2) with
        {
            SheetSize = PaperSizes.A4,
            Margins = PtMargins.UniformMm(8),
            GutterX = 6,
            GutterY = 6,
        };

        var outPdf = new PdfImposer().ImposeToPdf(pdf, settings);

        var (count, w, h) = Inspect(outPdf);
        Assert.Equal(2, count); // 6 pages / 4-up = 2 sheets
        Assert.Equal(PaperSizes.A4.Width, w, 1);
        Assert.Equal(PaperSizes.A4.Height, h, 1);
        Assert.True(outPdf.Length > 1000); // produced real content
    }

    [Fact]
    public void TwoUp_ProducesValidPdfWithExpectedSheetCount()
    {
        var pdf = TestPdfs.Colored(4);
        var settings = ImpositionSettings.NUp(1, 2) with { SheetSize = PaperSizes.A4 };

        var outPdf = new PdfImposer().ImposeToPdf(pdf, settings);

        var (count, _, _) = Inspect(outPdf);
        Assert.Equal(2, count); // 4 pages / 2-up = 2 sheets
    }

    [Fact]
    public void Booklet_EightPages_ProducesFourLandscapeSheets()
    {
        var pdf = TestPdfs.Colored(8);
        var outPdf = new PdfImposer().ImposeBookletToPdf(pdf, PaperSizes.A4);

        var (count, w, h) = Inspect(outPdf);
        Assert.Equal(4, count);     // 8 pages, 2-up, double-sided => 4 sheet sides
        Assert.True(w > h);         // landscape
    }

    [Fact]
    public void OneUp_KeepsPageCount()
    {
        var pdf = TestPdfs.Colored(5);
        var settings = ImpositionSettings.NUp(1, 1) with { SheetSize = PaperSizes.A4 };
        var outPdf = new PdfImposer().ImposeToPdf(pdf, settings);
        Assert.Equal(5, Inspect(outPdf).PageCount);
    }
}
