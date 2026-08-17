using PdfSharpCore.Drawing;
using PdfSharpCore.Pdf;

namespace OpenLeanPrint.Cli;

/// <summary>Generates a colored, visually distinct multi-page PDF for trying imposition.</summary>
internal static class SamplePdf
{
    private static readonly XColor[] Palette =
    {
        XColors.IndianRed, XColors.SteelBlue, XColors.SeaGreen, XColors.Goldenrod,
        XColors.MediumPurple, XColors.Teal, XColors.Chocolate, XColors.SlateGray,
        XColors.DarkCyan, XColors.Crimson,
    };

    public static byte[] Colored(int pageCount, double width = 595, double height = 842)
    {
        if (pageCount < 1) pageCount = 1;
        using var doc = new PdfDocument();
        for (int i = 0; i < pageCount; i++)
        {
            var page = doc.AddPage();
            page.Width = XUnit.FromPoint(width);
            page.Height = XUnit.FromPoint(height);
            using var gfx = XGraphics.FromPdfPage(page);

            var fill = Palette[i % Palette.Length];
            fill.A = 0.30;
            gfx.DrawRectangle(new XSolidBrush(fill), 0, 0, width, height);
            gfx.DrawRectangle(new XPen(XColors.Black, 3), 1.5, 1.5, width - 3, height - 3);

            // A big page-number "bar chart" of ticks so each page is identifiable
            // and orientation is obvious, without needing an embedded font.
            var solid = Palette[i % Palette.Length];
            solid.A = 1.0;
            var brush = new XSolidBrush(solid);
            int n = i + 1;
            double barW = 26, gap = 12, x = 40, top = 60, barH = 90;
            for (int b = 0; b < n; b++)
            {
                gfx.DrawRectangle(brush, x + b * (barW + gap), top, barW, barH);
            }
            // Red top edge marks "up".
            gfx.DrawLine(new XPen(XColors.Red, 6), 0, 3, width, 3);
            // Diagonal from top-left corner.
            gfx.DrawLine(new XPen(XColors.Black, 1.5), 0, 0, width, height);
        }

        using var ms = new MemoryStream();
        doc.Save(ms, false);
        return ms.ToArray();
    }
}
