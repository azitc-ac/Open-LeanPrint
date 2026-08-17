using PdfSharpCore.Drawing;
using PdfSharpCore.Pdf;

namespace OpenLeanPrint.Compose.Tests;

/// <summary>Builds small multi-page PDFs with visually distinct pages (no fonts needed).</summary>
internal static class TestPdfs
{
    private static readonly XColor[] Palette =
    {
        XColors.IndianRed, XColors.SteelBlue, XColors.SeaGreen, XColors.Goldenrod,
        XColors.MediumPurple, XColors.Teal, XColors.Chocolate, XColors.SlateGray,
    };

    /// <summary>
    /// Creates a PDF with <paramref name="pageCount"/> portrait pages of the given
    /// size (points). Each page has a distinct fill, a border, and a diagonal line
    /// from top-left to bottom-right so orientation/rotation is visible.
    /// </summary>
    public static byte[] Colored(int pageCount, double width = 595, double height = 842)
    {
        using var doc = new PdfDocument();
        for (int i = 0; i < pageCount; i++)
        {
            var page = doc.AddPage();
            page.Width = XUnit.FromPoint(width);
            page.Height = XUnit.FromPoint(height);
            using var gfx = XGraphics.FromPdfPage(page);

            var fill = Palette[i % Palette.Length];
            fill.A = 0.35; // translucent so overlaps are visible
            gfx.DrawRectangle(new XSolidBrush(fill), 0, 0, width, height);
            gfx.DrawRectangle(new XPen(XColors.Black, 3), 1.5, 1.5, width - 3, height - 3);
            // Diagonal marks the page's "up": corner-to-corner.
            gfx.DrawLine(new XPen(XColors.Black, 2), 0, 0, width, height);
            gfx.DrawLine(new XPen(XColors.Red, 4), 0, 0, width, 0); // top edge in red
        }

        using var ms = new MemoryStream();
        doc.Save(ms, false);
        return ms.ToArray();
    }
}
