namespace OpenLeanPrint.Print;

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
}
