# Open-LeanPrint — context for Claude Code

You are working on **Open-LeanPrint**, a modern, lightweight, open-source
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
`…\Documents\GitHub\Open-LeanPrint`. `Add-Printer` needs an **elevated**
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
  round trip capture → impose → print. A sheet was also sent to the physical
  Brother queue and completed (1/1 page); nobody has inspected the paper yet.

- **M4 — desktop app** ✅ (`OpenLeanPrint.App`, **WPF**): job pool (several PDFs
  combined onto shared sheets), presets, live WYSIWYG preview, page borders,
  print, "Print and close", save PDF, settings that survive a restart, drag &
  drop, one instance per session, and a tray icon that keeps collecting with the
  window closed. Verified by driving the real window.
  `scripts/Publish-App.ps1` produces one self-contained exe and
  `scripts/Build-Msix.ps1` a signed MSIX — see [`docs/M4-APP.md`](docs/M4-APP.md).
- **A Windows service** (`--service` on the capture host) holds the loopback IPP
  port, so the printer works from system start, with nobody logged in and with
  the app closed. The installer registers it. Captured jobs are a hand-over, not
  an archive: the app reads each file into the pool and deletes it, and the
  service prunes what nobody collected (7 days / 500 MB, `CapturedFolder`).
- **`watch`** ✅: imposes (and optionally prints) every new PDF in a folder — the
  hands-free workflow. Arrival detection is `CapturedFolderWatcher` in
  `OpenLeanPrint.Capture`, shared with the app and unit-tested.
- **M5 — parity features** ◑: duplex (`DuplexMode`, short edge for booklets),
  page selection (`PageSelection`, "1-4,7", counted per document), watermarks
  (`Watermark` on `PdfImposer`), per-page rotation (`SourcePage.Rotation`, which
  also disables auto-rotate for that page) and named layout profiles are done in
  engine, CLI and app. Skip-blank-page detection was dropped on purpose — the
  preview plus right-click removal covers it without guessing.

Everything is pushed to `main`. 163 tests pass (Windows-only ones self-skip on
Linux; CI runs both a Linux and a Windows job). Latest release: **0.3.1**.

**Two solutions:** `OpenLeanPrint.sln` is the cross-platform one that CI builds
and tests — **do not add the WPF app to it**, WPF cannot build on Linux.
`OpenLeanPrint.Windows.sln` contains everything including the app.

## What is next (pick up here)

Roughly in priority order — confirm with the user which they want:

1. **SignPath Foundation application** — the user intends to apply for a free
   open-source code-signing certificate. Everything else is ready: MIT licence,
   public repository, CI, documentation, reproducible signed builds. Only a
   publicly trusted certificate is missing.
2. **Look at the printed sheet** — a 4-up test page went to the Brother on
   2026-08-17 and the spooler completed it, but the paper itself has not been
   checked. Confirming margins/scale on paper closes M3's last exit criterion.
3. **More than one language** — see "Later" in `docs/ROADMAP.md`. The interface
   is English with no localisation layer; strings sit in the XAML.

**Two-sided printing is closed as far as this code goes.** Choosing long or short
edge produces short-edge output on the user's Brother, and every layer that can
be inspected carries the right value: the app sets `Duplex.Vertical`, the queued
job holds `dmDuplex 2` with `DM_DUPLEX` set, the driver reads it back as
`TwoSidedLongEdge`, and it leaves as `sides=two-sided-long-edge`. Handing the
DEVMODE back to the driver to reconcile changes nothing - long and short differ
by one byte of the documented field and none of the driver's private area. The
device is where it stops. Do not "fix" this in `PdfPrinter` without new evidence.

## Build, test, run

Requires the **.NET 8 SDK**. Everything except the WPF app is cross-platform.

```powershell
dotnet test                                   # build + run all tests (110)
dotnet run --project src/OpenLeanPrint.Capture.Host -- --port 6310
dotnet run --project src/OpenLeanPrint.Cli -- impose in.pdf out.pdf --nup 2x2 --paper A4
dotnet run --project src/OpenLeanPrint.Cli -- sample sample.pdf --pages 8
dotnet run --project src/OpenLeanPrint.Cli -- list-printers
dotnet run --project src/OpenLeanPrint.Cli -- print out.pdf --printer "Microsoft Print to PDF" --out proof.pdf
dotnet run --project src/OpenLeanPrint.Cli -- watch captured --nup 2x2 --paper A4   # hands-free
dotnet run --project src/OpenLeanPrint.App                    # the desktop app
dotnet build OpenLeanPrint.Windows.sln                        # everything incl. the app
```

