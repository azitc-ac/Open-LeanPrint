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
- **Collect captured jobs** — with this on, every job the capture host writes
  drops into the pool as it arrives, so printing from any application lands in
  the app. Only jobs arriving *from now on* are taken; the folder may hold older
  jobs nobody wants reprinted.
- **Drag & drop** — drop PDFs anywhere on the window; anything that is not an
  existing `.pdf` is ignored.
- **Remove pages by right-clicking them** in the preview. The click is mapped
  back through the imposition layout to the exact source page, so what you point
  at is what goes.
- **Tray icon** — while collecting, closing the window only hides it, because
  the point of collecting is that jobs keep arriving. The tray menu shows the
  window again, toggles collecting, and quits for real; a balloon announces
  jobs that arrive while the window is hidden.
- **Remembers itself** — layout, paper, margin, gutter, printer and whether it
  was collecting are saved to `%APPDATA%\OpenLeanPrint\settings.json` on exit.
  Unreadable settings fall back to defaults rather than blocking startup.

## Why WPF, and why a second solution

WPF ships with the .NET SDK, runs natively on **ARM64** with no extra runtime to
install, and needs no MSIX to start — so the app runs the moment it is built.
(WinUI 3 was the original plan; it would have meant installing the Windows App
SDK runtime first. MSIX packaging works either way — see below.)

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

Settings persistence and job collecting were verified the same way:

- Clicking **2-up** turned the 8-page job from 2 sheets into 4, and after
  closing, `settings.json` held `Rows: 1, Columns: 2`. A fresh start came back
  up at 1×2-up with the 2-up preset lit and 4 sheets — restored, not defaulted.
- With **Collect captured jobs** on, dropping a PDF into the capture folder took
  the pool from `1 job · 8 pages → 4 sheets` to `2 jobs · 12 pages → 6 sheets`
  by itself, preview included.

Tray and drag & drop, same way:

- With collecting on, closing the window left it alive but hidden
  (`visible=False loaded=True`) instead of ending the app.
- The drop filter accepted 1 of 3 offered paths — the real PDF, not the `.txt`
  and not the missing file.

Right-click page removal, same way: on a 4-up sheet the four quadrants resolved
to source pages 1, 2, 3 and 4, and removing page 2 took the pool from 8 pages to
7 with the remaining pages flowing up into the gap.

Not verified by hand yet: printing from the app to a physical printer (it calls
the same `PdfPrinter.Print` that M3 verified), the file dialogs, and the tray
menu's own mouse interactions.

## Shipping it

```powershell
.\scripts\Publish-App.ps1                 # this machine's architecture
.\scripts\Publish-App.ps1 -Runtime win-x64
```

That produces **one self-contained executable** (`dist\<runtime>\OpenLeanPrint.exe`,
~187 MB) that runs on a machine with no .NET installed — copy it anywhere and
start it. Verified: the published win-arm64 build starts and opens its window
standalone. `-SelfContained:$false` makes it far smaller but requires the .NET 8
Desktop Runtime on the target.

Because PDFium and SkiaSharp are native, the runtime identifier must match the
target machine — there is no architecture-neutral build.

## MSIX package

MSIX is built here too, without anyone installing the Windows SDK:
`makeappx.exe` and `signtool.exe` come from the **Microsoft.Windows.SDK.BuildTools**
NuGet package, pinned by `packaging/SdkTools/SdkTools.csproj` and restored on
demand. That package ships arm64 binaries, so it also works on Windows on ARM,
and it needs no administrator rights.

```powershell
# once: a signing certificate (self-signed is fine for your own machines)
.\scripts\New-SigningCertificate.ps1

# then, per architecture:
.\scripts\Build-Msix.ps1 -CertificateSubject "CN=Alexander Zarenko" -Version 0.1.0.0
```

Without `-Password` the certificate's private key stays in
`Cert:\CurrentUser\My` and signing happens from there, so no key file and no
password exist anywhere to leak. `-Password` still exports a `.pfx` when a build
server needs one; `Build-Msix.ps1` then takes `-CertificatePath`.

The result is `dist\OpenLeanPrint-win-arm64.msix` — 72.7 MB for a
self-contained build. Installing it takes two steps, the first of which needs an
elevated PowerShell **once per machine**:

```powershell
Import-Certificate -FilePath certs\Alexander-Zarenko.cer -CertStoreLocation Cert:\LocalMachine\TrustedPeople
Add-AppxPackage <path>\OpenLeanPrint-win-arm64.msix
```

`-Output` puts the package wherever you want it.

Verified: the package builds, signs, and carries the expected identity
(`AlexanderZarenko.OpenLeanPrint`, 1.0.0.0, arm64, 494 files). Its signature
reports `UntrustedRoot` until that certificate is imported — which is the
correct behaviour for a self-signed certificate, not a packaging fault. The
package was not installed during verification, since trusting a certificate
machine-wide is the user's call.

Two things to know about the manifest: `Publisher` must match the signing
certificate's subject *exactly*, and the architecture is baked in, so one
package per runtime.

## Signing for other people

Sideloading on machines you control works with the self-signed certificate
above. Handing the app to strangers does not: they would have to trust your
certificate manually, and SmartScreen will warn about an unknown publisher.
That needs a certificate from a public CA, and since June 2023 the private key
of any publicly trusted code-signing certificate has to live on certified
hardware (a USB token) or in a cloud HSM, which is what sets the price floor.

For an open-source project the cheapest routes are certificate *sponsorship*
programmes rather than buying one outright — check current terms, they change:

- **SignPath Foundation** — free code signing for open-source projects,
  certificate and signing service included.
- **Certum Open Source Code Signing** — a low-cost certificate for open-source
  authors; budget for the hardware token on first purchase.
- **Azure Trusted Signing** — cheap per month, but the identity checks target
  organisations with a verifiable history.

## Not in this slice

- A publicly trusted signature (see above) — the package is sideload-ready, not
  stranger-ready.
- Per-page rotation overrides and per-job layout settings.
- Auto-start with Windows.
