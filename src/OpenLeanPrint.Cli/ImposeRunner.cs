using OpenLeanPrint.Compose;
using OpenLeanPrint.Core;
using OpenLeanPrint.Core.Imposition;

namespace OpenLeanPrint.Cli;

/// <summary>The layout options shared by the `impose` and `watch` commands.</summary>
internal sealed record ImposeOptions
{
    public bool Booklet { get; init; }
    public int Rows { get; init; } = 2;
    public int Cols { get; init; } = 2;
    public PtSize Paper { get; init; } = PaperSizes.A4;
    public string PaperName { get; init; } = "A4";
    public double MarginMm { get; init; }
    public double Gutter { get; init; }
    public PageSelection Pages { get; init; } = PageSelection.All;

    /// <summary>Short human-readable form, e.g. "2x2-up on A4".</summary>
    public string Describe()
    {
        string layout = Booklet ? $"booklet on {PaperName}" : $"{Rows}x{Cols}-up on {PaperName}";
        return Pages.IsAll ? layout : $"{layout}, pages {Pages}";
    }

    /// <summary>File-name tag for imposed output, e.g. "2x2up".</summary>
    public string FileTag() => Booklet ? "booklet" : $"{Rows}x{Cols}up";
}

/// <summary>Applies <see cref="ImposeOptions"/> to a source PDF.</summary>
internal static class ImposeRunner
{
    public static byte[] Run(byte[] source, ImposeOptions options)
    {
        var imposer = new PdfImposer();
        var sources = new[] { source };
        var selections = new[] { options.Pages };

        if (options.Booklet)
        {
            return imposer.ImposeBookletToPdf(sources, options.Paper, PtMargins.UniformMm(options.MarginMm),
                                              options.Gutter, selections);
        }

        var settings = ImpositionSettings.NUp(options.Rows, options.Cols) with
        {
            SheetSize = options.Paper,
            Margins = PtMargins.UniformMm(options.MarginMm),
            GutterX = options.Gutter,
            GutterY = options.Gutter,
        };
        return imposer.ImposeToPdf(sources, settings, selections);
    }

    public static ImposeOptions Parse(ArgMap options)
    {
        string paperName = options.Get("paper", "A4");
        var (rows, cols) = ParseNUp(options.Get("nup", "2x2"));
        string pagesText = options.Get("pages", string.Empty);
        if (!PageSelection.TryParse(pagesText, out var pages))
            throw new ArgumentException($"Invalid --pages value '{pagesText}'. Use e.g. 1-4,7 or 3-.");
        return new ImposeOptions
        {
            Booklet = options.Has("booklet"),
            Rows = rows,
            Cols = cols,
            Paper = PaperSizes.ByName(paperName) ?? PaperSizes.A4,
            PaperName = paperName,
            MarginMm = options.GetDouble("margin", 0),
            Gutter = options.GetDouble("gutter", 0),
            Pages = pages,
        };
    }

    public static (int Rows, int Cols) ParseNUp(string s)
    {
        var parts = s.ToLowerInvariant().Split('x', 'X');
        if (parts.Length == 2 && int.TryParse(parts[0], out int r) && int.TryParse(parts[1], out int c) && r > 0 && c > 0)
            return (r, c);
        // Allow a single number: 4 -> 2x2, 2 -> 1x2, 9 -> 3x3, 6 -> 2x3.
        if (int.TryParse(s, out int n) && n > 0)
            return n switch { 1 => (1, 1), 2 => (1, 2), 4 => (2, 2), 6 => (2, 3), 9 => (3, 3), 16 => (4, 4), _ => (1, n) };
        throw new ArgumentException($"Invalid --nup value '{s}'. Use RxC (e.g. 2x2) or a count (e.g. 4).");
    }
}
