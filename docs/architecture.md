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

The App independently checks its view generation after await, preventing already-posted stale results from appearing after scrolling, zoom, replacement, or disposal. Expected cancellation is silent. Placeholders replace intrusive per-page rendering messages. The toolbar reports opening/closing only. A failed visible page remains a placeholder and is retried after leaving/re-entering demand; only the first failure in the current demand failure set displays a simple error.

Document replacement clears the App's layout/Bitmaps and invalidates main and thumbnail UI work immediately. The worker's open request cancels old native-result publication, clears both caches, and closes the old session after any active call finishes. It reads the new geometry before enabling its view. Old-document pixels never remain on screen while the new document opens. Shutdown cancels requests and awaits worker drainage with the WinForms message loop alive so posted result owners can dispose safely.

`RenderCacheKey` distinguishes document identity, page index, target width and target height. The existing **64 MiB LRU main neutral cache** and separate **8 MiB thumbnail cache** remain unchanged. They account for backing allocation capacity, not only visible pixels, and dispose on eviction, replacement, clear, or shutdown. A cache hit returns an independent caller-owned copy. A cacheable fresh render transfers the original neutral buffer to the cache and returns a copy. The App disposes copies after conversion; cache eviction cannot invalidate a displayed Bitmap. Target-size changes caused by the working-set budget are deliberately different cache keys.

Native page/bitmap handles remain scoped to a single render. The native bitmap is destroyed before unpinning its borrowed pixel buffer; the document handle belongs to the worker session. PDFiumCore remains solely within Rendering. No packages, cloud services, telemetry, or licensing changes were introduced.

A 1,001-page document therefore has O(page count) dimension/layout storage, but only O(visible pages) render demand and bounded Bitmap/cache storage. Deterministic tests cover geometry for 1/100/1,001 pages, huge extents, mixed sizes, visible ranges/gaps, optional layout overscan, fit/anchor rules, rapid replacement of 1,000 foreground intents, cache reuse, and batch priority. A local WinForms smoke harness checks real control handles, painting and disposal using a render stand-in; it is not a substitute for interactive validation with complex real PDFs. Metadata scanning can delay initial display on very large/malformed PDFs, and UI-thread pixel-to-Bitmap conversion can still briefly pause for large images.

## Thumbnail sidebar

The App uses a native WinForms `ListView` in virtual Details mode with owner-drawn fixed-height rows. `VirtualListSize` represents the page count; item text is supplied on demand. There are no per-page controls or eagerly allocated images. An empty `ImageList` with a 1-by-244 size establishes native row height: owner drawing alone does not change Details-view row metrics. It must remain empty; there is no spacer Bitmap and no thumbnail is added to it. This avoids retaining an already-disposed image until deferred native handle creation. The control owns this sizing resource, detaches it from `SmallImageList`, then disposes it during shutdown. Visible thumbnail Bitmaps belong only to the bounded `_images` collection and are drawn directly; placeholders are painted rectangles/text, not Image objects. The left `SplitContainer` panel defaults to 190 pixels, is resizable, and can be collapsed via the Thumbnails toolbar button or F4. The existing viewport resize handler retains its 150 ms fit-mode debounce; toggling the sidebar never reopens the session.

`ThumbnailSizeCalculator` uses a fixed **144-pixel target width**, preserving aspect ratio. Very tall pages are uniformly reduced to a **512-pixel maximum height** for predictable lightweight allocations. The UI fits that one raster into a fixed row without requesting another resolution. Portrait and landscape pages have the same target-width policy. Extremely tall/thin pages may be hard to read at thumbnail scale.

Visible demand comes from `TopItem.Index` and client height divided by fixed row height, with two extra rows to cover partial visibility. Scrolling/wheel, painting, resize, and selection-driven `EnsureVisible` update the range. A 100 ms debounce coalesces changes. The UI walks this small range sequentially, skipping existing images and failures; at most **32** visible-range bitmaps are retained even for exceptionally tall windows. Off-range bitmap copies are disposed. Normal desktop windows require only a handful. No document-wide thumbnail request list is created; the shared document geometry scan does not render thumbnails.

Thumbnail clicks use the existing main-page navigation path. Toolbar, keyboard, and direct entry update selection and ensure the requested page is visible. Since navigation scrolls the selected thumbnail into view, it participates in the visible-first range without a separate distant-page request. Hidden sidebars cancel their demand and release bitmap copies. Simple numbered placeholders remain until successful rendering. Individual failures only produce DEBUG diagnostics and a placeholder; they are retried after leaving/reentering the range or opening a document, never through error dialogs.

The worker has a **separate 8 MiB LRU thumbnail cache**, configurable through `thumbnailBudgetBytes`. It reuses `RenderCache` byte accounting/ownership and keys `(document identity, page index, target width, target height)`. Full-page cache activity cannot evict thumbnail entries, or vice versa. Original neutral buffers belong exclusively to the cache; independent result copies belong to the awaiting UI and are disposed immediately after bitmap conversion. Only the current small visible range also has WinForms bitmap representations; this bounded duplication allows neutral cache reuse after scrolling back. Eviction, replacement, document changes, and shutdown dispose cached buffers. Pool retention and displayed bitmap copies remain outside the cache budget.

`TryRequestThumbnail` deduplicates active/pending requests by document/page because the single thumbnail sizing policy deterministically fixes dimensions. Duplicates return false without sharing a disposable result task between callers. A different request can replace the single pending thumbnail slot. Scrolling/hiding cancels the old thumbnail demand version, but does not cancel main-page requests. New documents invalidate thumbnail and foreground work immediately using document/request generations; the worker then clears both caches and closes the old session safely. The UI independently checks its sidebar generation after await, so already-posted results from a previous document cannot appear in the new sidebar. Shutdown drains the same serialized worker.

DEBUG output reports thumbnail cache hits, fresh renders, skipped duplicates, and failures. There is no telemetry or persistent logging. As with the main viewer, an active PDFium call is not forcibly interrupted; complex thumbnail pages can briefly delay newly submitted foreground work. Foreground work is always next, ahead of pending thumbnails.

## Initial milestone scope

`0.1-alpha.1` covers launching MauriPDF, opening a local PDF, reading its page count, rendering pages, page navigation, zoom, fit to width, and closing the document. Editing, annotations, forms, signatures, and document content modification are deferred.
