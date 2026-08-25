# Open-LeanPrint architecture

Open-LeanPrint is a driverless, ARM64-friendly print utility. This document explains
the pipeline, the capture strategy, why it is chosen over the classic
virtual-driver approach, and how the code is organised.

## 1. The problem with the classic (FinePrint) approach

FinePrint and similar tools install a **virtual printer driver** (a v3 print
driver plus a redirection port monitor). The application prints to that virtual
printer; the driver/monitor hands the spooled data to the tool, which previews,
imposes and re-prints it.

That model is a poor fit for the direction Windows is moving:

- **Windows Protected Print (WPP)** — the modern print platform only allows the
  Microsoft **IPP class driver**. Third-party v3/v4 drivers are being retired:
  no new drivers via Windows Update from **Jan 2026**, IPP class driver default
  from **~mid 2026**, WPP the default around **2027**.
- **Driver signing** — new printer drivers are WHQL-signed only case-by-case.
- **Windows on ARM** — x64 kernel/print drivers are **not** emulated. A virtual
  driver would need to be built and signed **natively for ARM64**, and even
  correctly signed ARM64 print drivers currently have install issues.

Conclusion: **do not ship a printer driver.** Reuse Microsoft's in-box driver
and capture the job in user mode.

## 2. The Open-LeanPrint pipeline

```
┌─────────────┐   prints to    ┌───────────────────────────┐
│ Any Windows │ ─────────────► │ Microsoft in-box IPP class │
│ application │                │ driver  (driverless queue) │
└─────────────┘                └────────────┬──────────────┘
                                             │ IPP  (application/pdf)
                                             ▼
                             ┌───────────────────────────────┐
                             │ Open-LeanPrint loopback IPP service │  ← Capture
                             │ (localhost, user mode)         │
                             └───────────────┬───────────────┘
                                             │ PDF document
                                             ▼
                             ┌───────────────────────────────┐
                             │ Job pool (OpenLeanPrint.Core)      │  ← combine jobs
                             └───────────────┬───────────────┘
                                             │ SourcePages
                                             ▼
                             ┌───────────────────────────────┐
                             │ Imposition engine              │  ← THIS REPO,
                             │ (N-up / booklet, tested)       │    implemented
                             └───────────────┬───────────────┘
                                             │ Sheets + placements
                                             ▼
                    ┌────────────────────────┴───────────────┐
                    │ Renderer (PDFium)  →  WYSIWYG preview    │  ← review
                    └────────────────────────┬───────────────┘
                                             │ user confirms
                                             ▼
                             ┌───────────────────────────────┐
                             │ Forward to physical printer    │  ← output
                             └───────────────────────────────┘
```

## 3. Capture: a local loopback IPP printer

The capture layer registers a **local printer** that Windows drives with its
**in-box IPP class driver**, pointed at a loopback IPP endpoint that Open-LeanPrint
hosts (`ipp://localhost:PORT/leanprint`). When the user prints:

1. Windows renders the job and sends it to our endpoint via IPP.
2. The document arrives as **PDF** (`application/pdf`) — the modern print path's
   native transfer format — or PWG/PCLm raster as a fallback.
3. Open-LeanPrint parses the PDF's page sizes and hands a `PrintDocument` to the pool.

Why this is the right call:

- **No third-party driver, no kernel code, no WHQL** — WPP-compatible.
- **ARM64 is trivial**: the whole thing is a user-mode .NET app; build it
  ARM64-native (and x64). No driver signing, no emulation.
- **PDF in, PDF out** is ideal for preview and imposition.

### Capture alternatives considered

| Option | Verdict |
|---|---|
| v3 virtual driver + port monitor (FinePrint-style) | ✗ Blocked by WPP; ARM64 signing pain. |
| Port monitor on the in-box PostScript/PDF driver | ~ Port monitors are still spooler-loaded DLLs under the signing regime. |
| Print to "Microsoft Print to PDF" then pick up the file | ~ Works but no live hook / no job metadata; clumsy UX. |
| **Loopback IPP printer + in-box IPP class driver** | ✓ Driverless, ARM64-native, live, PDF payload. **Chosen.** |

## 4. Rendering & preview

- **PDFium** (BSD license, ARM64 builds available) renders source PDF pages to
  bitmaps for the on-screen preview and, ultimately, to the output surface.
- The preview draws each `Sheet` from an `ImpositionResult`: it paints the sheet
  media, then each `PlacedPage` into its `DestRect` (applying `Rotation`). The
  geometry is already computed by `OpenLeanPrint.Core`, so the preview and the final
  output share the exact same layout — true WYSIWYG.
