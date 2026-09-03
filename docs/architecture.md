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

## Single-page viewer

Core's immutable `ViewerState` holds page count, zero-based page index, last manual zoom percentage, and the active mode (Manual, Fit Page, or Fit Width). Page entry is one-based and clamped to the document range; nonnumeric input is rejected by restoring the current page number. Opening a document resets to page 1 at manual 100%. The App keeps requested and displayed states separate: repeated navigation advances immediately from the requested page while the previous bitmap remains visible. A failed current request restores displayed state when available; canceled requests never change the display or show errors.

Manual zoom ranges from 25% to 500%, in 25-percentage-point steps. 100% consistently means 96 output pixels per inch, or 96/72 pixels per PDF point. +/- leaves a fit mode and resumes from the last manual percentage; the 100% button resets it. This is a fixed raster scale, not a physical-screen-size calibration.

`RenderSizeCalculator` uses one scale for both dimensions:

- Manual: `96 / 72 * zoomPercent / 100` pixels per point.
- Fit Page: the smaller of viewport-width/page-width and viewport-height/page-height.
- Fit Width: viewport-width/page-width, reserving vertical scrollbar width when the page would exceed viewport height.

Manual pixel dimensions round to the nearest integer; fit dimensions round down so they cannot overflow the viewport. Both have a one-pixel minimum and preserve aspect ratio to integer-pixel precision. Fit modes are viewport-driven rather than limited by the manual percentage bounds. The rendering adapter's existing allocation safety limit still applies.

The App measures the full document viewport independently of scrollbars left by the previous image. Fit modes recalculate after a 150 ms resize debounce; manual zoom keeps its raster size. An exact cached target is reused. Page, zoom, and explicit fit changes reset scrolling to the top; the scrollable panel supports inspecting larger pages with scrollbars or the mouse wheel.

Every requested target size uses an exact-size cached result or the existing `RenderPage(index, width, height)` API, never bitmap scaling. The App copies each caller-owned `RenderedPage` into a new WinForms bitmap, disposes the neutral buffer immediately, and disposes the old displayed bitmap when replacing it. The native bitmap is destroyed before unpinning its borrowed buffer; page handles remain scoped to each render and sessions to the open document.

## Background rendering and cache

`PdfViewerRenderer` exclusively owns its engine and session. One dedicated BCL worker initializes the engine, opens documents, obtains page dimensions, renders, accesses the cache, and closes/disposes documents and the engine. These operations never run concurrently or on the WinForms thread. The synchronous adapter must not be used independently while owned by the coordinator. The App creates one coordinator; this is not a framework for multiple independently concurrent PDFium engines.

There is at most one active operation, one replaceable pending foreground request, and one replaceable pending thumbnail request. Foreground submission increments its version and cancels superseded foreground work, without canceling unrelated thumbnail work. The worker always chooses foreground/open work before a pending thumbnail. Active native rendering cannot safely be interrupted; an active thumbnail may finish before newly requested foreground work, but no pending thumbnail can precede that foreground request. A second UI request ID check after await prevents an already-posted main-page completion from replacing a newer requested page. Exceptions from obsolete work are suppressed. No per-input Task.Run calls or unbounded task queue are used.

Document replacement invalidates old requests immediately, then closes the old session and clears its cache on the worker after any active native call finishes. Each opened session gets a new document identity. The previous displayed bitmap can remain visible while the replacement opens/renders; it is independent of the closed document, and its navigation state is not reused. Closing the window cancels requests and awaits worker drainage with the message loop still active; native document/engine destruction never races rendering.

`RenderCacheKey` contains document identity, zero-based page index, target pixel width, and target pixel height. A dictionary plus linked-list LRU bounds retained backing allocations to **64 MiB** by default, configurable in the coordinator/cache constructor. Accounting uses `RenderedPage.AllocatedBytes`, including pool capacity rather than just visible pixels. Least-recently-used entries are disposed on eviction; replacement disposes the old entry; clear/shutdown disposes all entries. A single oversized render is displayed but bypasses caching. This budget is for retained cache allocations, not total process memory; native rendering, pool retention, temporary copies, and the displayed bitmap consume additional memory.

The cache exclusively owns neutral `RenderedPage` buffers, never native handles or WinForms bitmaps. A hit returns an independent caller-owned copy; a cacheable fresh result transfers its original buffer to the cache and returns a copy. The App disposes that copy immediately after bitmap conversion. Thus cache entries cannot be invalidated by UI disposal, and eviction cannot invalidate a displayed image. There is only one main-view bitmap; no main-view bitmap cache or long-lived duplicate UI render buffer. The cached current-page pixels and displayed bitmap may coexist, a deliberate bounded duplication to keep Core/Rendering platform-neutral. The shared memory pool may retain returned arrays for reuse.

