# Architecture

## Goals

MauriPDF is designed as a fast, lightweight Windows PDF application with strict local-only document handling. The architecture protects the UI and application logic from concrete PDF libraries while avoiding abstractions that are not supported by an implemented use case.

## Projects

### MauriPDF.Core

The independent application core. It has no project references and must not reference WinForms or PDF-specific third-party libraries. Application rules and concrete-use-case contracts belong here when they are needed.

### MauriPDF.Rendering

The PDF rendering implementation boundary. It references Core and is the only project that references PDFiumCore. PDFiumCore and native PDFium handles are implementation details and must not appear in Core or App APIs.

The selected rendering engine is PDFium, currently consumed through PDFiumCore 154.0.8035. A rendering session owns its PDFium document handle. Page and PDFium bitmap handles exist only for the duration of a render call and are closed before the call returns.

Rendered pixels cross the boundary as a Core `RenderedPage`: an owned, UI-neutral BGRA32 memory buffer with dimensions and stride. The caller disposes this buffer. MauriPDF.App copies it into a WinForms `Bitmap`; neither `System.Drawing.Bitmap` nor PDFium types are exposed by Core.

### MauriPDF.Editing

The future PDF editing implementation boundary. It references Core. Editing is outside the `0.1-alpha.1` milestone, so no editing model or API is defined yet.

### MauriPDF.Infrastructure

Local filesystem, settings, diagnostics, and Windows integration. It references Core and must not become a general-purpose dumping ground.

### MauriPDF.App

The WinForms executable and composition root. It references all production projects. Forms should remain focused on presentation and event forwarding rather than application or PDF business logic.

## Dependency graph

```text
MauriPDF.Core
  ^        ^             ^
  |        |             |
Rendering  Editing  Infrastructure
  ^        ^             ^
  |        |             |
  +--------+------App----+
```

Dependencies point toward Core. Core has no dependency on outer projects.

## Resource and performance principles

- Native handles, streams, rendered buffers, and caches must have explicit ownership and lifetime rules.
- Rendering is serialized on a dedicated background worker. Foreground pages and lazy visible thumbnails have independent bounded neutral-pixel caches. There is no full-page prefetching.
- Concrete PDF packages must be evaluated for performance, deployment, file-locking behavior, robustness, and GPLv3 compatibility before adoption.

## Continuous document viewer

Core's immutable `ViewerState` holds page count, current zero-based page index, last manual zoom percentage, and the active zoom mode. The App's `ContinuousPdfView` replaces the single-page PictureBox. It is one double-buffered, owner-painted control with native horizontal/vertical scrollbars, not a control per page. The sidebar has its own independent scrolling.

### Geometry and scrolling

`OpenDocumentAsync` reads all page dimensions sequentially on the PDFium worker before publishing the document. This is O(page count) metadata work, with cancellation checked between pages; it does not render or allocate page pixels. Core's `ContinuousPageLayout` stores one small `PageGeometry` value per page: index, width, height, top and derived bottom. Horizontal bounds are derived from the document/viewport width. Layout is O(page count), independent of rendered pixels. Pages have a 16-pixel margin/gap and are centered on the document canvas; mixed orientations and sizes retain their aspect ratios to integer-pixel precision.

Document vertical coordinates are doubles, so the total height can exceed Int32 without overflowing a WinForms scroll extent. Native scrollbar thumb positions map to one million logical steps. Wheel/line/page scrolling uses pixel offsets independently of that quantization. Both scrollbar gutters are reserved consistently; the horizontal scrollbar is disabled when unnecessary. This prevents fit/scrollbar feedback loops. Extremely long documents have coarser thumb-drag positioning, while wheel scrolling remains precise. Individual layout dimensions still must fit a positive Int32; oversized/invalid geometry reports an error rather than allocating pixels.

Visible-page intersection uses binary searches, O(log page count), followed by iteration over intersecting pages. Placeholders are white rectangles and borders at full layout bounds; they require no Bitmap. Rendering never happens in Paint. Scrolling updates geometry/current-page selection immediately; only changes to the demanded range restart the 60 ms rendering debounce. Resize/sidebar changes use 150 ms layout debounce. Completed pages invalidate their own bounds.

The current page is the page containing the viewport's vertical center; a center in a gap selects the nearest page, with exact ties choosing the earlier one. It updates the toolbar and thumbnail selection without causing a navigation feedback loop. Previous/Next and Left/Right navigate relative to that current page. Home/End select the first/last page, and direct entry/thumbnail clicks use the same scroll target: short pages centered, tall pages aligned at the top, clamped at document edges. Page Up/Down now scroll 90% of the viewport rather than changing pages. Up/Down scroll 48 pixels. The page-number editor retains native text navigation, and focused thumbnails retain native Up/Down/Page Up/Page Down behavior.

