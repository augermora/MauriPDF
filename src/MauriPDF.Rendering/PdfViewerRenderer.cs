using MauriPDF.Core.Rendering;
using MauriPDF.Core.Viewing;
using MauriPDF.Core.Text;
using MauriPDF.Core.Search;
using MauriPDF.Core.Outline;
using MauriPDF.Core.Documents;

namespace MauriPDF.Rendering;

/// <summary>Exclusive engine/session owner: one active operation, a bounded foreground batch, and replaceable text/thumbnail slots.</summary>
public sealed class PdfViewerRenderer : IAsyncDisposable
{
    private readonly object _gate = new();
    private readonly Func<IPdfRenderer> _createEngine;
    private readonly RenderCache _cache;
    public const long DefaultThumbnailBudgetBytes = 8L * 1024 * 1024;
    private readonly RenderCache _thumbnailCache;
    private readonly Task _worker;
    private Request? _pending;
    private readonly Queue<Request> _visiblePages = new();
    public const int MaximumVisiblePages = 32;
    private Request? _pendingThumbnail;
    private Request? _pendingText;
    private Request? _pendingOutline;
    private long _outlineVersion;
    private readonly TextPageCache _textCache = new();
    private long _textVersion;
    private Request? _pendingSearchGeometry;
    private Request? _pendingSearchScan;
    private long _searchGeneration;
    private long _geometryVersion;
    private long _scanVersion;
    private Request? _active;
    private bool _stopping;
    private long _version;
    private long _documentId;
    private long _documentGeneration;
    private long _thumbnailVersion;
    private Request? _pendingSource;
    private readonly Dictionary<Guid, (IPdfRenderSession Session, long CacheId)> _sources = [];
    private Guid _mainSourceId;
    private long _nextCacheId;

    public async Task<PdfSourceDocument> PrepareSourceAsync(string path, Guid sourceId)
    {
        if (sourceId == Guid.Empty) throw new ArgumentException("A source identity is required.", nameof(sourceId));
        path = Path.GetFullPath(path);
        Request request = new() { IsImport = true, SourceId = sourceId, Path = path };
        SubmitSource(request);
        return new(sourceId, path, await request.Opened.Task.ConfigureAwait(false));
    }

    public async Task ReleaseSourceAsync(Guid sourceId)
    {
        Request request = new() { IsRelease = true, SourceId = sourceId };
        SubmitSource(request);
        await request.Opened.Task.ConfigureAwait(false);
    }

