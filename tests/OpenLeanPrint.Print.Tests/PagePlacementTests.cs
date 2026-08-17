using Xunit;

namespace OpenLeanPrint.Print.Tests;

public class PagePlacementTests
{
    [Fact]
    public void Fit_SheetMatchingThePage_FillsItExactly()
    {
        // A4 sheet in points onto an A4 page in hundredths of an inch. The
        // driver's integer paper size (827 x 1169) is a hair off A4's true
        // aspect ratio, so allow half a hundredth of an inch (0.13 mm) of slack.
        const double slack = 0.5;

        var fit = PagePlacement.Fit(595.276, 841.89, 827, 1169);

        Assert.InRange(fit.X, 0, slack);
        Assert.InRange(fit.Y, 0, slack);
        Assert.InRange(fit.Width, 827 - slack, 827);
        Assert.InRange(fit.Height, 1169 - slack, 1169);
    }

    [Fact]
    public void Fit_WiderPage_CentresHorizontally()
    {
        var fit = PagePlacement.Fit(100, 100, 400, 200);

        Assert.Equal(200, fit.Width, 6);
        Assert.Equal(200, fit.Height, 6);
        Assert.Equal(100, fit.X, 6); // (400 - 200) / 2
        Assert.Equal(0, fit.Y, 6);
    }

    [Fact]
    public void Fit_TallerPage_CentresVertically()
    {
        var fit = PagePlacement.Fit(100, 100, 200, 400);

        Assert.Equal(200, fit.Width, 6);
        Assert.Equal(200, fit.Height, 6);
        Assert.Equal(0, fit.X, 6);
        Assert.Equal(100, fit.Y, 6);
    }

    [Fact]
    public void Fit_KeepsAspectRatio_WhenScalingDown()
    {
        var fit = PagePlacement.Fit(1000, 500, 300, 300);

        Assert.Equal(300, fit.Width, 6);
        Assert.Equal(150, fit.Height, 6);
        Assert.Equal(0, fit.X, 6);
        Assert.Equal(75, fit.Y, 6);
    }

    [Theory]
    [InlineData(0, 10, 10, 10)]
    [InlineData(10, 0, 10, 10)]
    [InlineData(10, 10, -1, 10)]
    [InlineData(10, 10, 10, 0)]
    public void Fit_RejectsNonPositiveDimensions(double sw, double sh, double pw, double ph)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => PagePlacement.Fit(sw, sh, pw, ph));
    }
}
