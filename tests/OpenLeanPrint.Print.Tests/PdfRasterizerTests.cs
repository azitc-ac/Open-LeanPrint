using System.Runtime.Versioning;
using PdfSharpCore.Drawing;
using PdfSharpCore.Pdf;
using Xunit;

namespace OpenLeanPrint.Print.Tests;

public class PdfRasterizerTests
{
    /// <summary>A minimal PDF with <paramref name="pages"/> A4 pages.</summary>
    private static byte[] A4Pdf(int pages = 1)
    {
        using var doc = new PdfDocument();
        for (int i = 0; i < pages; i++)
        {
            var page = doc.AddPage();
            page.Width = XUnit.FromPoint(595.276);
            page.Height = XUnit.FromPoint(841.89);
            using var gfx = XGraphics.FromPdfPage(page);
            gfx.DrawRectangle(new XSolidBrush(XColors.SteelBlue), 50, 50, 200, 200);
        }
        using var ms = new MemoryStream();
        doc.Save(ms, false);
        return ms.ToArray();
    }

    // PDFium runs on all three desktop platforms, so this test does too.
    [SupportedOSPlatform("windows")]
    [SupportedOSPlatform("linux")]
    [SupportedOSPlatform("macos")]
    [Fact]
    public void PageSizes_ReadsEveryPageInPoints()
    {
        var sizes = PdfRasterizer.PageSizes(A4Pdf(3));

        Assert.Equal(3, sizes.Count);
        Assert.All(sizes, size =>
        {
            Assert.Equal(595.276, size.Width, 1);
            Assert.Equal(841.89, size.Height, 1);
        });
    }

    [SupportedOSPlatform("windows")]
    [WindowsFact]
    public void RenderPage_ScalesTheSheetToTheRequestedDpi()
    {
        // A4 at 100 dpi: 595.276 pt / 72 * 100 = 827 px wide, 1169 px tall.
        using var bitmap = PdfRasterizer.RenderPage(A4Pdf(), pageIndex: 0, dpi: 100);

        Assert.InRange(bitmap.Width, 825, 829);
        Assert.InRange(bitmap.Height, 1167, 1171);
    }

    [SupportedOSPlatform("windows")]
    [WindowsFact]
    public void RenderPage_DoublingTheDpi_DoublesThePixels()
    {
        using var low = PdfRasterizer.RenderPage(A4Pdf(), pageIndex: 0, dpi: 100);
        using var high = PdfRasterizer.RenderPage(A4Pdf(), pageIndex: 0, dpi: 200);

        Assert.InRange(high.Width, low.Width * 2 - 2, low.Width * 2 + 2);
        Assert.InRange(high.Height, low.Height * 2 - 2, low.Height * 2 + 2);
    }

    [SupportedOSPlatform("windows")]
    [WindowsFact]
    public void RenderPage_RejectsAnUnknownPage()
    {
        byte[] pdf = A4Pdf();

        Assert.ThrowsAny<Exception>(() => PdfRasterizer.RenderPage(pdf, pageIndex: 5, dpi: 100));
    }
}
