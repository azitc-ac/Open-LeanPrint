# Security policy

## Reporting a vulnerability

Please report security issues privately by opening a
[GitHub security advisory](https://github.com/azitc-ac/Open-LeanPrint/security/advisories/new)
rather than a public issue. You should get a first reply within a few days.

## What OpenLeanPrint touches

Worth knowing when assessing risk:

- **It listens on localhost.** The capture host runs an IPP service bound to
  `http://localhost:<port>/leanprint` (6310 by default). It is intended for the
  local machine only; do not expose that port to a network.
- **It handles your documents.** Captured print jobs are written as PDFs — to
  `%ProgramData%\OpenLeanPrint\captured` when the service catches them, to
  `%LOCALAPPDATA%\OpenLeanPrint\captured` when the app or console host does. They
  are exactly the documents you printed. The app deletes each file as it takes it
  into the pool and the service clears up what nobody collected, but treat those
  folders like the documents themselves — a synced folder (OneDrive, Dropbox)
  would upload whatever is in them. The service's folder is machine-wide, so on a
  shared machine other users can read what is waiting there.
  [PRIVACY.md](PRIVACY.md) sets this out in full.
- **It parses untrusted PDFs.** Imposition and preview read PDFs through
  PdfSharpCore and PDFium. A malicious PDF is the most likely attack surface
  here; PDFium is sandboxed by neither of us.
- **It does not phone home.** No telemetry, no update check, no network access
  beyond the local IPP listener.
- **Signing.** Released binaries are signed; a self-signed certificate only
  establishes that two builds came from the same place, not who that is.
