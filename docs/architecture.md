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
- Rendering is currently synchronous and limited to the selected page. There is no cache, prefetching, or rendering scheduler.
- Concrete PDF packages must be evaluated for performance, deployment, file-locking behavior, robustness, and GPLv3 compatibility before adoption.

## Single-page viewer

Core's immutable `ViewerState` holds page count, zero-based page index, last manual zoom percentage, and the active mode (Manual, Fit Page, or Fit Width). Page entry is one-based and clamped to the document range; nonnumeric input is rejected by restoring the current page number. Opening a document resets to page 1 at manual 100%. The App commits a requested state only after rendering succeeds, preserving the previous image/state on a navigation or zoom failure.

Manual zoom ranges from 25% to 500%, in 25-percentage-point steps. 100% consistently means 96 output pixels per inch, or 96/72 pixels per PDF point. +/- leaves a fit mode and resumes from the last manual percentage; the 100% button resets it. This is a fixed raster scale, not a physical-screen-size calibration.

`RenderSizeCalculator` uses one scale for both dimensions:

- Manual: `96 / 72 * zoomPercent / 100` pixels per point.
- Fit Page: the smaller of viewport-width/page-width and viewport-height/page-height.
- Fit Width: viewport-width/page-width, reserving vertical scrollbar width when the page would exceed viewport height.

Manual pixel dimensions round to the nearest integer; fit dimensions round down so they cannot overflow the viewport. Both have a one-pixel minimum and preserve aspect ratio to integer-pixel precision. Fit modes are viewport-driven rather than limited by the manual percentage bounds. The rendering adapter's existing allocation safety limit still applies.

The App measures the full document viewport independently of scrollbars left by the previous image. Fit modes recalculate after a 150 ms resize debounce; manual zoom keeps its raster size. No rendering is repeated when the current state and target size are unchanged. Page, zoom, and explicit fit changes reset scrolling to the top; the scrollable panel supports inspecting larger pages with scrollbars or the mouse wheel.

Every changed target size is passed to the existing `RenderPage(index, width, height)` API for a fresh PDFium render, never bitmap scaling. The App copies each `RenderedPage` into a new WinForms bitmap, disposes the neutral buffer immediately, and disposes the old displayed bitmap when replacing it. The native bitmap is destroyed before unpinning its borrowed buffer; page handles remain scoped to each render and sessions to the open document.

## Initial milestone

`0.1-alpha.1` covers launching MauriPDF, opening a local PDF, reading its page count, rendering pages, page navigation, zoom, fit to width, and closing the document. Editing, annotations, forms, signatures, and document content modification are deferred.
