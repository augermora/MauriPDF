using MauriPDF.Core.Search;
using MauriPDF.Rendering;

namespace MauriPDF.App;

internal sealed class DocumentSearchBar : ToolStrip
{
    private readonly PdfViewerRenderer _renderer;
    private readonly ContinuousPdfView _view;
    private readonly ToolStripTextBox _query = new() { AutoSize = false, Width = 220, MaxLength = SearchablePageText.MaximumQueryLength, AccessibleName = "Find text" };
    private readonly ToolStripButton _previous = new("Previous");
    private readonly ToolStripButton _next = new("Next");
    private readonly ToolStripLabel _status = new();
    private readonly System.Windows.Forms.Timer _debounce = new() { Interval = 200 };
    private readonly System.Windows.Forms.Timer _progress = new() { Interval = 100 };
    private DocumentSearchState? _state;
    private int _pageCount;
    private long _intent;
    private long _workerGeneration;
    private bool _disposed;
    private bool _activateFirstResult = true;

    public DocumentSearchBar(PdfViewerRenderer renderer, ContinuousPdfView view)
    {
        _renderer = renderer;
        _view = view;
        GripStyle = ToolStripGripStyle.Hidden;
        Visible = false;
        ToolStripButton close = new("Close") { ToolTipText = "Close search (Esc)" };
        Items.AddRange([new ToolStripLabel("Find:"), _query, _previous, _next, _status, close]);
        _query.TextChanged += (_, _) => QueryChanged();
        _query.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; Navigate(e.Shift); }
            if (e.KeyCode == Keys.Escape) { e.SuppressKeyPress = true; CloseSearch(); }
        };
        _previous.Click += (_, _) => Navigate(true);
        _next.Click += (_, _) => Navigate(false);
        close.Click += (_, _) => CloseSearch();
        _debounce.Tick += (_, _) => { _debounce.Stop(); StartSearch(); };
        _progress.Tick += (_, _) => Publish();
    }

    public bool QueryFocused => _query.Focused;

    public void OpenSearch()
    {
        Visible = true;
        _query.Focus();
        _query.SelectAll();
        if (_state is null && _query.TextLength > 0) QueryChanged();
    }

    public void SetDocument(int pageCount)
    {
        CloseSearch();
        _query.Clear();
        _pageCount = pageCount;
    }

    public void PageSequenceChanged(int pageCount)
    {
        _pageCount = pageCount;
        QueryChanged(activateFirstResult: false); // Reindex without stealing the stable current page after an edit.
    }

    public void CloseSearch()
    {
        CancelCurrent();
        Visible = false;
        if (!_view.IsDisposed) _view.Focus();
    }

    private void CancelCurrent()
    {
        _intent++;
        _debounce.Stop();
        _progress.Stop();
        _state?.Cancel();
        _state = null;
        _renderer.CancelSearch();
        _view.SetSearch(null, 0);
        _status.Text = string.Empty;
        _next.Enabled = _previous.Enabled = false;
    }

    private void QueryChanged(bool activateFirstResult = true)
    {
        if (_disposed) return;
        _activateFirstResult = activateFirstResult;
        CancelCurrent(); // Invalidate on the keystroke, not at the end of debounce.
        if (Visible && _pageCount > 0 && _query.TextLength > 0) _debounce.Start();
    }

    private async void StartSearch()
    {
        if (_disposed || !Visible || _pageCount == 0 || _query.TextLength == 0) return;
        long intent = _intent;
        bool activateFirstResult = _activateFirstResult;
        DocumentSearchState state = new(_query.Text, _pageCount);
        _state = state;
        _workerGeneration = _renderer.BeginSearch();
        long generation = _workerGeneration;
        _view.SetSearch(state, generation);
        _progress.Start();
        Publish();
        try
        {
            while (!state.Complete && !state.Cancelled)
            {
                int page = state.PagesScanned;
                bool failed = false;
                PageSearchResult result;
                try
                {
                    result = await _renderer.SearchPageAsync(generation, _view.SourcePageIndex(page), state.Query, DocumentSearchState.MaximumResults - state.Count);
                    // Worker results are source-indexed; the search index and ordering are logical.
                    result = new(result.Matches.Select(match => match with { PageIndex = page }).ToArray(), result.Truncated);
                }
                catch (OperationCanceledException) { return; }
                catch (Exception)
                {
                    // Text-limit/invalid text pages do not prevent searching the rest of the document.
                    result = new(Array.Empty<SearchMatch>(), false);
                    failed = true;
                }
                if (_disposed || intent != _intent) return;
                bool first = state.Count == 0;
                state.Append(page, result, failed);
                if (first && state.Count > 0) { Publish(); if (activateFirstResult) _view.ActivateSearchMatch(); }
            }
        }
        finally
        {
            if (!_disposed && intent == _intent) { _progress.Stop(); Publish(); }
        }
    }

    public void Navigate(bool previous)
    {
        if (_state is null || _state.Count == 0) return;
        _state.Move(previous);
        Publish();
        _view.ActivateSearchMatch();
    }

    private void Publish()
    {
        if (_state is null) return;
        _status.Text = !_state.Complete
            ? $"Searching... {_state.Count} found ({_state.PagesScanned}/{_state.TotalPages} pages)"
            : _state.Count == 0 ? "No results" : $"{_state.ActiveIndex + 1} of {_state.Count}";
        if (_state.Truncated) _status.Text += " — result limit reached; truncated";
        if (_state.FailedPages > 0) _status.Text += $" — {_state.FailedPages} pages unavailable";
        _next.Enabled = _previous.Enabled = _state.Count > 0;
        _view.SetSearch(_state, _workerGeneration);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !_disposed)
        {
            _disposed = true;
            CancelCurrent();
            _debounce.Dispose();
            _progress.Dispose();
        }
        base.Dispose(disposing);
    }
}
