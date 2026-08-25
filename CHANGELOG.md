# Changelog

All notable changes to Open-LeanPrint. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and versions follow
[Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [0.4.2] — 2026-08-25

### Fixed

- **An installation could end with no printer.** Printer setup only ran on a
  first install. Installing a package whose product was already registered
  counted as "installed", so setup was skipped — and when a related product was
  removed in the same transaction, its uninstall took the printer with it.
  Nothing was left to put it back: an installed program, no printer, no
  explanation.

  Setup now runs on any installation that is not a removal, which makes it
  self-healing — if the printer is ever missing, installing again brings it back.
  The scripts were already safe to repeat: the printer step exits when a queue is
  there, and the service step only replaces itself.
## [0.4.1] — 2026-08-25

### Fixed

- **"Set up virtual printer…" did nothing.** No elevation prompt, no printer, no
  explanation. The button insisted on the app hosting the loopback service
  itself, and on an installed machine the Windows service already owns that
  port - so the app's own listener could not start and the code returned before
  it ever asked for the printer.

  What matters is that *something* is answering, not that it is this app. It now
  checks the endpoint, and only starts its own listener when nobody else is
  serving it.
## [0.4.0] — 2026-08-25

### Changed

- **The name is now written `Open-LeanPrint`.** Same tool, a hyphen: it reads
  better, it is easier to remember, and it matches the repository, which had the
  hyphen all along.

  What changed is what people see - the window, the tray, the printer in your
  print dialog (**Open-LeanPrint Virtual Printer**), the entry in Add/Remove
  Programs, the Start-menu shortcut, the installer file name, and the folder
  under Program Files.

  What did not change is anything that is an identifier rather than a name:
  namespaces, assemblies, project folders, the `openleanprint` command, the
  service id, and the data folders under `%ProgramData%` and `%APPDATA%`. A
  hyphen is not valid in a C# identifier, and renaming the data folders would
  orphan every captured job and every setting already in them.

  Printer lookups now match on `LeanPrint` rather than the full name, so a queue
  created before the rename is still found - and not quietly duplicated.

## [0.3.2] — 2026-08-25

### Added

- **Right-click a job in the pool** to remove it, to put all its pages back in
  the printing, or to empty the pool. The row under the pointer is selected
  first, so the menu acts on the job it opened over rather than on whatever was
  selected before.

### Changed

- **Jobs look like jobs.** Each is a white card with a border on a quieter
  background, instead of plain rows on the same white as their container - a
  list of three read as one block of text.
- **The selected layout buttons are readable.** They had white text in the
  template and black text on screen: WPF draws a button's label with a TextBlock
  of its own, and the theme's implicit TextBlock style set a colour explicitly,
  which beats anything the button inherits. Text colour now comes from the
  window, and controls that want something else say so.

- **A new icon.** The old one carried a 2x3 grid of pages: fine on a 256-pixel
  tile, a grey smudge at 16, which is the size that matters most because the
  notification area is where this app mostly sits. It is now two landscape pages
  on a folded sheet - simpler, and what 2-up actually produces.
- The icon has a generator in the repository (`scripts/New-Icon.ps1`) which
  writes the `.ico`, the MSIX tiles and the README image in one run. The previous
  one was drawn once by hand and left no way to make it again.

### Documentation

- Where captured jobs live now that the service writes them, that colour goes
  through, page borders, *Print and close*, and the current test count.
- `CLAUDE.md` records the four installer rules this week established the hard
  way: the launch must not inherit the installer's token, the product code
  follows the version while the upgrade code never changes, every build needs a
  rising file version, and `MSIRESTARTMANAGERCONTROL` does not remove the
  "please close these applications" question.
- Two-sided printing is written up as closed on this side, with the measurements,
  so nobody goes looking for it in `PdfPrinter` again.

## [0.3.1] — 2026-08-25

### Fixed

- **The installer started the app with administrator rights.** Its launch action
  inherited the installer's token, so the app ran as whoever had authorised the
  installation. Where that is a separate administrator account, the app then ran
  as *that* account: its profile in the file dialog, its settings, and
  administrator rights the app has no use for. A window with a file dialog in it
  and administrator rights behind it is a way to reach the whole machine as
  administrator, which is not something a print utility should hand out.

  The launch now goes through Explorer, which hands it to the session that
  already has one, so the app runs as the person sitting there. If nothing
  answers - a silent deployment, nobody logged in - nothing starts, which is the
  right way for this to fail: never running is a nuisance, running as
  administrator is a hole.

  Anyone who installed 0.3.0 has a copy running under the administrator account
  until they log out. It shows up in Task Manager under a different user name.
- The app records the account it is running under, and says so in the window if
  that account is an administrator. Nothing here needs administrator rights.

## [0.3.0] — 2026-08-24

The release that came out of using it. Everything here was found by printing
something and looking at what happened — a folder quietly filling with every
page anyone had ever printed, a 2-up layout that wasted half the sheet, colour
documents arriving grey, an uninstall that took two minutes and left its tray
icon behind. Several of them were fixed twice, because the first explanation was
wrong and the measurement said so.

### Added
- **Captured jobs are cleared up.** Nothing ever removed them, so the folder
  grew with every page anyone printed, somewhere nobody looks.

  In normal use it is now empty. A captured job is spool output on its way from
  the print queue into the window, not a document - what it was printed from is
  still open wherever it was - so the app reads the file into the pool and
  deletes it there and then. What can be left behind is jobs printed while no app
  was running to take them, and the service clears those after 7 days or 500 MB,
  whichever comes first, touching only files it wrote itself (`--keep-days`,
  `--keep-mb`). Uninstalling removes the folder outright.

  A PDF you dragged in yourself is never deleted. For those the file is the
  document, and that difference is the one thing checked before anything is
  removed - with its own test.
- The service grants the machine's users control of its capture folder.
  `C:\ProgramData` gives ordinary users read and create but not delete, so
  whether you could remove your own captured jobs depended on who created the
  folder first - the service, or an app running as you. Measured on a machine
  where the accident had gone the lucky way and hidden the problem.
- **Print and close**, now the default button: prints, empties the pool and
  puts the window away - the usual end of a job. What has been printed is dealt
  with, and keeping it in the pool only risks it going out again with the next
  job. Collecting carries on in the tray. **Print** still prints and changes
  nothing, for a second copy or another printer.
- Closing the window empties the pool, for the same reason. The captured files
  are untouched; only the list is cleared.
- **Page borders** — a thin frame around every page, in the app, the CLI
  (`--border`, `--border-width`, `--border-color`) and the engine. Four pages on
  a sheet with nothing but white between them read as one crowded page.
- The capture service records what each job asked for - `sides` and
  `print-color-mode` - which also makes the virtual printer a measuring
  instrument: printing into it shows what a print path really asked a driver for,
  rather than what it was told to ask for.
- A startup log, `%APPDATA%\Open-LeanPrint\app.log`. "The app is not running"
  was otherwise unanswerable after the fact: whether anything ever started it,
  under which account and in which session, left no trace anywhere.
- **A Windows service** holds the loopback IPP port, so the virtual printer
  works from system start, with nobody logged in and with the app closed.
  Previously the listener lived in the desktop app: printing failed right after
  installing and again whenever anyone quit the app from the tray, leaving jobs
  stuck in the queue with no explanation. The service writes to
  `%ProgramData%\Open-LeanPrint\captured` — LocalSystem has no per-user folder
  anyone could reach — and keeps a log next to it.
- The installer registers and starts the service, and removes it again on
  uninstall.

### Changed
- The app says what a print run actually did, including which duplex mode was
  applied. It only ever mentioned duplex when the printer could not do it, so
  "did it even ask for two-sided?" had no answer inside the app.
- Selecting a job in the pool turns the preview to the first sheet that job
  appears on. Clicking a row used to have no visible effect, which made the list
  look decorative.
- The gutter is entered in millimetres, like the margin. Millimetres beside
  points invited reading "1" as a millimetre and getting a third of one. Saved
  settings keep their meaning - the value is still stored in points.
- Plainer wording for the layout controls: what the preset buttons do to the
  grid box, and that the margin is the border around the sheet while the gutter
  is the space between the pages on it.
- The job pool explains itself, and the pool list reads its file name to screen
  readers.
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
- **The virtual printer was monochrome.** It advertised itself to Windows as a
  greyscale device, so Windows converted every job on the way in and colour
  documents arrived already grey - a loss that happens before the file reaches
  Open-LeanPrint and cannot be undone afterwards. It now advertises colour, which
  is the honest answer: this printer prints nothing itself, it hands the document
  to a real one, and that is where the question belongs.

  An existing printer queue keeps the capabilities it was created with. Uninstall
  and install again to pick this up.
- **Uninstalling was slow, noisy and left the tray icon behind.** The app was
  running, Windows found its files in use and set the Restart Manager on it -
  which asked the user to close applications, spent about two minutes looking,
  and had the process terminated in the end. A terminated process never takes
  its tray icon away, because the notification area only drops an icon when its
  owner asks it to.

  The app is now asked to stop before any file is touched and shuts down through
  its normal exit path, tray icon and all, so there is nothing left to find.
- Uninstalling removes `%ProgramData%\Open-LeanPrint`, which it used to leave
  behind entirely.
- **2-up was close to useless.** It laid two upright pages side by side on an
  upright sheet, which fills about half of it and wastes the top and bottom
  thirds. Two pages now stack, and the automatic turn - which was there all
  along - makes them fill the sheet. A plain count follows the same rule
  wherever it helps: 2 is `2x1`, 6 is `3x2`, 8 is `4x2`. Asking for `RxC`
  yourself still means exactly what it says.
- **The layout buttons never wrote the grid box.** Pressing *2-up* changed the
  layout and left the box reading whatever it said before - the code even
  carried a comment claiming the two could not disagree.
- The grid box rewrote itself under the caret: typing `2x3` passed through `2`,
  which parses as a count, so after one keystroke the box said `1x2` and the
  rest of what you typed landed after it.
- A narrow window squeezed the wrapped second row of the toolbar against the
  first: the controls had no vertical margin, so wrapping produced no gap.
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
  virtual printer, and runs Open-LeanPrint in the background at login. Works
  unattended as SYSTEM too, so it can be deployed by Intune or SCCM. The printer
  step needs `Impersonate="yes"` on the custom action: without it the action runs
  inside the Windows Installer service process, where `Add-Printer` is refused
  regardless of account.
- **The app hosts the capture service itself.** Previously capturing meant
  running a separate console host from a source checkout, which an installed
  copy had no way to do — so an installed Open-LeanPrint could not capture
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
- Captured jobs now default to `%LOCALAPPDATA%\Open-LeanPrint\captured` instead
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
