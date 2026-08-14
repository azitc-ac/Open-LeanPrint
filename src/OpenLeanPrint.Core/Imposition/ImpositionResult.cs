namespace OpenLeanPrint.Core.Imposition;

/// <summary>
/// One source page placed on a sheet: which page, where it goes, how it is
/// scaled and whether it is rotated. <see cref="DestRect"/> is the axis-aligned
/// box (top-left origin, points) the drawn page occupies; when
/// <see cref="Rotation"/> is 90 the renderer draws the source rotated within it.
/// </summary>
public sealed record PlacedPage
{
    public required SourcePage Source { get; init; }

    /// <summary>Target rectangle on the sheet, in points (top-left origin).</summary>
    public required PtRect DestRect { get; init; }

    /// <summary>Rotation applied to the source page in degrees: 0 or 90.</summary>
    public int Rotation { get; init; }

    /// <summary>Zero-based cell index on the sheet this page occupies.</summary>
    public int CellIndex { get; init; }
}

/// <summary>A single output sheet: its media size and the pages placed on it.</summary>
public sealed record Sheet
{
    public required PtSize Size { get; init; }

    public IReadOnlyList<PlacedPage> Pages => _pages;
    private readonly List<PlacedPage> _pages = new();

    internal void Add(PlacedPage page) => _pages.Add(page);
}

/// <summary>The full result of imposing a page sequence: the produced sheets.</summary>
public sealed record ImpositionResult
{
    public required IReadOnlyList<Sheet> Sheets { get; init; }

    public int SheetCount => Sheets.Count;
}