- **License note:** Ghostscript and MuPDF (both AGPL) are deliberately avoided
  so the project can stay permissively (MIT) licensed.

## 5. Output: forwarding to a physical printer

Implemented in two steps, which keeps the portable part portable:

1. **Build an output PDF** from the imposed sheets (`OpenLeanPrint.Compose`,
   PdfSharpCore). Pure vector, platform-neutral, and it doubles as the artefact
   the preview shows and the user can save.
2. **Print that PDF** (`OpenLeanPrint.Print`): the Windows spooler cannot render
   PDF, so each sheet is rasterised with PDFium and drawn onto the printer's
   `Graphics` — matching the driver's paper size to the sheet and mapping it 1:1
   onto the page. Works with any installed driver.

Sending PDF straight to IPP printers would keep the vectors end to end and is a
later optimisation; rasterising is the baseline that works everywhere.
Details and the WYSIWYG pitfalls: [M3-PRINT.md](M3-PRINT.md).

## 6. Coordinate system & units

`OpenLeanPrint.Core` works in **PostScript points (1/72")** with a **top-left
origin** (X right, Y down) to match UI toolkits. PDF uses a bottom-left origin;
the renderer/output layer converts. See `Geometry.cs`.

## 7. Details that have bitten us

Small facts with large consequences, each found the hard way:

- **PdfSharpCore imports external page content at `Save()` time**, not when the
  page is placed. Every `XPdfForm` and its source stream has to stay alive until
  then — see `PdfImposer.Compose`.
- **Printer `Graphics` uses `GraphicsUnit.Display` = 1/100 inch**, the same unit
  as `PageBounds` and `HardMarginX/Y`. With `OriginAtMargins = false` the origin
  is the top-left of the *printable* area rather than of the paper, so drawing a
  full-bleed sheet needs a `-HardMarginX/-HardMarginY` offset.
- **Windows drivers silently fall back to their default paper** unless
  `PageSettings.PaperSize` is set explicitly. Hence `PaperMatch`.
- **`PrinterSettings.PrintToFile` with `PrintFileName`** makes *Microsoft Print
  to PDF* write without a save dialog. The spooler writes that file
  asynchronously, so wait for it before reporting success.
- **Two-sided printing is correct as far as this code can reach.** Choosing long
  or short edge produced short-edge output on one device, and every layer that
  can be inspected carries the right value: the app sets `Duplex.Vertical`, the
  queued job holds `dmDuplex 2` with `DM_DUPLEX` set, the driver reads it back as
  `TwoSidedLongEdge`, and it leaves as `sides=two-sided-long-edge`. Handing the
  DEVMODE back to the driver to reconcile changes nothing — long and short differ
  by one byte of the documented field and none of the driver's private area. The
  device is where it stops; do not go looking for it in `PdfPrinter`.

## 8. Project structure

| Project | State | Responsibility |
|---|---|---|
| `OpenLeanPrint.Core` | **Implemented, tested** | Domain model (`PrintDocument`, `PrintJobPool`, `SourcePage`) and imposition engine (`NUpImposer`, `BookletImposer`). Platform-neutral. |
| `OpenLeanPrint.Capture` (+ `.Host`) | **Implemented, verified on Windows** | Loopback IPP service + printer registration; PDF page extraction. |
| `OpenLeanPrint.Compose` | **Implemented, tested** | Imposed sheets → output PDF (PdfSharpCore). Platform-neutral. |
| `OpenLeanPrint.Print` | **Implemented, verified via the PDF driver** | PDF → printer: PDFium raster + `System.Drawing.Printing`. Windows-only at runtime. |
| `OpenLeanPrint.Cli` | **Implemented** | `impose`, `sample`, `print`, `list-printers`. |
| `OpenLeanPrint.App` | Planned | WinUI 3 desktop app: pool list, WYSIWYG preview, settings, print. Windows, ARM64 + x64. |

Keeping `OpenLeanPrint.Core` free of Windows dependencies is a deliberate rule: it
keeps the geometric core unit-testable on any OS/CI, which is where correctness
bugs would otherwise be expensive to catch.

## 9. Why .NET

- First-class **ARM64** support; single codebase for ARM64 + x64.
- **WinUI 3 / WPF** for the desktop UI; good access to Windows print APIs.
- PDFium has .NET bindings and ARM64 native binaries.
