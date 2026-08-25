# Privacy

OpenLeanPrint collects nothing and sends nothing. There is no telemetry, no
update check, no analytics, no account, and no server belonging to this project.
Nothing you print, open or configure reaches its author or anybody else.

That said, a tool that catches your print jobs necessarily handles your
documents. This is what it does with them, so you can decide for yourself
whether the arrangement suits you.

## Your documents

Printing into the OpenLeanPrint printer writes the job to disk as a PDF — it is
exactly the document you printed. The file is a hand-over to the app, not an
archive:

| | |
|---|---|
| Where it lands | `%ProgramData%\OpenLeanPrint\captured` when the Windows service catches it, `%LOCALAPPDATA%\OpenLeanPrint\captured` when the app or the console host does |
| How long it stays | until the app reads it into its job pool, which deletes it immediately |
| If no app is running | it waits, and the service removes it after 7 days or 500 MB, whichever comes first |
| When you uninstall | the folder is removed |

PDFs you open or drag in yourself are read where they are and never moved,
copied or deleted.

**On a machine with several users**, the service's folder is machine-wide, so
one person's captured jobs can be read — and removed — by the others while they
are waiting there. If that matters to you, run without the service: the app and
the console host keep everything per-user. If the folder is inside a synced
folder such as OneDrive, the files will be uploaded by that service under its own
terms, not ours.

## What is written down

Two plain-text logs, both local, neither sent anywhere:

- `%ProgramData%\OpenLeanPrint\service.log` — when the service started, and one
  line per captured job: time, the account name the job came from, its size, the
  file it was written to, and what the job asked for (sides, colour).
- `%APPDATA%\OpenLeanPrint\app.log` — when the app started, under which account
  and session, and any crash. It exists because "is it even running, and as
  whom?" is otherwise unanswerable after the fact.

Your settings and saved layouts live in `%APPDATA%\OpenLeanPrint\settings.json`.

Delete any of these at any time; the program recreates what it needs.

## The network

The only listener is on loopback: `http://localhost:6310/leanprint`, which is
how Windows' own IPP class driver hands print jobs over. It is meant for the
local machine and should not be exposed to a network. Nothing else in
OpenLeanPrint opens a connection.

## Questions

Open an issue at
[github.com/azitc-ac/Open-LeanPrint](https://github.com/azitc-ac/Open-LeanPrint/issues),
or for anything security-related see [SECURITY.md](SECURITY.md).
