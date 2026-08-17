# M3 — Forward to a printer

M3 closes the chain: an **imposed output PDF** from M2 is sent to a real Windows
printer, silently, so the whole pipeline works end to end —
**print → capture → impose → printer**.

## Approach

The Windows spooler cannot render PDF itself, so each imposed sheet is
**rasterised with PDFium and drawn onto the printer's `Graphics`** via
`System.Drawing.Printing`. That works with any installed printer and driver and
needs no external tools. (Sending PDF straight to an IPP printer would keep the
vectors and is a later quality optimisation; raster is the robust baseline.)

```
imposed PDF ─► PDFium raster, 200 dpi ─► System.Drawing.Printing ─► printer
                    per sheet                 PrintDocument
```

Three details make the output WYSIWYG rather than "roughly the right size":

- **Paper matching** — the driver's paper list is searched for the size that
  matches the sheet (A4 sheet → A4 paper), comparing in portrait terms and
  leaving orientation to `PageSettings.Landscape`. Without this the driver
  silently uses its default paper and scales.
- **Full-page mapping** — the sheet fills `PageBounds` (the physical page), not
  `MarginBounds`. The imposed sheet already *is* the layout, so any extra printer
  margin would shrink it.
- **Hardware-margin offset** — with `OriginAtMargins = false` the graphics origin
  sits at the top-left of the *printable* area, so the draw is shifted by
  `HardMarginX/Y` to line up with the paper edge. Hardware that cannot print full
  bleed clips the outermost sliver; that is unavoidable and closest to WYSIWYG.

## Components

| Path | What |
|---|---|
| `src/OpenLeanPrint.Print/PdfPrinter.cs` | Print a PDF to a named printer; list installed printers. |
| `src/OpenLeanPrint.Print/PdfRasterizer.cs` | PDF page → GDI+ bitmap (PDFtoImage/PDFium). |
| `src/OpenLeanPrint.Print/PaperMatch.cs` | Sheet size → the driver's matching paper size. |
| `src/OpenLeanPrint.Print/PagePlacement.cs` | Pure geometry: fit a sheet onto a page. |
| `src/OpenLeanPrint.Cli` | `print` and `list-printers` commands. |
| `tests/OpenLeanPrint.Print.Tests` | Geometry/paper-matching (any OS) + GDI+ tests (Windows). |

### Why `net8.0` and not `net8.0-windows`

Printing is Windows-only, but `OpenLeanPrint.Print` targets plain **net8.0** and
marks its Windows-only surface `[SupportedOSPlatform("windows")]`. The
platform-compatibility analyser then enforces the guards, the CLI stays
single-TFM (so `dotnet run --project src/OpenLeanPrint.Cli -- …` keeps working
without `-f`), and the whole solution still builds and tests on Linux/CI.
`PDFtoImage` (MIT) supplies PDFium, whose native binaries include **win-arm64** —
required for Windows on ARM.

## CLI usage

```powershell
# Which printers are there? (* marks the Windows default)
dotnet run --project src/OpenLeanPrint.Cli -- list-printers

# Print an imposed sheet set to a printer
dotnet run --project src/OpenLeanPrint.Cli -- print out-4up.pdf --printer "Brother MFC-9332CDW Printer"

# Paper-free proof: print through the PDF driver into a file
dotnet run --project src/OpenLeanPrint.Cli -- print out-4up.pdf --printer "Microsoft Print to PDF" --out proof.pdf
```

Options for `print`:

| Option | Meaning | Default |
|---|---|---|
| `--printer NAME` | target printer | Windows default printer |
| `--out FILE` | write to a file instead of paper; also suppresses the driver's save dialog | off |
| `--copies N` | copies requested from the driver | `1` |
| `--dpi N` | rasterisation resolution (36–1200) | `200` |

`--out` only does something for "print to file" drivers such as *Microsoft Print
to PDF*. The spooler writes that file asynchronously, so the CLI waits for it and
reports its final size.

## Verified

On **Windows 11 ARM64** (dotnet `win-arm64`), against *Microsoft Print to PDF* —
no paper used:

1. `sample sample.pdf --pages 8` → `impose … --nup 2x2 --paper A4 --margin 8
   --gutter 6` → 2 sheets.
2. `print out-4up.pdf --printer "Microsoft Print to PDF" --out proof-4up.pdf`
   reported *"Sent 2 sheet(s) … (A4, 200 dpi, 1 copy)"* and wrote a 274 KB PDF
   with **no save dialog**.
3. Rasterising input and output side by side: both sheets match — 4 pages per
   sheet in row order, margins and gutters preserved, the red "this edge is up"
   marker at the top of every cell, pages 1–4 on sheet 1 and 5–8 on sheet 2
   (so page advance works, it is not sheet 1 twice).
4. Page size survives: the printed PDF's pages are 595.3 × 841.9 pt = A4, i.e.
   no unwanted scaling.
5. Full round trip on a **real captured job** (`captured/job-0001.pdf` from M1):
   imposed 4-up and printed through the PDF driver — one A4 sheet with the four
   source pages correctly placed.
6. **On physical paper** (Brother MFC-9332CDW, in-box IPP class driver): a 4-up
   A4 sheet was accepted and completed by the spooler — `PagesPrinted 1 / 1`,
   147 KB delivered to the device, paper resolved to A4. The sheet itself has
   not been looked at yet (nobody was at the printer), so hardware margins and
   colour on paper are still unconfirmed.

Automated: 21 tests in `OpenLeanPrint.Print.Tests` (56 in the solution). The
geometry and paper-matching tests run on any OS; the GDI+/PDFium ones are
`[WindowsFact]` and skip themselves elsewhere. The suite deliberately does **not**
spool a real job — that stays a manual check, so `dotnet test` has no side
effects.

**Not yet confirmed:** how the physical sheet actually looks. The spooler path to
a real printer works (point 6), but nobody has inspected the paper — the printed
margins, scale and colour are the last thing to eyeball.

## Next

- Look at the printed sheet from the Brother and confirm the layout on paper.
- Optional `watch <captured-folder> --nup 2x2 --printer "…"`: impose and print
  every new `job-*.pdf` automatically — a usable workflow before the GUI exists
  (`FileSystemWatcher`, debounced until the file is fully written).
- Duplex hint and per-job presets.
- Later: send PDF straight to IPP printers to keep vectors instead of rasterising.
