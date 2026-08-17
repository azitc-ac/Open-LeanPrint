# OpenLeanPrint

A modern, lightweight, open-source alternative to **FinePrint** — pool print
jobs, preview them with a WYSIWYG dialog, arrange them **N-up** (2-on-1,
4-on-1, booklet …), and forward the result to a real printer.

Designed from day one to run on **Windows on ARM (ARM64)** as well as x64, by
avoiding the one thing that makes classic virtual-printer tools ARM-hostile:
a third-party kernel/print driver.

> Status: **the whole chain works from the command line.** Printing from a real
> app is captured over driverless IPP, imposed N-up or as a booklet, and sent
> back out to a printer — verified on **Windows 11 ARM64**: `Add-Printer -IppURL`
> attaches the in-box Microsoft IPP Class Driver to the loopback service, and an
> imposed 4-up sheet printed through "Microsoft Print to PDF" comes back exactly
> as laid out. What is missing is the face of it: the on-screen WYSIWYG preview
> and the app UI — see the roadmap.

## Why another print tool?

Microsoft is phasing out third-party v3/v4 printer drivers and moving to
**Windows Protected Print (WPP)**, which only permits the in-box IPP class
driver. On **Windows on ARM**, x64 print drivers are not emulated at all. A
FinePrint-style tool built as a signed virtual **driver** is therefore both a
signing/ARM headache and a shrinking runway.

OpenLeanPrint takes the driverless route instead — see
[docs/ARCHITECTURE.md](docs/ARCHITECTURE.md).

## What it does (target feature set)

- **Pool print jobs** — print from any app; jobs collect in one place to be
  reviewed and combined ("summarise print jobs").
- **N-up imposition** — 1/2/4/n pages per sheet, rows × columns, margins,
  gutters, cell order, auto-rotate to maximise size.
- **Booklet** — saddle-stitch page reordering for fold-and-staple booklets.
- **WYSIWYG preview** — see the exact sheet layout before printing.
- **Forward to any printer** — send the imposed result to a physical printer.

## Architecture at a glance

```
App prints ─► Microsoft in-box IPP class driver
                     │  (PDF over IPP, driverless, ARM64-native)
                     ▼
        OpenLeanPrint local loopback IPP service  ──►  Job pool
                     │
                     ▼
        Imposition engine (this repo, tested)  ──►  WYSIWYG preview
                     │
                     ▼
              Forward to the chosen physical printer
```

Full detail, alternatives and trade-offs: [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md).

## Repository layout

| Path | Description |
|---|---|
| `src/OpenLeanPrint.Core` | Platform-neutral domain model + imposition engine (net8.0, no Windows deps). |
| `src/OpenLeanPrint.Capture` | Loopback IPP capture service + IPP codec + PDF page extraction (net8.0). |
| `src/OpenLeanPrint.Capture.Host` | Runnable console host: logs and saves captured jobs. |
| `src/OpenLeanPrint.Compose` | Imposes an `ImpositionResult` into an output PDF (PdfSharpCore, net8.0). |
| `src/OpenLeanPrint.Print` | Prints an imposed PDF to a Windows printer (PDFium raster + spooler). |
| `src/OpenLeanPrint.Cli` | `openleanprint` CLI: `impose` / `sample` / `print` / `list-printers`. |
| `tests/OpenLeanPrint.Core.Tests` | xUnit tests for the engine; run on any OS. |
| `tests/OpenLeanPrint.Capture.Tests` | Codec, loopback-server and PDF tests; run on any OS. |
| `tests/OpenLeanPrint.Compose.Tests` | Imposition-to-PDF composition tests; run on any OS. |
| `tests/OpenLeanPrint.Print.Tests` | Placement/paper-matching (any OS) + GDI+ tests (Windows). |
| `scripts/` | Windows printer register/unregister PowerShell scripts. |
| `docs/ARCHITECTURE.md` | How capture, rendering and forwarding fit together, and why. |
| `docs/M1-CAPTURE.md` | The capture prototype: how to run and test it. |
| `docs/M2-IMPOSE.md` | Imposing a captured PDF N-up / booklet from the CLI. |
| `docs/M3-PRINT.md` | Printing an imposed PDF to a real printer. |
| `docs/ROADMAP.md` | Milestones from here to a usable app. |

Planned (see roadmap): on-screen raster preview + `src/OpenLeanPrint.App` (WinUI 3 GUI).

## Building & testing

Requires the [.NET 8 SDK](https://dotnet.microsoft.com/download). The core and
its tests are cross-platform — you can build them on Linux, macOS or Windows:

```bash
dotnet test
```

## Using the engine

```csharp
using OpenLeanPrint.Core;
using OpenLeanPrint.Core.Imposition;

var pool = new PrintJobPool();
var doc = new PrintDocument("Report.pdf", "Acrobat");
for (int i = 0; i < 6; i++) doc.AddPage(PaperSizes.A4);
pool.Add(doc);

// 4-up on A4:
var settings = ImpositionSettings.NUp(2, 2) with
{
    SheetSize = PaperSizes.A4,
    Margins = PtMargins.UniformMm(8),
    GutterX = 6,
    GutterY = 6,
};

ImpositionResult result = new NUpImposer().Impose(pool.Flatten(), settings);

foreach (var sheet in result.Sheets)
    foreach (var p in sheet.Pages)
        Console.WriteLine($"page {p.Source.PageIndex} -> {p.DestRect} rot {p.Rotation}");
```

## Contributing

Contributions are welcome. Capture, imposition and printing are in place and
tested; the highest-value next steps are the on-screen WYSIWYG preview and the
WinUI 3 app shell (see the roadmap). Please keep `OpenLeanPrint.Core`
platform-neutral so it stays testable on any OS.

## License

[MIT](LICENSE) © Alexander Zarenko.

## Notes / caveats

- **There is no GUI yet** — everything runs from the CLI. The capture host, the
  imposition engine and printing all work; the WYSIWYG preview and app shell are
  the next milestones.
- Printing is **Windows-only** (it goes through the Windows spooler) and is
  guarded as such; capture and imposition run on any OS.
- Printing has been verified through the *Microsoft Print to PDF* driver; a job
  to a physical printer was accepted and completed by the spooler, but the
  printed sheet has not been inspected yet.
- PDF rendering uses **PDFium** (BSD-licensed, via PDFtoImage/MIT). Ghostscript
  and MuPDF are AGPL and are intentionally avoided so OpenLeanPrint can stay
  permissively licensed.