    private void SubmitSource(Request request)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_stopping, this);
            if (_pendingSource is not null) throw new InvalidOperationException("A source operation is already pending.");
            request.DocumentGeneration = _documentGeneration;
            _pendingSource = request;
            Monitor.Pulse(_gate);
        }
    }

    public PdfViewerRenderer(Func<IPdfRenderer> createEngine, long cacheBudgetBytes = RenderCache.DefaultBudgetBytes,
        long thumbnailBudgetBytes = DefaultThumbnailBudgetBytes)
    {
        _createEngine = createEngine;
        _cache = new RenderCache(cacheBudgetBytes);
        _thumbnailCache = new RenderCache(thumbnailBudgetBytes);
        // A single dedicated worker owns all engine calls, including initialization and disposal.
        _worker = Task.Factory.StartNew(Run, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
    }

    public async Task<int> OpenAsync(string path) => (await OpenDocumentAsync(path).ConfigureAwait(false)).Count;

    /// <summary>Reads only page dimensions on the PDFium worker; never renders during open.</summary>
    public Task<IReadOnlyList<PdfPageSize>> OpenDocumentAsync(string path, Guid sourceId = default)
    {
        Request request = new() { Path = path, SourceId = sourceId };
        Submit(request);
        return request.Opened.Task;
    }

    /// <summary>Replaces the complete foreground intent. Each returned result has one consumer/owner.</summary>
    public IReadOnlyList<Task<ViewerRenderResult>> RenderVisible(IReadOnlyList<PageRenderTarget> targets)
    {
        if (targets.Count > MaximumVisiblePages) throw new ArgumentOutOfRangeException(nameof(targets));
        foreach (PageRenderTarget target in targets)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(target.PageIndex);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(target.Size.Width);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(target.Size.Height);
        }
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_stopping, this);
            CancelMainLocked();
            Task<ViewerRenderResult>[] tasks = new Task<ViewerRenderResult>[targets.Count];
            for (int index = 0; index < targets.Count; index++)
            {
                Request request = new() { Version = _version, Target = targets[index], SourceId = targets[index].SourceDocumentId };
                _visiblePages.Enqueue(request);
                tasks[index] = request.Rendered.Task;
            }
            Monitor.Pulse(_gate);
            return tasks;
        }
    }

    public void CancelVisible()
    {
        lock (_gate)
        {
            // Resizing/resetting an empty viewport must not cancel a document still opening.
            if (_pending?.IsOpen == true || (_active?.IsOpen == true && IsCurrent(_active))) return;
            CancelMainLocked();
        }
    }

    private void CancelMainLocked()
    {
        _version++;
        _pending?.Cancel();
        _pending = null;
        while (_visiblePages.TryDequeue(out Request? old)) old.Cancel();
        if (_active?.IsSourceOperation != true && _active?.ThumbnailPage is null && _active?.TextPageIndex is null && _active?.SearchPageIndex is null && _active?.IsOutline != true) _active?.Cancel();
    }

    /// <summary>One replaceable metadata slot, below visible rasters and interactive text work.</summary>
    public Task<PdfOutline> ExtractOutlineAsync()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_stopping, this);
            if (_active?.IsOutline == true && IsCurrent(_active)) return _active.Outline.Task;
            if (_pendingOutline is not null) return _pendingOutline.Outline.Task;
            Request request = new() { IsOutline = true, OutlineVersion = _outlineVersion, DocumentGeneration = _documentGeneration };
            _pendingOutline = request;
            Monitor.Pulse(_gate);
            return request.Outline.Task;
        }
    }

    public void CancelOutline()
    {
        lock (_gate) { CancelOutlineLocked(); }
    }

    private void CancelOutlineLocked()
    {
        _outlineVersion++;
        _pendingOutline?.Cancel();
        _pendingOutline = null;
        if (_active?.IsOutline == true) _active.Cancel();
    }

    public long BeginSearch()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_stopping, this);
            CancelSearchLocked();
            return _searchGeneration;
        }
    }

    public void CancelSearch()
    {
        lock (_gate) { CancelSearchLocked(); }
    }

    private void CancelSearchLocked()
    {
        _searchGeneration++;
        CancelSearchGeometryLocked();
        _pendingSearchScan?.Cancel();
        _pendingSearchScan = null;
        if (_active?.ScanQuery is not null) _active.Cancel();
    }

    public void CancelSearchGeometry()
    {
        lock (_gate) { CancelSearchGeometryLocked(); }
    }

    private void CancelSearchGeometryLocked()
    {
        _geometryVersion++;
        _pendingSearchGeometry?.Cancel();
        _pendingSearchGeometry = null;
        if (_active?.SearchPageIndex is not null && _active.ScanQuery is null) _active.Cancel();
    }

    public Task<PdfTextPage> SearchGeometryAsync(long generation, int pageIndex, Guid sourceId = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(pageIndex);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_stopping, this);
            if (generation != _searchGeneration) return Task.FromCanceled<PdfTextPage>(new CancellationToken(true));
            CancelSearchGeometryLocked();
            Request request = new() { SourceId = sourceId, SearchPageIndex = pageIndex, SearchGeneration = generation,
                GeometryVersion = _geometryVersion, DocumentGeneration = _documentGeneration };
            _pendingSearchGeometry = request;
            Monitor.Pulse(_gate);
            return request.Text.Task;
        }
    }

    /// <summary>One replenished scan slot; the caller awaits this page before requesting the next.</summary>
    public Task<PageSearchResult> SearchPageAsync(long generation, int pageIndex, string query, int remainingResults, Guid sourceId = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(pageIndex);
        ArgumentOutOfRangeException.ThrowIfNegative(remainingResults);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(remainingResults, DocumentSearchState.MaximumResults);
        if (query.Length > SearchablePageText.MaximumQueryLength) throw new ArgumentOutOfRangeException(nameof(query));
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_stopping, this);
            if (generation != _searchGeneration) return Task.FromCanceled<PageSearchResult>(new CancellationToken(true));
            _pendingSearchScan?.Cancel();
            if (_active?.ScanQuery is not null) _active.Cancel();
            Request request = new() { SourceId = sourceId, SearchPageIndex = pageIndex, ScanQuery = query, ResultLimit = remainingResults,
                SearchGeneration = generation, ScanVersion = ++_scanVersion, DocumentGeneration = _documentGeneration };
            _pendingSearchScan = request;
            Monitor.Pulse(_gate);
            return request.Scanned.Task;
        }
    }

    /// <summary>One replaceable text slot. Immutable results may safely be shared by duplicate requests.</summary>
    public Task<PdfTextPage> ExtractTextAsync(int pageIndex, Guid sourceId = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(pageIndex);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_stopping, this);
            if (_active?.SourceId == sourceId && _active.TextPageIndex == pageIndex && IsCurrent(_active)) return _active.Text.Task;
            if (_pendingText?.SourceId == sourceId && _pendingText.TextPageIndex == pageIndex) return _pendingText.Text.Task;
            CancelTextLocked();
            Request request = new() { SourceId = sourceId, TextPageIndex = pageIndex, TextVersion = _textVersion, DocumentGeneration = _documentGeneration };
            _pendingText = request;
            Monitor.Pulse(_gate);
            return request.Text.Task;
        }
    }

    public void CancelText()
    {
        lock (_gate) { CancelTextLocked(); }
    }

    private void CancelTextLocked()
    {
        _textVersion++;
        _pendingText?.Cancel();
        _pendingText = null;
        if (_active?.TextPageIndex is not null) _active.Cancel();
    }

    public Task<ViewerRenderResult> RenderAsync(ViewerState state, int width, int height, int scrollbarWidth)
    {
        Request request = new() { State = state, Width = width, Height = height, ScrollbarWidth = scrollbarWidth };
        Submit(request);
        return request.Rendered.Task;
    }

    /// <summary>Single-consumer thumbnail request. False means duplicate: no task/ownership is shared.</summary>
    public bool TryRequestThumbnail(int pageIndex, out Task<ViewerRenderResult>? task, VisualRotation rotation = default, Guid sourceId = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(pageIndex);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_stopping, this);
            if ((_active?.SourceId == sourceId && _active.ThumbnailPage == pageIndex && _active.Rotation == rotation && IsCurrent(_active) && !_active.Rendered.Task.IsCompleted)
                || (_pendingThumbnail?.SourceId == sourceId && _pendingThumbnail.ThumbnailPage == pageIndex && _pendingThumbnail.Rotation == rotation))
            {
                System.Diagnostics.Debug.WriteLine($"Thumbnail {pageIndex + 1}: duplicate skipped");
                task = null;
                return false;
            }

            _pendingThumbnail?.Cancel();
            Request request = new()
            {
                ThumbnailPage = pageIndex,
                SourceId = sourceId,
                Rotation = rotation,
                ThumbnailVersion = _thumbnailVersion,
                DocumentGeneration = _documentGeneration
            };
            _pendingThumbnail = request;
            task = request.Rendered.Task;
            Monitor.Pulse(_gate);
            return true;
        }
    }

    public void CancelThumbnails()
    {
        lock (_gate)
        {
            CancelThumbnailsLocked();
        }
    }

    private void CancelThumbnailsLocked()
    {
        _thumbnailVersion++;
        _pendingThumbnail?.Cancel();
        _pendingThumbnail = null;
        if (_active?.ThumbnailPage is not null) _active.Cancel();
    }

    public ValueTask DisposeAsync()
    {
        lock (_gate)
        {
            _stopping = true;
            _pendingSource?.Cancel();
            _pendingSource = null;
            CancelOutlineLocked();
            CancelSearchLocked();
            CancelMainLocked();
            CancelThumbnailsLocked();
            CancelTextLocked();
            _pending?.Cancel();
            _active?.Cancel();
            _pending = null;
            Monitor.PulseAll(_gate);
        }

        return new ValueTask(_worker);
    }

    private void Submit(Request request)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_stopping, this);
            CancelMainLocked();
            request.Version = _version;
            if (request.IsOpen)
            {
                _documentGeneration++;
                _pendingSource?.Cancel(); _pendingSource = null;
                if (_active?.IsSourceOperation == true) _active.Cancel();
                CancelOutlineLocked();
                CancelSearchLocked();
                CancelThumbnailsLocked();
                CancelTextLocked();
            }
            _pending?.Cancel();
            if (_active?.IsSourceOperation != true && _active?.ThumbnailPage is null && _active?.TextPageIndex is null && _active?.SearchPageIndex is null && _active?.IsOutline != true) _active?.Cancel();
            _pending = request;
            Monitor.Pulse(_gate);
        }
    }

    private void Run()
    {
        IPdfRenderer? engine = null;
        IPdfRenderSession? session = null;
        try
        {
            while (true)
            {
                Request request;
                lock (_gate)
                {
                    while (_pending is null && _visiblePages.Count == 0 && _pendingText is null
                        && _pendingSource is null && _pendingSearchGeometry is null && _pendingOutline is null && _pendingSearchScan is null && _pendingThumbnail is null && !_stopping)
                    {
                        Monitor.Wait(_gate);
                    }

                    if (_stopping)
                    {
                        return;
                    }

                    // Explicit page/open requests always precede pending thumbnails.
                    request = _pending ?? _pendingSource ?? (_visiblePages.Count > 0 ? _visiblePages.Dequeue()
                        : _pendingText ?? _pendingSearchGeometry ?? _pendingOutline ?? _pendingSearchScan ?? _pendingThumbnail!);
                    if (_pending is not null) _pending = null;
                    else if (request.IsSourceOperation) _pendingSource = null;
                    else if (request.ThumbnailPage.HasValue) _pendingThumbnail = null;
                    else if (request.TextPageIndex.HasValue) _pendingText = null;
                    else if (request.ScanQuery is not null) _pendingSearchScan = null;
                    else if (request.SearchPageIndex.HasValue) _pendingSearchGeometry = null;
                    else if (request.IsOutline) _pendingOutline = null;
                    _active = request;
                    if (!IsCurrent(request)) { request.Cancel(); _active = null; continue; }
                }

                ViewerRenderResult? result = null;
                try
                {
                    engine ??= _createEngine();
                    if (request.IsImport)
                    {
                        IPdfRenderSession? imported = null;
                        try
                        {
                            if (session is null) throw new InvalidOperationException("No main document is open.");
                            if (request.SourceId == _mainSourceId || _sources.ContainsKey(request.SourceId)) throw new ArgumentException("Source identity is already open.");
                            if (_sources.Count >= 63) throw new InvalidOperationException("At most 64 sources can be retained.");
                            imported = engine.Open(request.Path!);
                            if (imported.PageCount <= 0 || imported.PageCount > 250_000) throw new InvalidDataException("The source has no pages or exceeds the page metadata limit.");
                            PdfPageSize[] sizes = new PdfPageSize[imported.PageCount];
                            for (int index = 0; index < sizes.Length; index++)
                            {
                                lock (_gate) { if (!IsCurrent(request)) throw new OperationCanceledException(); }
                                sizes[index] = imported.GetPageSize(index);
                                if (!double.IsFinite(sizes[index].WidthPoints) || !double.IsFinite(sizes[index].HeightPoints)
                                    || sizes[index].WidthPoints <= 0 || sizes[index].HeightPoints <= 0)
                                    throw new InvalidDataException("The source has invalid page dimensions.");
                            }
                            lock (_gate)
                            {
                                if (IsCurrent(request))
                                {
                                    _sources.Add(request.SourceId, (imported, ++_nextCacheId));
                                    imported = null;
                                    request.Opened.TrySetResult(Array.AsReadOnly(sizes));
                                }
                            }
                        }
                        finally { imported?.Dispose(); }
                    }
                    else if (request.IsRelease)
                    {
                        if (_sources.Remove(request.SourceId, out var removed)) removed.Session.Dispose();
                        request.Opened.TrySetResult(Array.Empty<PdfPageSize>());
                    }
                    else if (request.IsOpen)
                    {
                        foreach (var source in _sources.Values) source.Session.Dispose();
                        _sources.Clear();
                        _cache.Clear();
                        _thumbnailCache.Clear();
                        _textCache.Clear();
                        session?.Dispose();
                        session = null;
                        _documentId = ++_nextCacheId;
                        _mainSourceId = request.SourceId;
                        session = engine.Open(request.Path!);
                        PdfPageSize[] sizes = new PdfPageSize[session.PageCount];
                        for (int index = 0; index < sizes.Length; index++)
                        {
                            lock (_gate) { if (!IsCurrent(request)) break; }
                            sizes[index] = session.GetPageSize(index);
                        }
                        bool current;
                        lock (_gate)
                        {
                            current = IsCurrent(request);
                            if (current)
                            {
                                request.Opened.TrySetResult(Array.AsReadOnly(sizes));
                            }
                        }

                        if (!current)
                        {
                            // Never hold the submission gate during native disposal.
                            session.Dispose();
                            session = null;
                        }
                    }
                    else if (request.IsOutline)
                    {
                        if (session is null) throw new InvalidOperationException("No document is open.");
                        PdfOutline outline = session.ExtractOutline();
                        lock (_gate)
                        {
                            if (IsCurrent(request)) request.Outline.TrySetResult(outline);
                        }
                    }
                    else if ((request.TextPageIndex ?? request.SearchPageIndex) is int textPageIndex)
                    {
                        if (session is null) throw new InvalidOperationException("No document is open.");
                        var source = ResolveSource(request.SourceId, session);
                        PdfTextPage? text = _textCache.Get(source.CacheId, textPageIndex);
                        text ??= source.Session.ExtractText(textPageIndex);
                        PageSearchResult? matches = request.ScanQuery is null ? null
                            : new SearchablePageText(text).Find(textPageIndex, request.ScanQuery, request.ResultLimit);
                        lock (_gate)
                        {
                            if (IsCurrent(request))
                            {
                                _textCache.Store(source.CacheId, textPageIndex, text);
                                if (matches is not null) request.Scanned.TrySetResult(matches);
                                else request.Text.TrySetResult(text);
                            }
                        }
                    }
                    else
                    {
                        if (session is null)
                        {
                            throw new InvalidOperationException("No document is open.");
                        }

                        int pageIndex = request.ThumbnailPage ?? request.Target?.PageIndex ?? request.State!.PageIndex;
                        var source = ResolveSource(request.SourceId, session);
                        PdfPageSize pageSize = source.Session.GetPageSize(pageIndex);
                        VisualRotation rotation = request.ThumbnailPage.HasValue ? request.Rotation : request.Target?.Rotation ?? request.State!.Rotation;
                        RenderSize size = request.ThumbnailPage.HasValue
                            ? ThumbnailSizeCalculator.Calculate(rotation.EffectiveSize(pageSize))
                            : request.Target?.Size ?? RenderSizeCalculator.Calculate(pageSize, request.State!, request.Width, request.Height, request.ScrollbarWidth);
                        RenderCache cache = request.ThumbnailPage.HasValue ? _thumbnailCache : _cache;
                        RenderCacheKey key = new(source.CacheId, pageIndex, size.Width, size.Height, rotation);
                        lock (_gate)
                        {
                            if (!IsCurrent(request)) continue;
                        }
                        RenderedPage? pixels = cache.GetCopy(key);
                        bool hit = pixels is not null;
                        if (pixels is null)
                        {
                            pixels = source.Session.RenderPage(pageIndex, size.Width, size.Height, rotation);
                            // Native work cannot be interrupted. Never cache or publish obsolete pixels.
                            lock (_gate)
                            {
                                if (!IsCurrent(request))
                                {
                                    pixels.Dispose();
                                    continue;
                                }
                            }

                            if (pixels.AllocatedBytes <= cache.BudgetBytes)
                            {
                                RenderedPage cached = pixels;
                                try
                                {
                                    pixels = RenderCache.Copy(cached);
                                    cache.Store(key, cached);
                                }
                                catch
                                {
                                    cached.Dispose();
                                    pixels.Dispose();
                                    throw;
                                }
                            }
                        }

                        result = new ViewerRenderResult(pixels, hit);
                        if (request.ThumbnailPage.HasValue)
                        {
                            System.Diagnostics.Debug.WriteLine($"Thumbnail {pageIndex + 1}: {(hit ? "cache" : "fresh render")}");
                        }
                        lock (_gate)
                        {
                            if (IsCurrent(request) && request.Rendered.TrySetResult(result))
                            {
                                result = null; // Transfer ownership to the awaiting caller.
                            }
                        }
                    }
                }
                catch (Exception exception)
                {
                    if (request.IsOpen)
                    {
                        session?.Dispose();
                        session = null;
                    }
                    lock (_gate)
                    {
                        if (IsCurrent(request))
                        {
                            request.Fail(exception);
                        }
                    }
                }
                finally
                {
                    result?.Dispose();
                    lock (_gate)
                    {
                        _active = null;
                    }
                }
            }
        }
        finally
        {
            foreach (var source in _sources.Values) source.Session.Dispose();
            _sources.Clear();
            _cache.Dispose();
            _thumbnailCache.Dispose();
            _textCache.Clear();
            try
            {
                session?.Dispose();
            }
            finally
            {
                engine?.Dispose();
            }
        }
    }

    private (IPdfRenderSession Session, long CacheId) ResolveSource(Guid sourceId, IPdfRenderSession main) =>
        sourceId == Guid.Empty || sourceId == _mainSourceId ? (main, _documentId)
        : _sources.TryGetValue(sourceId, out var source) ? source : throw new InvalidOperationException("The requested source is no longer registered.");

    private bool IsCurrent(Request request) => !_stopping && (request.IsSourceOperation
        ? request.DocumentGeneration == _documentGeneration
        : request.IsOutline
        ? request.OutlineVersion == _outlineVersion && request.DocumentGeneration == _documentGeneration
        : request.SearchPageIndex.HasValue
        ? request.SearchGeneration == _searchGeneration && request.DocumentGeneration == _documentGeneration
            && (request.ScanQuery is null ? request.GeometryVersion == _geometryVersion : request.ScanVersion == _scanVersion)
        : request.TextPageIndex.HasValue
        ? request.TextVersion == _textVersion && request.DocumentGeneration == _documentGeneration
        : request.ThumbnailPage.HasValue
        ? request.ThumbnailVersion == _thumbnailVersion && request.DocumentGeneration == _documentGeneration
        : request.Version == _version);

    private sealed class Request
    {
        public Guid SourceId { get; init; }
        public bool IsImport { get; init; }
        public bool IsRelease { get; init; }
        public bool IsSourceOperation => IsImport || IsRelease;
        public long Version { get; set; }
        public VisualRotation Rotation { get; init; }
        public bool IsOutline { get; init; }
        public long OutlineVersion { get; init; }
        public int? ThumbnailPage { get; init; }
        public int? TextPageIndex { get; init; }
        public int? SearchPageIndex { get; init; }
        public string? ScanQuery { get; init; }
        public int ResultLimit { get; init; }
        public long SearchGeneration { get; init; }
        public long GeometryVersion { get; init; }
        public long ScanVersion { get; init; }
        public long TextVersion { get; init; }
        public long ThumbnailVersion { get; init; }
        public long DocumentGeneration { get; set; }
        public string? Path { get; init; }
        public ViewerState? State { get; init; }
        public PageRenderTarget? Target { get; init; }
        public bool IsOpen => !IsSourceOperation && !IsOutline && State is null && Target is null && ThumbnailPage is null && TextPageIndex is null && SearchPageIndex is null;
        public int Width { get; init; }
        public int Height { get; init; }
        public int ScrollbarWidth { get; init; }
        public TaskCompletionSource<IReadOnlyList<PdfPageSize>> Opened { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<ViewerRenderResult> Rendered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<PdfTextPage> Text { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<PageSearchResult> Scanned { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<PdfOutline> Outline { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public void Cancel()
        {
            if (IsOpen || IsSourceOperation) Opened.TrySetCanceled();
            else if (IsOutline) Outline.TrySetCanceled();
            else if (ScanQuery is not null) Scanned.TrySetCanceled();
            else if (TextPageIndex.HasValue || SearchPageIndex.HasValue) Text.TrySetCanceled();
            else Rendered.TrySetCanceled();
        }
        public void Fail(Exception exception)
        {
            if (IsOpen || IsSourceOperation) Opened.TrySetException(exception);
            else if (IsOutline) Outline.TrySetException(exception);
            else if (ScanQuery is not null) Scanned.TrySetException(exception);
            else if (TextPageIndex.HasValue || SearchPageIndex.HasValue) Text.TrySetException(exception);
            else Rendered.TrySetException(exception);
        }
    }
}
