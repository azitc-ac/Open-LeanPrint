namespace OpenLeanPrint.Print;

/// <summary>A rectangle in whatever unit the caller used (top-left origin).</summary>
public readonly record struct FitRect(double X, double Y, double Width, double Height);

/// <summary>
/// Maps an imposed sheet onto a printer page. Pure geometry, unit-agnostic
/// (points, hundredths of an inch, pixels — as long as both sides use the same
/// unit), so it is testable on any OS.
/// </summary>
public static class PagePlacement
{
    /// <summary>
    /// Largest rectangle with the sheet's aspect ratio that fits the page,
    /// centred. When sheet and page match (the normal case — an A4 sheet on A4
    /// paper) this is the full page, so the layout maps 1:1 onto the paper.
    /// </summary>
    public static FitRect Fit(double sheetWidth, double sheetHeight, double pageWidth, double pageHeight)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sheetWidth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sheetHeight);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(pageWidth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(pageHeight);

        double scale = Math.Min(pageWidth / sheetWidth, pageHeight / sheetHeight);
        double w = sheetWidth * scale;
        double h = sheetHeight * scale;
        return new FitRect((pageWidth - w) / 2, (pageHeight - h) / 2, w, h);
    }
}
