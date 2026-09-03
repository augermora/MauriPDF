using MauriPDF.Core.Rendering;
using MauriPDF.Core.Viewing;

namespace MauriPDF.Rendering;

/// <summary>Exclusive engine/session owner: one active operation and one pending slot per priority class.</summary>
public sealed class PdfViewerRenderer : IAsyncDisposable
{
    private readonly object _gate = new();
    private readonly Func<IPdfRenderer> _createEngine;
    private readonly RenderCache _cache;
    public const long DefaultThumbnailBudgetBytes = 8L * 1024 * 1024;
    private readonly RenderCache _thumbnailCache;
    private readonly Task _worker;
    private Request? _pending;
    private Request? _pendingThumbnail;
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

    public Task<int> OpenAsync(string path)
    {
        Request request = new() { Path = path };
        Submit(request);
        return request.Opened.Task;
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
            _version++;
            CancelThumbnailsLocked();
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
            request.Version = ++_version;
            if (request.State is null)
            {
                _documentGeneration++;
                CancelThumbnailsLocked();
            }
            _pending?.Cancel();
            if (_active?.ThumbnailPage is null) _active?.Cancel();
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
                    while (_pending is null && _pendingThumbnail is null && !_stopping)
                    {
                        Monitor.Wait(_gate);
                    }

                    if (_stopping)
                    {
                        return;
                    }

                    // Explicit page/open requests always precede pending thumbnails.
                    request = _pending ?? _pendingThumbnail!;
                    if (_pending is not null) _pending = null;
                    else _pendingThumbnail = null;
                    _active = request;
                }

                ViewerRenderResult? result = null;
                try
                {
                    engine ??= _createEngine();
                    if (request.State is null && request.ThumbnailPage is null)
                    {
                        _cache.Clear();
                        _thumbnailCache.Clear();
                        session?.Dispose();
                        session = null;
                        _documentId++;
                        session = engine.Open(request.Path!);
                        bool current;
                        lock (_gate)
                        {
                            current = IsCurrent(request);
                            if (current)
                            {
                                request.Opened.TrySetResult(session.PageCount);
                            }
                        }

                        if (!current)
                        {
                            // Never hold the submission gate during native disposal.
                            session.Dispose();
                            session = null;
                        }
                    }
                    else
                    {
                        if (session is null)
                        {
                            throw new InvalidOperationException("No document is open.");
                        }

                        int pageIndex = request.ThumbnailPage ?? request.State!.PageIndex;
                        PdfPageSize pageSize = session.GetPageSize(pageIndex);
                        RenderSize size = request.ThumbnailPage.HasValue
                            ? ThumbnailSizeCalculator.Calculate(pageSize)
                            : RenderSizeCalculator.Calculate(pageSize, request.State!, request.Width, request.Height, request.ScrollbarWidth);
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

    private bool IsCurrent(Request request) => !_stopping && (request.ThumbnailPage.HasValue
        ? request.ThumbnailVersion == _thumbnailVersion && request.DocumentGeneration == _documentGeneration
        : request.Version == _version);

    private sealed class Request
    {
        public long Version { get; set; }
        public int? ThumbnailPage { get; init; }
        public long ThumbnailVersion { get; init; }
        public long DocumentGeneration { get; init; }
        public string? Path { get; init; }
        public ViewerState? State { get; init; }
        public int Width { get; init; }
        public int Height { get; init; }
        public int ScrollbarWidth { get; init; }
        public TaskCompletionSource<int> Opened { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<ViewerRenderResult> Rendered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public void Cancel()
        {
            if (State is null && ThumbnailPage is null) Opened.TrySetCanceled();
            else Rendered.TrySetCanceled();
        }
        public void Fail(Exception exception)
        {
            if (State is null && ThumbnailPage is null) Opened.TrySetException(exception);
            else Rendered.TrySetException(exception);
        }
    }
}
