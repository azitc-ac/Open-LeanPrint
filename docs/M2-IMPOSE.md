# M2 — Imposition to PDF (render/compose core)

M2 turns a captured PDF into a real **imposed output PDF**: 2-up, 4-up, any
`rows × cols` grid, or a saddle-stitch booklet. It is pure vector composition
(PdfSharpCore) — the source pages are placed into their computed cells with the
exact position, scale and rotation from the Core imposition engine, so the
output stays crisp and is directly usable for printing (M3).

This is the "render/compose core" slice of M2. An on-screen raster preview and
the desktop GUI build on top of it later.

## Components

| Path | What |
|---|---|
| `src/OpenLeanPrint.Compose/PdfImposer.cs` | `ImpositionResult` → output PDF (places pages via PdfSharpCore). |
| `src/OpenLeanPrint.Cli` | `openleanprint` CLI: `impose` and `sample`. |
| `tests/OpenLeanPrint.Compose.Tests` | Sheet count / size / validity tests (any OS). |

## CLI usage

```bash
# Impose a captured PDF 4-up on A4 with an 8 mm margin and 6 pt gutter:
dotnet run --project src/OpenLeanPrint.Cli -- \
    impose captured/job-0002.pdf out-4up.pdf --nup 2x2 --paper A4 --margin 8 --gutter 6

# 2-up, booklet, or a plain count:
dotnet run --project src/OpenLeanPrint.Cli -- impose in.pdf out-2up.pdf --nup 1x2
dotnet run --project src/OpenLeanPrint.Cli -- impose in.pdf booklet.pdf --booklet --paper A4
dotnet run --project src/OpenLeanPrint.Cli -- impose in.pdf out.pdf --nup 4   # 4 => 2x2

# Make a colored sample PDF to experiment with:
dotnet run --project src/OpenLeanPrint.Cli -- sample sample.pdf --pages 8
```

Options for `impose`:

| Option | Meaning | Default |
|---|---|---|
| `--nup RxC` | grid (e.g. `2x2`), or a count (`2`, `4`, `9`) | `2x2` |
| `--paper NAME` | `A4`, `A5`, `A3`, `Letter`, `Legal`, `Tabloid` | `A4` |
| `--booklet` | saddle-stitch booklet (overrides `--nup`) | off |
| `--margin MM` | outer margin, millimetres | `0` |
| `--gutter PT` | spacing between cells, points | `0` |
| `--pages LIST` | which source pages to keep, e.g. `1-4,7` | all |
| `--watermark TEXT` | text drawn across every sheet | none |

Watermark colour, opacity, angle and size have their own options — the full
list is in the [user guide](USER-GUIDE.md#impose).

## How it fits the pipeline

```
Captured PDF ─► PdfImposer.ReadPageSizes ─► NUpImposer / BookletImposer (Core)
                                                     │  ImpositionResult
                                                     ▼
                               PdfImposer.Compose ─► imposed output PDF
                                                     │
                                                     ▼
                                    (M3) forward to a physical printer
```

## Verified

Composition is covered by automated tests (sheet counts, sheet sizes, valid
output PDF for N-up and booklet) and was checked visually by rasterising the
output: 4-up places pages 1–4 in row order, booklet uses the correct
saddle-stitch order (e.g. sheet 1 = pages 8 and 1), and stacked 2×1 rotates
each page 90° to fill its cell.

## Next

- On-screen raster preview (PDFium) for a live WYSIWYG dialog.
- `OpenLeanPrint.App` (WinUI 3): job pool, preset buttons (2-up/4-up/booklet),
  live preview, print.
