using OpenLeanPrint.Core;

namespace OpenLeanPrint.Capture;

/// <summary>
/// A print job captured by the loopback IPP service: the raw document bytes,
/// its format, the requested job name, and — once parsed — the domain
/// <see cref="PrintDocument"/> describing its pages.
/// </summary>
public sealed class CapturedJob
{
    public required int JobId { get; init; }

    /// <summary>Requested job name (IPP "job-name"), if the client supplied one.</summary>
    public string? JobName { get; init; }

    /// <summary>Originating user (IPP "requesting-user-name"), if supplied.</summary>
    public string? UserName { get; init; }

    /// <summary>Document MIME type (IPP "document-format"), e.g. application/pdf.</summary>
    public string DocumentFormat { get; init; } = "application/octet-stream";

    /// <summary>
    /// Two-sided setting the client asked for (IPP "sides"): <c>one-sided</c>,
    /// <c>two-sided-long-edge</c> or <c>two-sided-short-edge</c>.
    /// <para>
    /// Worth recording for its own sake, and useful beyond that: this is what
    /// Windows makes of a print job's DEVMODE, so printing into this printer
    /// says what any other print path actually asked a driver for.
    /// </para>
    /// </summary>
    public string? Sides { get; init; }

    /// <summary>Colour setting the client asked for (IPP "print-color-mode").</summary>
    public string? ColorMode { get; init; }

    /// <summary>The raw spooled document bytes.</summary>
    public required byte[] Data { get; init; }

    /// <summary>
    /// The parsed page model, when the document could be parsed (PDF). Null when
    /// the format is unsupported or parsing failed (see <see cref="ParseError"/>).
    /// </summary>
    public PrintDocument? Document { get; set; }

    /// <summary>Parse failure message, if parsing was attempted and failed.</summary>
    public string? ParseError { get; set; }

    public bool IsPdf => DocumentFormat.Contains("pdf", StringComparison.OrdinalIgnoreCase);
}
