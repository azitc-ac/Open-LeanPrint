using OpenLeanPrint.Core.Imposition;
using Xunit;

namespace OpenLeanPrint.Core.Tests;

public class NUpGridTests
{
    [Theory]
    [InlineData("2x2", 2, 2)]
    [InlineData("1x2", 1, 2)]
    [InlineData("2x3", 2, 3)]
    [InlineData("4X4", 4, 4)]
    [InlineData(" 3x1 ", 3, 1)]
    public void RowsByColumns_ParseAsWritten(string text, int rows, int columns)
    {
        Assert.True(NUpGrid.TryParse(text, out int r, out int c));
        Assert.Equal((rows, columns), (r, c));
    }

    [Theory]
    [InlineData("1", 1, 1)]
    [InlineData("2", 1, 2)]
    [InlineData("4", 2, 2)]
    [InlineData("6", 2, 3)]   // two rows of three, not one row of six
    [InlineData("9", 3, 3)]
    [InlineData("16", 4, 4)]
    [InlineData("5", 1, 5)]   // no obvious shape: a single row
    public void APlainCount_BecomesTheGridPeopleMean(string text, int rows, int columns)
    {
        Assert.True(NUpGrid.TryParse(text, out int r, out int c));
        Assert.Equal((rows, columns), (r, c));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("0x2")]
    [InlineData("2x0")]
    [InlineData("-1x2")]
    [InlineData("2x2x2")]
    [InlineData("axb")]
    [InlineData("2.5")]
    public void Nonsense_IsRejected(string? text)
    {
        Assert.False(NUpGrid.TryParse(text, out int rows, out int columns));
        Assert.Equal((0, 0), (rows, columns));
    }

    [Fact]
    public void Format_RoundTrips()
    {
        Assert.Equal("2x3", NUpGrid.Format(2, 3));

        Assert.True(NUpGrid.TryParse(NUpGrid.Format(3, 4), out int rows, out int columns));
        Assert.Equal((3, 4), (rows, columns));
    }
}
