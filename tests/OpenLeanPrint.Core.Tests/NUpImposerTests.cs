using OpenLeanPrint.Core;
using OpenLeanPrint.Core.Imposition;
using Xunit;

namespace OpenLeanPrint.Core.Tests;

public class NUpImposerTests
{
    private const double Tol = 1e-6;

    private static SourcePage Page(PtSize size, int pageIndex = 0) => new(0, pageIndex, size);

    private static List<SourcePage> Pages(int count, PtSize size)
    {
        var list = new List<SourcePage>(count);
        for (int i = 0; i < count; i++)
            list.Add(new SourcePage(0, i, size));
        return list;
    }

    [Fact]
    public void OneUp_SinglePage_ProducesOneSheetOneFullBleedPlacement()
    {
        var settings = ImpositionSettings.NUp(1, 1) with { SheetSize = PaperSizes.A4 };
        var result = new NUpImposer().Impose(new[] { Page(PaperSizes.A4) }, settings);

        Assert.Equal(1, result.SheetCount);
        var placed = Assert.Single(result.Sheets[0].Pages);
        Assert.Equal(0, placed.Rotation);
        // A4 page on A4 sheet with no margins: fills the whole sheet.
        Assert.Equal(0, placed.DestRect.X, Tol);
        Assert.Equal(0, placed.DestRect.Y, Tol);
        Assert.Equal(PaperSizes.A4.Width, placed.DestRect.Width, Tol);
        Assert.Equal(PaperSizes.A4.Height, placed.DestRect.Height, Tol);
    }

    [Fact]
    public void FourUp_FillsFourCellsThenSpillsToSecondSheet()
    {
        var settings = ImpositionSettings.NUp(2, 2) with { SheetSize = PaperSizes.A4 };
        var result = new NUpImposer().Impose(Pages(5, PaperSizes.A4), settings);

        Assert.Equal(2, result.SheetCount);
        Assert.Equal(4, result.Sheets[0].Pages.Count);
        Assert.Single(result.Sheets[1].Pages);
    }

    [Fact]
    public void RowMajor_PlacesCellsLeftToRightThenTopToBottom()
    {
        // Distinct-sized pages let us assert which cell got which page via CellIndex.
        var settings = ImpositionSettings.NUp(2, 2) with { SheetSize = PaperSizes.A4 };
        var pages = Pages(4, PaperSizes.A4);
        var sheet = new NUpImposer().Impose(pages, settings).Sheets[0];

        // Cell 0 = top-left, cell 1 = top-right, cell 2 = bottom-left, cell 3 = bottom-right.
        var byCell = sheet.Pages.ToDictionary(p => p.CellIndex);
        Assert.True(byCell[0].DestRect.X < byCell[1].DestRect.X);           // 0 left of 1
        Assert.Equal(byCell[0].DestRect.Y, byCell[1].DestRect.Y, Tol);      // 0 same row as 1
        Assert.True(byCell[0].DestRect.Y < byCell[2].DestRect.Y);           // 0 above 2
        Assert.Equal(byCell[0].DestRect.X, byCell[2].DestRect.X, Tol);      // 0 same column as 2
    }

    [Fact]
    public void ColumnMajor_PlacesCellsTopToBottomThenLeftToRight()
    {
        var settings = ImpositionSettings.NUp(2, 2) with
        {
            SheetSize = PaperSizes.A4,
            Order = PageOrder.ColumnMajor,
        };
        var sheet = new NUpImposer().Impose(Pages(4, PaperSizes.A4), settings).Sheets[0];
        var byCell = sheet.Pages.ToDictionary(p => p.CellIndex);

        // In column-major order cell 1 sits below cell 0 (same column).
        Assert.Equal(byCell[0].DestRect.X, byCell[1].DestRect.X, Tol);
        Assert.True(byCell[0].DestRect.Y < byCell[1].DestRect.Y);
    }

    [Fact]
    public void TwoUp_StackedOnPortraitSheet_RotatesPortraitPagesToMaximiseSize()
    {
        // Two portrait A4 pages stacked 2-up (2 rows, 1 col) on a portrait A4 sheet:
        // each cell is wide and short, so rotating the portrait page 90° fits it larger
        // (rotated scale ~0.71 beats un-rotated ~0.5).
        var settings = ImpositionSettings.NUp(2, 1) with { SheetSize = PaperSizes.A4 };
        var sheet = new NUpImposer().Impose(Pages(2, PaperSizes.A4), settings).Sheets[0];

        Assert.All(sheet.Pages, p => Assert.Equal(90, p.Rotation));
    }

