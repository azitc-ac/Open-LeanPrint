namespace OpenLeanPrint.Core;

/// <summary>
/// All geometry in OpenLeanPrint.Core is expressed in PostScript points (1/72 inch),
/// the natural unit of PDF/XPS print output.
/// <para>
/// The coordinate system used by the imposition engine is <b>top-left origin</b>:
/// X increases to the right, Y increases downward. This matches most 2D UI
/// toolkits (WinUI/WPF/GDI). The rendering layer converts to PDF's bottom-left
/// origin when it draws.
/// </para>
/// </summary>
public static class Units
{
    /// <summary>Points per inch.</summary>
    public const double PointsPerInch = 72.0;

    /// <summary>Points per millimetre (72 / 25.4).</summary>
    public const double PointsPerMm = PointsPerInch / 25.4;

    public static double MmToPoints(double mm) => mm * PointsPerMm;
    public static double PointsToMm(double pt) => pt / PointsPerMm;
    public static double InchesToPoints(double inches) => inches * PointsPerInch;
}

/// <summary>A width/height pair in points.</summary>
public readonly record struct PtSize(double Width, double Height)
{
    /// <summary>Returns this size with width and height swapped (a 90° rotation of the bounding box).</summary>
    public PtSize Rotated() => new(Height, Width);

    /// <summary>Aspect ratio (width / height). Undefined for zero height.</summary>
    public double Aspect => Width / Height;

    public bool IsPositive => Width > 0 && Height > 0;
}

/// <summary>An axis-aligned rectangle in points, top-left origin.</summary>
public readonly record struct PtRect(double X, double Y, double Width, double Height)
{
    public double Right => X + Width;
    public double Bottom => Y + Height;
    public PtSize Size => new(Width, Height);

    public static PtRect FromSize(PtSize size) => new(0, 0, size.Width, size.Height);
}

/// <summary>Page margins in points.</summary>
public readonly record struct PtMargins(double Left, double Top, double Right, double Bottom)
{
    public static readonly PtMargins Zero = new(0, 0, 0, 0);

    /// <summary>Same margin on all four sides.</summary>
    public static PtMargins Uniform(double all) => new(all, all, all, all);

    /// <summary>Margins from a value given in millimetres, applied uniformly.</summary>
    public static PtMargins UniformMm(double mm) => Uniform(Units.MmToPoints(mm));
}
