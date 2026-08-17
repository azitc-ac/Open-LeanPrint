# M4 — The desktop app

The app puts a face on the pipeline: drop PDFs into a job pool, see the imposed
sheet exactly as it will print, and send it to a printer. Everything it does
runs on the same tested libraries as the CLI — the window is a thin shell.

```
Job pool (several PDFs)  ─►  OpenLeanPrint.Core imposition
                                     │  imposed PDF (Compose)
                                     ├──►  PDFium raster ─► live preview
                                     └──►  OpenLeanPrint.Print ─► printer
```

## What it does

- **Job pool** — add any number of PDFs, reorder them (↑ ↓), remove or clear.
  Several jobs are **combined onto shared sheets** in pool order, which is the
  whole point of pooling: three 2-page memos become one 4-up sheet, not three.
- **Layout presets** — 1-up, 2-up, 4-up, 9-up and booklet, plus paper size,
  margin (mm) and gutter (pt).
- **Live WYSIWYG preview** — every change re-imposes in the background and
  repaints; page through the sheets with ◀ ▶.
- **Print** — pick any installed printer (the Windows default is preselected)
  and print at 200 dpi.
- **Save PDF…** — write the imposed PDF out instead of printing it.
- **Open with** — `OpenLeanPrint a.pdf b.pdf` starts with a filled pool.

## Why WPF, and why a second solution

WPF ships with the .NET SDK, runs natively on **ARM64** with no extra runtime to
install, and needs no MSIX to start — so the app runs the moment it is built.
(WinUI 3 was the original plan; it would have meant installing the Windows App
SDK runtime first. MSIX packaging can still be added later.)

WPF does need the Windows Desktop SDK, which does not exist on Linux, so the app
is **not** part of `OpenLeanPrint.sln` — that solution stays buildable and
testable on Linux/CI. Use `OpenLeanPrint.Windows.sln` on Windows, which contains
everything including the app.

```powershell
dotnet build OpenLeanPrint.Windows.sln     # everything, app included
dotnet run --project src/OpenLeanPrint.App
dotnet test OpenLeanPrint.sln              # the portable half, as before
```

The look lives in `Theme.xaml` as a plain resource dictionary rather than inside
`App.xaml`, so a window can be hosted or rendered without booting the whole
application.

## Verified

Driven through the real window on **Windows 11 ARM64**, with two pooled sample
PDFs (8 pages + 4 pages):

- The pool reports `2 jobs · 12 pages → 3 sheets · 2×2-up on A4` — the two jobs
  are combined, not imposed separately.
- The preview renders A4 at 120 dpi (991 × 1403 px) and matches what the CLI
  produces for the same input.
- Clicking ▶ walks sheet 1 → 2 → 3 and repaints each time: sheet 1 holds pages
  1–4 and sheet 2 pages 5–8 of the first job, sheet 3 the second job's four
  pages. Prev/Next disable themselves at the ends.
- Print and Save enable only once there is something to print, and the printer
  box preselects the Windows default.

Not verified by hand yet: printing from the app to a physical printer (it calls
the same `PdfPrinter.Print` that M3 verified), and the file dialogs.

## Not in this slice

- Settings persistence (the app starts at 4-up/A4/8 mm/6 pt every time).
- Live capture integration: the pool is filled from files, not yet from the
  capture host as jobs arrive. `openleanprint watch` covers that headlessly.
- Tray icon / "keep collecting jobs", MSIX installer, per-page overrides.
