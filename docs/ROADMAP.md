# OpenLeanPrint roadmap

Incremental milestones from the current foundation to a usable app. Each
milestone is meant to be independently reviewable.

## ✅ M0 — Portable core (done)

- Domain model: `SourcePage`, `PrintDocument`, `PrintJobPool` (job pooling).
- Imposition engine: `NUpImposer` (N-up grid, margins, gutters, cell order,
  scale modes, auto-rotate) and `BookletImposer` (saddle-stitch ordering).
- Geometry types in points, top-left origin.
- xUnit test suite, green on any OS.

## ✅ M1 — Capture prototype (done, verified end-to-end on Windows)

Goal: print from a real app and receive the PDF in OpenLeanPrint, with **no
third-party driver**. See [M1-CAPTURE.md](M1-CAPTURE.md) for details.

- ✅ `OpenLeanPrint.Capture`: loopback **IPP service** (`localhost:PORT`) —
  handles Get-Printer-Attributes, Validate-Job, Print-Job, Create-Job/Send-Document.
- ✅ IPP wire-format codec (`IppReader`/`IppWriter`), unit-tested.
- ✅ Full IPP Everywhere printer-attribute set (the in-box IPP class driver
  requires it before it will create the queue).
- ✅ Accept a job, store the incoming `application/pdf`.
- ✅ Extract page count and page sizes → build a `PrintDocument`.
- ✅ Runnable `OpenLeanPrint.Capture.Host` + loopback integration tests (green on CI).
- ✅ **Verified on Windows 11**: `Add-Printer -IppURL` attaches the **Microsoft
  IPP Class Driver** to the loopback URL; printing from a real app is captured
  via Create-Job/Send-Document as **application/pdf** and parsed (e.g. a 4-page
  A4 document → four 595×842 pt pages).

Exit criteria met: a captured PDF on disk and a populated `PrintDocument`,
both automated on Linux and confirmed end-to-end on Windows.

## ✅ M2 — Render & compose (done)

- ✅ `OpenLeanPrint.Compose`: imposes an `ImpositionResult` into an output PDF —
  each source page placed with the computed position, scale and rotation
  (PdfSharpCore, vector, no rasterisation). N-up and booklet.
- ✅ `OpenLeanPrint.Cli` (`impose` / `sample`) to run it on a captured PDF.
- ✅ Verified by tests + visual raster check (4-up row order, booklet order,
  90° rotation for stacked layouts). See [M2-IMPOSE.md](M2-IMPOSE.md).
- ✅ On-screen **raster preview** (PDFium via `PdfRasterizer.RenderPagePng`).
- ✅ Live re-impose when settings change (N-up, paper, margins, gutter).

Exit criteria met: a job is shown 2-up/4-up on screen, matching the engine —
in the desktop app (see [M4-APP.md](M4-APP.md)).

## ◑ M3 — Forward to a printer (implemented; physical print still to confirm)

- ✅ Compose an **output PDF** from the imposed sheets (done in M2).
- ✅ `OpenLeanPrint.Print`: rasterises each sheet with PDFium and prints it via
  `System.Drawing.Printing`, picking the driver paper size that matches the
  sheet and mapping it 1:1 onto the page.
- ✅ CLI `print` and `list-printers`, with `--printer`, `--out`, `--copies`,
  `--dpi`.
- ✅ Verified paper-free on Windows 11 ARM64: an imposed 4-up sheet printed to
  "Microsoft Print to PDF" comes back pixel-for-pixel like the imposed input,
  A4 stays A4, and multi-sheet jobs advance correctly. Full round trip
  capture → impose → print confirmed. See [M3-PRINT.md](M3-PRINT.md).
- ◑ A 4-up sheet was also printed to the **physical Brother** queue and the
  spooler reported it complete (1/1 page, A4) — but the paper has not been
  looked at yet, so the printed layout is unconfirmed.
- ✅ `watch <folder>`: imposes (and optionally prints) every new PDF dropped into
  a folder — the first hands-free workflow, no GUI required.
- ▶ Duplex hint and per-job presets.

Exit criteria: the paper-free PDF-driver test matches the imposed layout (met);
4-up output comes out of a real printer correctly (job accepted and completed;
visual check on paper still open).

## ◑ M4 — App shell & UX (usable app; polish outstanding)

- ✅ `OpenLeanPrint.App` (**WPF**, not WinUI 3 — it needs no extra runtime and is
  ARM64-native out of the box): pool list, reorder/remove, jobs combined onto
  shared sheets, presets (1/2/4/9-up, booklet), live preview, print, save PDF.
  See [M4-APP.md](M4-APP.md).
- ▶ Settings persistence — the app starts at 4-up/A4 every time.
- ▶ Fill the pool from the capture host as jobs arrive (today: files, or
  `openleanprint watch` headlessly).
- ▶ Tray/quick-launch; "keep collecting jobs" workflow.
- ▶ Installer (MSIX), signed. **ARM64 + x64** builds.

## ▶ M5 — Polish & parity

- Duplex-aware booklet output; per-page rotation overrides.
- Edge cases: mixed page sizes, huge jobs, high-DPI/multi-monitor preview.
- Optional extras toward FinePrint parity: watermarks/stationery, PDF export,
  page deletion, profiles.

## Cross-cutting

- CI: build + test `OpenLeanPrint.Core` on every push (OS-agnostic); Windows job for
  ARM64/x64 app builds once M1+ exist.
- Keep `OpenLeanPrint.Core` platform-neutral and well-tested.
