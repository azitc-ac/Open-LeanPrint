using OpenLeanPrint.Core;
using OpenLeanPrint.Core.Imposition;
using Xunit;

namespace OpenLeanPrint.Core.Tests;

public class PageRotationTests
{
    private static readonly PtSize Portrait = new(400, 800);

    private static ImpositionSettings OneUp() =>
        ImpositionSettings.NUp(1, 1) with { SheetSize = PaperSizes.A4 };

    private static PlacedPage Place(SourcePage page, ImpositionSettings settings) =>
        new NUpImposer().Impose(new[] { page }, settings).Sheets[0].Pages[0];

    [Theory]
    [InlineData(0, 0)]
    [InlineData(90, 90)]
    [InlineData(180, 180)]
    [InlineData(270, 270)]
    [InlineData(360, 0)]
    [InlineData(-90, 270)]
    [InlineData(450, 90)]
    public void Rotation_IsNormalisedToQuarterTurns(int requested, int expected)
    {
        var page = new SourcePage(0, 0, Portrait) { Rotation = requested };

        Assert.Equal(expected, Place(page, OneUp() with { AllowRotation = false }).Rotation);
    }

    [Fact]
    public void QuarterTurn_FitsThePageAtItsNewProportions()
    {
        // A tall page turned on its side must be laid out as a wide one, so it
        // ends up wider than it is high.
        var settings = OneUp() with { AllowRotation = false };

        var upright = Place(new SourcePage(0, 0, Portrait), settings).DestRect;
        var turned = Place(new SourcePage(0, 0, Portrait) { Rotation = 90 }, settings).DestRect;

        Assert.True(upright.Height > upright.Width, "the untouched page stays tall");
        Assert.True(turned.Width > turned.Height, "the turned page becomes wide");
    }

    [Fact]
    public void HalfTurn_KeepsTheSameRectangle()
    {
        var settings = OneUp() with { AllowRotation = false };

        var upright = Place(new SourcePage(0, 0, Portrait), settings).DestRect;
        var upsideDown = Place(new SourcePage(0, 0, Portrait) { Rotation = 180 }, settings).DestRect;

        Assert.Equal(upright.Width, upsideDown.Width, 6);
        Assert.Equal(upright.Height, upsideDown.Height, 6);
    }

    [Fact]
    public void AnExplicitTurn_StopsAutoRotationFromArguing()
    {
        // Stacked cells are wide and short, so the engine would normally turn a
        // portrait page on its side to make it bigger. Once the user has said
        // 180, that must be the answer - not 180 plus a helpful 90.
        var settings = ImpositionSettings.NUp(2, 1) with
        {
            SheetSize = PaperSizes.A4,
            AllowRotation = true,
        };

        var automatic = Place(new SourcePage(0, 0, Portrait), settings);
        var chosen = Place(new SourcePage(0, 0, Portrait) { Rotation = 180 }, settings);

        Assert.Equal(90, automatic.Rotation);  // the engine's own idea
        Assert.Equal(180, chosen.Rotation);    // the user's, untouched
    }

    [Fact]
    public void RotationTravelsWithThePage_NotTheCell()
    {
        var settings = ImpositionSettings.NUp(2, 2) with
        {
            SheetSize = PaperSizes.A4,
            AllowRotation = false,
        };
        var pages = new[]
        {
            new SourcePage(0, 0, Portrait),
            new SourcePage(0, 1, Portrait) { Rotation = 90 },
            new SourcePage(0, 2, Portrait) { Rotation = 180 },
            new SourcePage(0, 3, Portrait),
        };

        var sheet = new NUpImposer().Impose(pages, settings).Sheets[0];

        Assert.Equal(new[] { 0, 90, 180, 0 }, sheet.Pages.Select(p => p.Rotation));
    }
}
