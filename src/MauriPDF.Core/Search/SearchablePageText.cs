using System.Text;
using MauriPDF.Core.Text;

namespace MauriPDF.Core.Search;

public readonly record struct SearchMatch(int PageIndex, int Start, int End);
public sealed record PageSearchResult(IReadOnlyList<SearchMatch> Matches, bool Truncated);

/// <summary>Exact text (no whitespace/canonical normalization), with a logical index per UTF-16 code unit.</summary>
public sealed class SearchablePageText
{
    public const int MaximumQueryLength = 1024;
    private readonly int[] _logicalIndices;
    public string Text { get; }

    public SearchablePageText(PdfTextPage page)
    {
        StringBuilder text = new();
        List<int> indices = [];
        Span<char> utf16 = stackalloc char[2];
        for (int index = 0; index < page.Count; index++)
        {
            uint value = page[index].Unicode;
            if (value == 0) continue;
            Rune rune = Rune.TryCreate(value, out Rune valid) ? valid : Rune.ReplacementChar;
            int length = rune.EncodeToUtf16(utf16);
            text.Append(utf16[..length]);
            for (int unit = 0; unit < length; unit++) indices.Add(index);
        }
        Text = text.ToString();
        _logicalIndices = indices.ToArray();
    }

    public PageSearchResult Find(int pageIndex, string query, int limit)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(limit);
        if (query.Length > MaximumQueryLength) throw new ArgumentOutOfRangeException(nameof(query));
        List<SearchMatch> matches = [];
        if (query.Length == 0) return new(matches.AsReadOnly(), false);
        int offset = 0;
        while (offset <= Text.Length - query.Length)
        {
            int found = Text.IndexOf(query, offset, StringComparison.OrdinalIgnoreCase);
            if (found < 0) break;
            int end = found + query.Length;
            // Never match just one half of a supplementary scalar, even for malformed queries.
            if (char.IsLowSurrogate(Text[found]) || (end < Text.Length && char.IsLowSurrogate(Text[end])))
            {
                offset = found + 1;
                continue;
            }
            if (matches.Count == limit) return new(matches.AsReadOnly(), true);
            matches.Add(new(pageIndex, _logicalIndices[found], _logicalIndices[end - 1] + 1));
            offset = end; // Deterministic non-overlapping matches.
        }
        return new(matches.AsReadOnly(), false);
    }
}
