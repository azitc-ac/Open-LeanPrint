using System.Globalization;

namespace OpenLeanPrint.Compose;

/// <summary>
/// A text watermark drawn across every imposed sheet — "DRAFT", "CONFIDENTIAL",
/// a file name. Drawn on top of the placed pages, so it survives whatever the
/// source documents contain.
/// </summary>
public sealed record Watermark
{
    /// <summary>The text. An empty watermark is simply not drawn.</summary>
    public required string Text { get; init; }

    /// <summary>0 = invisible, 1 = solid. Low values keep the pages readable.</summary>
    public double Opacity { get; init; } = 0.18;

    /// <summary>Rotation in degrees; the default runs bottom-left to top-right.</summary>
    public double AngleDegrees { get; init; } = -45;

    /// <summary>Font family name. Falls back to a generic face if unavailable.</summary>
    public string FontFamily { get; init; } = "Arial";

    /// <summary>Font size in points, or 0 to size it to the sheet automatically.</summary>
    public double FontSize { get; init; }

    /// <summary>Colour as <c>#RRGGBB</c>.</summary>
    public string ColorHex { get; init; } = "#808080";

    /// <summary>Nothing to draw?</summary>
    public bool IsEmpty => string.IsNullOrWhiteSpace(Text);

    /// <summary>Parses <see cref="ColorHex"/>; grey when it is not valid.</summary>
    public (byte R, byte G, byte B) Color()
    {
        string hex = ColorHex.TrimStart('#');
        if (hex.Length == 6 &&
            byte.TryParse(hex[..2], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte r) &&
            byte.TryParse(hex[2..4], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte g) &&
            byte.TryParse(hex[4..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte b))
            return (r, g, b);
        return (0x80, 0x80, 0x80);
    }

    /// <summary>
    /// Font size that makes the text span most of the sheet's diagonal, so the
    /// same watermark looks right on A5 and on A3.
    /// </summary>
    public double EffectiveFontSize(double sheetWidth, double sheetHeight)
    {
        if (FontSize > 0) return FontSize;

        double diagonal = Math.Sqrt(sheetWidth * sheetWidth + sheetHeight * sheetHeight);
        // Rough advance width per point of font size for upper-case text; the
        // caller measures properly and scales, this is the starting point.
        int length = Math.Max(1, Text.Trim().Length);
        return Math.Clamp(diagonal * 0.85 / (length * 0.62), 8, 400);
    }
}
