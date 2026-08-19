# Contributing to OpenLeanPrint

Thanks for considering it. Bug reports, layout edge cases and printer
compatibility reports are as welcome as code.

## Getting set up

You need the [.NET 8 SDK](https://dotnet.microsoft.com/download). Nothing else —
no Visual Studio, no Windows SDK, no print driver.

```bash
git clone https://github.com/azitc-ac/Open-LeanPrint.git
cd OpenLeanPrint
dotnet test                              # the portable half, on any OS
dotnet build OpenLeanPrint.Windows.sln   # everything, Windows only
```

**Two solutions on purpose.** `OpenLeanPrint.sln` contains everything that
builds on Linux and macOS, and it is what CI builds and tests. The WPF app needs
the Windows Desktop SDK, so it lives only in `OpenLeanPrint.Windows.sln`. Please
do not add the app to the cross-platform solution.

## What the project cares about

- **`OpenLeanPrint.Core` stays platform-neutral.** The geometry is the part that
  is easy to get subtly wrong, so it must stay unit-testable on any OS. No
  Windows or native dependencies there.
- **Windows-only code stays on `net8.0`** and is marked
  `[SupportedOSPlatform("windows")]` rather than moving to a `net8.0-windows`
  target framework. The platform-compatibility analyser then enforces the guards
  for us and the CLI stays single-target.
- **Native dependencies must ship `win-arm64`.** Windows on ARM is a first-class
  target; that is the whole reason this tool is driverless. PDFium (via
  PDFtoImage) and SkiaSharp qualify, `Docnet.Core` does not.
- **Permissive licences only.** Dependencies are MIT, Apache-2.0 or BSD.
  Ghostscript, MuPDF and iText are AGPL and are deliberately avoided so the
  project can stay MIT.
- **The build is warning-clean.** `TreatWarningsAsErrors` is on.

## Tests

Every behaviour that can be tested without hardware should be. Tests that need
GDI+ or a spooler are marked `[WindowsFact]` and skip themselves elsewhere, so
`dotnet test` stays green on Linux.

Please do not add tests that actually spool a print job — printing is verified
by hand against *Microsoft Print to PDF*, which costs no paper. See
[docs/M3-PRINT.md](docs/M3-PRINT.md).

## Pull requests

- Keep the change focused, and say in the description what you verified and how.
- If you touched printing or capture, say which Windows version and
  architecture you tried it on. "I could not test this" is a fine thing to write
  and much better than a guess.
- Match the surrounding style: comments explain *why*, not *what*.
