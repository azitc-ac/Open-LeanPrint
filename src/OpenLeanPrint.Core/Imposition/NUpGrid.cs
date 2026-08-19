using System.Globalization;

namespace OpenLeanPrint.Core.Imposition;

/// <summary>
/// Reads and writes the way people describe an N-up grid: <c>2x2</c> for rows ×
/// columns, or just a count like <c>4</c>. Shared by the CLI and the app so both
/// understand exactly the same thing.
/// </summary>
public static class NUpGrid
{
    /// <summary>
    /// Parses <c>RxC</c> (e.g. <c>2x3</c>) or a plain page count (e.g. <c>6</c>).
    /// A count is turned into the grid people mean by it — 6 is two rows of
    /// three, not one row of six — falling back to a single row for counts with
    /// no obvious shape.
    /// </summary>
    public static bool TryParse(string? text, out int rows, out int columns)
    {
        rows = columns = 0;
        string trimmed = text?.Trim() ?? string.Empty;
        if (trimmed.Length == 0) return false;

        var parts = trimmed.Split('x', 'X');
        if (parts.Length == 2)
        {
            if (!TryPositive(parts[0], out rows) || !TryPositive(parts[1], out columns))
            {
                rows = columns = 0;
                return false;
            }
            return true;
        }

        if (parts.Length == 1 && TryPositive(parts[0], out int count))
        {
            (rows, columns) = count switch
            {
                1 => (1, 1),
                2 => (1, 2),
                3 => (1, 3),
                4 => (2, 2),
                6 => (2, 3),
                8 => (2, 4),
                9 => (3, 3),
                12 => (3, 4),
                16 => (4, 4),
                _ => (1, count),
            };
            return true;
        }

        return false;

        static bool TryPositive(string value, out int number) =>
            int.TryParse(value.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out number) && number >= 1;
    }

    /// <summary>The canonical spelling of a grid, e.g. <c>2x3</c>.</summary>
    public static string Format(int rows, int columns) =>
        string.Create(CultureInfo.InvariantCulture, $"{rows}x{columns}");
}
