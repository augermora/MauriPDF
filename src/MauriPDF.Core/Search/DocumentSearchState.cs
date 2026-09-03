namespace MauriPDF.Core.Search;

/// <summary>UI-neutral progressive state. One consumer appends pages in document order.</summary>
public sealed class DocumentSearchState
{
    public const int MaximumResults = 10_000;
    private readonly List<SearchMatch> _matches = [];

    public DocumentSearchState(string query, int totalPages)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(totalPages);
        if (query.Length == 0 || query.Length > SearchablePageText.MaximumQueryLength) throw new ArgumentOutOfRangeException(nameof(query));
        Query = query;
        TotalPages = totalPages;
    }

    public string Query { get; }
    public int TotalPages { get; }
    public int PagesScanned { get; private set; }
    public int FailedPages { get; private set; }
    public bool Complete => !Cancelled && (PagesScanned == TotalPages || Truncated);
    public bool Cancelled { get; private set; }
    public bool Truncated { get; private set; }
    public int Count => _matches.Count;
    public int ActiveIndex { get; private set; } = -1;
    public SearchMatch? Active => ActiveIndex < 0 ? null : _matches[ActiveIndex];
    public SearchMatch this[int index] => _matches[index];

    public void Append(int page, PageSearchResult result, bool failed = false)
    {
        if (Cancelled || Complete) return;
        if (page != PagesScanned || result.Matches.Count > MaximumResults - Count) throw new ArgumentException("Invalid progressive search page.", nameof(page));
        int last = -1;
        foreach (SearchMatch match in result.Matches)
        {
            if (match.PageIndex != page || match.Start < last || match.End <= match.Start) throw new ArgumentException("Unordered matches.", nameof(result));
            last = match.End;
        }
        _matches.AddRange(result.Matches);
        if (ActiveIndex < 0 && Count > 0) ActiveIndex = 0;
        PagesScanned++;
        if (failed) FailedPages++;
        Truncated = result.Truncated;
    }

    public void Move(bool previous)
    {
        if (Count == 0 || Cancelled) return;
        int next = ActiveIndex + (previous ? -1 : 1);
        ActiveIndex = Complete ? (next + Count) % Count : Math.Clamp(next, 0, Count - 1);
    }

    public void Cancel() => Cancelled = true;

    public (int Start, int End) MatchesOnPage(int page)
    {
        int Bound(int target)
        {
            int low = 0, high = Count;
            while (low < high)
            {
                int middle = low + (high - low) / 2;
                if (_matches[middle].PageIndex < target) low = middle + 1;
                else high = middle;
            }
            return low;
        }
        return (Bound(page), Bound(page + 1));
    }
}
