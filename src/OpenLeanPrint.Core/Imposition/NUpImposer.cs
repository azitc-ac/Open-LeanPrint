namespace OpenLeanPrint.Core.Imposition;

/// <summary>
/// Lays a flat sequence of source pages onto sheets in an N-up grid
/// (1-up, 2-up, 4-up, arbitrary rows × columns), computing the exact position,
/// scale and rotation of every page. This is the geometric heart of OpenLeanPrint
/// and is deliberately free of any rendering or platform dependency.
/// </summary>
public sealed class NUpImposer
{
    /// <summary>
    /// Imposes <paramref name="pages"/> according to <paramref name="settings"/>.
    /// A null entry in <paramref name="pages"/> leaves its cell blank but still
    /// consumes it (used for booklet padding and explicit blanks).
    /// </summary>
    public ImpositionResult Impose(IReadOnlyList<SourcePage?> pages, ImpositionSettings settings)
    {
        ArgumentNullException.ThrowIfNull(pages);
        ArgumentNullException.ThrowIfNull(settings);
        settings.Validate();

        var sheetSize = settings.EffectiveSheetSize();
        int cols = settings.Columns;
        int rows = settings.Rows;
        int cellsPerSheet = settings.CellsPerSheet;

        double contentW = sheetSize.Width - settings.Margins.Left - settings.Margins.Right;
        double contentH = sheetSize.Height - settings.Margins.Top - settings.Margins.Bottom;
        double cellW = (contentW - (cols - 1) * settings.GutterX) / cols;
        double cellH = (contentH - (rows - 1) * settings.GutterY) / rows;

        var sheets = new List<Sheet>();
        Sheet? current = null;

        for (int i = 0; i < pages.Count; i++)
        {
            int cellIndex = i % cellsPerSheet;
            if (cellIndex == 0)
            {
                current = new Sheet { Size = sheetSize };
                sheets.Add(current);
            }

            var page = pages[i];
            if (page is null)
                continue; // blank cell

            var (row, col) = ResolveCell(cellIndex, rows, cols, settings.Order);
            double cellX = settings.Margins.Left + col * (cellW + settings.GutterX);
            double cellY = settings.Margins.Top + row * (cellH + settings.GutterY);

            // A page turned by the user is fitted at its new proportions, and
            // auto-rotation stands down so it cannot undo the request.
            bool turned = page.Rotation != 0;
            bool quarterTurn = NormalizeRotation(page.Rotation) is 90 or 270;
            var fitSize = quarterTurn ? page.Size.Rotated() : page.Size;

            var (dest, rotation) = FitPage(fitSize, cellX, cellY, cellW, cellH,
                                           settings.ScaleMode,
                                           settings.AllowRotation && !turned);

            current!.Add(new PlacedPage
            {
                Source = page,
                DestRect = dest,
                Rotation = NormalizeRotation(rotation + page.Rotation),
                CellIndex = cellIndex,
            });
        }

        return new ImpositionResult { Sheets = sheets };
    }

    /// <summary>Brings any angle into the 0/90/180/270 the renderer understands.</summary>
    internal static int NormalizeRotation(int degrees)
    {
        int normalized = degrees % 360;
        if (normalized < 0) normalized += 360;
        // Round to the nearest quarter turn: the placement maths only handles those.
        return ((normalized + 45) / 90 % 4) * 90;
    }

    /// <summary>Maps a linear cell index to a (row, column) pair for the given fill order.</summary>
    internal static (int Row, int Col) ResolveCell(int cellIndex, int rows, int cols, PageOrder order)
        => order switch
        {
            PageOrder.ColumnMajor => (cellIndex % rows, cellIndex / rows),
            _ => (cellIndex / cols, cellIndex % cols), // RowMajor
        };

    /// <summary>
    /// Computes the placement rectangle and rotation for a single page inside a cell.
    /// </summary>
    internal static (PtRect Dest, int Rotation) FitPage(
        PtSize pageSize, double cellX, double cellY, double cellW, double cellH,
        ScaleMode mode, bool allowRotation)
    {
        bool rotate = false;
        var effective = pageSize;

        if (allowRotation && mode != ScaleMode.Stretch)
        {
            double scaleNoRot = FitScale(pageSize, cellW, cellH, mode);
            double scaleRot = FitScale(pageSize.Rotated(), cellW, cellH, mode);
            if (scaleRot > scaleNoRot)
            {
                rotate = true;
                effective = pageSize.Rotated();
            }
        }

        double drawW, drawH;
        if (mode == ScaleMode.Stretch)
        {
            drawW = cellW;
            drawH = cellH;
        }
        else
        {
            double scale = FitScale(effective, cellW, cellH, mode);
            drawW = effective.Width * scale;
            drawH = effective.Height * scale;
        }

        // Centre within the cell.
        double x = cellX + (cellW - drawW) / 2.0;
        double y = cellY + (cellH - drawH) / 2.0;

        return (new PtRect(x, y, drawW, drawH), rotate ? 90 : 0);
    }

    /// <summary>Uniform scale factor for a page in a cell under a scale mode.</summary>
    internal static double FitScale(PtSize page, double cellW, double cellH, ScaleMode mode)
    {
        double sx = cellW / page.Width;
        double sy = cellH / page.Height;
        return mode switch
        {
            ScaleMode.Fill => Math.Max(sx, sy),
            ScaleMode.ActualSize => 1.0,
            _ => Math.Min(sx, sy), // Fit (Stretch handled by the caller)
        };
    }
}
