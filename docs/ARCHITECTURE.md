# OpenLeanPrint architecture

OpenLeanPrint is a driverless, ARM64-friendly print utility. This document explains
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

## 2. The OpenLeanPrint pipeline

```
┌─────────────┐   prints to    ┌───────────────────────────┐
│ Any Windows │ ─────────────► │ Microsoft in-box IPP class │
│ application │                │ driver  (driverless queue) │
└─────────────┘                └────────────┬──────────────┘
                                             │ IPP  (application/pdf)
                                             ▼
                             ┌───────────────────────────────┐
                             │ OpenLeanPrint loopback IPP service │  ← Capture
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
**in-box IPP class driver**, pointed at a loopback IPP endpoint that OpenLeanPrint
hosts (`ipp://localhost:PORT/leanprint`). When the user prints:

1. Windows renders the job and sends it to our endpoint via IPP.
2. The document arrives as **PDF** (`application/pdf`) — the modern print path's
   native transfer format — or PWG/PCLm raster as a fallback.
3. OpenLeanPrint parses the PDF's page sizes and hands a `PrintDocument` to the pool.

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

Two viable paths, to be decided during implementation:

1. **Build an output PDF** from the imposed sheets (using a PDF composition
   library) and send it to the chosen printer via the standard Windows print
   API / IPP. Cleanest and most portable.
2. **GDI/Direct2D draw** each sheet directly to the printer DC. More control
   over device features, more Windows-specific code.

Path 1 is preferred for portability and to reuse the same PDFium/PDF stack.

## 6. Coordinate system & units

`OpenLeanPrint.Core` works in **PostScript points (1/72")** with a **top-left
origin** (X right, Y down) to match UI toolkits. PDF uses a bottom-left origin;
the renderer/output layer converts. See `Geometry.cs`.

## 7. Project structure

| Project | State | Responsibility |
|---|---|---|
| `OpenLeanPrint.Core` | **Implemented, tested** | Domain model (`PrintDocument`, `PrintJobPool`, `SourcePage`) and imposition engine (`NUpImposer`, `BookletImposer`). Platform-neutral. |
| `OpenLeanPrint.Capture` | Planned | Loopback IPP service + printer registration; PDF page extraction. Windows. |
| `OpenLeanPrint.Render` | Planned | PDFium binding; render sheets/pages to bitmaps and to an output PDF. |
| `OpenLeanPrint.App` | Planned | WinUI 3 desktop app: pool list, WYSIWYG preview, settings, print. Windows, ARM64 + x64. |

Keeping `OpenLeanPrint.Core` free of Windows dependencies is a deliberate rule: it
keeps the geometric core unit-testable on any OS/CI, which is where correctness
bugs would otherwise be expensive to catch.

## 8. Why .NET

- First-class **ARM64** support; single codebase for ARM64 + x64.
- **WinUI 3 / WPF** for the desktop UI; good access to Windows print APIs.
- PDFium has .NET bindings and ARM64 native binaries.
