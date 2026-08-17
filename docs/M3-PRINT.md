# M3 — Forward to a physical printer (implementation plan)

Goal: send an **imposed output PDF** (from M2) to a user-chosen Windows printer,
silently, so the full chain works: **print → capture → impose → real printer**.

This plan is written so a Claude Code instance running on Windows can implement
and **actually verify** it (unlike the Linux cloud environment, which cannot
reach a printer).

## Acceptance criteria

1. `openleanprint print <pdf> --printer "<name>"` prints every page of the PDF
   to the named printer at the correct size (A4 sheet → A4 output, no extra
   scaling/margins beyond the printer's hardware margins).
2. `openleanprint list-printers` lists installed printers.
3. Verifiable **without paper**: printing to **"Microsoft Print to PDF"** yields
   an output PDF that visually matches the imposed input (e.g. a 4-up sheet).
4. End-to-end round trip confirmed on Windows: capture a job (M1) → impose it
   (M2) → print to "Microsoft Print to PDF" (M3) → the result is the 4-up layout.
5. Cross-platform tests still build and pass; the Windows-only print code must
   not break `dotnet test` on Linux/CI.

## Approach

Windows' spooler cannot render PDF itself, so **rasterise each imposed sheet to
a bitmap and print via `System.Drawing.Printing.PrintDocument`**. This works
with any installed printer/driver and needs no external tools. (A future
enhancement can send PDF directly to IPP printers to keep vectors; raster is the
robust baseline.)

Pipeline:

```
imposed PDF ─► render each page to a bitmap (PDFium) ─► System.Drawing.Printing
                 at ~200–300 DPI                         PrintDocument -> printer
```

### Suggested structure

- New project **`OpenLeanPrint.Print`** targeting **`net8.0-windows`**
  (System.Drawing.Printing is Windows-only). Keep it out of `OpenLeanPrint.Core`.
- Rendering dependency (permissive licences only): **PDFtoImage** (MIT, uses
  SkiaSharp) or **Docnet.Core** (MIT, bundles native PDFium). Render each page to
  a bitmap, then draw it onto the `PrintPageEventArgs.Graphics` scaled to the
  page bounds.
- Expose `print` and `list-printers` from the CLI **without breaking the
  cross-platform build**. Two clean options:
  - multi-target the CLI: `<TargetFrameworks>net8.0;net8.0-windows</TargetFrameworks>`
    and guard the print command with `#if WINDOWS` (only wired up in the
    windows TFM); or
  - a separate `OpenLeanPrint.PrintCli` (net8.0-windows) that references
    `OpenLeanPrint.Print`.
  Prefer whichever keeps `dotnet test` green on Linux.

### API sketch

```csharp
// OpenLeanPrint.Print (net8.0-windows)
public sealed record PrintOptions
{
    public int Copies { get; init; } = 1;
    public int Dpi { get; init; } = 200;
    public bool Landscape { get; init; }          // usually infer per sheet
}

public static class PdfPrinter
{
    public static IReadOnlyList<string> InstalledPrinters();   // PrinterSettings.InstalledPrinters
    public static void Print(byte[] imposedPdf, string printerName, PrintOptions? options = null);
}
```

Implementation notes:
- Render page `i` to a bitmap sized `sheetPoints * Dpi/72`. Use the page's own
  size to set `PageSettings` orientation/paper where possible.
- In `PrintPage`, draw the bitmap to `e.PageBounds` (or `e.MarginBounds` if you
  want to respect the printer's soft margins — for imposition, `PageBounds` and
  let hardware margins clip is usually closest to WYSIWYG). Advance pages via
  `e.HasMorePages`.
- Set `PrinterSettings.PrinterName`; for the PDF-driver test, that is
  `"Microsoft Print to PDF"` (it prompts for an output path unless
  `PrintToFile` + `PrintFileName` are set — set them for silent runs).

### Optional: folder watcher (usable without a GUI)

`openleanprint watch <captured-folder> --nup 2x2 --paper A4 --printer "<name>"`:
`FileSystemWatcher` on the folder; when the capture host writes a new
`job-*.pdf`, impose it with the configured preset and print it. This already
gives a usable "print → auto 4-up → real printer" workflow before the GUI
exists. Debounce file-created events (wait until the file is fully written).

## How to test on Windows (no paper needed)

```powershell
# 1. Build & unit tests still green
dotnet test

# 2. Make an imposed PDF (M2)
dotnet run --project src/OpenLeanPrint.Cli -- sample sample.pdf --pages 8
dotnet run --project src/OpenLeanPrint.Cli -- impose sample.pdf out-4up.pdf --nup 2x2 --paper A4 --margin 8 --gutter 6

# 3. Print it to the PDF driver (silent, to a file) and eyeball the result
dotnet run --project src/OpenLeanPrint.Cli -- print out-4up.pdf --printer "Microsoft Print to PDF"
#   -> open the produced PDF; it must look like out-4up.pdf (4 pages per sheet)

# 4. Full round trip: capture (M1) -> impose -> print to PDF driver
#    Start the capture host, register the printer, print a document, then:
dotnet run --project src/OpenLeanPrint.Cli -- impose "captured\job-0001.pdf" rt.pdf --nup 2x2
dotnet run --project src/OpenLeanPrint.Cli -- print rt.pdf --printer "Microsoft Print to PDF"

# 5. Finally, print to a real printer to confirm physical output.
```

Report exactly what you ran and saw (page counts, how the output looked). If a
step needs elevation or is blocked by Constrained Language Mode, say so.

## Out of scope for M3 (later)

- On-screen live WYSIWYG preview and the WinUI 3 GUI.
- Direct vector/PDF submission to IPP printers (quality optimisation).
- Duplex/finishing options, per-job printer presets.
