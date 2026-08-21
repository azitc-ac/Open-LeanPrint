using System.Globalization;

namespace OpenLeanPrint.Compose;

/// <summary>
/// A thin frame drawn around every page placed on a sheet.
/// <para>
/// It earns its place on N-up output: four pages on one sheet with nothing but
/// white between them read as one crowded page, and a hairline is what tells the
/// eye where one page ends and the next begins. It follows the page, not the
/// cell, so a page that does not fill its cell is still framed at its own edges.
/// </para>
/// </summary>
public sealed record PageBorder
{
    /// <summary>Line width in points. 0 or less draws nothing.</summary>
    public double WidthPt { get; init; } = 0.75;

    /// <summary>Colour as <c>#RRGGBB</c>.</summary>
    public string ColorHex { get; init; } = "#9A9AA2";

    /// <summary>Nothing to draw?</summary>
    public bool IsEmpty => WidthPt <= 0;

    /// <summary>Parses <see cref="ColorHex"/>; grey when it is not valid.</summary>
    public (byte R, byte G, byte B) Color() => ParseHex(ColorHex);

    /// <summary>Shared with <see cref="Watermark"/>: both take a <c>#RRGGBB</c> string.</summary>
    internal static (byte R, byte G, byte B) ParseHex(string colorHex)
    {
        string hex = colorHex.TrimStart('#');
        if (hex.Length == 6 &&
            byte.TryParse(hex[..2], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte r) &&
            byte.TryParse(hex[2..4], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte g) &&
            byte.TryParse(hex[4..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte b))
            return (r, g, b);
        return (0x80, 0x80, 0x80);
    }
}
