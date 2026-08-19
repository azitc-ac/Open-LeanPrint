namespace OpenLeanPrint.Print;

/// <summary>How the printer should put the sheets on paper.</summary>
public enum DuplexMode
{
    /// <summary>Leave it to the driver's own default.</summary>
    Default,

    /// <summary>One side only.</summary>
    Simplex,

    /// <summary>
    /// Two-sided, flipped along the long edge — the usual "like a book" setting
    /// for portrait documents.
    /// </summary>
    LongEdge,

    /// <summary>
    /// Two-sided, flipped along the short edge. This is what a saddle-stitch
    /// booklet needs: its sheets are landscape, so flipping the long edge would
    /// print every second side upside down.
    /// </summary>
    ShortEdge,
}

/// <summary>Parsing for <see cref="DuplexMode"/>, shared by the CLI and the app.</summary>
public static class DuplexModes
{
    /// <summary>
    /// Accepts the spellings a person is likely to type: <c>off</c>, <c>long</c>,
    /// <c>short</c>, <c>booklet</c>, <c>auto</c>…
    /// </summary>
    public static bool TryParse(string? text, out DuplexMode mode)
    {
        switch (text?.Trim().ToLowerInvariant())
        {
            case null or "" or "auto" or "default" or "printer":
                mode = DuplexMode.Default;
                return true;
            case "off" or "no" or "none" or "simplex" or "one" or "1":
                mode = DuplexMode.Simplex;
                return true;
            case "long" or "longedge" or "long-edge" or "book" or "vertical" or "on" or "yes" or "2":
                mode = DuplexMode.LongEdge;
                return true;
            case "short" or "shortedge" or "short-edge" or "booklet" or "flip" or "horizontal":
                mode = DuplexMode.ShortEdge;
                return true;
            default:
                mode = DuplexMode.Default;
                return false;
        }
    }

    /// <summary>The mode a layout wants when the user has not said otherwise.</summary>
    public static DuplexMode PreferredFor(bool booklet) =>
        booklet ? DuplexMode.ShortEdge : DuplexMode.LongEdge;
}

/// <summary>Options for sending an imposed PDF to a printer.</summary>
public sealed record PrintOptions
{
    /// <summary>Number of copies to request from the driver.</summary>
    public int Copies { get; init; } = 1;

    /// <summary>
    /// Resolution the sheets are rasterised at before being handed to the
    /// spooler. 200 dpi is a good balance of quality and memory; 300 dpi is
    /// visibly better for fine text at the cost of ~2.5x the pixels.
    /// </summary>
    public int Dpi { get; init; } = 200;

    /// <summary>
    /// When set, the job is written to this file instead of going to paper.
    /// Only meaningful for "print to file" drivers such as
    /// <c>Microsoft Print to PDF</c>, where it also suppresses the save dialog.
    /// </summary>
    public string? OutputFile { get; init; }

    /// <summary>Job name shown in the Windows print queue.</summary>
    public string? JobName { get; init; }

    /// <summary>
    /// Two-sided printing. Ignored with a note in the report when the printer
    /// cannot do it.
    /// </summary>
    public DuplexMode Duplex { get; init; } = DuplexMode.Default;
}

/// <summary>What a print run actually did — so callers can report honestly.</summary>
public sealed record PrintReport
{
    /// <summary>The printer the job was sent to.</summary>
    public required string PrinterName { get; init; }

    /// <summary>Number of sheets (PDF pages) sent.</summary>
    public required int Sheets { get; init; }

    /// <summary>Rasterisation resolution used.</summary>
    public required int Dpi { get; init; }

    /// <summary>Copies requested from the driver.</summary>
    public required int Copies { get; init; }

    /// <summary>
    /// Paper name the driver resolved per sheet (e.g. <c>A4</c>). Distinct
    /// entries mean the job mixed sheet sizes.
    /// </summary>
    public required IReadOnlyList<string> PaperNames { get; init; }

    /// <summary>Absolute path the job was written to, if it went to a file.</summary>
    public string? OutputFile { get; init; }

    /// <summary>The duplex mode actually applied — <see cref="DuplexMode.Default"/> if none was.</summary>
    public required DuplexMode Duplex { get; init; }

    /// <summary>True when duplex was asked for but the printer cannot do it.</summary>
    public bool DuplexUnsupported { get; init; }
}
