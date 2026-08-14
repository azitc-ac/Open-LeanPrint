namespace OpenLeanPrint.Core.Imposition;

/// <summary>
/// Produces a saddle-stitch booklet: pages are reordered and placed 2-up on
/// landscape sheets so that, once every sheet is printed double-sided, stacked
/// and folded down the middle, the pages read in their original order.
/// </summary>
public sealed class BookletImposer
{
    /// <summary>
    /// Builds the flat cell sequence for a booklet of <paramref name="pageCount"/>
    /// logical pages. The result length is a multiple of 4; entries are zero-based
    /// source page indices, or <c>null</c> for blank padding pages. Feeding this
    /// sequence into a 2-up (1×2) <see cref="NUpImposer"/> pass yields the booklet.
    /// </summary>
    /// <example>
    /// For 4 pages the order is [3, 0, 1, 2] → sheet front = pages 4,1; sheet back = pages 2,3.
    /// </example>
    public static IReadOnlyList<int?> BuildOrder(int pageCount)
    {
        if (pageCount < 0) throw new ArgumentOutOfRangeException(nameof(pageCount));
        if (pageCount == 0) return Array.Empty<int?>();

        int padded = ((pageCount + 3) / 4) * 4; // round up to a multiple of 4
        var order = new List<int?>(padded);

        int lo = 0;
        int hi = padded - 1;
        bool flip = false;
        while (lo < hi)
        {
            if (!flip)
            {
                order.Add(Slot(hi, pageCount));
                order.Add(Slot(lo, pageCount));
            }
            else
            {
                order.Add(Slot(lo, pageCount));
                order.Add(Slot(hi, pageCount));
            }
            lo++;
            hi--;
            flip = !flip;
        }
        return order;

        static int? Slot(int index, int count) => index < count ? index : (int?)null;
    }

    /// <summary>
    /// Imposes a booklet from the given source pages onto <paramref name="sheetSize"/>
    /// (portrait media; it is placed in landscape and split into two half-pages).
    /// </summary>
    public ImpositionResult Impose(IReadOnlyList<SourcePage> pages, PtSize sheetSize,
                                   PtMargins margins = default, double gutter = 0)
    {
        ArgumentNullException.ThrowIfNull(pages);

        var order = BuildOrder(pages.Count);
        var sequence = new List<SourcePage?>(order.Count);
        foreach (var slot in order)
            sequence.Add(slot is int idx ? pages[idx] : null);

        var settings = new ImpositionSettings
        {
            Rows = 1,
            Columns = 2,
            SheetSize = sheetSize,
            Orientation = SheetOrientation.Landscape,
            Margins = margins,
            GutterX = gutter,
            Order = PageOrder.RowMajor,
            ScaleMode = ScaleMode.Fit,
            AllowRotation = true,
        };

        return new NUpImposer().Impose(sequence, settings);
    }
}
