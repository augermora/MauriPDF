# MauriPDF

MauriPDF is a fast, lightweight, free, and open-source PDF reader and editor for Windows.

The project is privacy-first and local-first: it requires no account, includes no telemetry or subscription, and does not depend on cloud services. User documents must never leave the computer.

## Status

MauriPDF is in early development. The `0.1-alpha.1` milestone is focused on opening a local PDF, determining its page count, rendering and navigating pages, zooming, fitting a page to the available width, and closing the document.

The viewer uses PDFium through PDFiumCore and supports local PDF opening, mixed-size pages, previous/next and direct page navigation, 25–500% manual zoom, Fit Page, Fit Width, and independent document/sidebar scrolling. Opening and rendering run on a serialized background worker. Only a bounded visible range is rendered, with latest-viewport-wins scheduling, a 64 MiB neutral LRU cache, and separate bounded UI Bitmaps. Pages keep their layout as placeholders while loading; zoom preserves the approximate reading position. The collapsible thumbnail sidebar keeps its lazy rendering and separate 8 MiB cache.

The toolbar selects **Continuous** (vertical page stack) or **Single Page** (only the current page). Mode switches preserve current page, manual zoom, search, selected text, and sidebar state. The ↶ / ↷ toolbar commands rotate the entire view in 90° steps, including thumbnails and text/search overlays. This is additional visual rotation on top of the PDF's intrinsic orientation: it never modifies the PDF, marks it dirty, or introduces saving. New documents start in Continuous mode, at 0° visual rotation and 100% zoom. Mode and view-rotation commands add no shortcuts; document Undo/Redo use Ctrl+Z/Ctrl+Y.

Fit Width uses each rotated page's width; Fit Page uses a common reference-page scale in Continuous mode and fits the current rotated page in Single Page mode. In Single Page, wheel/arrow scrolling stays within the page, and Page Up/Down scrolls an oversized page vertically or changes pages when its height fits the viewport. Previous/Next and Home/End retain logical document navigation. In Continuous, Page Up/Down always scrolls the viewport; small outer scroll margins let even a short first/last page stay current after navigation or mode changes.

The main display retains at most 32 visible Bitmaps and 64 MiB of pixel storage. Extremely large targets are rendered at reduced resolution to stay within this budget; exceptional views with more than 32 tiny visible pages leave the remainder as placeholders. There is no full-page prefetch, OCR, or annotation editing.

The **Pages** menu edits the current logical page: Delete, Move Earlier/Later, and structural Rotate Clockwise/Counter-clockwise. These changes immediately affect both display modes and thumbnails while the source PDF stays unchanged. Structural rotation is per-page edit state, separate from the view-only ↶ / ↷ buttons. Undo/Redo (Ctrl+Z/Ctrl+Y outside text editors) retains up to 100 operations. A title asterisk marks edits; undoing to the saved baseline clears it. Closing or replacing a dirty document offers **Discard / Cancel**, with Cancel the default.

**Save** (Ctrl+S) and **Save As** (Ctrl+Shift+S) write the logical order, deletions, and structural rotations without rasterizing pages. The first Save routes to Save As and never overwrites the open source; later Saves safely replace the most recently chosen output. A validated temporary sibling, backup-backed replacement, and SHA-256 destination identity protect the last valid copy and warn before overwriting an externally changed or missing output. The viewer stays backed by the original source, while each successful save becomes the clean baseline; Undo makes it dirty and Redo can return to clean. Bookmarks, named destinations, document metadata, and page links are not copied. Insertion/import, extraction/export, merge, and annotation editing are not implemented.

Moves keep the same source page current; deleting the current page chooses the following page, or the previous page at the end. The final remaining page cannot be deleted. Any deletion/reorder, including Undo/Redo, clears text selection and restarts an open search in edited order while retaining its query and current page. Structural rotation preserves selection and search. Bookmarks resolve their source page to its edited position; a deleted destination remains visible but does nothing. Reopening starts with the original order, zero structural/visual rotation, and empty history.

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
- `src/MauriPDF.Editing`: logical edit state/history and PDF Save/Save As materialization
- `src/MauriPDF.Pdfium`: shared PDFium lifetime and process-wide native-call serialization
- `src/MauriPDF.Infrastructure`: local filesystem, settings, and operating-system integration
- `tests`: test projects corresponding to the production projects
- `docs`: architecture and privacy documentation

## License

MauriPDF is licensed under the GNU General Public License version 3. See [LICENSE](LICENSE).

Dependencies and distributed artifacts must be compatible with GPLv3 and satisfy all applicable source-code and notice requirements.

See [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md) for rendering dependency notices.
