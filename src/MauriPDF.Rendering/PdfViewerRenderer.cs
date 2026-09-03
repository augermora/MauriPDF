using MauriPDF.Core.Rendering;
using MauriPDF.Core.Viewing;

namespace MauriPDF.Rendering;

/// <summary>Exclusive owner of an engine and session, with one active and one replaceable pending operation.</summary>
public sealed class PdfViewerRenderer : IAsyncDisposable
{
    private readonly object _gate = new();
    private readonly Func<IPdfRenderer> _createEngine;
    private readonly RenderCache _cache;
    private readonly Task _worker;
    private Request? _pending;
    private Request? _active;
    private bool _stopping;
    private long _version;
    private long _documentId;

    public PdfViewerRenderer(Func<IPdfRenderer> createEngine, long cacheBudgetBytes = RenderCache.DefaultBudgetBytes)
    {
        _createEngine = createEngine;
        _cache = new RenderCache(cacheBudgetBytes);
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

    public ValueTask DisposeAsync()
    {
        lock (_gate)
        {
            _stopping = true;
            _version++;
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
            _pending?.Cancel();
            _active?.Cancel();
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
                    while (_pending is null && !_stopping)
                    {
                        Monitor.Wait(_gate);
                    }

                    if (_stopping)
                    {
                        return;
                    }

                    request = _pending!;
                    _pending = null;
                    _active = request;
                }

                ViewerRenderResult? result = null;
                try
                {
                    engine ??= _createEngine();
                    if (request.State is null)
                    {
                        _cache.Clear();
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

                        RenderSize size = RenderSizeCalculator.Calculate(session.GetPageSize(request.State.PageIndex),
                            request.State, request.Width, request.Height, request.ScrollbarWidth);
                        RenderCacheKey key = new(_documentId, request.State.PageIndex, size.Width, size.Height);
                        lock (_gate)
                        {
                            if (!IsCurrent(request)) continue;
                        }
                        RenderedPage? pixels = _cache.GetCopy(key);
                        bool hit = pixels is not null;
                        if (pixels is null)
                        {
                            pixels = session.RenderPage(request.State.PageIndex, size.Width, size.Height);
                            // Native work cannot be interrupted. Never cache or publish obsolete pixels.
                            lock (_gate)
                            {
                                if (!IsCurrent(request))
                                {
                                    pixels.Dispose();
                                    continue;
                                }
                            }

                            if (pixels.AllocatedBytes <= _cache.BudgetBytes)
                            {
                                RenderedPage cached = pixels;
                                try
                                {
                                    pixels = RenderCache.Copy(cached);
                                    _cache.Store(key, cached);
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

    private bool IsCurrent(Request request) => !_stopping && request.Version == _version;

    private sealed class Request
    {
        public long Version { get; set; }
        public string? Path { get; init; }
        public ViewerState? State { get; init; }
        public int Width { get; init; }
        public int Height { get; init; }
        public int ScrollbarWidth { get; init; }
        public TaskCompletionSource<int> Opened { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<ViewerRenderResult> Rendered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public void Cancel()
        {
            if (State is null) Opened.TrySetCanceled();
            else Rendered.TrySetCanceled();
        }
        public void Fail(Exception exception)
        {
            if (State is null) Opened.TrySetException(exception);
            else Rendered.TrySetException(exception);
        }
    }
}
