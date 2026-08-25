# M1 — Capture prototype

M1 delivers the **capture layer**: a driverless way to receive a print job from
any Windows application as a PDF, and turn it into the Open-LeanPrint domain
model. It proves out the only real technical risk in the whole project.

## What it does

```
App prints ─► Microsoft in-box IPP class driver ─► Open-LeanPrint loopback IPP
                                                     service (this component)
                                                          │
                                                          ▼
                                            CapturedJob { PDF bytes, PrintDocument }
```

- Hosts a small **loopback IPP service** over HTTP on `localhost` — no
  third-party print driver, so it is compatible with Windows Protected Print
  and runs natively on ARM64.
- Answers the IPP operations Windows needs: `Get-Printer-Attributes`,
  `Validate-Job`, `Print-Job`, and `Create-Job` / `Send-Document`.
- Extracts page count and page sizes from the received PDF (via PdfPig) and
  raises `JobCaptured` with a `CapturedJob` (raw bytes + `PrintDocument`).

## Components

| Path | What |
|---|---|
| `src/OpenLeanPrint.Capture/Ipp` | IPP wire-format codec (`IppReader`/`IppWriter`), tags, message model. |
| `src/OpenLeanPrint.Capture/Server/IppPrinterServer.cs` | The loopback IPP service. |
| `src/OpenLeanPrint.Capture/Pdf/PdfPageExtractor.cs` | PDF → `PrintDocument`. |
| `src/OpenLeanPrint.Capture.Host` | Runnable console host (logs + saves captured jobs). |
| `scripts/Register-Printer.ps1` | Registers the Windows printer (best-effort). |
| `tests/OpenLeanPrint.Capture.Tests` | Codec, server (loopback) and PDF tests. |

## What is verified

- ✅ **Linux/CI** (automated tests): the IPP codec round-trips, the loopback
  server accepts a real HTTP/IPP `Print-Job` and `Create-Job` + `Send-Document`
  and captures the PDF, page sizes are parsed correctly, and the advertised
  attribute set contains the IPP Everywhere required attributes.
- ✅ **Windows 11** (manual, real hardware): `Add-Printer -IppURL
  http://localhost:6310/leanprint` attaches the **Microsoft IPP Class Driver**
  to the loopback service (no third-party driver). Printing from a real
  application is captured end-to-end via Create-Job/Send-Document as
  **application/pdf**, and the pages are parsed (a 4-page A4 document produced
  four 595×842 pt pages in the host log and a valid PDF in `captured/`).

Note: the full IPP Everywhere printer-attribute set is what makes the class
driver create the queue — a minimal set is queried successfully but the printer
is silently not created.

## Try it on Windows

1. **Build & run the host** (leave it running):
   ```powershell
   dotnet run --project src/OpenLeanPrint.Capture.Host -- --port 6310
   ```
   It prints the printer URI `ipp://localhost:6310/leanprint` and waits.

2. **Register the printer** — in an **elevated** PowerShell ("Run as
   administrator"), from the repo folder:
   ```powershell
   .\scripts\Register-Printer.ps1 -Port 6310
   ```
   This attaches the in-box **Microsoft IPP Class Driver** to the loopback URL.
   The equivalent one-liner (Windows 11 / Protected Print) is just:
   ```powershell
   Add-Printer -IppURL http://localhost:6310/leanprint
   ```
   > Do **not** use *"Select a shared printer by name"* in the GUI — that path
   > uses the legacy Internet Printing client, not the modern IPP class driver,
   > and will not connect to the loopback service.

   As soon as the printer is added, **watch the host window**: Windows sends a
   `Get-Printer-Attributes` request, which the host now logs (e.g.
   `POST /leanprint [...] IPP GetPrinterAttributes -> SuccessfulOk`). That line
   confirms Windows is talking to the service.

3. **Print** to the *Open-LeanPrint* printer from any app. The host logs the job
   (name, user, page count and sizes) and saves the PDF to
   `%LOCALAPPDATA%\Open-LeanPrint\captured` — captured jobs are your real
   documents, so they deliberately do not land in the working directory (which
   is often a source tree, and may be cloud-synced). `--out DIR` overrides it;
   the host prints the folder it uses on startup.

4. **Remove** the printer when done:
   ```powershell
   .\scripts\Unregister-Printer.ps1 -Port 6310
   ```

## Smoke-test on any OS

The host runs on Linux/macOS too. Start it, then POST an IPP `Print-Job` to
`http://localhost:6310/leanprint` (the automated tests in
`OpenLeanPrint.Capture.Tests` do exactly this).

## Next (M2)

Feed a `CapturedJob` into the imposition engine and render the resulting sheets
with **PDFium** for a WYSIWYG preview — see [ROADMAP.md](ROADMAP.md).
