using System.Text;
using MauriPDF.Core.Outline;
using PDFiumCore;

namespace MauriPDF.Rendering;

/// <summary>Short-lived, worker-only reader. Every native wrapper below borrows document-owned data.</summary>
internal sealed class PdfiumOutlineReader(FpdfDocumentT document, int pageCount)
{
    private readonly HashSet<nint> _visited = [];
    private bool _limited;

    public PdfOutline Read()
    {
        List<PdfOutlineNode> roots = ReadSiblings(fpdf_doc.FPDFBookmarkGetFirstChild(document, null), 1);
        return new(roots, _limited);
    }

    // Recursion is bounded to 32 frames; siblings use iteration. The global pointer set also
    // rejects shared subtrees, not just ancestor cycles. No /Count values are trusted.
    private List<PdfOutlineNode> ReadSiblings(FpdfBookmarkT? bookmark, int depth)
    {
        List<PdfOutlineNode> nodes = [];
        while (bookmark is not null)
        {
            if (_visited.Count >= PdfOutline.MaximumNodes || !_visited.Add(bookmark.__Instance))
            {
                _limited = true;
                break;
            }

            string title = ReadTitle(bookmark);
            int? pageIndex = ResolvePage(bookmark);
            FpdfBookmarkT? child = fpdf_doc.FPDFBookmarkGetFirstChild(document, bookmark);
            List<PdfOutlineNode> children = [];
            if (child is not null)
            {
                if (depth < PdfOutline.MaximumDepth) children = ReadSiblings(child, depth + 1);
                else _limited = true;
            }
            nodes.Add(new(title, pageIndex, children));
            bookmark = fpdf_doc.FPDFBookmarkGetNextSibling(document, bookmark);
        }
        return nodes;
    }

    private unsafe string ReadTitle(FpdfBookmarkT bookmark)
    {
        ulong bytes = fpdf_doc.FPDFBookmarkGetTitle(bookmark, nint.Zero, 0);
        if (bytes <= 2) return "(Untitled bookmark)";
        // PDFium will not write a partial title. Never allocate the untrusted required size.
        if (bytes > (PdfOutline.MaximumTitleLength + 1) * 2)
        {
            _limited = true;
            return "(Bookmark title too long)";
        }
        if (bytes % 2 != 0)
        {
            _limited = true;
            return "(Invalid bookmark title)";
        }
        byte* buffer = stackalloc byte[(int)bytes];
        new Span<byte>(buffer, (int)bytes).Clear();
        if (fpdf_doc.FPDFBookmarkGetTitle(bookmark, (nint)buffer, bytes) != bytes)
        {
            _limited = true;
            return "(Invalid bookmark title)";
        }
        // Explicit UTF-16LE decoding preserves surrogate pairs; malformed sequences use U+FFFD.
        string title = Encoding.Unicode.GetString(new ReadOnlySpan<byte>(buffer, (int)bytes - 2));
        // Embedded NULs cannot be displayed by TreeView; do not let them silently hide a suffix.
        if (title.Contains('\0', StringComparison.Ordinal))
        {
            _limited = true;
            title = title.Replace('\0', '\uFFFD');
        }
        return string.IsNullOrWhiteSpace(title) ? "(Untitled bookmark)" : title;
    }

    private int? ResolvePage(FpdfBookmarkT bookmark)
    {
        FpdfActionT? action = fpdf_doc.FPDFBookmarkGetAction(bookmark);
        FpdfDestT? destination;
        if (action is not null)
        {
            const ulong localGoTo = 1; // PDFACTION_GOTO in PDFium public/fpdf_doc.h.
            if (fpdf_doc.FPDFActionGetType(action) != localGoTo) return null;
            destination = fpdf_doc.FPDFActionGetDest(document, action);
        }
        else destination = fpdf_doc.FPDFBookmarkGetDest(document, bookmark);
        if (destination is null) return null;
        int pageIndex = fpdf_doc.FPDFDestGetDestPageIndex(document, destination);
        return pageIndex >= 0 && pageIndex < pageCount ? pageIndex : null;
    }
}
