using System.Drawing;
using System.Runtime.Versioning;
using OpenLeanPrint.Core;
using PDFtoImage;
using SkiaSharp;

namespace OpenLeanPrint.Print;

/// <summary>
/// Rasterises PDF pages via PDFium (PDFtoImage, MIT — native binaries include
/// win-arm64, so this works on Windows on ARM). The Windows print spooler
/// cannot render PDF itself, so every sheet becomes a bitmap before printing.
/// </summary>
public static class PdfRasterizer
{
    /// <summary>Page sizes in points, in document order.</summary>
    // PDFium ships native binaries for these platforms only - stating that here
    // keeps the platform-compatibility analyser honest for callers.
    [SupportedOSPlatform("windows")]
    [SupportedOSPlatform("linux")]
    [SupportedOSPlatform("macos")]
    public static IReadOnlyList<PtSize> PageSizes(byte[] pdf)
    {
        ArgumentNullException.ThrowIfNull(pdf);

        var sizes = Conversion.GetPageSizes(pdf);
        var result = new List<PtSize>(sizes.Count);
        foreach (var size in sizes) result.Add(new PtSize(size.Width, size.Height));
        return result;
    }

    /// <summary>
    /// Renders one page at <paramref name="dpi"/> to PNG bytes. This is the
    /// toolkit-neutral form — a WPF preview can load it directly, with no GDI+
    /// involved.
    /// </summary>
    [SupportedOSPlatform("windows")]
    [SupportedOSPlatform("linux")]
    [SupportedOSPlatform("macos")]
    public static byte[] RenderPagePng(byte[] pdf, int pageIndex, int dpi)
    {
        ArgumentNullException.ThrowIfNull(pdf);
        ArgumentOutOfRangeException.ThrowIfNegative(pageIndex);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(dpi);

        var options = new RenderOptions
        {
            Dpi = dpi,
            WithAnnotations = true,
            WithFormFill = true,
            AntiAliasing = PdfAntiAliasing.All,
            BackgroundColor = SKColors.White,
        };

        using var rendered = Conversion.ToImage(pdf, page: (Index)pageIndex, options: options);
        using var encoded = rendered.Encode(SKEncodedImageFormat.Png, 100)
            ?? throw new InvalidOperationException($"Could not encode page {pageIndex + 1} of the PDF.");
        return encoded.ToArray();
    }

    /// <summary>
    /// Renders one page at <paramref name="dpi"/> into a GDI+ bitmap the caller
    /// owns (and must dispose) — what the printing path needs.
    /// </summary>
    [SupportedOSPlatform("windows")]
    public static Bitmap RenderPage(byte[] pdf, int pageIndex, int dpi)
    {
        // Going through PNG is the one path whose colour handling is
        // unambiguous (Skia's own buffer is premultiplied BGRA). A Bitmap
        // decoded from a stream keeps referencing that stream, so copy into a
        // bitmap that owns its pixels and can outlive it.
        using var ms = new MemoryStream(RenderPagePng(pdf, pageIndex, dpi));
        using var decoded = new Bitmap(ms);
        return new Bitmap(decoded);
    }
}
