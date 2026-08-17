# OpenLeanPrint — context for Claude Code

You are working on **OpenLeanPrint**, a modern, lightweight, open-source
alternative to **FinePrint**: pool print jobs, impose them **N-up** (2-on-1,
4-on-1, booklet) with a WYSIWYG dialog, and forward the result to a real
printer. It is designed to run on **Windows on ARM (ARM64)** as well as x64 by
avoiding third-party print drivers entirely (driverless loopback IPP).

Read `docs/ARCHITECTURE.md` first — it explains the whole pipeline and *why*
the design is driverless. Then `docs/ROADMAP.md` for milestones.

## If you are running on the user's Windows PC

Earlier work happened in an isolated Linux cloud container that **could not**
test the Windows-specific pieces (printer registration, real printing). If you
are running locally on Windows, **you can now verify those end to end** — that
is a big part of why this handoff exists. Do it for real rather than assuming.

The user's environment (from prior sessions): Windows, repo cloned under
`…\Documents\GitHub\OpenLeanPrint`. `Add-Printer` needs an **elevated**
PowerShell. The account may run PowerShell in *Constrained Language Mode* — if a
script is blocked, say so rather than working around security.

## Current status (what is done)

- **M0 — imposition engine** ✅ (`OpenLeanPrint.Core`): N-up grid + booklet
  geometry, unit-tested. Platform-neutral, no Windows deps.
- **M1 — driverless capture** ✅ (`OpenLeanPrint.Capture`): a loopback IPP
  service the in-box **Microsoft IPP Class Driver** attaches to. **Verified on
  real Windows 11**: printing from an app is captured as PDF and pages parsed.
- **M2 — compose core** ✅ (`OpenLeanPrint.Compose` + `OpenLeanPrint.Cli`):
  imposes a captured PDF into an output PDF (N-up / booklet), vector, crisp.
  Verified by tests + visual raster check.
- **M3 — print** ✅ (`OpenLeanPrint.Print` + CLI `print` / `list-printers`):
  rasterises each imposed sheet with PDFium and prints it through the Windows
  spooler, on the matching paper size at 1:1 scale. **Verified on Windows 11
  ARM64** against *Microsoft Print to PDF* (paper-free), including the full
  round trip capture → impose → print. Physical paper not yet tried.

Everything is pushed to `main`. 56 tests pass (Windows-only ones self-skip
elsewhere, so Linux/CI stays green).

## What is next (pick up here)

Roughly in priority order — confirm with the user which they want:

1. **M2 preview + GUI.** On-screen raster preview (PDFium — `PdfRasterizer` in
   `OpenLeanPrint.Print` already renders a page to a bitmap) + a WinUI 3 desktop
   app: job pool list, preset buttons (2-up/4-up/booklet), live WYSIWYG preview,
   print. Keep rendering logic in a platform-neutral project where possible so
   it stays testable.
2. **Confirm a physical print** — everything so far went through the PDF driver.
   One real print on the Brother queue closes M3's last exit criterion.
3. **`watch` command** (sketched in [`docs/M3-PRINT.md`](docs/M3-PRINT.md)):
   `FileSystemWatcher` on `captured/`, impose each new job with a preset and
   print it. That is a usable "print → auto 4-up → printer" workflow *before*
   the GUI exists.

## Build, test, run

Requires the **.NET 8 SDK**. Everything except a future WinUI app is
cross-platform.

```powershell
dotnet test                                   # build + run all tests (56)
dotnet run --project src/OpenLeanPrint.Capture.Host -- --port 6310
dotnet run --project src/OpenLeanPrint.Cli -- impose in.pdf out.pdf --nup 2x2 --paper A4
dotnet run --project src/OpenLeanPrint.Cli -- sample sample.pdf --pages 8
dotnet run --project src/OpenLeanPrint.Cli -- list-printers
dotnet run --project src/OpenLeanPrint.Cli -- print out.pdf --printer "Microsoft Print to PDF" --out proof.pdf
```

End-to-end capture test on Windows (see `docs/M1-CAPTURE.md`):
1. Start the capture host (above), leave it running.
2. **Elevated** PowerShell: `.\scripts\Register-Printer.ps1 -Port 6310`
   (or `Add-Printer -IppURL http://localhost:6310/leanprint`).
3. Print to "OpenLeanPrint Virtual Printer" from any app.
4. The host logs the job and saves the PDF to `captured/`.
5. `.\scripts\Unregister-Printer.ps1 -Port 6310` to clean up.

