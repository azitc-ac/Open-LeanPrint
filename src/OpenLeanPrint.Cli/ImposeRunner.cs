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
    public Watermark? Watermark { get; init; }
    public int Rotate { get; init; }

    /// <summary>Short human-readable form, e.g. "2x2-up on A4".</summary>
    public string Describe()
    {
        string layout = Booklet ? $"booklet on {PaperName}" : $"{Rows}x{Cols}-up on {PaperName}";
        if (!Pages.IsAll) layout += $", pages {Pages}";
        if (Rotate != 0) layout += $", rotated {Rotate}°";
        if (Watermark is { IsEmpty: false } mark) layout += $", watermarked \"{mark.Text}\"";
        return layout;
    }

    /// <summary>File-name tag for imposed output, e.g. "2x2up".</summary>
    public string FileTag() => Booklet ? "booklet" : $"{Rows}x{Cols}up";
}

/// <summary>Applies <see cref="ImposeOptions"/> to a source PDF.</summary>
internal static class ImposeRunner
{
    public static byte[] Run(byte[] source, ImposeOptions options)
    {
        var imposer = new PdfImposer { Watermark = options.Watermark };
        var sources = new[] { source };
        var selections = new[] { options.Pages };

        var pages = PdfImposer.ReadPageSizes(sources, selections);
        if (options.Rotate != 0)
            pages = pages.Select(page => page with { Rotation = options.Rotate }).ToList();

        var result = options.Booklet
            ? new BookletImposer().Impose(pages, options.Paper, PtMargins.UniformMm(options.MarginMm), options.Gutter)
            : new NUpImposer().Impose(pages, ImpositionSettings.NUp(options.Rows, options.Cols) with
            {
                SheetSize = options.Paper,
                Margins = PtMargins.UniformMm(options.MarginMm),
                GutterX = options.Gutter,
                GutterY = options.Gutter,
            });

        return imposer.Compose(sources, result);
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
            Watermark = ParseWatermark(options),
            Rotate = (int)options.GetDouble("rotate", 0),
        };
    }

    private static Watermark? ParseWatermark(ArgMap options)
    {
        string text = options.Get("watermark", string.Empty);
        if (string.IsNullOrWhiteSpace(text)) return null;

        return new Watermark
        {
            Text = text,
            Opacity = options.GetDouble("watermark-opacity", 0.18),
            FontSize = options.GetDouble("watermark-size", 0),
            AngleDegrees = options.GetDouble("watermark-angle", -45),
            ColorHex = options.Get("watermark-color", "#808080"),
        };
    }

    public static (int Rows, int Cols) ParseNUp(string s)
    {
        if (NUpGrid.TryParse(s, out int rows, out int columns)) return (rows, columns);
        throw new ArgumentException($"Invalid --nup value '{s}'. Use RxC (e.g. 2x2) or a count (e.g. 4).");
    }
}
