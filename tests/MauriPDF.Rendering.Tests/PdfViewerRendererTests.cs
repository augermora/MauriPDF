using MauriPDF.Core.Rendering;
using MauriPDF.Core.Viewing;
using Xunit;

namespace MauriPDF.Rendering.Tests;

public sealed class PdfViewerRendererTests
{
    [Fact]
    public async Task RepeatedTargetHitsCacheAndDocumentChangeClearsIt()
    {
        FakeEngine engine = new();
        await using PdfViewerRenderer viewer = new(() => engine);
        Assert.Equal(10, await viewer.OpenAsync("first"));
        using ViewerRenderResult first = await viewer.RenderAsync(new(10), 800, 600, 17);
        using ViewerRenderResult second = await viewer.RenderAsync(new(10), 800, 600, 17);
        Assert.False(first.FromCache);
        Assert.True(second.FromCache);
        Assert.Equal(1, engine.RenderCount);
        await viewer.OpenAsync("second");
        Assert.Equal(1, engine.ClosedDocuments);
        Assert.Equal(1, engine.Owners[0].DisposeCount);
        using ViewerRenderResult changed = await viewer.RenderAsync(new(10), 800, 600, 17);
        Assert.False(changed.FromCache);
        Assert.Equal(2, engine.RenderCount);
    }

    [Fact]
    public async Task LatestWinsAndPendingRequestsNeverExecute()
    {
        FakeEngine engine = new() { BlockFirst = true };
        await using PdfViewerRenderer viewer = new(() => engine);
        await viewer.OpenAsync("first");
        Task<ViewerRenderResult> first = viewer.RenderAsync(new(10), 800, 600, 17);
        try
        {
            await engine.Started.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            Task<ViewerRenderResult> middle = viewer.RenderAsync(new ViewerState(10).GoToPage(2), 800, 600, 17);
            Task<ViewerRenderResult> latest = viewer.RenderAsync(new ViewerState(10).GoToPage(7), 800, 600, 17);
            Assert.True(first.IsCanceled);
            Assert.True(middle.IsCanceled);
            engine.Release.Set();
            using ViewerRenderResult result = await latest;
            Assert.Equal(6, result.Pixels.Pixels.Span[0]);
            Assert.Equal(2, engine.RenderCount);
            Assert.Equal(1, engine.Owners[0].DisposeCount);
        }
        finally
        {
            engine.Release.Set();
        }
    }

    [Fact]
    public async Task DocumentReplacementInvalidatesActiveRender()
    {
        FakeEngine engine = new() { BlockFirst = true };
        await using PdfViewerRenderer viewer = new(() => engine);
        await viewer.OpenAsync("first");
        Task<ViewerRenderResult> old = viewer.RenderAsync(new(10), 800, 600, 17);
        try
        {
            await engine.Started.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            Task<int> opened = viewer.OpenAsync("second");
            Assert.True(old.IsCanceled);
            Assert.Equal(0, engine.ClosedDocuments);
            engine.Release.Set();
            await opened;
            Assert.Equal(1, engine.ClosedDocuments);
            Assert.Equal(1, engine.Owners[0].DisposeCount);
            using ViewerRenderResult next = await viewer.RenderAsync(new(10), 800, 600, 17);
            Assert.False(next.FromCache);
        }
        finally
        {
            engine.Release.Set();
        }
    }

    [Fact]
    public async Task ShutdownCancelsThenWaitsForNativeWorkBeforeDisposal()
    {
        FakeEngine engine = new() { BlockFirst = true };
        PdfViewerRenderer viewer = new(() => engine);
        await viewer.OpenAsync("first");
        Task<ViewerRenderResult> pending = viewer.RenderAsync(new(10), 800, 600, 17);
        try
        {
            await engine.Started.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            Task shutdown = viewer.DisposeAsync().AsTask();
            Assert.True(pending.IsCanceled);
            Assert.False(shutdown.IsCompleted);
            Assert.Equal(0, engine.ClosedDocuments);
        }
        finally
        {
            engine.Release.Set();
            await viewer.DisposeAsync();
        }

        Assert.True(engine.Disposed);
        Assert.Equal(1, engine.ClosedDocuments);
        Assert.Equal(1, engine.Owners[0].DisposeCount);
    }

    [Fact]
    public async Task RenderFailureDoesNotPreventNextRequest()
    {
        FakeEngine engine = new() { FailFirst = true };
        await using PdfViewerRenderer viewer = new(() => engine);
        await viewer.OpenAsync("first");
        await Assert.ThrowsAsync<InvalidDataException>(() => viewer.RenderAsync(new(10), 800, 600, 17));
        using ViewerRenderResult next = await viewer.RenderAsync(new ViewerState(10).GoToPage(2), 800, 600, 17);
        Assert.Equal(1, next.Pixels.Pixels.Span[0]);
    }

    private sealed class FakeEngine : IPdfRenderer
    {
        public bool BlockFirst { get; init; }
        public bool FailFirst { get; init; }
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ManualResetEventSlim Release { get; } = new();
        public List<TrackingOwner> Owners { get; } = [];
        public int RenderCount { get; private set; }
        public int ClosedDocuments { get; private set; }
        public bool Disposed { get; private set; }
        public IPdfRenderSession Open(string filePath) => new Session(this);
        public void Dispose()
        {
            Disposed = true;
            Release.Dispose();
        }

        private sealed class Session(FakeEngine engine) : IPdfRenderSession
        {
            public int PageCount => 10;
            public PdfPageSize GetPageSize(int pageIndex) => new(72, 72);
            public RenderedPage RenderPage(int pageIndex, int pixelWidth, int pixelHeight)
            {
                engine.RenderCount++;
                if (engine.RenderCount == 1)
                {
                    engine.Started.TrySetResult();
                    if (engine.BlockFirst) engine.Release.Wait();
                    if (engine.FailFirst) throw new InvalidDataException("Test render failure.");
                }

                TrackingOwner owner = new(pixelWidth * pixelHeight * 4);
                owner.Memory.Span.Fill((byte)pageIndex);
                engine.Owners.Add(owner);
                return new RenderedPage(pixelWidth, pixelHeight, pixelWidth * 4, RenderedPixelFormat.Bgra32, owner);
            }
            public void Dispose() => engine.ClosedDocuments++;
        }
    }
}
