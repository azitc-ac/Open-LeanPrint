# Changelog

All notable changes to OpenLeanPrint. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and versions follow
[Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added
- **An installer (.msi)** that sets everything up: installs the app, creates the
  virtual printer, and runs OpenLeanPrint in the background at login. The
  printer step needs `Impersonate="yes"` on the custom action — the print stack
  refuses `Add-Printer` from SYSTEM even when elevated, and accepts it from the
  user performing the installation. Established by running the same call from
  four differently scheduled custom actions.
- **The app hosts the capture service itself.** Previously capturing meant
  running a separate console host from a source checkout, which an installed
  copy had no way to do — so an installed OpenLeanPrint could not capture
  anything at all.
- **Set up virtual printer…** in the app, for installs that cannot do it
  themselves (MSIX forbids install-time scripts). One confirmation, once.
- `--capture-service` (service only, no window) and `--tray` (start hidden,
  already collecting) command-line switches.

### Fixed
- The documentation described a workflow only a source checkout could follow.
- `Build-Installer.ps1` warns when it writes the .msi into a synced folder:
  Windows Installer cannot open a cloud placeholder and blames the package.

## [0.2.0] — 2026-08-19

Everything a page needs doing to it before it prints: turn it, drop it, or lay
it out the way you want.

### Added
- **Per-page rotation** — turn a single page from the preview's right-click
  menu, or every page at once with `--rotate 90|180|270`. An explicit turn
  switches auto-rotation off for that page and the layout refits it at its new
  proportions.
- **Layout profiles** — save the current layout under a name and come back to
  it later. Stored with the app's settings.
- **Free grid input in the app** — every layout a profile can store is now
  reachable from the UI, including grids the presets do not cover (`2x3`,
  `1x4`). Grid parsing moved to `NUpGrid` in the core so the CLI and the app
  read it identically.

### Changed
- New application icon: a dog-eared sheet carrying a 2×3 grid of pages, which
  says "paper" and "several pages on one sheet" without looking like a window.

## [0.1.0] — 2026-08-19

First release: the whole chain works, and there is an app for it.

### Added
- **Duplex printing** (`--duplex off|long|short|auto`, "Sides" in the app).
  Booklets want short-edge flipping; the printer is only asked for duplex when
  it reports support, and the result says what actually happened.
- **Page selection** (`--pages 1-4,7`, per-job "Pages" in the app) — drop pages
  before imposing. Numbers count within each document, not across the pool.
- **Right-click a page in the preview to remove it** — the click is traced back
  through the layout to the source page it landed on.
- **Watermarks** (`--watermark DRAFT`, plus colour, opacity, angle and size) —
  drawn across every finished sheet, auto-sized to the paper.
- **MSIX packaging** (`scripts/Build-Msix.ps1`) using makeappx/signtool from a
  NuGet package: no Windows SDK installation and no administrator rights.
  `scripts/New-SigningCertificate.ps1` creates the sideloading certificate;
  signing can happen straight from the certificate store, so no key file and no
  password need to exist.
- **Desktop app** (`OpenLeanPrint.App`, WPF): job pool, live WYSIWYG preview,
  layout presets, print, save PDF, drag & drop, tray icon that keeps collecting
  captured jobs with the window closed, and settings that survive a restart.
- **`watch`** — impose (and optionally print) every new PDF in a folder.
- **`print` / `list-printers`** in the CLI, printing through the Windows spooler.
- Single-file distributable build (`scripts/Publish-App.ps1`).

### Changed
- Captured jobs now default to `%LOCALAPPDATA%\OpenLeanPrint\captured` instead
  of the working directory: they are real documents, and a working directory is
  often a source tree.
- CI runs on Linux **and** Windows, so the Windows-only tests actually execute.

### Foundation

- Imposition engine: N-up grids and saddle-stitch booklets, in points with a
  top-left origin, unit-tested and platform-neutral.
- Driverless capture: a loopback IPP service that Windows' in-box IPP class
  driver attaches to, so printing from any application arrives as PDF without a
  third-party print driver.
- Composition of imposed sheets into an output PDF (vector, via PdfSharpCore).