### Zoom and fit

- Manual zoom is 25–500%, in 25-percentage-point steps. 100% means 96 pixels per inch (96/72 pixels per PDF point). Ctrl++/Ctrl+- resume from the last manual percentage; Ctrl+0 resets to 100%.
- Fit Width sizes each page independently to the usable viewport width minus two 16-pixel margins. Mixed-size pages therefore share a target width but can have different scales/heights.
- Fit Page chooses the smaller width/height scale for the current reference page, subtracting the margins, then applies that one scale to every page. Other differently sized pages may require scrolling. Scrolling/navigation alone does not recalculate the scale. Pressing Fit Page again or resizing refits using the then-current page.
- Zoom/resize captures the page and fractional position at the previous viewport center and restores that center in the new layout, clamped to document edges. It never resets unconditionally to page 1.
- Fit modes are not constrained by manual zoom bounds. Layout sizes round to the nearest pixel, with a one-pixel minimum.

### Displayed Bitmap ownership and bounds

`ContinuousPdfView._images` exclusively owns main-view WinForms Bitmaps. They are created on the UI thread by copying a caller-owned Core BGRA32 result through `WinFormsImageConverter`; the neutral result is disposed immediately afterward. No ImageList owns these Bitmaps, and Core/Rendering never reference System.Drawing.

`VisiblePageDemand` limits the working set to **32 currently visible pages** and **64 MiB of Bitmap pixel storage**, separate from the neutral cache. In the exceptional case of more than 32 visible tiny pages, the contiguous 32-page range around the center receives pixels; the other pages keep their correct placeholders. There is **no overscan or full-page prefetch** in this milestone. Each demanded page receives at most 1/count of the Bitmap budget. Normally its raster exactly matches layout dimensions; oversized targets are uniformly reduced to fit that share and a 16,384-pixel maximum raster dimension, then painted into their full layout bounds. Extremely large/high-zoom pages can consequently appear softer. Targets remain aspect-preserving within integer-pixel rounding. No tiling or additional pixel cache is introduced.

Off-range images and images whose target dimensions changed are disposed before new requests. Re-entering pages get fresh UI Bitmaps from cached neutral pixels when the exact target is available. Unchanged target-size Bitmaps survive layout/resize recalculation. Document replacement and control disposal release every displayed Bitmap. Late results are disposed without conversion/publication after generation changes; duplicate UI insertions dispose the unused Bitmap. Placeholder painting uses shared brushes/pens, not owned image resources.

The 64 MiB UI budget covers pixel storage, not process memory: GDI object overhead, the double-buffered viewport, neutral cache, outstanding result copies, active native rendering, and pool retention are additional. Pending results are bounded by the same at-most-32-target batch; there is no document-wide Bitmap or task list.

## Background rendering and cache

`PdfViewerRenderer` exclusively owns its engine/session on one dedicated worker. Opening, dimension reads, rendering, cache access, and native disposal never overlap or run on the UI thread. The existing single-page rendering API is retained for compatibility/tests; the continuous UI submits explicit page/target-size batches.

There is one active native operation, at most one replaceable pending open/legacy foreground request, a replaceable foreground batch of at most 32 pages, and one replaceable pending thumbnail slot. Each viewport intent cancels/discards the old pending batch and invalidates active old foreground work. Requested pages nearest the viewport center are ordered first; **all pending visible main pages precede thumbnails**. No overscan tier is currently used. An already-active native call is allowed to finish, but obsolete results are discarded rather than cached/published. New batches replace demand rather than forming a FIFO history. Empty-viewport resize cancellation does not cancel a document still opening.

The App independently checks its view generation after await, preventing already-posted stale results from appearing after scrolling, zoom, replacement, or disposal. Expected cancellation is silent. Placeholders replace intrusive per-page rendering messages. The toolbar reports opening/closing and text-selection/clipboard problems, not per-page render activity. A failed visible page remains a placeholder and is retried after leaving/re-entering demand; only the first failure in the current demand failure set displays a simple error.

Document replacement clears the App's layout/Bitmaps and invalidates main and thumbnail UI work immediately. The worker's open request cancels old native-result publication, clears both caches, and closes the old session after any active call finishes. It reads the new geometry before enabling its view. Old-document pixels never remain on screen while the new document opens. Shutdown cancels requests and awaits worker drainage with the WinForms message loop alive so posted result owners can dispose safely.

