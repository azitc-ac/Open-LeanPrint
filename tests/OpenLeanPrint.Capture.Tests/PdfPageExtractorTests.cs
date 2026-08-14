using OpenLeanPrint.Capture.Pdf;
using Xunit;

namespace OpenLeanPrint.Capture.Tests;

public class PdfPageExtractorTests
{
    [Fact]
    public void ToPrintDocument_ReadsPageCountAndSizes()
    {
        // A4 portrait (595x842) then Letter (612x792).
        byte[] pdf = TestPdfs.WithPages((595, 842), (612, 792));

        var doc = PdfPageExtractor.ToPrintDocument(pdf, "Test.pdf", "xunit");

        Assert.Equal("Test.pdf", doc.Title);
        Assert.Equal("xunit", doc.SourceApplication);
        Assert.Equal(2, doc.PageCount);
        // Allow a small tolerance: PDF writers may round media-box values.
        Assert.Equal(595, doc.PageSizes[0].Width, 0);
        Assert.Equal(842, doc.PageSizes[0].Height, 0);
        Assert.Equal(612, doc.PageSizes[1].Width, 0);
        Assert.Equal(792, doc.PageSizes[1].Height, 0);
    }
}