Full-page prefetch remains deferred. Only visible-range thumbnail work may run in the background, subject to the explicitly permitted one-active-thumbnail delay. No N-1/N+1 full-page prefetch, alternate zoom levels, or document-wide speculation is performed.

The toolbar shows `Rendering...` while awaiting a result and preserves the previous valid page. Cache hits avoid native work but still involve a worker round trip and a short copy. Await continuations resume on the WinForms synchronization context; controls and bitmap assignment are touched only there. Bitmap conversion remains synchronous on the UI thread, so very large image copies can cause a brief pause even though PDFium work is off-thread. DEBUG-only `Debug.WriteLine` identifies displayed cache hits versus fresh renders without telemetry or persistent logs.

## Thumbnail sidebar

The App uses a native WinForms `ListView` in virtual Details mode with owner-drawn fixed-height rows. `VirtualListSize` represents the page count; item text is supplied on demand. There are no per-page controls or eagerly allocated images. An empty `ImageList` with a 1-by-244 size establishes native row height: owner drawing alone does not change Details-view row metrics. It must remain empty; there is no spacer Bitmap and no thumbnail is added to it. This avoids retaining an already-disposed image until deferred native handle creation. The control owns this sizing resource, detaches it from `SmallImageList`, then disposes it during shutdown. Visible thumbnail Bitmaps belong only to the bounded `_images` collection and are drawn directly; placeholders are painted rectangles/text, not Image objects. The left `SplitContainer` panel defaults to 190 pixels, is resizable, and can be collapsed via the Thumbnails toolbar button or F4. The existing viewport resize handler retains its 150 ms fit-mode debounce; toggling the sidebar never reopens the session.

`ThumbnailSizeCalculator` uses a fixed **144-pixel target width**, preserving aspect ratio. Very tall pages are uniformly reduced to a **512-pixel maximum height** for predictable lightweight allocations. The UI fits that one raster into a fixed row without requesting another resolution. Portrait and landscape pages have the same target-width policy. Extremely tall/thin pages may be hard to read at thumbnail scale.

Visible demand comes from `TopItem.Index` and client height divided by fixed row height, with two extra rows to cover partial visibility. Scrolling/wheel, painting, resize, and selection-driven `EnsureVisible` update the range. A 100 ms debounce coalesces changes. The UI walks this small range sequentially, skipping existing images and failures; at most **32** visible-range bitmaps are retained even for exceptionally tall windows. Off-range bitmap copies are disposed. Normal desktop windows require only a handful. No document-wide request list or eager page-dimension scan is created for large PDFs.

Thumbnail clicks use the existing main-page navigation path. Toolbar, keyboard, and direct entry update selection and ensure the requested page is visible. Since navigation scrolls the selected thumbnail into view, it participates in the visible-first range without a separate distant-page request. Hidden sidebars cancel their demand and release bitmap copies. Simple numbered placeholders remain until successful rendering. Individual failures only produce DEBUG diagnostics and a placeholder; they are retried after leaving/reentering the range or opening a document, never through error dialogs.

The worker has a **separate 8 MiB LRU thumbnail cache**, configurable through `thumbnailBudgetBytes`. It reuses `RenderCache` byte accounting/ownership and keys `(document identity, page index, target width, target height)`. Full-page cache activity cannot evict thumbnail entries, or vice versa. Original neutral buffers belong exclusively to the cache; independent result copies belong to the awaiting UI and are disposed immediately after bitmap conversion. Only the current small visible range also has WinForms bitmap representations; this bounded duplication allows neutral cache reuse after scrolling back. Eviction, replacement, document changes, and shutdown dispose cached buffers. Pool retention and displayed bitmap copies remain outside the cache budget.

`TryRequestThumbnail` deduplicates active/pending requests by document/page because the single thumbnail sizing policy deterministically fixes dimensions. Duplicates return false without sharing a disposable result task between callers. A different request can replace the single pending thumbnail slot. Scrolling/hiding cancels the old thumbnail demand version, but does not cancel main-page requests. New documents invalidate thumbnail and foreground work immediately using document/request generations; the worker then clears both caches and closes the old session safely. The UI independently checks its sidebar generation after await, so already-posted results from a previous document cannot appear in the new sidebar. Shutdown drains the same serialized worker.

DEBUG output reports thumbnail cache hits, fresh renders, skipped duplicates, and failures. There is no telemetry or persistent logging. As with the main viewer, an active PDFium call is not forcibly interrupted; complex thumbnail pages can briefly delay newly submitted foreground work. Foreground work is always next, ahead of pending thumbnails.

## Initial milestone scope

`0.1-alpha.1` covers launching MauriPDF, opening a local PDF, reading its page count, rendering pages, page navigation, zoom, fit to width, and closing the document. Editing, annotations, forms, signatures, and document content modification are deferred.
