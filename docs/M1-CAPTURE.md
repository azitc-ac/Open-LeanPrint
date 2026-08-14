# M1 — Capture prototype

M1 delivers the **capture layer**: a driverless way to receive a print job from
any Windows application as a PDF, and turn it into the OpenLeanPrint domain
model. It proves out the only real technical risk in the whole project.

## What it does

```
App prints ─► Microsoft in-box IPP class driver ─► OpenLeanPrint loopback IPP
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

## What is verified, and what is not

- ✅ **Verified on Linux/CI** by automated tests: the IPP codec round-trips, the
  loopback server accepts a real HTTP/IPP `Print-Job` (and `Create-Job` +
  `Send-Document`) and captures the PDF, and page sizes are parsed correctly.
- ⚠️ **Not yet verified on Windows**: registering the printer with the in-box
  IPP class driver and printing to it end-to-end. The IPP attribute set the
  server advertises is a sensible starting point but may need tuning for the
  Windows IPP client. This must be tried on real Windows (ARM64/x64) hardware.

## Try it on Windows

1. **Build & run the host** (leave it running):
   ```powershell
   dotnet run --project src/OpenLeanPrint.Capture.Host -- --port 6310
   ```
   It prints the printer URI `ipp://localhost:6310/leanprint` and waits.

2. **Register the printer** (new terminal):
   ```powershell
   .\scripts\Register-Printer.ps1 -Port 6310
   ```
   If that does not produce a working printer, use the **manual method** (this
   is the reliable path):
   - Settings → *Printers & scanners* → **Add device**
   - *The printer that I want isn't listed* → **Add manually**
   - *Select a shared printer by name* and enter:
     `http://localhost:6310/leanprint`
   - Finish the wizard — Windows attaches using its IPP class driver.

3. **Print** to the *OpenLeanPrint* printer from any app. The host logs the job
   (name, user, page count and sizes) and saves the PDF to `./captured/`.

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