`RenderCacheKey` distinguishes document identity, page index, target width and target height. The existing **64 MiB LRU main neutral cache** and separate **8 MiB thumbnail cache** remain unchanged. They account for backing allocation capacity, not only visible pixels, and dispose on eviction, replacement, clear, or shutdown. A cache hit returns an independent caller-owned copy. A cacheable fresh render transfers the original neutral buffer to the cache and returns a copy. The App disposes copies after conversion; cache eviction cannot invalidate a displayed Bitmap. Target-size changes caused by the working-set budget are deliberately different cache keys.

Native page/bitmap handles remain scoped to a single render. The native bitmap is destroyed before unpinning its borrowed pixel buffer; the document handle belongs to the worker session. PDFiumCore remains solely within Rendering. No packages, cloud services, telemetry, or licensing changes were introduced.

A 1,001-page document therefore has O(page count) dimension/layout storage, but only O(visible pages) render demand and bounded Bitmap/cache storage. Deterministic tests cover geometry for 1/100/1,001 pages, huge extents, mixed sizes, visible ranges/gaps, optional layout overscan, fit/anchor rules, rapid replacement of 1,000 foreground intents, cache reuse, and batch priority. A local WinForms smoke harness checks real control handles, painting and disposal using a render stand-in; it is not a substitute for interactive validation with complex real PDFs. Metadata scanning can delay initial display on very large/malformed PDFs, and UI-thread pixel-to-Bitmap conversion can still briefly pause for large images.

## Thumbnail sidebar

The App uses a native WinForms `ListView` in virtual Details mode with owner-drawn fixed-height rows. `VirtualListSize` represents the page count; item text is supplied on demand. There are no per-page controls or eagerly allocated images. An empty `ImageList` with a 1-by-244 size establishes native row height: owner drawing alone does not change Details-view row metrics. It must remain empty; there is no spacer Bitmap and no thumbnail is added to it. This avoids retaining an already-disposed image until deferred native handle creation. The control owns this sizing resource, detaches it from `SmallImageList`, then disposes it during shutdown. Visible thumbnail Bitmaps belong only to the bounded `_images` collection and are drawn directly; placeholders are painted rectangles/text, not Image objects. The left `SplitContainer` panel defaults to 190 pixels, is resizable, and can be collapsed via the Sidebar toolbar button or F4. Thumbnails and Outline occupy tabs in this same panel. Thumbnails are active only when their tab and sidebar are visible; switching away cancels demand and disposes displayed thumbnail Bitmaps without changing cache budgets. The existing viewport resize handler retains its 150 ms fit-mode debounce; toggling the sidebar never reopens the session.

`ThumbnailSizeCalculator` uses a fixed **144-pixel target width**, preserving aspect ratio. Very tall pages are uniformly reduced to a **512-pixel maximum height** for predictable lightweight allocations. The UI fits that one raster into a fixed row without requesting another resolution. Portrait and landscape pages have the same target-width policy. Extremely tall/thin pages may be hard to read at thumbnail scale.

Visible demand comes from `TopItem.Index` and client height divided by fixed row height, with two extra rows to cover partial visibility. Scrolling/wheel, painting, resize, and selection-driven `EnsureVisible` update the range. A 100 ms debounce coalesces changes. The UI walks this small range sequentially, skipping existing images and failures; at most **32** visible-range bitmaps are retained even for exceptionally tall windows. Off-range bitmap copies are disposed. Normal desktop windows require only a handful. No document-wide thumbnail request list is created; the shared document geometry scan does not render thumbnails.

Thumbnail clicks use the existing main-page navigation path. Toolbar, keyboard, and direct entry update selection and ensure the requested page is visible. Since navigation scrolls the selected thumbnail into view, it participates in the visible-first range without a separate distant-page request. Hidden sidebars cancel their demand and release bitmap copies. Simple numbered placeholders remain until successful rendering. Individual failures only produce DEBUG diagnostics and a placeholder; they are retried after leaving/reentering the range or opening a document, never through error dialogs.

The worker has a **separate 8 MiB LRU thumbnail cache**, configurable through `thumbnailBudgetBytes`. It reuses `RenderCache` byte accounting/ownership and keys `(document identity, page index, target width, target height)`. Full-page cache activity cannot evict thumbnail entries, or vice versa. Original neutral buffers belong exclusively to the cache; independent result copies belong to the awaiting UI and are disposed immediately after bitmap conversion. Only the current small visible range also has WinForms bitmap representations; this bounded duplication allows neutral cache reuse after scrolling back. Eviction, replacement, document changes, and shutdown dispose cached buffers. Pool retention and displayed bitmap copies remain outside the cache budget.

