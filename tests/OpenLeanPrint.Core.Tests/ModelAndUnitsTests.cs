using OpenLeanPrint.Core;
using Xunit;

namespace OpenLeanPrint.Core.Tests;

public class ModelAndUnitsTests
{
    [Fact]
    public void PrintJobPool_FlattensDocumentsInOrder()
    {
        var pool = new PrintJobPool();

        var a = new PrintDocument("Report.pdf", "Acrobat");
        a.AddPage(PaperSizes.A4);
        a.AddPage(PaperSizes.A4);

        var b = new PrintDocument("Invoice.docx", "Word");
        b.AddPage(PaperSizes.Letter);

        pool.Add(a);
        pool.Add(b);

        Assert.Equal(3, pool.TotalPageCount);

        var flat = pool.Flatten();
        Assert.Equal(3, flat.Count);
        Assert.Equal((0, 0), (flat[0].DocumentIndex, flat[0].PageIndex));
        Assert.Equal((0, 1), (flat[1].DocumentIndex, flat[1].PageIndex));
        Assert.Equal((1, 0), (flat[2].DocumentIndex, flat[2].PageIndex));
        Assert.Equal(PaperSizes.Letter, flat[2].Size);
    }

    [Theory]
    [InlineData(25.4, 72.0)]   // 1 inch = 72 pt
    [InlineData(210.0, 595.0)] // A4 width ~ 595 pt
    public void MmToPoints_ConvertsCorrectly(double mm, double expectedPt)
    {
        Assert.Equal(expectedPt, Units.MmToPoints(mm), 0); // nearest point
    }

    [Fact]
    public void PtSize_Rotated_SwapsDimensions()
    {
        var s = new PtSize(300, 100).Rotated();
        Assert.Equal(100, s.Width);
        Assert.Equal(300, s.Height);
    }

    [Fact]
    public void PaperSizes_ByName_IsCaseInsensitiveAndHandlesUnknown()
    {
        Assert.Equal(PaperSizes.A4, PaperSizes.ByName("a4"));
        Assert.Equal(PaperSizes.Tabloid, PaperSizes.ByName("Ledger"));
        Assert.Null(PaperSizes.ByName("B7"));
    }
}
