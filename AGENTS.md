# MauriPDF contributor guidance

## Product constraints

- MauriPDF is a Windows x64 desktop application built with C#, .NET 10, and WinForms.
- MauriPDF is GPLv3. Dependencies and distribution choices must be GPLv3-compatible.
- The product is privacy-first and local-first. User documents must never leave the computer.
- Do not add accounts, telemetry, subscriptions, or cloud dependencies.
- Performance and low memory usage are priorities.

## Architecture

- `MauriPDF.Core` must remain independent of WinForms and PDF-specific third-party libraries.
- The UI should not depend directly on a PDF library when avoidable.
- Rendering and editing implementations belong behind boundaries defined only when a concrete use case requires them.
- Business logic must not live in WinForms forms.
- Avoid speculative abstractions and keep ownership of native resources and large buffers explicit.
- Preserve nullable reference types and treat compiler warnings as errors.

## Current milestone

For `0.1-alpha.1`, limit product work to launching the app, opening and closing a local PDF, determining page count, rendering and navigating pages, zooming, and fitting to width.

Do not introduce editing APIs or PDF packages without an explicit project decision.

## Verification

Before handing off code changes, restore, build the complete solution in Release configuration, and run all tests. Do not commit changes unless explicitly requested.
