using OpenLeanPrint.Core;
using Xunit;

namespace OpenLeanPrint.Core.Tests;

public class PageSelectionTests
{
    private static PageSelection Parse(string text)
    {
        Assert.True(PageSelection.TryParse(text, out var selection), $"'{text}' should parse");
        return selection;
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("all")]
    [InlineData("ALL")]
    public void EmptyOrAll_KeepsEverything(string? text)
    {
        Assert.True(PageSelection.TryParse(text, out var selection));

        Assert.True(selection.IsAll);
        Assert.True(selection.Includes(1));
        Assert.True(selection.Includes(9999));
    }

    [Fact]
    public void SinglePages_AreKeptExactly()
    {
        var selection = Parse("1,3,5");

        Assert.True(selection.Includes(1));
        Assert.False(selection.Includes(2));
        Assert.True(selection.Includes(3));
        Assert.False(selection.Includes(4));
        Assert.True(selection.Includes(5));
        Assert.False(selection.Includes(6));
    }

    [Fact]
    public void ClosedRange_IsInclusiveAtBothEnds()
    {
        var selection = Parse("2-4");

        Assert.False(selection.Includes(1));
        Assert.True(selection.Includes(2));
        Assert.True(selection.Includes(4));
        Assert.False(selection.Includes(5));
    }

    [Fact]
    public void OpenEndedRange_RunsToTheEnd()
    {
        var selection = Parse("3-");

        Assert.False(selection.Includes(2));
        Assert.True(selection.Includes(3));
        Assert.True(selection.Includes(100000));
    }

    [Fact]
    public void OpenStartRange_StartsAtOne()
    {
        var selection = Parse("-3");

        Assert.True(selection.Includes(1));
        Assert.True(selection.Includes(3));
        Assert.False(selection.Includes(4));
    }

    [Fact]
    public void MixedNotation_WithSpaces_Parses()
    {
        var selection = Parse(" 1-2, 5 , 8- ");

        Assert.True(selection.Includes(1));
        Assert.True(selection.Includes(2));
        Assert.False(selection.Includes(3));
        Assert.True(selection.Includes(5));
        Assert.False(selection.Includes(7));
        Assert.True(selection.Includes(9));
    }

    [Theory]
    [InlineData("0")]          // pages are 1-based
    [InlineData("4-2")]        // backwards
    [InlineData("-")]          // no bounds at all
    [InlineData("2-x")]
    [InlineData("abc")]
    [InlineData("1,,2")]
    [InlineData("1;2")]
    [InlineData("-1.5")]
    public void Nonsense_IsRejectedRatherThanGuessed(string text)
    {
        Assert.False(PageSelection.TryParse(text, out var selection));
        Assert.True(selection.IsAll); // caller gets a safe fallback
    }

    [Fact]
    public void Filter_KeepsPagesByTheirNumberWithinTheDocument()
    {
        var pages = Enumerable.Range(0, 6)
            .Select(i => new SourcePage(0, i, PaperSizes.A4))
            .ToList();

        var kept = Parse("1-2,5").Filter(pages);

        Assert.Equal(new[] { 0, 1, 4 }, kept.Select(p => p.PageIndex));
    }

    [Fact]
    public void Filter_CountsPerDocument_NotAcrossThePool()
    {
        // Two documents of three pages each: "1" keeps the first page of both.
        var pages = new List<SourcePage>();
        for (int document = 0; document < 2; document++)
            for (int page = 0; page < 3; page++)
                pages.Add(new SourcePage(document, page, PaperSizes.A4));

        var kept = Parse("1").Filter(pages);

        Assert.Equal(2, kept.Count);
        Assert.All(kept, page => Assert.Equal(0, page.PageIndex));
    }

    [Fact]
    public void Filter_WithAll_ReturnsTheSameList()
    {
        var pages = new List<SourcePage> { new(0, 0, PaperSizes.A4) };

        Assert.Same(pages, PageSelection.All.Filter(pages));
    }

    [Theory]
    [InlineData("1-4,7", "1-4,7")]
    [InlineData("3-", "3-")]
    [InlineData("-3", "1-3")]
    [InlineData("5", "5")]
    public void ToString_RoundTripsTheNotation(string input, string expected)
    {
        Assert.Equal(expected, Parse(input).ToString());
    }
}
