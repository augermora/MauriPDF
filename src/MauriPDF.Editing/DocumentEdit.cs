using MauriPDF.Core.Documents;

namespace MauriPDF.Editing;

/// <summary>Explicit current-source page operations. No operation writes the source document.</summary>
public abstract record DocumentEdit(DocumentPageId PageId);
public sealed record DeletePageEdit(DocumentPageId PageId) : DocumentEdit(PageId);
/// <summary>Target is the zero-based final logical position after the move.</summary>
public sealed record MovePageEdit(DocumentPageId PageId, int TargetIndex) : DocumentEdit(PageId);
public sealed record RotatePageEdit(DocumentPageId PageId, bool Clockwise) : DocumentEdit(PageId);
