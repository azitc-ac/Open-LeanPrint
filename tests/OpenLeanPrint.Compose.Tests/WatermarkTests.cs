using OpenLeanPrint.Core;
using OpenLeanPrint.Core.Imposition;
using PdfSharpCore.Pdf.IO;
using Xunit;

namespace OpenLeanPrint.Compose.Tests;

public class WatermarkTests
{
    private static int PageCount(byte[] pdf)
    {
        using var doc = PdfReader.Open(new MemoryStream(pdf), PdfDocumentOpenMode.InformationOnly);
        return doc.PageCount;
    }

    [Fact]
    public void Watermark_AddsContent_WithoutChangingTheSheets()
    {
        var pdf = TestPdfs.Colored(4);
        var settings = ImpositionSettings.NUp(2, 2) with { SheetSize = PaperSizes.A4 };

        byte[] plain = new PdfImposer().ImposeToPdf(pdf, settings);
        byte[] marked = new PdfImposer { Watermark = new Watermark { Text = "DRAFT" } }.ImposeToPdf(pdf, settings);

        Assert.Equal(PageCount(plain), PageCount(marked));
        Assert.True(marked.Length > plain.Length, "the watermark should add content to the file");
    }

    [Fact]
    public void EmptyWatermark_ChangesNothing()
    {
        var pdf = TestPdfs.Colored(2);
        var settings = ImpositionSettings.NUp(1, 2) with { SheetSize = PaperSizes.A4 };

        byte[] plain = new PdfImposer().ImposeToPdf(pdf, settings);
        byte[] blank = new PdfImposer { Watermark = new Watermark { Text = "   " } }.ImposeToPdf(pdf, settings);

        Assert.Equal(PageCount(plain), PageCount(blank));
    }

    [Theory]
    [InlineData("#FF0000", 0xFF, 0x00, 0x00)]
    [InlineData("00FF80", 0x00, 0xFF, 0x80)]
    [InlineData("nonsense", 0x80, 0x80, 0x80)] // falls back to grey
    [InlineData("#12345", 0x80, 0x80, 0x80)]   // wrong length
    public void Color_ParsesHexOrFallsBackToGrey(string hex, byte r, byte g, byte b)
    {
        var watermark = new Watermark { Text = "x", ColorHex = hex };

        Assert.Equal((r, g, b), watermark.Color());
    }

    [Fact]
    public void EffectiveFontSize_ScalesWithTheSheet_AndRespectsAnExplicitSize()
    {
        var watermark = new Watermark { Text = "DRAFT" };

        double a5 = watermark.EffectiveFontSize(PaperSizes.A5.Width, PaperSizes.A5.Height);
        double a3 = watermark.EffectiveFontSize(PaperSizes.A3.Width, PaperSizes.A3.Height);
        Assert.True(a3 > a5, "a bigger sheet deserves bigger text");

        var fixedSize = watermark with { FontSize = 42 };
        Assert.Equal(42, fixedSize.EffectiveFontSize(PaperSizes.A5.Width, PaperSizes.A5.Height));
    }

    [Fact]
    public void LongerText_GetsSmallerType()
    {
        var short_ = new Watermark { Text = "HI" };
        var long_ = new Watermark { Text = "STRICTLY CONFIDENTIAL" };

        Assert.True(long_.EffectiveFontSize(595, 842) < short_.EffectiveFontSize(595, 842));
    }
}
