namespace OpenLeanPrint.Core;

/// <summary>
/// Common paper sizes as portrait <see cref="PtSize"/> values in points.
/// Use <see cref="PtSize.Rotated"/> for the landscape variant.
/// </summary>
public static class PaperSizes
{
    // ISO A series (portrait). Values rounded to the nearest point, as most
    // print pipelines do.
    public static PtSize A6 { get; } = new(298, 420);
    public static PtSize A5 { get; } = new(420, 595);
    public static PtSize A4 { get; } = new(595, 842);
    public static PtSize A3 { get; } = new(842, 1191);

    // North American sizes.
    public static PtSize Letter { get; } = new(612, 792);
    public static PtSize Legal { get; } = new(612, 1008);
    public static PtSize Tabloid { get; } = new(792, 1224);

    /// <summary>Look up a size by case-insensitive name (e.g. "A4", "Letter"); null if unknown.</summary>
    public static PtSize? ByName(string name) => name?.Trim().ToUpperInvariant() switch
    {
        "A6" => A6,
        "A5" => A5,
        "A4" => A4,
        "A3" => A3,
        "LETTER" => Letter,
        "LEGAL" => Legal,
        "TABLOID" or "LEDGER" => Tabloid,
        _ => null,
    };
}
