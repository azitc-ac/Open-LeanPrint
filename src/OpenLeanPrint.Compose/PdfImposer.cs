using OpenLeanPrint.Core;
using OpenLeanPrint.Core.Imposition;
using PdfSharpCore.Drawing;
using PdfSharpCore.Pdf;
using PdfSharpCore.Pdf.IO;

namespace OpenLeanPrint.Compose;

/// <summary>
/// Composes an <see cref="ImpositionResult"/> into a real imposed output PDF by
/// placing each source PDF page into its computed cell (position, scale,
/// rotation). Pure vector composition via PdfSharpCore - no rasterisation, so
/// the output stays crisp and is directly usable for printing (M3).
/// </summary>
public sealed class PdfImposer
{
    /// <summary>Imposes a single source PDF N-up according to <paramref name="settings"/>.</summary>
    public byte[] ImposeToPdf(byte[] sourcePdf, ImpositionSettings settings)
    {
        var pages = ReadPageSizes(sourcePdf);
        var result = new NUpImposer().Impose(pages, settings);
        return Compose(new[] { sourcePdf }, result);
    }

    /// <summary>Imposes a single source PDF as a saddle-stitch booklet.</summary>
    public byte[] ImposeBookletToPdf(byte[] sourcePdf, PtSize sheetSize,
                                     PtMargins margins = default, double gutter = 0)
    {
        var pages = ReadPageSizes(sourcePdf);
        var result = new BookletImposer().Impose(pages, sheetSize, margins, gutter);
        return Compose(new[] { sourcePdf }, result);
    }

    /// <summary>Reads page sizes (points) from a PDF into the Core page model.</summary>
    public static IReadOnlyList<SourcePage> ReadPageSizes(byte[] pdf)
    {
        using var doc = PdfReader.Open(new MemoryStream(pdf), PdfDocumentOpenMode.InformationOnly);
        var list = new List<SourcePage>(doc.PageCount);
        for (int i = 0; i < doc.PageCount; i++)
        {
            var p = doc.Pages[i];
            list.Add(new SourcePage(0, i, new PtSize(p.Width.Point, p.Height.Point)));
        }
        return list;
    }

    /// <summary>
    /// Composes the sheets of <paramref name="result"/> into an output PDF,
    /// pulling page content from <paramref name="sourcePdfs"/> (indexed by
    /// <see cref="SourcePage.DocumentIndex"/>).
    /// </summary>
    public byte[] Compose(IReadOnlyList<byte[]> sourcePdfs, ImpositionResult result)
    {
        ArgumentNullException.ThrowIfNull(sourcePdfs);
        ArgumentNullException.ThrowIfNull(result);

        // PdfSharpCore imports external page content at Save() time, so every
        // XPdfForm (and its backing stream) must stay alive until then. Cache one
        // form per (document, page) so a reused page is imported once and a form
        // is never re-pointed at a different page.
        var forms = new Dictionary<(int Doc, int Page), XPdfForm>();
        var openStreams = new List<MemoryStream>();

        XPdfForm FormFor(int doc, int page)
        {
            if (!forms.TryGetValue((doc, page), out var form))
            {
                var ms = new MemoryStream(sourcePdfs[doc]);
                openStreams.Add(ms);
                form = XPdfForm.FromStream(ms);
                form.PageNumber = page + 1; // PdfSharp page numbers are 1-based
                forms[(doc, page)] = form;
            }
            return form;
        }

        try
        {
            using var output = new PdfDocument();
            foreach (var sheet in result.Sheets)
            {
                var page = output.AddPage();
                page.Width = XUnit.FromPoint(sheet.Size.Width);
                page.Height = XUnit.FromPoint(sheet.Size.Height);

                using var gfx = XGraphics.FromPdfPage(page);
                foreach (var placed in sheet.Pages)
                {
                    var form = FormFor(placed.Source.DocumentIndex, placed.Source.PageIndex);
                    DrawPlaced(gfx, form, placed);
                }
            }

            using var outMs = new MemoryStream();
            output.Save(outMs, false);
            return outMs.ToArray();
        }
        finally
        {
            foreach (var s in openStreams) s.Dispose();
        }
    }

    /// <summary>Draws one placed page into its destination rectangle, honouring rotation.</summary>
    private static void DrawPlaced(XGraphics gfx, XPdfForm form, PlacedPage placed)
    {
        var r = placed.DestRect;
        if (placed.Rotation == 0)
        {
            gfx.DrawImage(form, r.X, r.Y, r.Width, r.Height);
            return;
        }

        // 90° clockwise: rotate the coordinate system about the cell's top-right
        // corner, then draw with width/height swapped so the page fills DestRect.
        var state = gfx.Save();
        gfx.TranslateTransform(r.X + r.Width, r.Y);
        gfx.RotateTransform(90);
        gfx.DrawImage(form, 0, 0, r.Height, r.Width);
        gfx.Restore(state);
    }
}