End-to-end capture test on Windows (see `docs/M1-CAPTURE.md`):
1. Start the capture host (above), leave it running.
2. **Elevated** PowerShell: `.\scripts\Register-Printer.ps1 -Port 6310`
   (or `Add-Printer -IppURL http://localhost:6310/leanprint`).
3. Print to "Open-LeanPrint Virtual Printer" from any app.
4. The host logs the job and saves the PDF to
   `%LOCALAPPDATA%\Open-LeanPrint\captured` (`CaptureLocations.DefaultFolder`;
   `--out DIR` overrides). Deliberately not the working directory: captured jobs
   are the user's real documents and the repo lives in a synced OneDrive folder.
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
| `src/OpenLeanPrint.App` | WPF desktop app: pool, live preview, print. Not in OpenLeanPrint.sln. |
| `src/OpenLeanPrint.Cli` | `openleanprint` CLI: `impose` / `sample` / `print` / `list-printers` / `watch`. |
| `tests/**` | xUnit tests (Core, Capture, Compose, Print) — run on any OS. |
| `scripts/*.ps1` | Printer register/unregister; `Publish-App.ps1` (single exe), `Build-Installer.ps1` (.msi), `Build-Msix.ps1` + `New-SigningCertificate.ps1`, `New-Icon.ps1` (draws the icon and every packaged size). |
| `packaging/` | MSIX manifest, tile assets, and the pinned SDK packaging tools. |
| `installer/` | WiX .msi: sets up the virtual printer during installation. |
| `docs/` | ARCHITECTURE, USER-GUIDE, ROADMAP, M1-CAPTURE, M2-IMPOSE, M3-PRINT, M4-APP. |

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
- **The GUI is WPF, deliberately.** It ships with the .NET SDK, is ARM64-native
  and needs no runtime install or MSIX to start; WinUI 3 would have required the
  Windows App SDK runtime first. Keep app styles in `Theme.xaml` (a plain
  resource dictionary) so a window can be hosted or rendered without booting
  `App`. WPF implicitly imports `System.Windows.Shapes`, so files that touch the
  file system need `using Path = System.IO.Path;`.
- **The app also enables WinForms** — only for the tray `NotifyIcon`. Its
  implicit usings are removed in the csproj (`<Using Remove="System.Windows.Forms" />`
  and `System.Drawing`), otherwise `Application`, `MessageBox`, `Point` and
  `Size` become ambiguous with WPF's. Keep WinForms types behind `TrayPresence`.
- **`ShutdownMode` is `OnExplicitShutdown`** so hiding to the tray cannot end
  the app; `MainWindow.OnClosed` is the single place that shuts it down.
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
- **Never let the installer start the app directly.** A custom action inherits
  the installer's token, so `ExeCommand="[INSTALLFOLDER]OpenLeanPrint.exe"` runs
  the app as whoever authorised the installation - on this machine a separate
  admin account, giving an elevated window with a file dialog in it. The launch
  goes through `explorer.exe` and the Startup shortcut, which hands it to the
  session that already has one. Failing to start is the acceptable failure;
  starting as an administrator is not.
- **The ProductCode must follow the version, the UpgradeCode never changes.** Let
  WiX generate a ProductCode per build and the uninstall GUID moves under your
  feet; nail it to one value and upgrading dies with "another version of this
  product is already installed", because Windows skips a package whose code is
  already installed. `Build-Installer.ps1` derives it from the version.
- **Every build needs a rising FileVersion.** Windows Installer skips a packaged
  file whose version is not higher than the installed one, so rebuilding without
  a new stamp leaves the old binaries in place and the change under test never
  runs. That cost an afternoon of measuring a binary from hours earlier. The
  stamp is days-then-minutes; use `TimeSpan.Days`, not `[int]` on `TotalDays`,
  which rounds and can make the newer build lose.
- `MSIRESTARTMANAGERCONTROL=Disable` does **not** remove the "please close these
  applications" question. It falls back to older, coarser detection that then
  names unrelated programs. Stopping our own processes first is the actual fix.
- **Creating a printer queue needs administrator rights** — verified, not
  assumed: `Add-Printer` fails with access denied as a normal user even with the
  IPP service answering. That is why the .msi exists (an installer is already
  elevated) and why the app's own setup button raises a UAC prompt.
- **The app hosts the capture service itself** (`CaptureService`), so an
  installed copy needs no console host. `--capture-service` runs it headless,
  which is what the installer uses to have the queue created against a live
  endpoint; `--tray` starts hidden and collecting.
- **MSIX needs no Windows SDK install:** `makeappx`/`signtool` come from the
  `Microsoft.Windows.SDK.BuildTools` NuGet package (arm64 included), pinned in
  `packaging/SdkTools`. The manifest's `Publisher` must match the signing
  certificate's subject exactly.
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
