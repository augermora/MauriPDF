using MauriPDF.Core.Rendering;
using MauriPDF.Core.Viewing;
using MauriPDF.Core.Text;

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
    private readonly TextPageCache _textCache = new();
    private long _textVersion;
    private Request? _active;
    private bool _stopping;
    private long _version;
    private long _documentId;
    private long _documentGeneration;
    private long _thumbnailVersion;

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
    public Task<IReadOnlyList<PdfPageSize>> OpenDocumentAsync(string path)
    {
        Request request = new() { Path = path };
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
                Request request = new() { Version = _version, Target = targets[index] };
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
        if (_active?.ThumbnailPage is null && _active?.TextPageIndex is null) _active?.Cancel();
    }

    /// <summary>One replaceable text slot. Immutable results may safely be shared by duplicate requests.</summary>
    public Task<PdfTextPage> ExtractTextAsync(int pageIndex)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(pageIndex);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_stopping, this);
            if (_active?.TextPageIndex == pageIndex && IsCurrent(_active)) return _active.Text.Task;
            if (_pendingText?.TextPageIndex == pageIndex) return _pendingText.Text.Task;
            CancelTextLocked();
            Request request = new() { TextPageIndex = pageIndex, TextVersion = _textVersion, DocumentGeneration = _documentGeneration };
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
    public bool TryRequestThumbnail(int pageIndex, out Task<ViewerRenderResult>? task)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(pageIndex);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_stopping, this);
            if ((_active?.ThumbnailPage == pageIndex && IsCurrent(_active) && !_active.Rendered.Task.IsCompleted)
                || _pendingThumbnail?.ThumbnailPage == pageIndex)
            {
                System.Diagnostics.Debug.WriteLine($"Thumbnail {pageIndex + 1}: duplicate skipped");
                task = null;
                return false;
            }

            _pendingThumbnail?.Cancel();
            Request request = new()
            {
                ThumbnailPage = pageIndex,
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
                CancelThumbnailsLocked();
                CancelTextLocked();
            }
            _pending?.Cancel();
            if (_active?.ThumbnailPage is null && _active?.TextPageIndex is null) _active?.Cancel();
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
                    while (_pending is null && _visiblePages.Count == 0 && _pendingText is null && _pendingThumbnail is null && !_stopping)
                    {
                        Monitor.Wait(_gate);
                    }

                    if (_stopping)
                    {
                        return;
                    }

                    // Explicit page/open requests always precede pending thumbnails.
                    request = _pending ?? (_visiblePages.Count > 0 ? _visiblePages.Dequeue() : _pendingText ?? _pendingThumbnail!);
                    if (_pending is not null) _pending = null;
                    else if (request.ThumbnailPage.HasValue) _pendingThumbnail = null;
                    else if (request.TextPageIndex.HasValue) _pendingText = null;
                    _active = request;
                }

                ViewerRenderResult? result = null;
                try
                {
                    engine ??= _createEngine();
                    if (request.IsOpen)
                    {
                        _cache.Clear();
                        _thumbnailCache.Clear();
                        _textCache.Clear();
                        session?.Dispose();
                        session = null;
                        _documentId++;
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
                    else if (request.TextPageIndex is int textPageIndex)
                    {
                        if (session is null) throw new InvalidOperationException("No document is open.");
                        PdfTextPage? text = _textCache.Get(_documentId, textPageIndex);
                        text ??= session.ExtractText(textPageIndex);
                        lock (_gate)
                        {
                            if (IsCurrent(request))
                            {
                                _textCache.Store(_documentId, textPageIndex, text);
                                request.Text.TrySetResult(text);
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
                        PdfPageSize pageSize = session.GetPageSize(pageIndex);
                        RenderSize size = request.ThumbnailPage.HasValue
                            ? ThumbnailSizeCalculator.Calculate(pageSize)
                            : request.Target?.Size ?? RenderSizeCalculator.Calculate(pageSize, request.State!, request.Width, request.Height, request.ScrollbarWidth);
                        RenderCache cache = request.ThumbnailPage.HasValue ? _thumbnailCache : _cache;
                        RenderCacheKey key = new(_documentId, pageIndex, size.Width, size.Height);
                        lock (_gate)
                        {
                            if (!IsCurrent(request)) continue;
                        }
                        RenderedPage? pixels = cache.GetCopy(key);
                        bool hit = pixels is not null;
                        if (pixels is null)
                        {
                            pixels = session.RenderPage(pageIndex, size.Width, size.Height);
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

    private bool IsCurrent(Request request) => !_stopping && (request.TextPageIndex.HasValue
        ? request.TextVersion == _textVersion && request.DocumentGeneration == _documentGeneration
        : request.ThumbnailPage.HasValue
        ? request.ThumbnailVersion == _thumbnailVersion && request.DocumentGeneration == _documentGeneration
        : request.Version == _version);

    private sealed class Request
    {
        public long Version { get; set; }
        public int? ThumbnailPage { get; init; }
        public int? TextPageIndex { get; init; }
        public long TextVersion { get; init; }
        public long ThumbnailVersion { get; init; }
        public long DocumentGeneration { get; init; }
        public string? Path { get; init; }
        public ViewerState? State { get; init; }
        public PageRenderTarget? Target { get; init; }
        public bool IsOpen => State is null && Target is null && ThumbnailPage is null && TextPageIndex is null;
        public int Width { get; init; }
        public int Height { get; init; }
        public int ScrollbarWidth { get; init; }
        public TaskCompletionSource<IReadOnlyList<PdfPageSize>> Opened { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<ViewerRenderResult> Rendered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<PdfTextPage> Text { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public void Cancel()
        {
            if (IsOpen) Opened.TrySetCanceled();
            else if (TextPageIndex.HasValue) Text.TrySetCanceled();
            else Rendered.TrySetCanceled();
        }
        public void Fail(Exception exception)
        {
            if (IsOpen) Opened.TrySetException(exception);
            else if (TextPageIndex.HasValue) Text.TrySetException(exception);
            else Rendered.TrySetException(exception);
        }
    }
}
