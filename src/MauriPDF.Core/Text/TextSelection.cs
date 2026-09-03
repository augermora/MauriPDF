using System.Text;

namespace MauriPDF.Core.Text;

/// <summary>Zero-based page and half-open insertion offset in PDFium character order.</summary>
public readonly record struct TextPosition(int PageIndex, int Offset) : IComparable<TextPosition>
{
    public int CompareTo(TextPosition other) => PageIndex == other.PageIndex
        ? Offset.CompareTo(other.Offset) : PageIndex.CompareTo(other.PageIndex);
    public static bool operator <(TextPosition left, TextPosition right) => left.CompareTo(right) < 0;
    public static bool operator >(TextPosition left, TextPosition right) => left.CompareTo(right) > 0;
    public static bool operator <=(TextPosition left, TextPosition right) => left.CompareTo(right) <= 0;
    public static bool operator >=(TextPosition left, TextPosition right) => left.CompareTo(right) >= 0;
}

public sealed record TextSelection(TextPosition Anchor, TextPosition Active)
{
    public const int MaximumPages = 16;
    public const int MaximumCharacters = 131_072;
    public TextPosition Start => Anchor.CompareTo(Active) <= 0 ? Anchor : Active;
    public TextPosition End => Anchor.CompareTo(Active) <= 0 ? Active : Anchor;
    public bool IsEmpty => Start == End;

    public (int Start, int End) RangeForPage(int pageIndex, int count)
    {
        if (pageIndex < Start.PageIndex || pageIndex > End.PageIndex) return (0, 0);
        return (pageIndex == Start.PageIndex ? Math.Clamp(Start.Offset, 0, count) : 0,
            pageIndex == End.PageIndex ? Math.Clamp(End.Offset, 0, count) : count);
    }

    /// <summary>Never copies a partial selection when a selected page is still missing.</summary>
    public string? Reconstruct(IReadOnlyDictionary<int, PdfTextPage> pages)
    {
        if (IsEmpty) return string.Empty;
        if (End.PageIndex - Start.PageIndex >= MaximumPages) return null;
        StringBuilder text = new();
        int characters = 0;
        for (int page = Start.PageIndex; page <= End.PageIndex; page++)
        {
            if (!pages.TryGetValue(page, out PdfTextPage? data)) return null;
            (int start, int end) = RangeForPage(page, data.Count);
            characters += end - start;
            if (characters > MaximumCharacters) return null;
            if (page > Start.PageIndex) text.Append("\r\n\r\n");
            data.AppendText(text, start, end);
        }
        return text.ToString();
    }
}
