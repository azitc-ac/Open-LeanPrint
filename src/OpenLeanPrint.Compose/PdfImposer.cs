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
    /// <summary>Optional text watermark drawn across every finished sheet.</summary>
    public Watermark? Watermark { get; init; }

    /// <summary>Optional frame drawn around every page placed on a sheet.</summary>
    public PageBorder? PageBorder { get; init; }

    /// <summary>Imposes a single source PDF N-up according to <paramref name="settings"/>.</summary>
    public byte[] ImposeToPdf(byte[] sourcePdf, ImpositionSettings settings) =>
        ImposeToPdf(new[] { sourcePdf }, settings);

    /// <summary>
    /// Imposes several source PDFs as one continuous sequence — the pooled
    /// "combine several print jobs onto shared sheets" case.
    /// </summary>
    /// <param name="sourcePdfs">The pooled source documents, in order.</param>
    /// <param name="settings">Grid, sheet size, margins and gutters.</param>
    /// <param name="selections">
    /// Optional per-document page selection, positional to
    /// <paramref name="sourcePdfs"/>. Missing entries keep every page.
    /// </param>
    public byte[] ImposeToPdf(IReadOnlyList<byte[]> sourcePdfs, ImpositionSettings settings,
                              IReadOnlyList<PageSelection>? selections = null)
    {
        var pages = ReadPageSizes(sourcePdfs, selections);
        var result = new NUpImposer().Impose(pages, settings);
        return Compose(sourcePdfs, result);
    }

    /// <summary>Imposes a single source PDF as a saddle-stitch booklet.</summary>
    public byte[] ImposeBookletToPdf(byte[] sourcePdf, PtSize sheetSize,
                                     PtMargins margins = default, double gutter = 0) =>
        ImposeBookletToPdf(new[] { sourcePdf }, sheetSize, margins, gutter);

    /// <summary>Imposes several source PDFs as one saddle-stitch booklet.</summary>
    public byte[] ImposeBookletToPdf(IReadOnlyList<byte[]> sourcePdfs, PtSize sheetSize,
                                     PtMargins margins = default, double gutter = 0,
                                     IReadOnlyList<PageSelection>? selections = null)
    {
        var pages = ReadPageSizes(sourcePdfs, selections);
        var result = new BookletImposer().Impose(pages, sheetSize, margins, gutter);
        return Compose(sourcePdfs, result);
    }

    /// <summary>
    /// Reads page sizes from several PDFs, tagging each page with the index of
    /// the document it came from so <see cref="Compose"/> can find it again.
    /// </summary>
    public static IReadOnlyList<SourcePage> ReadPageSizes(IReadOnlyList<byte[]> pdfs,
                                                          IReadOnlyList<PageSelection>? selections = null)
    {
        ArgumentNullException.ThrowIfNull(pdfs);

        var pages = new List<SourcePage>();
        for (int document = 0; document < pdfs.Count; document++)
        {
            var selection = selections is not null && document < selections.Count
                ? selections[document] ?? PageSelection.All
                : PageSelection.All;

            foreach (var page in selection.Filter(ReadPageSizes(pdfs[document])))
                pages.Add(page with { DocumentIndex = document });
        }

        if (pages.Count == 0)
            throw new ArgumentException("The page selection left no pages to impose.", nameof(selections));
        return pages;
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

                    // After the page, so a page with a dark edge cannot hide it.
                    if (PageBorder is { IsEmpty: false } border) DrawBorder(gfx, border, placed);
                }

                // On top of the pages, so it cannot be hidden by their content.
                if (Watermark is { IsEmpty: false } watermark)
                    DrawWatermark(gfx, watermark, sheet.Size);
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

    /// <summary>
    /// Frames one placed page. The stroke is inset by half its width, so the
    /// whole line stays inside the page area instead of straddling its edge and
    /// bleeding into the gutter.
    /// </summary>
    private static void DrawBorder(XGraphics gfx, PageBorder border, PlacedPage placed)
    {
        var (r, g, b) = border.Color();
        var pen = new XPen(XColor.FromArgb(r, g, b), border.WidthPt);
        double inset = border.WidthPt / 2;
        var rect = placed.DestRect;

        // A page smaller than the line itself has nothing left to frame.
        if (rect.Width <= border.WidthPt || rect.Height <= border.WidthPt) return;

        gfx.DrawRectangle(pen, rect.X + inset, rect.Y + inset,
                          rect.Width - border.WidthPt, rect.Height - border.WidthPt);
    }

    /// <summary>Draws the watermark across the middle of a sheet.</summary>
    private static void DrawWatermark(XGraphics gfx, Watermark watermark, PtSize sheet)
    {
        var (r, g, b) = watermark.Color();
        var colour = XColor.FromArgb(r, g, b);
        colour.A = Math.Clamp(watermark.Opacity, 0, 1);

        string text = watermark.Text.Trim();
        double size = watermark.EffectiveFontSize(sheet.Width, sheet.Height);
        XFont font;
        try
        {
            font = new XFont(watermark.FontFamily, size, XFontStyle.Bold);
        }
        catch (Exception)
        {
            // An unavailable family must not cost the whole document.
            font = new XFont("Arial", size, XFontStyle.Bold);
        }

        // Measure and rescale so the text really spans the sheet rather than
        // relying on the estimate.
        double target = Math.Sqrt(sheet.Width * sheet.Width + sheet.Height * sheet.Height) * 0.8;
        double measured = gfx.MeasureString(text, font).Width;
        if (measured > 0 && watermark.FontSize <= 0)
        {
            double corrected = Math.Clamp(size * target / measured, 8, 400);
            if (Math.Abs(corrected - size) > 0.5) font = new XFont(watermark.FontFamily, corrected, XFontStyle.Bold);
        }

        var state = gfx.Save();
        gfx.TranslateTransform(sheet.Width / 2, sheet.Height / 2);
        gfx.RotateTransform(watermark.AngleDegrees);
        gfx.DrawString(text, font, new XSolidBrush(colour), 0, 0,
                       new XStringFormat { Alignment = XStringAlignment.Center, LineAlignment = XLineAlignment.Center });
        gfx.Restore(state);
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

        // Rotate the coordinate system about the corner that ends up at the
        // origin, then draw the page there; width and height swap on a quarter
        // turn so the page still fills DestRect.
        var state = gfx.Save();
        switch (placed.Rotation)
        {
            case 90:
                gfx.TranslateTransform(r.X + r.Width, r.Y);
                gfx.RotateTransform(90);
                gfx.DrawImage(form, 0, 0, r.Height, r.Width);
                break;
            case 180:
                gfx.TranslateTransform(r.X + r.Width, r.Y + r.Height);
                gfx.RotateTransform(180);
                gfx.DrawImage(form, 0, 0, r.Width, r.Height);
                break;
            case 270:
                gfx.TranslateTransform(r.X, r.Y + r.Height);
                gfx.RotateTransform(270);
                gfx.DrawImage(form, 0, 0, r.Height, r.Width);
                break;
            default:
                gfx.DrawImage(form, r.X, r.Y, r.Width, r.Height);
                break;
        }
        gfx.Restore(state);
    }
}
