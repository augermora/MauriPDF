# MauriPDF

MauriPDF is a fast, lightweight, free, and open-source PDF reader and editor for Windows.

The project is privacy-first and local-first: it requires no account, includes no telemetry or subscription, and does not depend on cloud services. User documents must never leave the computer.

## Status

MauriPDF is in early development. The `0.1-alpha.1` milestone is focused on opening a local PDF, determining its page count, rendering and navigating pages, zooming, fitting a page to the available width, and closing the document.

The continuous vertical viewer uses PDFium through PDFiumCore and supports local PDF opening, mixed-size pages, previous/next and direct page navigation, 25–500% manual zoom, Fit Page, Fit Width, and independent document/sidebar scrolling. Opening and rendering run on a serialized background worker. Only a bounded visible range is rendered, with latest-viewport-wins scheduling, a 64 MiB neutral LRU cache, and separate bounded UI Bitmaps. Pages keep their layout as placeholders while loading; zoom preserves the approximate reading position. Fit Width sizes each page to the viewport; Fit Page applies the current reference page's fit scale across the document. The collapsible thumbnail sidebar keeps its lazy rendering and separate 8 MiB cache.

The main display retains at most 32 visible Bitmaps and 64 MiB of pixel storage. Extremely large targets are rendered at reduced resolution to stay within this budget; exceptional views with more than 32 tiny visible pages leave the remainder as placeholders. There is no full-page prefetch, OCR, annotations, or editing.

Real PDF text can be selected by left-dragging forward or backward, including across consecutive pages, then copied with Ctrl+C or the Copy context menu. Extraction is lazy and local, using PDFium's text layer rather than pixels. Selection survives scrolling, zoom, and resize; a new left-click or document replacement clears it. Image-only/scanned pages still render but have no selectable text. Text ordering follows the PDF text layer, without column reconstruction or OCR. Selection is bounded to 16 consecutive pages / 131,072 retained characters, with a 32,768-character per-page extraction limit and a separate 8 MiB / 16-page text cache. Edge auto-scroll is deferred; the mouse wheel can scroll while dragging. Copy places plain Unicode text on the Windows clipboard, which is shared with other applications and subject to Windows clipboard settings.

Enter a page number and press Enter, or click a thumbnail. Keyboard shortcuts: Left/Right for previous/next page; Page Up/Page Down for viewport scrolling; Up/Down for line scrolling; Home/End for first/last page; Ctrl++ and Ctrl+- for zoom; Ctrl+0 for 100%; Ctrl+O to open; F4 to show/hide the navigation sidebar. The page-number editor retains normal text navigation, and the focused thumbnail list retains native vertical navigation keys.

The single navigation sidebar has **Thumbnails / Outline** tabs. Outline shows the PDF's existing bookmark hierarchy; click a label or press Enter on a selected bookmark to navigate to its local destination page. Tree arrows expand/collapse and move selection normally. Expanded state and selection are preserved while reading; there is no automatic chapter tracking. Switching tabs preserves the page, selected text, and search, and releases hidden thumbnail Bitmaps. PDFs without bookmarks show **No bookmarks**. Extraction is asynchronous and bounded to 2,048 nodes, 32 levels, and 512 UTF-16 code units per title; limited data is indicated, with placeholders for oversized/empty titles. Only local page destinations and local GoTo actions are supported, including named destinations resolved by PDFium. Other actions remain visible but do nothing; no URLs, files, or scripts are launched. Destination-specific coordinates/zoom and bookmark creation/editing are not implemented.

Ctrl+F opens a compact search bar. Search is progressive, ordinal case-insensitive, and uses the real PDF text layer. Whitespace, accents, and explicit line breaks are not normalized; matches do not cross pages. Enter/F3 advances and Shift+Enter/Shift+F3 goes back. Navigation clamps to known results while scanning and wraps when finished. Matches are yellow, the active match orange, and mouse selection remains independently blue. Escape/Close removes search highlights without clearing selected text. New queries use a 200 ms debounce and invalidate old results immediately. Search retains at most 10,000 logical matches and reports truncation explicitly. Unavailable text pages are counted in the status rather than silently treated as fully searched. Document replacement closes and clears search. No regex, fuzzy matching, replace, or OCR search is supported.

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
