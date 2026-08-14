using UglyToad.PdfPig.Writer;

namespace OpenLeanPrint.Capture.Tests;

/// <summary>Builds small in-memory PDFs with known page sizes for tests.</summary>
internal static class TestPdfs
{
    /// <summary>Creates a PDF with the given page sizes (width, height in points).</summary>
    public static byte[] WithPages(params (double Width, double Height)[] pages)
    {
        var builder = new PdfDocumentBuilder();
        foreach (var (w, h) in pages)
            builder.AddPage(w, h);
        return builder.Build();
    }
}
