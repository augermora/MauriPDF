# MauriPDF

MauriPDF is a fast, lightweight, free, and open-source PDF reader and editor for Windows.

The project is privacy-first and local-first: it requires no account, includes no telemetry or subscription, and does not depend on cloud services. User documents must never leave the computer.

## Status

MauriPDF is in early development. The `0.1-alpha.1` milestone is focused on opening a local PDF, determining its page count, rendering and navigating pages, zooming, fitting a page to the available width, and closing the document.

An initial one-page rendering spike uses PDFium through PDFiumCore. Navigation, zoom, caching, and editing are not implemented yet.

## Requirements

- Windows x64
- .NET 10 SDK
- Visual Studio with .NET desktop development support, or the .NET CLI

## Build and test

```powershell
dotnet restore MauriPDF.slnx
dotnet build MauriPDF.slnx --configuration Release --no-restore
dotnet test MauriPDF.slnx --configuration Release --no-build
```

## Repository layout

- `src/MauriPDF.App`: WinForms executable and composition root
- `src/MauriPDF.Core`: UI- and PDF-library-independent application core
- `src/MauriPDF.Rendering`: PDF rendering implementation boundary
- `src/MauriPDF.Editing`: future PDF editing implementation boundary
- `src/MauriPDF.Infrastructure`: local filesystem, settings, and operating-system integration
- `tests`: test projects corresponding to the production projects
- `docs`: architecture and privacy documentation

## License

MauriPDF is licensed under the GNU General Public License version 3. See [LICENSE](LICENSE).

Dependencies and distributed artifacts must be compatible with GPLv3 and satisfy all applicable source-code and notice requirements.

See [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md) for rendering dependency notices.
