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
- Rendering should be cancellable where supported by the selected engine.
- Page caches must be bounded by estimated memory cost rather than page count alone.
- Only visible pages and a small prefetch window should be rendered.
- Concrete PDF packages must be evaluated for performance, deployment, file-locking behavior, robustness, and GPLv3 compatibility before adoption.

## Initial milestone

`0.1-alpha.1` covers launching MauriPDF, opening a local PDF, reading its page count, rendering pages, page navigation, zoom, fit to width, and closing the document. Editing, annotations, forms, signatures, and document content modification are deferred.