Impose a captured job (see `docs/M2-IMPOSE.md`):
```powershell
dotnet run --project src/OpenLeanPrint.Cli -- impose "captured\job-0001.pdf" out-4up.pdf --nup 2x2 --paper A4 --margin 8 --gutter 6
```

## Repository layout

| Path | What |
|---|---|
| `src/OpenLeanPrint.Core` | Domain model + imposition engine (net8.0, platform-neutral). |
| `src/OpenLeanPrint.Capture` | Loopback IPP service, IPP codec, PDF page extraction. |
| `src/OpenLeanPrint.Capture.Host` | Runnable console host (logs + saves captured jobs). |
| `src/OpenLeanPrint.Compose` | ImpositionResult → output PDF (PdfSharpCore). |
| `src/OpenLeanPrint.Print` | Imposed PDF → Windows printer (PDFium raster + spooler). |
| `src/OpenLeanPrint.Cli` | `openleanprint` CLI: `impose` / `sample` / `print` / `list-printers`. |
| `tests/**` | xUnit tests (Core, Capture, Compose, Print) — run on any OS. |
| `scripts/*.ps1` | Windows printer register/unregister. |
| `docs/` | ARCHITECTURE, ROADMAP, M1-CAPTURE, M2-IMPOSE, M3-PRINT. |

## Conventions & guardrails

- **Git:** commit and **push directly to `main`** (the user asked for no PRs).
  Use `git push -u origin main`. `.gitattributes` pins `*.ps1` to CRLF — do not
  fight it.
- **License:** MIT © Alexander Zarenko. Keep it permissively licensed:
  dependencies are **PdfPig** (Apache-2.0), **PdfSharpCore** (MIT) and
  **PDFtoImage** (MIT, bundling PDFium/BSD + SkiaSharp/MIT). **Do not** introduce
  AGPL libraries (Ghostscript, MuPDF, iText) into shipping code.
- **Windows-only code stays on `net8.0`** and is marked
  `[SupportedOSPlatform("windows")]` rather than moving to a `net8.0-windows`
  TFM. The analyser then forces callers to guard with
  `OperatingSystem.IsWindows()`, the CLI stays single-TFM (multi-targeting would
  make every `dotnet run` need `-f`), and Linux/CI still builds everything.
- **Native dependencies must ship win-arm64.** PDFium (via PDFtoImage) and
  SkiaSharp do; `Docnet.Core` does *not* — that is why it was not used.
- **Keep `OpenLeanPrint.Core` free of Windows/native dependencies** so the
  geometric core stays unit-testable on any OS. New platform-neutral logic
  should stay testable too.
- `TreatWarningsAsErrors` is on (see `Directory.Build.props`) — keep the build
  warning-clean.
- **NuGet note:** in some sandboxes the feed only exposes vetted package
  versions (e.g. PdfPig showed as `1.7.0-custom-5`). On a normal Windows machine
  you have the full nuget.org feed, so use normal current versions.
- **Honesty:** when you verify something on Windows, say what you actually ran
  and saw. When you can't verify a step, say so — don't claim success.
- CI (`.github/workflows/ci.yml`) builds + tests on push to `main`.

## Design notes worth knowing

- Geometry is in **PostScript points (1/72")**, **top-left origin** (`Geometry.cs`).
- Windows' IPP class driver only creates the queue if the printer advertises a
  full **IPP Everywhere** attribute set — see `IppPrinterServer.GetPrinterAttributes`.
  A minimal set is queried OK but the printer is silently not created.
- PdfSharpCore imports external page content at **`Save()`** time, so keep every
  `XPdfForm` and its source stream alive until then (see `PdfImposer.Compose`).
- Printer `Graphics` uses `GraphicsUnit.Display` = **1/100 inch**, the same unit
  as `PageBounds` and `HardMarginX/Y`. With `OriginAtMargins = false` the origin
  is the top-left of the *printable* area, not of the paper — so drawing a
  full-bleed sheet needs a `-HardMarginX/-HardMarginY` offset (`PdfPrinter`).
- Windows drivers silently fall back to their default paper unless
  `PageSettings.PaperSize` is set explicitly — hence `PaperMatch`.
- `PrinterSettings.PrintToFile` + `PrintFileName` makes *Microsoft Print to PDF*
  write silently (no save dialog). The spooler writes that file asynchronously,
  so wait for it before reporting success.
