using OpenLeanPrint.Core;
using OpenLeanPrint.Core.Imposition;
using Xunit;

namespace OpenLeanPrint.Core.Tests;

public class BookletImposerTests
{
    [Fact]
    public void BuildOrder_FourPages_ProducesStandardSaddleStitchOrder()
    {
        // Sheet front = [4,1], sheet back = [2,3]  =>  zero-based [3,0,1,2].
        Assert.Equal(new int?[] { 3, 0, 1, 2 }, BookletImposer.BuildOrder(4));
    }

    [Fact]
    public void BuildOrder_EightPages_ProducesStandardSaddleStitchOrder()
    {
        // 1-based sheets: [8,1] [2,7] [6,3] [4,5]  =>  zero-based below.
        Assert.Equal(new int?[] { 7, 0, 1, 6, 5, 2, 3, 4 }, BookletImposer.BuildOrder(8));
    }

    [Fact]
    public void BuildOrder_PadsUpToMultipleOfFourWithBlanks()
    {
        // 5 pages -> padded to 8; the three padding slots are null.
        var order = BookletImposer.BuildOrder(5);
        Assert.Equal(8, order.Count);
        Assert.Equal(3, order.Count(x => x is null));
        // Every real page index 0..4 appears exactly once.
        var present = order.Where(x => x is not null).Select(x => x!.Value).OrderBy(x => x);
        Assert.Equal(Enumerable.Range(0, 5), present);
    }

    [Fact]
    public void BuildOrder_Zero_IsEmpty()
    {
        Assert.Empty(BookletImposer.BuildOrder(0));
    }

    [Fact]
    public void Impose_FourPages_ProducesTwoSheetsOfTwoPagesEach()
    {
        var pages = Enumerable.Range(0, 4)
                              .Select(i => new SourcePage(0, i, PaperSizes.A5))
                              .ToList();
        var result = new BookletImposer().Impose(pages, PaperSizes.A4);

        // 4 pages, 2 per sheet => 2 sheets, each landscape A4.
        Assert.Equal(2, result.SheetCount);
        Assert.All(result.Sheets, s =>
        {
            Assert.Equal(2, s.Pages.Count);
            Assert.True(s.Size.Width > s.Size.Height); // landscape
        });

        // First sheet's first cell holds the last page (index 3).
        var firstCell = result.Sheets[0].Pages.Single(p => p.CellIndex == 0);
        Assert.Equal(3, firstCell.Source.PageIndex);
    }
}
