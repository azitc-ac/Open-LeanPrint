# OpenLeanPrint

A modern, lightweight, open-source alternative to **FinePrint** — pool print
jobs, preview them with a WYSIWYG dialog, arrange them **N-up** (2-on-1,
4-on-1, booklet …), and forward the result to a real printer.

Designed from day one to run on **Windows on ARM (ARM64)** as well as x64, by
avoiding the one thing that makes classic virtual-printer tools ARM-hostile:
a third-party kernel/print driver.

> Status: **capture works end-to-end.** The portable imposition engine and the
> driverless IPP **capture** layer are implemented and unit-tested on any OS —
> and the capture path is **verified on Windows 11**: `Add-Printer -IppURL`
> attaches the in-box Microsoft IPP Class Driver to the loopback service, and
> printing from a real app is captured as PDF (no third-party driver). The
> rendering + WYSIWYG preview and the app UI are next — see the roadmap.

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
| `tests/OpenLeanPrint.Core.Tests` | xUnit tests for the engine; run on any OS. |
| `tests/OpenLeanPrint.Capture.Tests` | Codec, loopback-server and PDF tests; run on any OS. |
| `scripts/` | Windows printer register/unregister PowerShell scripts. |
| `docs/ARCHITECTURE.md` | How capture, rendering and forwarding fit together, and why. |
| `docs/M1-CAPTURE.md` | The capture prototype: how to run and test it. |
| `docs/ROADMAP.md` | Milestones from here to a usable app. |

Planned (see roadmap): `src/OpenLeanPrint.Render` (PDFium),
`src/OpenLeanPrint.App` (WinUI 3 GUI).

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

Contributions are welcome. The imposition engine is the stable, well-tested
foundation; the highest-value next steps are the IPP capture prototype and the
PDFium-based renderer (see the roadmap). Please keep `OpenLeanPrint.Core`
platform-neutral so it stays testable on any OS.

## License

[MIT](LICENSE) © Alexander Zarenko.

## Notes / caveats

- **Windows-specific layers are not yet implemented** — this repo currently
  proves out the portable core. Nothing here talks to a printer yet.
- PDF rendering will use **PDFium** (BSD-licensed). Ghostscript and MuPDF are
  AGPL and are intentionally avoided so OpenLeanPrint can stay permissively licensed.
