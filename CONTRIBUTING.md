# Contributing to Open-LeanPrint

Thanks for considering it. Bug reports, layout edge cases and printer
compatibility reports are as welcome as code.

## Getting set up

You need the [.NET 8 SDK](https://dotnet.microsoft.com/download). Nothing else —
no Visual Studio, no Windows SDK, no print driver.

```bash
git clone https://github.com/azitc-ac/Open-LeanPrint.git
cd Open-LeanPrint
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

## The Windows pieces

Most of the project builds and tests anywhere. These parts do not, and they have
their own habits:

- **The desktop app is WPF, deliberately.** It ships with the .NET SDK, is
  ARM64-native and needs no runtime installed first; WinUI 3 would have required
  the Windows App SDK runtime before the app could start at all.
- **The app enables WinForms for one thing only**: the tray icon. Its implicit
  usings are removed in the `.csproj`, because otherwise `Application`,
  `MessageBox`, `Point` and `Size` become ambiguous with WPF's. Keep WinForms
  types behind `TrayPresence`.
- **WPF imports `System.Windows.Shapes`**, whose `Path` collides with
  `System.IO.Path`. Files that touch the file system need
  `using Path = System.IO.Path;`.
- **`ShutdownMode` is `OnExplicitShutdown`** so hiding to the tray cannot end the
  app. `MainWindow.OnClosed` is the single place that ends it.
- **Styles live in `Theme.xaml`**, a plain resource dictionary, so a window can be
  rendered or hosted without booting `App`. One trap worth knowing: an implicit
  `TextBlock` style with an explicit `Foreground` beats anything a button tries
  to inherit, so a control template that sets white text will still draw black.
- **Creating a printer queue needs administrator rights.** Measured, not assumed:
  `Add-Printer` fails with access denied as an ordinary user even with the IPP
  service answering. That is why the installer does it, and why the app's own
  setup button raises a prompt.

## Packaging

Four rules, each of which cost an afternoon to learn:

- **Never let the installer start the app directly.** A custom action inherits
  the installer's token, so launching the executable runs the app as whoever
  authorised the installation — on a machine with a separate administrator
  account, that means an elevated window with a file dialog in it. The launch
  goes through `explorer.exe` and the startup shortcut, which hands it to the
  session that already has one. Not starting is an acceptable failure; starting
  elevated is not.
- **The `ProductCode` follows the version; the `UpgradeCode` never changes.** Let
  WiX generate a product code per build and the uninstall GUID moves under your
  feet. Nail it to one value and upgrading dies with *another version of this
  product is already installed*, because Windows skips a package whose code is
  already installed. `Build-Installer.ps1` derives it from the version.
- **Every build needs a rising `FileVersion`.** Windows Installer skips a
  packaged file whose version is not higher than the installed one, so rebuilding
  without a new stamp leaves the old binaries in place and the change under test
  never runs. The stamp is days-then-minutes — use `TimeSpan.Days`, not `[int]`
  on `TotalDays`, which rounds and can make the newer build lose.
- **Setup steps run on every installation that is not a removal**, not only on a
  first install. They are written to be safe to repeat, and the narrow condition
  once left a machine with the program installed and no printer.

`MSIRESTARTMANAGERCONTROL=Disable` does *not* remove the "please close these
applications" question — it falls back to older, coarser detection that then
names unrelated programs. Stopping our own processes first is the actual fix.

MSIX needs no Windows SDK installed: `makeappx` and `signtool` come from the
`Microsoft.Windows.SDK.BuildTools` NuGet package, pinned in `packaging/SdkTools`.
The manifest's `Publisher` must match the signing certificate's subject exactly.

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
