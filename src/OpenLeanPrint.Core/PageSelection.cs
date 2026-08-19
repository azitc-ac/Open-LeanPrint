using System.Globalization;

namespace OpenLeanPrint.Core;

/// <summary>
/// Which pages of a document to keep — "1-4,7,9-" and friends. This is how
/// pages get dropped before imposition: pooling six pages but printing only
/// four of them is a page-count saving that no layout can match.
/// <para>
/// Page numbers are 1-based, the way they are printed and the way people say
/// them. Ranges may be open at either end.
/// </para>
/// </summary>
public sealed class PageSelection
{
    private readonly IReadOnlyList<(int From, int To)> _ranges;

    private PageSelection(IReadOnlyList<(int From, int To)> ranges) => _ranges = ranges;

    /// <summary>Keeps every page.</summary>
    public static PageSelection All { get; } = new(Array.Empty<(int, int)>());

    /// <summary>True when this selection keeps everything.</summary>
    public bool IsAll => _ranges.Count == 0;

    /// <summary>
    /// Parses a range list such as <c>1-4,7,9-</c>. An empty or missing value,
    /// or <c>all</c>, means every page. Returns false on anything unparseable
    /// rather than guessing.
    /// </summary>
    public static bool TryParse(string? text, out PageSelection selection)
    {
        selection = All;
        string trimmed = text?.Trim() ?? string.Empty;
        if (trimmed.Length == 0 || trimmed.Equals("all", StringComparison.OrdinalIgnoreCase)) return true;

        var ranges = new List<(int From, int To)>();
        // Deliberately not RemoveEmptyEntries: "1,,2" is a typo, and this parser
        // rejects rather than guesses.
        foreach (string part in trimmed.Split(','))
        {
            string piece = part.Trim();
            if (piece.Length == 0) return false;

            int dash = piece.IndexOf('-');
            if (dash < 0)
            {
                if (!TryPage(piece, out int single)) return false;
                ranges.Add((single, single));
                continue;
            }

            string left = piece[..dash].Trim();
            string right = piece[(dash + 1)..].Trim();
            int from = 1, to = int.MaxValue;
            if (left.Length > 0 && !TryPage(left, out from)) return false;
            if (right.Length > 0 && !TryPage(right, out to)) return false;
            if (left.Length == 0 && right.Length == 0) return false; // a bare "-"
            if (to < from) return false;
            ranges.Add((from, to));
        }

        if (ranges.Count == 0) return false;
        selection = new PageSelection(ranges);
        return true;

        static bool TryPage(string text, out int value) =>
            int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value) && value >= 1;
    }

    /// <summary>Whether a 1-based page number is kept.</summary>
    public bool Includes(int pageNumber)
    {
        if (IsAll) return true;
        foreach (var (from, to) in _ranges)
            if (pageNumber >= from && pageNumber <= to) return true;
        return false;
    }

    /// <summary>
    /// Keeps the pages this selection includes, judged by
    /// <see cref="SourcePage.PageIndex"/> within its own document.
    /// </summary>
    public IReadOnlyList<SourcePage> Filter(IReadOnlyList<SourcePage> pages)
    {
        ArgumentNullException.ThrowIfNull(pages);
        if (IsAll) return pages;

        var kept = new List<SourcePage>(pages.Count);
        foreach (var page in pages)
            if (Includes(page.PageIndex + 1)) kept.Add(page);
        return kept;
    }

    /// <summary>Round-trips back to the notation it was parsed from.</summary>
    public override string ToString() =>
        IsAll ? "all" : string.Join(",", _ranges.Select(r =>
            r.From == r.To ? r.From.ToString(CultureInfo.InvariantCulture)
            : r.To == int.MaxValue ? $"{r.From}-"
            : $"{r.From}-{r.To}"));
}
