# Privacy

MauriPDF is privacy-first and local-first.

## Commitments

- User documents must never leave the computer.
- MauriPDF requires no user account.
- MauriPDF includes no telemetry or analytics.
- MauriPDF has no subscription requirement.
- Core document workflows have no cloud dependency.
- Document contents, filenames, paths, metadata, and usage behavior are not transmitted.

## Engineering requirements

- Do not add network access to document workflows.
- Do not add crash reporting or diagnostics that transmit data.
- Keep logs local and avoid recording document content or other sensitive data.
- Treat temporary files as sensitive and remove them when they are no longer needed.
- Review every dependency for network behavior, telemetry, licensing, and distribution implications before adoption.

If a future feature could transmit user data, it requires an explicit architectural and product decision and must not weaken the local-only guarantees of ordinary document use.

## Explicit printing

Viewing, editing and saving do not upload documents or contact a service. The user-requested Print
feature explicitly passes rendered page images and the document name to the selected Windows
printer/spooler. Windows drivers can retain spool data or send it to a network printer; that output
is controlled by the user's printer choice and Windows configuration, not a MauriPDF cloud service.
MauriPDF never prints automatically. Cancel cannot recall pages already accepted by the spooler.
