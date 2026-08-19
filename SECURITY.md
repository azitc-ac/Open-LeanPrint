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
- **It handles your documents.** Captured print jobs are written to
  `%LOCALAPPDATA%\OpenLeanPrint\captured` as PDFs and stay there until you
  delete them. They are exactly the documents you printed, so treat that folder
  like the documents themselves — and note that a synced folder (OneDrive,
  Dropbox) would upload them.
- **It parses untrusted PDFs.** Imposition and preview read PDFs through
  PdfSharpCore and PDFium. A malicious PDF is the most likely attack surface
  here; PDFium is sandboxed by neither of us.
- **It does not phone home.** No telemetry, no update check, no network access
  beyond the local IPP listener.
- **Signing.** Released binaries are signed; a self-signed certificate only
  establishes that two builds came from the same place, not who that is.
