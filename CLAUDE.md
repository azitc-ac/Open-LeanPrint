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

Everything is pushed to `main`. 35 tests pass on Linux/CI.

## What is next (pick up here)

Two tracks, roughly in priority order — confirm with the user which they want:

1. **M3 — forward to a physical printer.** Take an imposed output PDF and send
   it to a user-chosen Windows printer (e.g. via the print spooler / a silent
   PDF print, or GDI/Direct2D). Optionally a watcher that auto-imposes new files
   dropped in `captured/` and prints them. Acceptance: a captured job comes out
   of a real printer 4-up. **You can test this on Windows — do it.**
2. **M2 preview + GUI.** On-screen raster preview (PDFium) + a WinUI 3 desktop
   app: job pool list, preset buttons (2-up/4-up/booklet), live WYSIWYG preview,
   print. Keep rendering logic in a platform-neutral project where possible so
   it stays testable.

## Build, test, run

Requires the **.NET 8 SDK**. Everything except a future WinUI app is
cross-platform.

```powershell
dotnet test                                   # build + run all tests (35)
dotnet run --project src/OpenLeanPrint.Capture.Host -- --port 6310
dotnet run --project src/OpenLeanPrint.Cli -- impose in.pdf out.pdf --nup 2x2 --paper A4
dotnet run --project src/OpenLeanPrint.Cli -- sample sample.pdf --pages 8
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
| `src/OpenLeanPrint.Cli` | `openleanprint` CLI: `impose` / `sample`. |
| `tests/**` | xUnit tests (Core, Capture, Compose) — run on any OS. |
| `scripts/*.ps1` | Windows printer register/unregister. |
| `docs/` | ARCHITECTURE, ROADMAP, M1-CAPTURE, M2-IMPOSE. |

## Conventions & guardrails

- **Git:** commit and **push directly to `main`** (the user asked for no PRs).
  Use `git push -u origin main`. `.gitattributes` pins `*.ps1` to CRLF — do not
  fight it.
- **License:** MIT © Alexander Zarenko. Keep it permissively licensed:
  dependencies are **PdfPig** (Apache-2.0) and **PdfSharpCore** (MIT). **Do not**
  introduce AGPL libraries (Ghostscript, MuPDF, iText) into shipping code.
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
