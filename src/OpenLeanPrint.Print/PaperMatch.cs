using OpenLeanPrint.Core;

namespace OpenLeanPrint.Print;

/// <summary>
/// One paper size offered by a printer driver, in hundredths of an inch — the
/// unit <see cref="System.Drawing.Printing.PaperSize"/> uses.
/// </summary>
public readonly record struct PaperCandidate(string Name, double WidthHundredths, double HeightHundredths);

/// <summary>
/// Picks the driver paper size that matches an imposed sheet, so an A4 sheet is
/// printed on A4 rather than on whatever the driver defaults to. Pure logic,
/// testable without a printer.
/// </summary>
public static class PaperMatch
{
    /// <summary>Hundredths of an inch a candidate may differ per axis (~3 mm).</summary>
    public const double DefaultTolerance = 12;

    /// <summary>Hundredths of an inch per point.</summary>
    private const double HundredthsPerPoint = 100.0 / Units.PointsPerInch;

    /// <summary>
    /// Index of the closest candidate to a sheet of <paramref name="widthPt"/> ×
    /// <paramref name="heightPt"/> points, or -1 if none is within
    /// <paramref name="tolerance"/> on both axes. Both sides are compared in
    /// portrait orientation — orientation is a page setting, not a paper size.
    /// </summary>
    public static int BestIndex(IReadOnlyList<PaperCandidate> candidates, double widthPt, double heightPt,
                               double tolerance = DefaultTolerance)
    {
        ArgumentNullException.ThrowIfNull(candidates);

        double wanted = Math.Min(widthPt, heightPt) * HundredthsPerPoint;
        double wantedLong = Math.Max(widthPt, heightPt) * HundredthsPerPoint;

        int best = -1;
        double bestDistance = double.MaxValue;
        for (int i = 0; i < candidates.Count; i++)
        {
            var c = candidates[i];
            double shortSide = Math.Min(c.WidthHundredths, c.HeightHundredths);
            double longSide = Math.Max(c.WidthHundredths, c.HeightHundredths);
            if (shortSide <= 0 || longSide <= 0) continue; // driver placeholder ("Custom")

            double dShort = Math.Abs(shortSide - wanted);
            double dLong = Math.Abs(longSide - wantedLong);
            if (dShort > tolerance || dLong > tolerance) continue;

            double distance = dShort + dLong;
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = i;
            }
        }
        return best;
    }
}
