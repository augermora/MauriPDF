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
- Rendering is serialized on a dedicated background worker and limited to explicit selected-page requests, with a bounded neutral-pixel cache. There is no prefetching.
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

There is at most one active operation and one replaceable pending request. Submission increments a version, cancels superseded completion tasks, and replaces pending work. Active native rendering cannot safely be interrupted; it finishes and obsolete pixels are disposed without publication. A second UI request ID check after await prevents an already-posted completion from replacing a newer requested page. Exceptions from obsolete work are suppressed. No per-input Task.Run calls or unbounded task queue are used.

Document replacement invalidates old requests immediately, then closes the old session and clears its cache on the worker after any active native call finishes. Each opened session gets a new document identity. The previous displayed bitmap can remain visible while the replacement opens/renders; it is independent of the closed document, and its navigation state is not reused. Closing the window cancels requests and awaits worker drainage with the message loop still active; native document/engine destruction never races rendering.

`RenderCacheKey` contains document identity, zero-based page index, target pixel width, and target pixel height. A dictionary plus linked-list LRU bounds retained backing allocations to **64 MiB** by default, configurable in the coordinator/cache constructor. Accounting uses `RenderedPage.AllocatedBytes`, including pool capacity rather than just visible pixels. Least-recently-used entries are disposed on eviction; replacement disposes the old entry; clear/shutdown disposes all entries. A single oversized render is displayed but bypasses caching. This budget is for retained cache allocations, not total process memory; native rendering, pool retention, temporary copies, and the displayed bitmap consume additional memory.

The cache exclusively owns neutral `RenderedPage` buffers, never native handles or WinForms bitmaps. A hit returns an independent caller-owned copy; a cacheable fresh result transfers its original buffer to the cache and returns a copy. The App disposes that copy immediately after bitmap conversion. Thus cache entries cannot be invalidated by UI disposal, and eviction cannot invalidate a displayed image. There is only one displayed bitmap; no bitmap cache or long-lived duplicate UI render buffer. The cached current-page pixels and displayed bitmap may coexist, a deliberate bounded duplication to keep Core/Rendering platform-neutral. The shared memory pool may retain returned arrays for reuse.

Prefetch is deliberately deferred: a noninterruptible N+1 render could delay a subsequent explicit request, contradicting strict current-page priority. No N-1, alternate zoom levels, or document-wide speculation is performed.

The toolbar shows `Rendering...` while awaiting a result and preserves the previous valid page. Cache hits avoid native work but still involve a worker round trip and a short copy. Await continuations resume on the WinForms synchronization context; controls and bitmap assignment are touched only there. Bitmap conversion remains synchronous on the UI thread, so very large image copies can cause a brief pause even though PDFium work is off-thread. DEBUG-only `Debug.WriteLine` identifies displayed cache hits versus fresh renders without telemetry or persistent logs.

## Initial milestone

`0.1-alpha.1` covers launching MauriPDF, opening a local PDF, reading its page count, rendering pages, page navigation, zoom, fit to width, and closing the document. Editing, annotations, forms, signatures, and document content modification are deferred.
