# OpenLeanPrint user guide

Everything the tool does, from both ends: the desktop app and the command line.
For *why* it is built this way, read [ARCHITECTURE.md](ARCHITECTURE.md).

- [Two ways to get pages in](#two-ways-to-get-pages-in)
- [Capturing print jobs from other applications](#capturing-print-jobs-from-other-applications)
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
2. **Capture what you print** — register the virtual printer once, then print to
   it from any application. This is the FinePrint-style workflow and needs the
   one-time setup below.

## Capturing print jobs from other applications

Start the capture host and leave it running:

```powershell
dotnet run --project src/OpenLeanPrint.Capture.Host -- --port 6310
```

Register the printer once, in an **elevated** PowerShell:

```powershell
.\scripts\Register-Printer.ps1 -Port 6310
```

This attaches Windows' own **IPP class driver** to the loopback service — no
third-party driver is installed. Do *not* use "Select a shared printer by name"
in the Windows dialog: that path uses the legacy Internet Printing client and
will not connect.

Now print to **OpenLeanPrint Virtual Printer** from any application. The host
logs each job and writes it as a PDF to `%LOCALAPPDATA%\OpenLeanPrint\captured`.

When you are done:

```powershell
.\scripts\Unregister-Printer.ps1 -Port 6310
```

Leaving the printer registered while the host is *not* running means jobs sit in
the queue with nothing to receive them. Either keep the host running or
unregister the printer.

## The desktop app

```powershell
dotnet run --project src/OpenLeanPrint.App
```

**Job pool (left).** Every PDF you add is one job. Jobs are combined onto shared
sheets in list order — reorder with the arrows, remove what you do not want. The
**Pages** box applies to the selected job: `1-4,7` keeps those pages, `3-` keeps
everything from page three, empty keeps all. The box turns red while what you
typed is not a valid range, and nothing is applied until it is.

**Layout (top).** Presets for 1-up, 2-up, 4-up, 9-up and booklet — and a **Grid**
box for anything they do not cover: type `2x3`, `1x4`, `4x4`, or just a count
like `6`. The presets and the box always show the same thing; a grid with no
preset simply leaves them all unlit. Then paper size, margin in millimetres,
gutter in points, and a watermark. Every change
re-imposes in the background and repaints the preview.

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

**Output (bottom).** Choose sides (duplex), a printer, then **Print** — or
**Save PDF…** to keep the imposed document instead.

**Collect captured jobs.** With this on, every job the capture host writes drops
into the pool as it arrives. Only jobs arriving from then on are taken, so a
folder of old jobs is not reprinted by surprise. While collecting, closing the
window only hides it — the app keeps running in the tray so jobs keep arriving.
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
| `--gutter PT` | space between cells, points | `0` |
| `--pages LIST` | which source pages to keep, e.g. `1-4,7` | all |
| `--rotate DEG` | turn every page: `90`, `180` or `270` | `0` |
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
top to bottom. Pages are scaled to fit their cell and rotated 90° automatically
when that makes them bigger — which is why 2-up on a portrait sheet gives you
two upright pages side by side.

**Booklet** reorders pages for folding: with 8 pages, sheet one carries pages 8
and 1, sheet two carries 2 and 7, and so on, so that folding the stack in half
produces a readable booklet. Print it **two-sided, flipped on the short edge**;
long-edge flipping prints every second side upside down, because booklet sheets
are landscape.

**Margins and gutters** are different things: the margin is the border around
the whole sheet, the gutter is the space *between* cells. For stapling, a margin
of 10 mm or more is a good idea.

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
| Captured print jobs | `%LOCALAPPDATA%\OpenLeanPrint\captured` |
| App settings | `%APPDATA%\OpenLeanPrint\settings.json` |
| Imposed output from `watch` | `<watched folder>\imposed` |

Captured jobs are your real documents and are kept until you delete them. If the
capture folder is inside a synced folder (OneDrive, Dropbox), they will be
uploaded — use `--out DIR` on the host to put them somewhere else.

## Troubleshooting

**The printer appears but nothing is captured.** The capture host must be
running, on the same port the printer was registered with.

**Windows did not create the printer.** Its IPP class driver only creates a
queue if the service advertises a complete IPP Everywhere attribute set. Watch
the host window while adding the printer: a `GetPrinterAttributes` line means
Windows is talking to it.

**The print came out scaled or off-centre.** Check that the printer's paper
matches the sheet size you imposed for. OpenLeanPrint picks the matching paper,
but a driver forced to a different size will scale.

**A job in the pool will not load.** The PDF may be encrypted or damaged; the
app reports the file and keeps the others.

**Duplex was ignored.** Some drivers report no duplex support; the run then
prints single-sided and says so.
