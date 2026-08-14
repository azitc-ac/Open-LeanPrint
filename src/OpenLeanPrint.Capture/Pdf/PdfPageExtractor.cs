using OpenLeanPrint.Core;
using UglyToad.PdfPig;

namespace OpenLeanPrint.Capture.Pdf;

/// <summary>
/// Reads page geometry out of a captured PDF and turns it into the LeanPrint
/// domain model. PDF user-space units are points (1/72"), matching
/// <see cref="PtSize"/>, so page dimensions map across directly.
/// </summary>
public static class PdfPageExtractor
{
    /// <summary>
    /// Builds a <see cref="PrintDocument"/> from raw PDF bytes, one entry per page
    /// sized from that page's media box.
    /// </summary>
    public static PrintDocument ToPrintDocument(byte[] pdfBytes, string title, string? sourceApplication = null)
    {
        ArgumentNullException.ThrowIfNull(pdfBytes);

        var document = new PrintDocument(title, sourceApplication);
        using var pdf = PdfDocument.Open(pdfBytes);
        foreach (var page in pdf.GetPages())
            document.AddPage(new PtSize(page.Width, page.Height));

        return document;
    }
}