`TryRequestThumbnail` deduplicates active/pending requests by document/page because the single thumbnail sizing policy deterministically fixes dimensions. Duplicates return false without sharing a disposable result task between callers. A different request can replace the single pending thumbnail slot. Scrolling/hiding cancels the old thumbnail demand version, but does not cancel main-page requests. New documents invalidate thumbnail and foreground work immediately using document/request generations; the worker then clears both caches and closes the old session safely. The UI independently checks its sidebar generation after await, so already-posted results from a previous document cannot appear in the new sidebar. Shutdown drains the same serialized worker.

DEBUG output reports thumbnail cache hits, fresh renders, skipped duplicates, and failures. There is no telemetry or persistent logging. As with the main viewer, an active PDFium call is not forcibly interrupted; complex thumbnail pages can briefly delay newly submitted foreground work. Foreground work is always next, ahead of pending thumbnails.

## PDF text extraction and selection

### Native pipeline and ownership

The existing session boundary now exposes `ExtractText(pageIndex)`, returning an immutable Core `PdfTextPage`. PDFiumCore **154.0.8035** remains unchanged and is still referenced only by Rendering. Native entry points used are `FPDF_LoadPage`, `FPDFText_LoadPage`, `FPDFText_CountChars`, `FPDFText_GetUnicode`, `FPDFText_GetCharBox`, `FPDF_PageToDevice`, `FPDFText_ClosePage`, and `FPDF_ClosePage`. In this package the text bindings are named `fpdf_text.FPDFTextLoadPage`, `FPDFTextCountChars`, `FPDFTextGetUnicode`, `FPDFTextGetCharBox`, and `FPDFTextClosePage`. See the upstream [text API declarations](https://pdfium.googlesource.com/pdfium/+/refs/heads/main/public/fpdf_text.h) and [coordinate API declarations](https://pdfium.googlesource.com/pdfium/+/refs/heads/main/public/fpdfview.h).

Extraction opens a temporary native PDF page and then its text page, copies the required data, and closes the text page before the PDF page in nested `finally` blocks. The session/document remains worker-owned. No native text handles enter a cache or UI; cancellation, text-limit failure, replacement, and shutdown cannot leave them attached to UI state. An active extraction finishes safely before the worker disposes/replaces its session. Text errors are isolated from raster requests. A scanned/image-only page returns zero characters and continues rendering normally; no OCR or pixel-based text inference is used.

### Neutral data and coordinates

Each immutable `TextCharacter` has a Unicode value and a `TextBounds`. Array positions preserve PDFium's character ordering, including generated spaces and CR/LF entries. Windows PDFium can expose supplementary Unicode characters as surrogate pairs: `PdfTextPage` combines a valid pair into a scalar at the first index and leaves an empty continuation entry, retaining original indexing without splitting the Unicode scalar during copy. Unmapped values are omitted and malformed scalars become the Unicode replacement character. The array is copied on construction and exposed only through a read-only indexer.

PDFium supplies raw PDF user-space bounds. Rendering samples `FPDF_PageToDevice` at three basis points on a virtual million-unit device, derives its affine transform, and maps character-box corners into **normalized visible-page coordinates**: top-left origin, independent of display/raster size. This includes CropBox translation and intrinsic page rotation using the same rotation argument as raster rendering. No device Bitmap is allocated. Cropped-out glyphs are omitted; partially visible bounds are clipped to the page. Bounds are axis-aligned, so rotated/skewed individual glyphs may have approximate rectangular highlights.

The App translates pointer coordinates using continuous scroll offsets and page placement, then divides by the current layout dimensions to obtain normalized page coordinates. Hit testing measures distance to glyph bounds in display pixels (six-pixel initial tolerance), never in cached Bitmap pixels. Drag endpoints use the nearest glyph/insertion offset; an inferred local advance direction supports common horizontal, RTL, and rotated runs, but is not a full bidirectional layout algorithm. Painting applies the inverse mapping through `TextCoordinateTransform`, so zoom, Fit modes, mixed page sizes, and reduced-resolution rasters do not change text placement.

### Lazy work, cache, and memory limits

Opening a document does **not** extract text, including for 1,000+ pages. A left-button interaction requests its page; an ongoing drag requests missing consecutive pages between the anchor and latest endpoint sequentially. Selection does not pre-extract visible pages or scan unrelated pages. Mouse moves update one desired endpoint, not a queue. The PDFium worker has one replaceable pending interaction-text slot with its own version/document generation. Priority is visible main raster batches, interaction text, search highlight geometry, outline metadata, background search scan, then thumbnails; there is still no overscan tier. Raster/thumbnail changes do not cancel unrelated text work. An already-running native operation can delay newly submitted work until it finishes; PDFium access remains strictly serialized.

`TextPageCache` is a worker-owned LRU keyed by `(document identity, page index)`, capped at **8 MiB of estimated text storage and 16 pages**. Accounting is 40 bytes per character plus 128 bytes per page, with a hard page-count bound for metadata overhead. These are conservative managed-data limits, not a total process-memory guarantee. The cache holds immutable managed objects only; hits share them safely without copies or disposal ownership. Eviction removes references, not resources still borrowed by selection. Both document replacement and shutdown clear it separately from the unchanged raster caches.

Extraction rejects pages above **32,768 native character entries** before allocating managed geometry; those pages remain renderable but text selection reports a status message. Native PDFium parsing happens before this count is known and its internal memory/time are not controlled by the managed budget. The UI retains at most **16 consecutive pages and 131,072 character entries** for the active selection (about 5 MiB of character storage plus bounded metadata). Shrinking/changing the range drops unused references. Extending beyond 16 pages leaves the last valid endpoint and reports the limit; exceeding the retained-character limit clears selection and reports it. No oversized text page or selection is silently truncated for Copy. Managed results in flight and temporary extraction arrays are additionally bounded by one page/interaction, not document length.

### Logical range, overlay, and clipboard

`TextSelection` stores anchor/active `(page index, insertion offset)` positions, normalizes forward/backward endpoints, and derives half-open ranges per page. It spans consecutive pages logically, not one screen rectangle. Mouse capture tracks dragging; release preserves the selection even if lazy extraction completes afterward. The UI checks an independent interaction generation after every await. A new left-click clears the old range (including empty-space clicks); document replacement and disposal clear all UI text references, invalidate outstanding text work, and prevent old-document publication. Selection otherwise survives navigation, scrolling, resizing, and zoom because its indices and normalized geometry are stable.

Highlights use one UI-owned translucent `SolidBrush` in the normal page paint path. The brush/context menu are disposed with the view. No highlight Bitmaps are allocated and no raster invalidation/request is triggered merely by changing selection. Core and Rendering remain independent of WinForms and System.Drawing.

Ctrl+C and the Copy context item reconstruct plain Unicode text in PDFium's order, preserving explicit newlines and adding two CR/LF pairs at page boundaries. Missing intermediate data disables copying rather than copying a partial range; no selection or empty text leaves the clipboard untouched. The page-number textbox retains its normal Ctrl+C behavior. Windows clipboard failures become a status message, not an application crash. Copy is explicitly user-directed; MauriPDF does not upload text, but the system clipboard is shared and its history/synchronization policies are controlled by Windows.

### Limits and verification

Edge-triggered auto-scroll is deferred. Wheel scrolling while holding a drag can extend selection; there is no inertial scrolling, word selection, discontinuous selection, OCR, column reconstruction, or text reflow. PDFium's underlying ordering/font mappings can be imperfect, especially in complex multi-column, bidirectional, vertical, or malformed documents. Glyph-level axis-aligned highlights and caret inference are deliberately simple.

Deterministic tests cover immutable geometry, hit testing, forward/backward/multipage ranges, transforms at several zoom sizes, Unicode/surrogate normalization, exact copy reconstruction, empty pages, cache bounds/invalidation, priority, rapid text-demand replacement, stale-document rejection, and shutdown. Native PDFium fixtures verify Unicode including supplementary characters, CR/LF, rendered-glyph alignment for crop plus 0/90/180/270-degree rotation, image-only pages, and successful raster rendering after text-limit failure. The local ignored WinForms smoke harness additionally exercises actual mouse event handlers, release-before-completion, overlays, zoom persistence, empty clicks, and stale resets without modifying the system clipboard. Interactive testing with representative real-world PDFs is still recommended.

## Progressive document search

### UI and query lifetime

`DocumentSearchBar` is a compact non-modal WinForms ToolStrip below the main toolbar: query, Previous, Next, status, Close. Ctrl+F opens/focuses and selects the existing query. Enter/F3 advances; Shift+Enter/Shift+F3 goes back. Escape closes an open search. Query-field text-editing keys, including Ctrl+C, remain native textbox operations. Closing search clears its state/highlights but retains the query for reopening; document replacement closes the bar and empties it. Mouse selection is never cleared by search actions.

Every query edit immediately cancels its predecessor and removes stale matches/highlights. A **200 ms debounce** starts the new scan. The worker's search generation and document generation, plus the App's intent counter, reject both native results and already-posted UI continuations from obsolete queries/documents. Closing, replacing the document, and shutdown invalidate pending scanning/highlight work. Selection, raster, and thumbnail cancellation remain independent. Active PDFium calls finish before native handles are released; no concurrent session access is introduced.

### Matching and logical index mapping

Core's `SearchablePageText` builds one temporary UTF-16 string from the existing immutable `PdfTextPage`, along with an explicit logical character index for **each UTF-16 code unit**. Supplementary scalars have two string units mapped to one logical entry; the existing empty surrogate-continuation slots remain skipped. Unmapped zero entries are omitted, consistently with clipboard reconstruction, and malformed scalars use the replacement character. Search rejects matches that split a surrogate pair. Returned `SearchMatch` values contain only `(page index, logical start, logical exclusive end)`.

Matching uses `StringComparison.OrdinalIgnoreCase`, independent of the current culture. There is no canonical Unicode normalization, diacritic removal, expanding case fold, trimming, whitespace folding, paragraph reconstruction, or cross-page matching. CR, LF, spaces and tabs remain distinct literal characters; a space does not match a line break. The matching layer accepts literal newline queries, but the compact UI is a normal single-line textbox whose Enter key navigates. Matches are non-overlapping, found left-to-right. Queries are limited to 1,024 UTF-16 code units. No regex, wildcards, fuzzy matching, whole-word mode, OCR, or replacement is implemented.

### Progressive execution and result state

`SearchPageAsync` reuses the existing session `ExtractText` and bounded shared text cache; there is no second PDFium extraction implementation or new PDF API/package. Extraction and string matching run on the dedicated worker. The App awaits **one page** before submitting the next, from page zero upward, yielding to the message loop. The worker holds one replaceable background scan slot and one replaceable search-geometry slot, not one queued task per document page. The strict pending-work order is visible main raster, active mouse interaction text, active/visible search geometry, outline metadata, background scan, thumbnails. A currently executing operation is not forcibly interrupted. Continuous foreground activity can intentionally delay scan/thumbnail progress.

Core's `DocumentSearchState` stores pages scanned, total pages, failure count, match count, active index, cancellation and completion/truncation state. Results append in page/character order, so later discovery never changes the active logical match. The first discovered result activates once; subsequent discoveries do not scroll the document. Previously found results are immediately navigable. While scanning, Previous/Next clamp at the known boundaries; there is no queued navigation intent or premature wrapping. Once scanning ends (including explicit truncation), navigation wraps over the retained results. Progress publishing/highlight refresh is coalesced at **100 ms**, with immediate first-result and completion updates.

Results are capped at **10,000** logical records (12 bytes of value payload per match, plus bounded collection capacity). Finding an additional match stops scanning and labels the status as truncated/result-limit reached; an exact 10,000-match document is not falsely marked truncated. Only a deterministic document-order prefix is retained. Per-page scan results and UTF-16/index maps are temporary. Search never retains full geometry for every matching page.

### Highlight and navigation geometry

The existing **8 MiB estimated / 16-page** neutral text cache is shared by selection, scanning, and highlighting. The view separately retains immutable references for at most **16 visible/active search pages and 131,072 character entries** (roughly 5 MiB of character payload). These references do not acquire native handles or disposal ownership. Off-demand references are dropped; query/document changes and disposal clear them. Search does not alter the 64 MiB main raster cache, 8 MiB thumbnail cache, or bounded displayed Bitmaps.

`ContinuousPdfView.Search` loads active-result geometry first, followed by visible matching pages, sequentially in its own bounded demand generation. No offscreen result geometry is requested except for an explicit pending activation. Search result lookup for a visible page uses binary bounds into the ordered match list. Painting derives rectangles from logical ranges using the same normalized-page transform as selection, independent of raster resolution. Overlay order is yellow ordinary matches, orange active match (ranges do not overlap), then blue mouse selection on top. Brushes are view-owned and disposed; no highlight Bitmap is allocated or raster rerender requested by highlighting itself.

Activation centers the first bounded glyph of a result in the viewport unless already comfortably visible (middle 70% horizontally / 60% vertically). The page can still be a raster placeholder. If geometry is unavailable or has no drawable glyph, navigation falls back to the page's standard scroll target. Manual scrolling/page navigation cancels a pending automatic jump, without canceling the search. Zoom, Fit modes, resize, and sidebar changes rederive highlights from logical indices and normalized geometry; they do not rescan the query.

### Large documents, failures, and limitations

A 1,001-page scan performs serial/replenished page work, never creates 1,001 text handles, and does not retain 1,001 geometry objects. Native handles still close within each extraction. Image-only/scanned pages return no matches and a successful full scan displays **No results**. Pages that cannot be extracted, including those exceeding the existing **32,768 native-character per-page limit**, are skipped and counted as unavailable in the status. The UI therefore does not imply that an incomplete text layer was searched successfully. Raster rendering and selection elsewhere remain usable.

Exceptionally dense visible pages can exceed the highlight reference budget; some non-active matches may temporarily lack highlights. The logical results remain navigable and the active result has first claim on geometry space. Text ordering, ligature/font mappings, crop omissions, and glyph-box accuracy inherit the existing extraction limitations. An expensive native text-page load can still delay higher-priority requests until it returns. Search is local and introduces no telemetry, persistence, or cloud dependency.

Deterministic tests cover exact/ordinal matching, literal newlines, no Unicode normalization, supplementary/index mapping, ordering, incremental state, cancellation, stable active results, clamping/wrapping, cap detection, priority ordering, cache reuse, stale document/query rejection, and replenished 1,001-page work. The existing native Unicode fixture also validates search-to-logical-range reconstruction. An ignored local WinForms harness checks the search bar, debounce, result activation/scrolling, overlay painting, empty/replaced queries, bounded highlight references, and mouse-selection independence without modifying the clipboard. Representative real-world PDFs still merit interactive review.

## PDF outline / bookmarks navigation

### Pipeline and immutable boundary

After page dimensions have opened successfully and the viewer is initialized, App requests `PdfViewerRenderer.ExtractOutlineAsync()` once. One deduplicated/replaceable outline slot runs on the existing exclusive PDFium worker, below all pending visible raster and interactive text/search-geometry work. There is no synchronous outline extraction in a UI event, Paint, or scroll handler. A bounded whole-tree extraction is simpler than lazy subtree jobs; switching sidebar tabs never extracts it again.

Core's `IPdfRenderSession.ExtractOutline()` returns a `PdfOutline` containing copied read-only `Roots` and `WasLimited`. Each immutable `PdfOutlineNode` contains only `Title`, nullable zero-based `PageIndex`, and copied read-only `Children`. A null destination is a visible, non-navigable item. These objects require no disposal and contain neither native pointers, drawing types, nor WinForms objects. There is no outline raster/cache, editing API, or new package. PDFiumCore remains version **154.0.8035**, isolated in Rendering; GPLv3 and existing dependency notices are unchanged.

### PDFium APIs, traversal, and limits

`PdfiumOutlineReader` uses these exact installed PDFiumCore bindings from `fpdf_doc`: `FPDFBookmarkGetFirstChild`, `FPDFBookmarkGetNextSibling`, `FPDFBookmarkGetTitle`, `FPDFBookmarkGetAction`, `FPDFActionGetType`, `FPDFActionGetDest`, `FPDFBookmarkGetDest`, and `FPDFDestGetDestPageIndex`. Their native counterparts and contracts are documented in [PDFium's public fpdf_doc.h](https://pdfium.googlesource.com/pdfium/+/refs/heads/main/public/fpdf_doc.h). We do not use bookmark Find/Count, execute actions, or call URI/file-path/JavaScript APIs.

Traversal is document-order depth-first: iterative siblings with recursion limited to **32 levels** (roots are level 1). At most **2,048 distinct nodes** are retained. A global set of native bookmark pointer identities (`__Instance`, never managed wrapper identity) prevents ancestor/sibling cycles and repeated/shared subtrees. Repeated pointers terminate that sibling chain; over-depth child branches are skipped while later reachable siblings remain available. Total-node exhaustion stops further collection. Untrusted PDF `/Count` values never drive allocation. `WasLimited` reports omitted structure or replaced oversized/invalid title data; the UI displays **Some bookmarks were limited**. Exceptional extraction failures instead produce **Bookmarks unavailable**, without closing the document or presenting a modal open/render error.

Titles are read using PDFium's required-byte-count query, followed by a bounded temporary buffer. The count includes the UTF-16 NUL terminator. Titles are decoded explicitly as **UTF-16LE**, preserving accents, scripts and valid surrogate pairs without Unicode normalization. The maximum is **512 UTF-16 code units**, excluding the terminator (up to 1,026 buffer bytes). PDFium does not fill undersized buffers, so oversized titles become **(Bookmark title too long)** rather than allocating their full size to obtain a prefix. Empty/whitespace-only titles become **(Untitled bookmark)**. Odd/inconsistent byte counts become an invalid-title placeholder; decoder fallback replaces malformed surrogates with U+FFFD. Embedded NULs become U+FFFD so WinForms does not silently hide a suffix. Valid nonempty titles are otherwise left unchanged.

The installed PDFium also converts embedded NULs in PDF title strings to spaces before returning them, as verified by a native fixture. MauriPDF preserves that returned text; the U+FFFD safeguard above applies only to NULs that actually reach the adapter. This means the outline reflects PDFium's interpreted labels rather than necessarily the exact original PDF string bytes.

Traversal/model storage and API call count are bounded by these limits (about 2 MiB maximum title payload, plus bounded managed node/TreeView/set overhead). This is not a sandbox or a wall-clock/native allocator guarantee: PDFium itself parses untrusted objects inside each call, and a slow call or already-active extraction can delay subsequently submitted higher-priority work. Operations are never forcibly interrupted mid-native-call. Visible work wins when choosing the next pending operation.

### Destinations and lifetime

If an action is present, only `PDFACTION_GOTO` (1: current document) is accepted, using `FPDFActionGetDest`; otherwise `FPDFBookmarkGetDest` reads the direct destination. Both explicit page arrays and named destinations resolved by PDFium are supported. `FPDFDestGetDestPageIndex` must return an index in `[0, PageCount)`; unresolved, invalid and missing destinations remain null. Remote/embedded GoTo, URI, Launch, JavaScript and other actions are inert, including additional/chained actions. No external document, resource, or application is opened. Destination coordinates, fit flags and zoom are deliberately not retained: activation uses existing page navigation and preserves the user's zoom mode.

Bookmark, destination and action handles are **borrowed references into the open document**, not resources to close individually. The reader and all wrappers/pointer identities exist only within the worker's extraction call. Strings/indices/hierarchy are copied before returning; no native references enter Core, App, TreeNode.Tag or caches. No native page/text/bitmap handle is loaded for outline extraction. The existing session exclusively owns/ closes the document and its library lease after any active extraction returns.

Opening a replacement document increments the worker's document generation and invalidates active/pending outline requests. UI clearing also cancels the independent outline generation and increments the existing open-intent ID; already-posted completions are checked before publication. MainForm clears old nodes immediately, and OutlineView rejects activation of a detached node (`node.TreeView != current tree`). Shutdown clears UI data, cancels pending work and awaits the worker before document disposal. Malformed outline failures are isolated from rendering. Native pointer references never survive the session.

### Sidebar behavior and verification

One TabControl inside the existing left panel contains **Thumbnails** and **Outline**. F4/Sidebar collapses that panel without changing the selected tab. OutlineView uses one standard TreeView with lightweight nodes tagged only with immutable Core node references; it allocates no images. Labels navigate on left click; Enter activates the selected node. Clicking expand/collapse glyphs does not navigate. Standard tree keys move selection/expand/collapse; MainForm does not intercept them as document-page shortcuts. Activation keeps keyboard focus in the tree and uses existing asynchronous `ContinuousPdfView.ApplyState` direct-page navigation: short pages centered, tall pages aligned at the top, clamped at document boundaries. Current-page toolbar and thumbnail selection update through the existing path; raster pixels arrive asynchronously.

Tree expansion/selection survives scrolling, zoom, mode switches and hiding/reopening the sidebar. Keyboard selection can move without activation; scrolling does not auto-select a chapter. Tab switches do not change viewport width, current page, search state, or text selection; hidden thumbnail Bitmap ownership is released through existing `SetActive(false)`. Hiding the entire sidebar still performs the existing fit/reading-position resize behavior. With no bookmarks the outline displays **No bookmarks**, with no fabricated headings or dialog. Bookmark styles/default PDF expansion state, exact destination viewport semantics, creation/editing and persistence are deferred.

Deterministic tests cover copied immutable hierarchy/order/Unicode, empty and null destinations, native parent/child/sibling traversal, named/local GoTo pages, unsupported actions, title/depth/node limits, malformed title data, cycles, worker priority, stale-document cancellation, metadata failure isolation and disposal sequencing. The ignored local WinForms smoke harness additionally exercises actual TreeView handles, Enter activation/focus, synchronization, detached-node rejection, tab/F4 state and search/selection independence. These checks do not replace interactive review with representative real-world PDFs.

## Initial milestone scope

`0.1-alpha.1` covers launching MauriPDF, opening a local PDF, reading its page count, rendering pages, page navigation, zoom, fit to width, and closing the document. Editing, annotations, forms, signatures, and document content modification are deferred.
