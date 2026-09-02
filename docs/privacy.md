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