    [Fact]
    public void TwoUp_StackedAndTurned_UsesFarMoreOfTheSheetThanSideBySide()
    {
        // Why a plain count of 2 means 2x1 and not 1x2. Two upright pages side
        // by side on an upright sheet fill about half of it and waste the top
        // and bottom thirds; stacked, the imposer turns them and they fill it.
        static double Covered(ImpositionResult result) =>
            result.Sheets[0].Pages.Sum(page => page.DestRect.Width * page.DestRect.Height);

        var pages = Pages(2, PaperSizes.A4);
        var stacked = new NUpImposer().Impose(pages,
            ImpositionSettings.NUp(2, 1) with { SheetSize = PaperSizes.A4 });
        var sideBySide = new NUpImposer().Impose(pages,
            ImpositionSettings.NUp(1, 2) with { SheetSize = PaperSizes.A4 });

        Assert.All(stacked.Sheets[0].Pages, page => Assert.Equal(90, page.Rotation));
        Assert.True(Covered(stacked) > Covered(sideBySide) * 1.9,
                    $"stacked covered {Covered(stacked):N0}, side by side {Covered(sideBySide):N0}");
    }

    [Fact]
    public void TwoUp_SideBySideOnPortraitSheet_DoesNotRotate()
    {
        // 1 row, 2 cols on a portrait sheet gives tall/narrow cells that match the
        // portrait page orientation, so no rotation is needed.
        var settings = ImpositionSettings.NUp(1, 2) with { SheetSize = PaperSizes.A4 };
        var sheet = new NUpImposer().Impose(Pages(2, PaperSizes.A4), settings).Sheets[0];

        Assert.All(sheet.Pages, p => Assert.Equal(0, p.Rotation));
    }

    [Fact]
    public void Fit_PreservesAspectRatio()
    {
        // A wide 2:1 page fitted into a square-ish cell keeps its 2:1 ratio.
        var wide = new PtSize(400, 200);
        var settings = ImpositionSettings.NUp(1, 1) with
        {
            SheetSize = new PtSize(300, 300),
            AllowRotation = false,
            ScaleMode = ScaleMode.Fit,
        };
        var placed = new NUpImposer().Impose(new[] { Page(wide) }, settings).Sheets[0].Pages[0];

        Assert.Equal(2.0, placed.DestRect.Width / placed.DestRect.Height, Tol);
        Assert.True(placed.DestRect.Width <= 300 + Tol);
        Assert.True(placed.DestRect.Height <= 300 + Tol);
    }

    [Fact]
    public void Stretch_FillsCellExactlyAndDistorts()
    {
        var settings = ImpositionSettings.NUp(1, 1) with
        {
            SheetSize = new PtSize(300, 300),
            ScaleMode = ScaleMode.Stretch,
        };
        var placed = new NUpImposer().Impose(new[] { Page(new PtSize(400, 200)) }, settings)
                                     .Sheets[0].Pages[0];

        Assert.Equal(300, placed.DestRect.Width, Tol);
        Assert.Equal(300, placed.DestRect.Height, Tol);
    }

    [Fact]
    public void MarginsAndGutter_ReduceAndOffsetCells()
    {
        var settings = ImpositionSettings.NUp(1, 2) with
        {
            SheetSize = new PtSize(1000, 1000),
            Orientation = SheetOrientation.AsIs,
            Margins = PtMargins.Uniform(50),
            GutterX = 20,
            AllowRotation = false,
            ScaleMode = ScaleMode.Stretch, // fill cells so DestRect == cell rect
        };
        var sheet = new NUpImposer().Impose(Pages(2, new PtSize(100, 100)), settings).Sheets[0];
        var byCell = sheet.Pages.ToDictionary(p => p.CellIndex);

        // content width = 1000 - 2*50 = 900; minus gutter 20 => 880 across 2 cols => 440 each.
        Assert.Equal(440, byCell[0].DestRect.Width, Tol);
        Assert.Equal(50, byCell[0].DestRect.X, Tol);               // left margin
        Assert.Equal(50 + 440 + 20, byCell[1].DestRect.X, Tol);    // margin + cell + gutter
    }

    [Fact]
    public void BlankCell_IsSkippedButStillConsumesSlot()
    {
        var settings = ImpositionSettings.NUp(1, 2) with { SheetSize = PaperSizes.A4 };
        var pages = new SourcePage?[] { null, Page(PaperSizes.A4) };
        var sheet = new NUpImposer().Impose(pages, settings).Sheets[0];

        var placed = Assert.Single(sheet.Pages);
        Assert.Equal(1, placed.CellIndex); // landed in the second cell, not the first
    }

    [Fact]
    public void Validate_RejectsMarginsLargerThanSheet()
    {
        var settings = ImpositionSettings.NUp(1, 1) with
        {
            SheetSize = PaperSizes.A4,
            Margins = PtMargins.Uniform(1000),
        };
        Assert.Throws<ArgumentException>(() =>
            new NUpImposer().Impose(new[] { Page(PaperSizes.A4) }, settings));
    }
}
