using Xunit;

namespace OpenLeanPrint.Print.Tests;

public class PaperMatchTests
{
    // A driver's typical offering, in hundredths of an inch.
    private static readonly PaperCandidate[] Papers =
    {
        new("Letter", 850, 1100),
        new("A4", 827, 1169),
        new("A5", 583, 827),
        new("A3", 1169, 1654),
        new("Custom", 0, 0), // drivers list placeholders like this
    };

    [Fact]
    public void BestIndex_FindsA4_ForAnA4Sheet()
    {
        int i = PaperMatch.BestIndex(Papers, 595.276, 841.89);

        Assert.Equal("A4", Papers[i].Name);
    }

    [Fact]
    public void BestIndex_FindsLetter_ForALetterSheet()
    {
        int i = PaperMatch.BestIndex(Papers, 612, 792);

        Assert.Equal("Letter", Papers[i].Name);
    }

    [Fact]
    public void BestIndex_IgnoresOrientation()
    {
        // A landscape A4 sheet must still resolve to the A4 paper size -
        // orientation is a page setting, not a different paper.
        int i = PaperMatch.BestIndex(Papers, 841.89, 595.276);

        Assert.Equal("A4", Papers[i].Name);
    }

    [Fact]
    public void BestIndex_ReturnsMinusOne_WhenNothingIsCloseEnough()
    {
        // 200 x 300 pt is no standard size.
        Assert.Equal(-1, PaperMatch.BestIndex(Papers, 200, 300));
    }

    [Fact]
    public void BestIndex_SkipsPlaceholderSizes()
    {
        var onlyPlaceholder = new[] { new PaperCandidate("Custom", 0, 0) };

        Assert.Equal(-1, PaperMatch.BestIndex(onlyPlaceholder, 595.276, 841.89));
    }

    [Fact]
    public void BestIndex_PicksTheClosestNeighbour()
    {
        // Two candidates within tolerance of an A4 sheet; the exact one wins.
        var near = new[]
        {
            new PaperCandidate("A4 (rounded)", 830, 1172),
            new PaperCandidate("A4", 827, 1169),
        };

        int i = PaperMatch.BestIndex(near, 595.276, 841.89);

        Assert.Equal("A4", near[i].Name);
    }
}
