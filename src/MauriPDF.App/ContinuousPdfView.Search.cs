using MauriPDF.Core.Search;
using MauriPDF.Core.Text;

namespace MauriPDF.App;

internal sealed partial class ContinuousPdfView
{
    private readonly Dictionary<int, PdfTextPage> _searchPages = [];
    private readonly SolidBrush _matchBrush = new(Color.FromArgb(90, 255, 220, 0));
    private readonly SolidBrush _activeMatchBrush = new(Color.FromArgb(145, 255, 125, 0));
    private DocumentSearchState? _search;
    private SearchMatch? _searchNavigation;
    private int[] _searchDemand = [];
    private long _searchWorkerGeneration;
    private long _searchViewGeneration;

    public void SetSearch(DocumentSearchState? search, long workerGeneration)
    {
        if (_disposed) return;
        if (!ReferenceEquals(search, _search))
        {
            _searchViewGeneration++;
            _renderer.CancelSearchGeometry();
            _searchPages.Clear();
            _searchDemand = [];
            _searchNavigation = null;
        }
        _search = search;
        _searchWorkerGeneration = workerGeneration;
        RefreshSearchGeometry();
        Invalidate();
    }

    public void ActivateSearchMatch()
    {
        var active = _search?.Active;
        if (active.HasValue && _state?.DisplayMode == Core.Viewing.ViewerDisplayMode.SinglePage)
            NavigatePage(active.Value.PageIndex);
        _searchNavigation = active;
        if (_searchNavigation is null) return;
        NavigateSearchIfReady();
        RefreshSearchGeometry();
        Invalidate();
    }

    private void RefreshSearchGeometry()
    {
        if (_search is null || _layout is null || _disposed) return;
        List<int> pages = [];
        if (_searchNavigation is SearchMatch active) pages.Add(active.PageIndex);
        var visible = _layout.Visible(_top, ViewHeight);
        for (int page = visible.First; page <= visible.Last && pages.Count < 16; page++)
        {
            var matches = _search.MatchesOnPage(page);
            if (matches.Start < matches.End && !pages.Contains(page)) pages.Add(page);
        }
        if (_searchDemand.AsSpan().SequenceEqual(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(pages))) return;
        long generation = ++_searchViewGeneration;
        _renderer.CancelSearchGeometry();
        _searchDemand = pages.ToArray();
        foreach (int page in _searchPages.Keys.Where(page => !pages.Contains(page)).ToArray()) _searchPages.Remove(page);
        LoadSearchGeometry(_searchDemand, generation, _searchWorkerGeneration);
    }

    private async void LoadSearchGeometry(int[] pages, long generation, long workerGeneration)
    {
        foreach (int page in pages)
        {
            if (_disposed || generation != _searchViewGeneration) return;
            if (!_searchPages.ContainsKey(page))
            {
                try
                {
                    PdfTextPage text = await _renderer.SearchGeometryAsync(workerGeneration, SourcePageIndex(page));
                    if (_disposed || generation != _searchViewGeneration) return;
                    // Separate UI reference bound, not a second document-wide geometry cache.
                    if (_searchPages.Values.Sum(value => value.Count) + text.Count > TextSelection.MaximumCharacters)
                    {
                        if (_searchNavigation?.PageIndex != page) continue;
                        _searchPages.Clear(); // Active result has first claim on the small geometry budget.
                    }
                    _searchPages.Add(page, text);
                }
                catch (OperationCanceledException) { return; }
                catch (Exception)
                {
                    if (_disposed || generation != _searchViewGeneration) return;
                    if (_searchNavigation?.PageIndex == page && _layout is not null)
                    {
                        _searchNavigation = null;
                        MoveTo(_layout.ScrollTarget(page, ViewHeight), _left);
                    }
                    continue; // Unavailable geometry does not affect raster/selection.
                }
            }
            NavigateSearchIfReady();
            if (_layout?.ContainsPage(page) == true) Invalidate(PageRectangle(page));
        }
    }

    private void NavigateSearchIfReady()
    {
        if (_layout is null || _searchNavigation is not SearchMatch match || !_searchPages.TryGetValue(match.PageIndex, out PdfTextPage? text)) return;
        _searchNavigation = null; // Clear before MoveTo triggers viewport demand again.
        var page = _layout[match.PageIndex];
        for (int index = match.Start; index < match.End && index < text.Count; index++)
        {
            TextBounds box = text[index].Bounds;
            if (!box.HasArea) continue;
            TextPoint center = _layout.RotationForPage(match.PageIndex).ToDisplay(new((box.Left + box.Right) / 2, (box.Top + box.Bottom) / 2));
            double x = _layout.Left(match.PageIndex, ViewWidth) + center.X * page.Width;
            double y = page.Top + center.Y * page.Height;
            bool comfortable = x >= _left + ViewWidth * .15 && x <= _left + ViewWidth * .85
                && y >= _top + ViewHeight * .2 && y <= _top + ViewHeight * .8;
            if (!comfortable) MoveTo(y - ViewHeight / 2, x - ViewWidth / 2);
            return;
        }
        MoveTo(_layout.ScrollTarget(match.PageIndex, ViewHeight), _left);
    }

    private void PaintSearch(Graphics graphics, int page, Rectangle display)
    {
        if (_search is null || !_searchPages.TryGetValue(page, out PdfTextPage? text)) return;
        (int start, int end) = _search.MatchesOnPage(page);
        for (int result = start; result < end; result++)
        {
            SearchMatch match = _search[result];
            Brush brush = result == _search.ActiveIndex ? _activeMatchBrush : _matchBrush;
            for (int index = match.Start; index < match.End && index < text.Count; index++)
            {
                TextBounds box = text[index].Bounds;
                if (!box.HasArea || text[index].Unicode is 0 or 10 or 13) continue;
                TextBounds mapped = TextCoordinateTransform.ToDisplay(box, display.Left, display.Top, display.Width, display.Height, _layout!.RotationForPage(page));
                graphics.FillRectangle(brush, (float)mapped.Left, (float)mapped.Top,
                    (float)(mapped.Right - mapped.Left), (float)(mapped.Bottom - mapped.Top));
            }
        }
    }

    private void DisposeSearch()
    {
        _searchViewGeneration++;
        _renderer.CancelSearchGeometry();
        _searchPages.Clear();
        _search = null;
        _matchBrush.Dispose();
        _activeMatchBrush.Dispose();
    }
}
