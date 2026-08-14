# OpenLeanPrint roadmap

Incremental milestones from the current foundation to a usable app. Each
milestone is meant to be independently reviewable.

## ✅ M0 — Portable core (done)

- Domain model: `SourcePage`, `PrintDocument`, `PrintJobPool` (job pooling).
- Imposition engine: `NUpImposer` (N-up grid, margins, gutters, cell order,
  scale modes, auto-rotate) and `BookletImposer` (saddle-stitch ordering).
- Geometry types in points, top-left origin.
- xUnit test suite, green on any OS.

## ◑ M1 — Capture prototype (core done; Windows validation pending)

Goal: print from a real app and receive the PDF in OpenLeanPrint, with **no
third-party driver**. See [M1-CAPTURE.md](M1-CAPTURE.md) for details.

- ✅ `OpenLeanPrint.Capture`: loopback **IPP service** (`localhost:PORT`) —
  handles Get-Printer-Attributes, Validate-Job, Print-Job, Create-Job/Send-Document.
- ✅ IPP wire-format codec (`IppReader`/`IppWriter`), unit-tested.
- ✅ Accept an IPP `Print-Job`, store the incoming `application/pdf`.
- ✅ Extract page count and page sizes → build a `PrintDocument`.
- ✅ Runnable `OpenLeanPrint.Capture.Host` + loopback integration tests (green on CI).
- ⚠️ Register a local printer bound to the **in-box IPP class driver** →
  best-effort PowerShell script provided; **needs validation on Windows (ARM64/x64)**.
- ⚠️ Manual test: "Print → OpenLeanPrint" from Notepad/Edge — to be run on Windows.

Exit criteria: a captured PDF on disk and a populated `PrintDocument`
(automated on Linux; end-to-end on Windows still to confirm).

## ▶ M2 — Render & WYSIWYG preview

- `OpenLeanPrint.Render`: **PDFium** binding; render a source PDF page to a bitmap.
- Preview control: draw a `Sheet` — media, then each `PlacedPage` in its
  `DestRect` with `Rotation`.
- Live re-impose when settings change (N-up, margins, order, scale).

Exit criteria: captured job shown 2-up/4-up on screen, matching the engine.

## ▶ M3 — Forward to a physical printer

- Compose an **output PDF** from the imposed sheets (place each source page per
  `PlacedPage`).
- Send to a user-chosen physical printer via the Windows print API / IPP.
- Copies, printer/paper selection, duplex hint.

Exit criteria: 4-up output comes out of a real printer correctly.

## ▶ M4 — App shell & UX

- `OpenLeanPrint.App` (WinUI 3): pool list, reorder/remove jobs, combine, presets
  (2-up, 4-up, booklet), settings persistence.
- Tray/quick-launch; "keep collecting jobs" workflow.
- Installer (MSIX), signed. **ARM64 + x64** builds.

## ▶ M5 — Polish & parity

- Duplex-aware booklet output; per-page rotation overrides.
- Edge cases: mixed page sizes, huge jobs, high-DPI/multi-monitor preview.
- Optional extras toward FinePrint parity: watermarks/stationery, PDF export,
  page deletion, profiles.

## Cross-cutting

- CI: build + test `OpenLeanPrint.Core` on every push (OS-agnostic); Windows job for
  ARM64/x64 app builds once M1+ exist.
- Keep `OpenLeanPrint.Core` platform-neutral and well-tested.
