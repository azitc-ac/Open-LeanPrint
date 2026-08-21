# OpenLeanPrint user guide

Everything the tool does, from both ends: the desktop app and the command line.
For *why* it is built this way, read [ARCHITECTURE.md](ARCHITECTURE.md).

- [Two ways to get pages in](#two-ways-to-get-pages-in)
- [Setting up the virtual printer](#setting-up-the-virtual-printer)
- [The desktop app](#the-desktop-app)
- [Hands-free: watch](#hands-free-watch)
- [Command-line reference](#command-line-reference)
- [Layouts explained](#layouts-explained)
- [Printing](#printing)
- [Where your files live](#where-your-files-live)
- [Troubleshooting](#troubleshooting)

## Two ways to get pages in

1. **Give it PDFs directly** — drag them onto the app, use *Add PDFs…*, or pass
   them on the command line. Nothing to set up.
2. **Capture what you print** — print to the OpenLeanPrint printer from any
   application. This is the FinePrint-style workflow. If you used the installer
   it already works; otherwise see the next section.

## Setting up the virtual printer

How you get the virtual printer depends on how you got OpenLeanPrint. Creating
a printer queue in Windows requires administrator rights — there is no way
around that — so the difference is *who* asks for them.

### If you installed the .msi

Nothing to do. The installer registers the **OpenLeanPrint Capture** Windows
service and creates the printer. The service holds the loopback IPP port from
system start onwards, so the printer works whether or not anyone is logged in
and whether or not the app is running — print to **OpenLeanPrint** from any
application and the job is captured.

Open the app to see the jobs, impose them and print them.

### If you installed the .msix

MSIX packages may not run install-time scripts, so the app does it: start
OpenLeanPrint once and it offers to add the printer, with one Windows
confirmation. The toolbar button **Set up virtual printer…** does the same at
any time.

### Unattended deployment

The .msi works when deployed silently as SYSTEM — Intune, SCCM, a scheduled
task — and creates the printer there too. That was measured, not assumed:
`msiexec /i … /qn` started from a SYSTEM context installed the app and the
printer without anyone logged in to help.

One implementation detail matters if you repackage this: the printer is created
from a custom action with `Impersonate="yes"`. Without it, the action runs
inside the Windows Installer service process, where `Add-Printer` is refused no
matter which account it runs as.

### If you are running from source

The app can do it the same way as above. Or, if you prefer the console host —
useful when you want to watch the IPP traffic:

```powershell
dotnet run --project src/OpenLeanPrint.Capture.Host -- --port 6310
```

and, in an **elevated** PowerShell, once:

```powershell
.\scripts\Register-Printer.ps1 -Port 6310
```

Only one of the two may listen on the port at a time. If the console host is
running, the app says so and falls back to watching the capture folder, which
still fills the pool.

### What actually happens

Windows drives the queue with its own **IPP class driver** pointed at
`http://localhost:6310/leanprint` — no third-party print driver is installed,
which is the whole reason this works on Windows on ARM. Jobs arrive as PDF and
are written to `%LOCALAPPDATA%\OpenLeanPrint\captured`.

To remove the printer again: **Remove virtual printer** in the app, or
`.\scripts\Unregister-Printer.ps1` from a source checkout. Uninstalling the
.msi removes it for you.

> A printer queue with nothing listening behind it swallows jobs. If you
> uninstall or stop OpenLeanPrint permanently, remove the printer too.

## The desktop app

```powershell
dotnet run --project src/OpenLeanPrint.App
```

**Job pool (left).** Every PDF you add is one job — this is the list of what
will be printed, in the order it will be printed. Jobs share sheets: with 4-up,
a three-page job leaves one cell of its last sheet for the job after it, which is
the point of pooling in the first place. Reorder with the arrows, remove what you
do not want.

Selecting a job does two things: the preview turns to the first sheet that job
appears on — useful once several are pooled — and the **Pages** box below applies
to it. `1-4,7` keeps those pages, `3-` keeps everything from page three, empty
keeps all. The box turns red while what you typed is not a valid range, and
nothing is applied until it is.

**Layout (top).** How many pages go on one sheet. The buttons — 1-up, 2-up,
4-up, 9-up, booklet — are the usual choices, and all they do is fill in the
**Grid** box next to them: pressing *4-up* writes `2x2`, meaning two rows by two
columns, four pages per sheet. *2-up* writes `2x1`: two pages stacked and turned
sideways, which fills the sheet — side by side and upright would leave the top
and bottom thirds empty. Type `1x2` if you want that anyway. Type into the box for anything the buttons do not
cover: `2x3`, `1x4`, `4x4`, or just a count like `6`. A grid with no matching
button simply leaves them all unlit.

**Margin** is the blank border around the whole sheet; **gutter** is the space
between the pages on it. With a 1x1 layout the gutter does nothing, because
there is nothing to be between. Both are in millimetres in the app. Leave 10 mm
or more of margin if the sheets are to be stapled or punched.

**Page borders** draws a thin frame around every page. Four pages on one sheet
separated only by white space read as a single crowded page; the frame is what
tells the eye where one ends.

**Watermark** puts text diagonally across every sheet — `DRAFT`, a file name.
Every change re-imposes in the background and repaints the preview.

**Preview (middle).** The imposed sheet as it will print. Page through the
sheets with the arrows below it.

**Right-click a page in the preview** to remove it or turn it. The click is
traced back through the layout to the page it landed on — "Remove page 3 of
report.pdf", "Turn page 3 by 90°" — and for a removal the remaining pages flow
up to fill the gap. The menu also offers to restore all pages of that job, or to
put a turned page back upright.

Removing is the quick way to do what the **Pages** box spells out; both edit the
same selection, so whichever you use, the other shows it. Turning a page switches
auto-rotation off for it: once you have said which way up you want it, the engine
stops rearranging it.

**Profiles.** Configure a layout you use often, type a name in the *Profile* box
and press **Save**. Picking it later restores the grid, paper, margins, gutter,
watermark and duplex setting in one go. **✕** deletes the selected profile.

**Output (bottom).** Choose sides (duplex), a printer, then print — or
**Save PDF…** to keep the imposed document instead.

**Print and close** is the usual end of a job: it prints, empties the pool and
puts the window away. What you have printed is dealt with; leaving it in the pool
only risks it going out again with the next job. Collecting carries on in the
tray, so the next thing you print still arrives. **Print** on its own prints and
changes nothing — for a second copy, or the same sheets on another printer.

Closing the window with **✕** empties the pool too, for the same reason. The
captured files themselves are untouched; only the list is cleared.

After printing, the status line says what was actually applied, including whether
two-sided printing happened: a printer that cannot do it prints single-sided and
says so, rather than leaving you to find out from the paper.

**Collect captured jobs.** On by default, because receiving what you print is
what the app is for. Everything the capture service writes lands in the pool —
including jobs printed while the app was closed, since the service catches those
whether or not anyone is looking. A job that arrives brings the window up, the
way a print dialog would; the tray menu's *Show the window when a job arrives*
turns that into a balloon instead.

No job enters the pool twice, restarts included: what you cleared stays cleared.
A first start facing a large backlog takes the newest 20 jobs and tells you how
many it left in the folder.

While collecting, closing the window only hides it — the app keeps running in
the tray so jobs keep arriving, and the pool starts empty when you come back.
Double-click the tray icon to bring it back, or use *Exit* there to really quit.

The app remembers your layout, paper, margins, printer, watermark and whether it
was collecting.

## Hands-free: watch

The workflow with no window at all — print from anywhere, get an imposed sheet:

```powershell
openleanprint watch --nup 2x2 --paper A4 --printer "Brother MFC-9332CDW Printer"
```

With no folder given it watches wherever the capture host writes. Every new PDF
is imposed and printed as it appears; imposed copies are also written to
`<folder>\imposed`. Ctrl+C stops it.

| Option | Meaning | Default |
|---|---|---|
| `--printer NAME` | also print each result | off — only write files |
| `--out-dir DIR` | where imposed PDFs go | `<folder>\imposed` |
| `--existing` | also process what is already there | off — only new jobs |
| `--duplex MODE` | `off`, `long`, `short`, `auto` | `auto` |
| `--dpi N` | rasterisation resolution when printing | `200` |

It also takes every layout option of `impose`.

## Command-line reference

### impose

`openleanprint impose <in.pdf> <out.pdf> [options]`

| Option | Meaning | Default |
|---|---|---|
| `--nup RxC` | grid, e.g. `2x2`; or a count, e.g. `4` | `2x2` |
| `--booklet` | saddle-stitch booklet (overrides `--nup`) | off |
| `--paper NAME` | `A6`, `A5`, `A4`, `A3`, `Letter`, `Legal`, `Tabloid` | `A4` |
| `--margin MM` | outer margin, millimetres | `0` |
| `--gutter PT` | space between cells, **points** (the app uses mm) | `0` |
| `--pages LIST` | which source pages to keep, e.g. `1-4,7` | all |
| `--rotate DEG` | turn every page: `90`, `180` or `270` | `0` |
| `--border` | thin frame around every page | off |
| `--border-width PT` | line width; implies `--border` | `0.75` |
| `--border-color HEX` | e.g. `#202020` | `#9A9AA2` |
| `--watermark TEXT` | text across every sheet | none |
| `--watermark-opacity N` | `0`–`1` | `0.18` |
| `--watermark-color HEX` | e.g. `#C00000` | `#808080` |
| `--watermark-size PT` | `0` fits it to the sheet | `0` |

### print

`openleanprint print <in.pdf> [options]`

| Option | Meaning | Default |
|---|---|---|
| `--printer NAME` | target printer | Windows default |
| `--duplex MODE` | `off`, `long`, `short`, `auto` | `auto` |
| `--copies N` | copies requested from the driver | `1` |
| `--dpi N` | rasterisation resolution, 36–1200 | `200` |
| `--out FILE` | write to a file instead of paper | off |

### list-printers, sample

`list-printers` shows what is installed (`*` marks the default).
`sample out.pdf --pages 8` writes a colourful test document — each page carries
its number as a bar chart and a red "this edge is up" marker, which makes layout
mistakes obvious at a glance.

## Layouts explained

**N-up** places `rows × columns` source pages on each sheet, left to right then
top to bottom. Pages are scaled to fit their cell and turned 90° automatically
when that makes them bigger.

That is why a plain count means a taller grid than you might expect: **2 is
`2x1`** — two rows of one — not `1x2`. Sheets are upright and so are most source
pages, and a page turned sideways fills a wide, short cell far better than an
upright page fills a narrow, tall one. Two pages side by side on an upright A4
cover about half of it; stacked and turned, they cover it. The same reasoning
gives `3x2` for six and `4x2` for eight.

Writing `RxC` yourself overrides all of that: `1x2` really is one row of two.

**Booklet** reorders pages for folding: with 8 pages, sheet one carries pages 8
and 1, sheet two carries 2 and 7, and so on, so that folding the stack in half
produces a readable booklet. Print it **two-sided, flipped on the short edge**;
long-edge flipping prints every second side upside down, because booklet sheets
are landscape.

**Margins and gutters** are different things: the margin is the border around
the whole sheet, the gutter is the space *between* cells. For stapling, a margin
of 10 mm or more is a good idea.

**Page borders** are drawn around each page, not around each cell — a page that
does not fill its cell is framed at its own edges, so the frame tells you the
real page size rather than the grid.

## Printing

Sheets are rasterised with PDFium and drawn onto the printer at 1:1, on the
paper size that matches the sheet. Three details make that WYSIWYG rather than
"about right", and they are documented in [M3-PRINT.md](M3-PRINT.md).

Two practical notes:

- Printers cannot print to the very edge. The sheet is mapped to the full paper,
  so the outermost millimetres fall into the hardware margin. Give layouts a
  margin if edge content matters.
- `--duplex` is only requested when the printer reports it can; otherwise the
  job prints single-sided and says so rather than failing.

Test without paper by printing to **Microsoft Print to PDF** with `--out`:

```powershell
openleanprint print out.pdf --printer "Microsoft Print to PDF" --out proof.pdf
```

## Where your files live

| What | Where |
|---|---|
| Captured print jobs (service) | `%ProgramData%\OpenLeanPrint\captured` |
| Captured print jobs (app or console host) | `%LOCALAPPDATA%\OpenLeanPrint\captured` |
| What the service did | `%ProgramData%\OpenLeanPrint\service.log` |
| App settings | `%APPDATA%\OpenLeanPrint\settings.json` |
| Imposed output from `watch` | `<watched folder>\imposed` |

Captured jobs are your real documents and are kept until you delete them. If the
capture folder is inside a synced folder (OneDrive, Dropbox), they will be
uploaded — use `--out DIR` on the host to put them somewhere else.

## Troubleshooting

**"This installation package could not be opened."** The .msi is in a synced
folder — OneDrive, Dropbox, or a redirected Downloads folder. Windows Installer
runs as SYSTEM and cannot pull down a cloud placeholder, so it reports the file
as invalid when it is merely not there yet. Copy the .msi somewhere local, such
as `C:\Users\Public\Downloads`, and run it from there.

**There is no tray icon.** Windows 11 keeps new notification-area icons
hidden: click the chevron next to the clock ("Show hidden icons") and drag
OpenLeanPrint onto the taskbar if you want it there permanently. Whether the app
is running at all is answered by `%APPDATA%\OpenLeanPrint\app.log`, which
records every start with account and session.

**I printed and nothing happened.** Look at
`%ProgramData%\OpenLeanPrint\service.log` first. A `Captured job #n` line means
the job did arrive and only the display was missing: open OpenLeanPrint from the
Start menu and the waiting jobs are in the pool. No such line means nothing
reached the service — check that it is running with
`Get-Service OpenLeanPrintCapture`.

**The printer appears but nothing is captured.** Something must be listening on
the port the printer was registered with — the **OpenLeanPrint Capture** service
if you installed the .msi, otherwise the app or the console host.

**Windows did not create the printer.** Its IPP class driver only creates a
queue if the service advertises a complete IPP Everywhere attribute set. Watch
the host window while adding the printer: a `GetPrinterAttributes` line means
Windows is talking to it.

**The print came out scaled or off-centre.** Check that the printer's paper
matches the sheet size you imposed for. OpenLeanPrint picks the matching paper,
but a driver forced to a different size will scale.

**A job in the pool will not load.** The PDF may be encrypted or damaged; the
app reports the file and keeps the others.

**Duplex was ignored, or came out flipped the wrong way.** Some drivers report
no duplex support; the run then prints single-sided and says so in the status
line, which is worth reading before blaming the layout. If two-sided printing
happens but on the wrong edge, check the printer's own default: OpenLeanPrint
asks for long or short edge exactly as Windows defines it, but a driver whose
private settings say otherwise can still have the last word. Windows' own
setting is `Get-PrintConfiguration -PrinterName "…"`.
