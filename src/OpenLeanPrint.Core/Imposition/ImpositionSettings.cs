namespace OpenLeanPrint.Core.Imposition;

/// <summary>Order in which source pages fill the cells of a sheet.</summary>
public enum PageOrder
{
    /// <summary>Left-to-right, then top-to-bottom (the usual N-up order).</summary>
    RowMajor,

    /// <summary>Top-to-bottom, then left-to-right.</summary>
    ColumnMajor,
}

/// <summary>How a source page is scaled to fit its cell.</summary>
public enum ScaleMode
{
    /// <summary>Scale uniformly so the whole page fits inside the cell (letterboxed). The default.</summary>
    Fit,

    /// <summary>Scale uniformly so the cell is fully covered; overflow is clipped by the renderer.</summary>
    Fill,

    /// <summary>Scale non-uniformly to exactly fill the cell (distorts aspect ratio).</summary>
    Stretch,

    /// <summary>No scaling; place at 100% and centre (may clip if larger than the cell).</summary>
    ActualSize,
}

/// <summary>Sheet orientation independent of the paper's stored dimensions.</summary>
public enum SheetOrientation
{
    /// <summary>Use the paper size as given.</summary>
    AsIs,
    Portrait,
    Landscape,
}

/// <summary>
/// Everything the <see cref="NUpImposer"/> needs to lay pages onto sheets.
/// </summary>
public sealed record ImpositionSettings
{
    /// <summary>Number of rows of cells per sheet. Must be >= 1.</summary>
    public int Rows { get; init; } = 1;

    /// <summary>Number of columns of cells per sheet. Must be >= 1.</summary>
    public int Columns { get; init; } = 1;

    /// <summary>Target sheet media size, in points (portrait dimensions unless already rotated).</summary>
    public PtSize SheetSize { get; init; } = PaperSizes.A4;

    /// <summary>Forces the sheet into portrait/landscape regardless of <see cref="SheetSize"/>.</summary>
    public SheetOrientation Orientation { get; init; } = SheetOrientation.AsIs;

    /// <summary>Outer margins of the printable area.</summary>
    public PtMargins Margins { get; init; } = PtMargins.Zero;

    /// <summary>Horizontal spacing between adjacent columns, in points.</summary>
    public double GutterX { get; init; }

    /// <summary>Vertical spacing between adjacent rows, in points.</summary>
    public double GutterY { get; init; }

    /// <summary>Cell fill order.</summary>
    public PageOrder Order { get; init; } = PageOrder.RowMajor;

    /// <summary>How each page is scaled within its cell.</summary>
    public ScaleMode ScaleMode { get; init; } = ScaleMode.Fit;

    /// <summary>
    /// When true, a page may be rotated 90° if that lets it be drawn larger
    /// within its cell (e.g. two portrait pages 2-up on a portrait sheet).
    /// </summary>
    public bool AllowRotation { get; init; } = true;

    /// <summary>Number of cells on one sheet.</summary>
    public int CellsPerSheet => Rows * Columns;

    /// <summary>Convenience factory for an "N up" grid, e.g. 2-up as (1 row, 2 cols).</summary>
    public static ImpositionSettings NUp(int rows, int columns) =>
        new() { Rows = rows, Columns = columns };

    /// <summary>The sheet size after applying <see cref="Orientation"/>.</summary>
    public PtSize EffectiveSheetSize()
    {
        bool isLandscape = SheetSize.Width > SheetSize.Height;
        return Orientation switch
        {
            SheetOrientation.Portrait => isLandscape ? SheetSize.Rotated() : SheetSize,
            SheetOrientation.Landscape => isLandscape ? SheetSize : SheetSize.Rotated(),
            _ => SheetSize,
        };
    }

    /// <summary>Throws <see cref="ArgumentException"/> if the settings cannot produce a valid layout.</summary>
    public void Validate()
    {
        if (Rows < 1) throw new ArgumentException("Rows must be >= 1.", nameof(Rows));
        if (Columns < 1) throw new ArgumentException("Columns must be >= 1.", nameof(Columns));
        if (!SheetSize.IsPositive) throw new ArgumentException("SheetSize must be positive.", nameof(SheetSize));
        if (GutterX < 0 || GutterY < 0) throw new ArgumentException("Gutters must be >= 0.");

        var sheet = EffectiveSheetSize();
        double contentW = sheet.Width - Margins.Left - Margins.Right - (Columns - 1) * GutterX;
        double contentH = sheet.Height - Margins.Top - Margins.Bottom - (Rows - 1) * GutterY;
        if (contentW <= 0 || contentH <= 0)
            throw new ArgumentException("Margins/gutters leave no room for cells on this sheet.");
    }
}
