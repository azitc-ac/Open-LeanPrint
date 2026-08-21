# Changelog

All notable changes to OpenLeanPrint. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and versions follow
[Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added
- A startup log, `%APPDATA%\OpenLeanPrint\app.log`. "The app is not running"
  was otherwise unanswerable after the fact: whether anything ever started it,
  under which account and in which session, left no trace anywhere.
- **A Windows service** holds the loopback IPP port, so the virtual printer
  works from system start, with nobody logged in and with the app closed.
  Previously the listener lived in the desktop app: printing failed right after
  installing and again whenever anyone quit the app from the tray, leaving jobs
  stuck in the queue with no explanation. The service writes to
  `%ProgramData%\OpenLeanPrint\captured` — LocalSystem has no per-user folder
  anyone could reach — and keeps a log next to it.
- The installer registers and starts the service, and removes it again on
  uninstall.

### Changed
- The app watches both the machine-wide and the per-user capture folder, and
  steps aside when the service already owns the port.
- **Captured jobs actually show up.** Three things had to be true at once and
  only one of them was, so printing into the virtual printer looked like nothing
  happening at all — while the service was capturing the jobs correctly the
  whole time:
  - jobs already waiting in the capture folder now go into the pool when the app
    starts, instead of only ones arriving while it happens to be open. The
    service captures around the clock; the app has to catch up with it.
  - a job never enters the pool twice, across restarts, so that catching up
    cannot turn into re-showing what you already dealt with.
  - a job arriving now brings the window up, the way a print dialog would.
    Switch that off in the tray menu for a balloon instead.
- *Collect captured jobs* is on by default. An app whose purpose is to receive
  what you print should not have to be told to.
- A first start with a large backlog takes the newest 20 jobs and says how many
  it left in the folder, rather than filling the pool with months of history.

### Fixed
- **The installer never started the app.** Its launch action named the
  program relatively, and a relative program name is not found: a throwaway
  package ran both forms side by side and the relative one failed with error
  1721 while the full path started. The script actions only looked like the
  failing form - there the program is `powershell.exe` from PATH and only its
  argument is relative, which is why they worked and this one silently did not.
  It now uses the full path and runs from the UI sequence, in the session with
  a desktop.
- Only one copy of the app runs per session. The login shortcut and the Start
  menu entry used to give you two windows, two folder watchers - every captured
  job collected twice - and two attempts on the same port. A second start now
  hands over any files it was asked to open and raises the copy already running.
- Rows in the job pool announced themselves to screen readers as
  `OpenLeanPrint.App.JobItem`, not as the file name.
- The installer keeps a fixed product code. WiX generates a new one per build
  unless told not to, which made every build a different product to Windows: a
  written-down `msiexec /x` line stopped working after the next build.

### Note for multi-user machines
- Jobs captured by the service land in a machine-wide folder, so they are
  readable by other users of that machine. Running without the service keeps
  captured documents per-user.

## [0.2.1] — 2026-08-20

Install it and print — the setup that used to be a page of instructions is now
part of installing.

### Added
- **An installer (.msi)** that sets everything up: installs the app, creates the
  virtual printer, and runs OpenLeanPrint in the background at login. Works
  unattended as SYSTEM too, so it can be deployed by Intune or SCCM. The printer
  step needs `Impersonate="yes"` on the custom action: without it the action runs
  inside the Windows Installer service process, where `Add-Printer` is refused
  regardless of account.
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
