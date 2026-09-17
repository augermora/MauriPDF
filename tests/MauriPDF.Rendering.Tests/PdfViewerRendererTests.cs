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

    [Fact]
    public async Task ThumbnailCacheIsSeparateFromMainCache()
    {
        FakeEngine engine = new();
        await using PdfViewerRenderer viewer = new(() => engine);
        await viewer.OpenAsync("first");
        Assert.Equal(0, engine.RenderCount); // Opening never eagerly renders thumbnails.
        using ViewerRenderResult first = await Thumbnail(viewer, 0);
        using ViewerRenderResult hit = await Thumbnail(viewer, 0);
        Assert.False(first.FromCache);
        Assert.True(hit.FromCache);
        Assert.Equal(144, first.Pixels.Width);
        Assert.Equal(144, first.Pixels.Height);
        // Even an identical target/page belongs to a separate resource class/cache.
        using ViewerRenderResult main = await viewer.RenderAsync(new ViewerState(10).SetZoom(150), 800, 600, 17);
        Assert.False(main.FromCache);
        using ViewerRenderResult stillCached = await Thumbnail(viewer, 0);
        Assert.True(stillCached.FromCache);
        Assert.Equal(2, engine.RenderCount);
    }

    [Fact]
    public async Task ThumbnailBudgetEvictsOldBuffers()
    {
        FakeEngine engine = new();
        await using PdfViewerRenderer viewer = new(() => engine, thumbnailBudgetBytes: 144 * 144 * 4);
        await viewer.OpenAsync("first");
        using ViewerRenderResult first = await Thumbnail(viewer, 0);
        using ViewerRenderResult second = await Thumbnail(viewer, 1);
        Assert.Equal(1, engine.Owners[0].DisposeCount);
        using ViewerRenderResult evicted = await Thumbnail(viewer, 0);
        Assert.False(evicted.FromCache);
        Assert.Equal(3, engine.RenderCount);
    }

    [Fact]
    public async Task ForegroundRunsBeforePendingThumbnailAndDuplicatesAreSkipped()
    {
        FakeEngine engine = new() { BlockFirst = true };
        await using PdfViewerRenderer viewer = new(() => engine);
        await viewer.OpenAsync("first");
        Task<ViewerRenderResult> active = Thumbnail(viewer, 0);
        try
        {
            await engine.Started.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            Assert.False(viewer.TryRequestThumbnail(0, out Task<ViewerRenderResult>? duplicateActive));
            Assert.Null(duplicateActive);
            Task<ViewerRenderResult> pending = Thumbnail(viewer, 1);
            Assert.False(viewer.TryRequestThumbnail(1, out Task<ViewerRenderResult>? duplicatePending));
            Assert.Null(duplicatePending);
            Task<ViewerRenderResult> foreground = viewer.RenderAsync(new ViewerState(10).GoToPage(7), 800, 600, 17);
            engine.Release.Set();
            using ViewerRenderResult activeResult = await active;
            using ViewerRenderResult mainResult = await foreground;
            using ViewerRenderResult pendingResult = await pending;
            Assert.Collection(engine.RenderOrder, page => Assert.Equal(0, page), page => Assert.Equal(6, page), page => Assert.Equal(1, page));
        }
        finally { engine.Release.Set(); }
    }

    [Fact]
    public async Task DocumentChangeInvalidatesActiveAndPendingThumbnails()
    {
        FakeEngine engine = new() { BlockFirst = true };
        await using PdfViewerRenderer viewer = new(() => engine);
        await viewer.OpenAsync("first");
        Task<ViewerRenderResult> active = Thumbnail(viewer, 0);
        try
        {
            await engine.Started.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            Task<ViewerRenderResult> pending = Thumbnail(viewer, 1);
            Task<int> opened = viewer.OpenAsync("second");
            Assert.True(active.IsCanceled);
            Assert.True(pending.IsCanceled);
            engine.Release.Set();
            await opened;
            Assert.Equal(1, engine.Owners[0].DisposeCount);
            using ViewerRenderResult next = await Thumbnail(viewer, 0);
            Assert.False(next.FromCache);
            Assert.Collection(engine.RenderOrder, page => Assert.Equal(0, page), page => Assert.Equal(0, page));
        }
        finally { engine.Release.Set(); }
    }

    [Fact]
    public async Task ScrollingReplacesThumbnailSlotWithoutCancelingMainWork()
    {
        FakeEngine engine = new() { BlockFirst = true };
        await using PdfViewerRenderer viewer = new(() => engine);
        await viewer.OpenAsync("first");
        Task<ViewerRenderResult> foreground = viewer.RenderAsync(new(10), 800, 600, 17);
        try
        {
            await engine.Started.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            Task<ViewerRenderResult> old = Thumbnail(viewer, 1);
            Task<ViewerRenderResult> next = Thumbnail(viewer, 2);
            Assert.True(old.IsCanceled);
            viewer.CancelThumbnails();
            Assert.True(next.IsCanceled);
            Assert.False(foreground.IsCanceled);
            Task<ViewerRenderResult> newest = Thumbnail(viewer, 3);
            engine.Release.Set();
            using ViewerRenderResult mainResult = await foreground;
            using ViewerRenderResult thumbResult = await newest;
            Assert.Collection(engine.RenderOrder, page => Assert.Equal(0, page), page => Assert.Equal(3, page));
        }
        finally { engine.Release.Set(); }
    }

    private static Task<ViewerRenderResult> Thumbnail(PdfViewerRenderer viewer, int page)
    {
        Assert.True(viewer.TryRequestThumbnail(page, out Task<ViewerRenderResult>? task));
        return Assert.IsType<Task<ViewerRenderResult>>(task, exactMatch: false);
    }

    [Fact]
    public async Task GeometryOpenDoesNotRenderEvenFor1001Pages()
    {
        FakeEngine engine = new() { PageCount = 1001 };
        await using PdfViewerRenderer viewer = new(() => engine);
        IReadOnlyList<PdfPageSize> pages = await viewer.OpenDocumentAsync("large");
        Assert.Equal(1001, pages.Count);
        Assert.All(pages, size => Assert.Equal(new PdfPageSize(72, 72), size));
        Assert.Equal(0, engine.RenderCount);
    }

    [Fact]
    public async Task VisibleBatchReplacesObsoleteRangeAndRunsBeforeThumbnails()
    {
        FakeEngine engine = new() { BlockFirst = true };
        await using PdfViewerRenderer viewer = new(() => engine);
        await viewer.OpenAsync("first");
        IReadOnlyList<Task<ViewerRenderResult>> old = viewer.RenderVisible([new(0, new(96, 96)), new(1, new(96, 96))]);
        try
        {
            await engine.Started.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            Task<ViewerRenderResult> thumbnail = Thumbnail(viewer, 9);
            IReadOnlyList<Task<ViewerRenderResult>> latest = viewer.RenderVisible([new(5, new(96, 96)), new(6, new(96, 96))]);
            Assert.All(old, task => Assert.True(task.IsCanceled));
            engine.Release.Set();
            foreach (Task<ViewerRenderResult> task in latest) { using ViewerRenderResult result = await task; }
            using ViewerRenderResult thumb = await thumbnail;
            Assert.Equal([0, 5, 6, 9], engine.RenderOrder);
            Assert.Equal(1, engine.Owners[0].DisposeCount);
        }
        finally { engine.Release.Set(); }
    }

    [Fact]
    public async Task ReturningVisiblePageReusesExactSizeCache()
    {
        FakeEngine engine = new();
        await using PdfViewerRenderer viewer = new(() => engine);
        await viewer.OpenAsync("first");
        using ViewerRenderResult first = await viewer.RenderVisible([new(2, new(120, 160))])[0];
        using ViewerRenderResult other = await viewer.RenderVisible([new(3, new(120, 160))])[0];
        using ViewerRenderResult returned = await viewer.RenderVisible([new(2, new(120, 160))])[0];
        Assert.True(returned.FromCache);
        using ViewerRenderResult resized = await viewer.RenderVisible([new(2, new(240, 320))])[0];
        Assert.False(resized.FromCache);
        Assert.Equal(3, engine.RenderCount);
    }

    [Fact]
    public async Task ReplacementCancelsEntireVisibleBatchAndClearsCache()
    {
        FakeEngine engine = new() { BlockFirst = true };
        await using PdfViewerRenderer viewer = new(() => engine);
        await viewer.OpenAsync("first");
        IReadOnlyList<Task<ViewerRenderResult>> old = viewer.RenderVisible([new(0, new(96, 96)), new(1, new(96, 96))]);
        try
        {
            await engine.Started.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            Task<int> opened = viewer.OpenAsync("second");
            Assert.All(old, task => Assert.True(task.IsCanceled));
            engine.Release.Set();
            await opened;
            using ViewerRenderResult current = await viewer.RenderVisible([new(0, new(96, 96))])[0];
            Assert.False(current.FromCache);
            Assert.Equal(1, engine.ClosedDocuments);
            Assert.Equal([0, 0], engine.RenderOrder);
        }
        finally { engine.Release.Set(); }
    }

    [Fact]
    public async Task RapidRangesDoNotAccumulateNativeRenderJobs()
    {
        FakeEngine engine = new() { BlockFirst = true };
        await using PdfViewerRenderer viewer = new(() => engine);
        await viewer.OpenAsync("first");
        IReadOnlyList<Task<ViewerRenderResult>> previous = viewer.RenderVisible([new(0, new(96, 96))]);
        try
        {
            await engine.Started.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            for (int index = 0; index < 1000; index++)
            {
                IReadOnlyList<Task<ViewerRenderResult>> next = viewer.RenderVisible([new(index % 10, new(96, 96))]);
                Assert.All(previous, task => Assert.True(task.IsCanceled));
                previous = next;
            }
            engine.Release.Set();
            using ViewerRenderResult result = await previous[0];
            Assert.Equal(2, engine.RenderCount);
        }
        finally { engine.Release.Set(); }
    }

    [Fact]
    public async Task VisibleBatchRejectsUnboundedDemand()
    {
        await using PdfViewerRenderer viewer = new(() => new FakeEngine());
        Assert.Throws<ArgumentOutOfRangeException>(() => viewer.RenderVisible(
            Enumerable.Repeat(new PageRenderTarget(0, new(96, 96)), 33).ToArray()));
    }

    [Fact]
    public async Task EmptyViewportResizeDoesNotCancelDocumentOpening()
    {
        FakeEngine engine = new() { BlockGeometry = true };
        await using PdfViewerRenderer viewer = new(() => engine);
        Task<IReadOnlyList<PdfPageSize>> opened = viewer.OpenDocumentAsync("first");
        try
        {
            await engine.Started.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            viewer.CancelVisible();
            Assert.False(opened.IsCanceled);
            engine.Release.Set();
            Assert.Equal(10, (await opened).Count);
        }
        finally { engine.Release.Set(); }
    }

    [Fact]
    public async Task TextIsLazyCachedAndFailureDoesNotAffectRasterWork()
    {
        FakeEngine engine = new() { PageCount = 1001 };
        await using PdfViewerRenderer viewer = new(() => engine);
        await viewer.OpenDocumentAsync("large");
        Assert.Equal(0, engine.TextCount);
        var first = await viewer.ExtractTextAsync(0);
        Assert.Same(first, await viewer.ExtractTextAsync(0));
        Assert.Equal(1, engine.TextCount);
        await viewer.OpenDocumentAsync("replacement");
        Assert.NotSame(first, await viewer.ExtractTextAsync(0));
        Assert.Equal(2, engine.TextCount);

        FakeEngine failing = new() { FailText = true };
        await using PdfViewerRenderer failureViewer = new(() => failing);
        await failureViewer.OpenAsync("first");
        await Assert.ThrowsAsync<InvalidDataException>(() => failureViewer.ExtractTextAsync(0));
        using ViewerRenderResult render = await failureViewer.RenderVisible([new(0, new(96, 96))])[0];
        Assert.Equal(1, failing.RenderCount);
    }

    [Fact]
    public async Task VisibleRastersPrecedeTextAndTextPrecedesThumbnails()
    {
        FakeEngine engine = new() { BlockFirst = true };
        await using PdfViewerRenderer viewer = new(() => engine);
        await viewer.OpenAsync("first");
        Task<ViewerRenderResult> old = viewer.RenderVisible([new(0, new(96, 96))])[0];
        try
        {
            await engine.Started.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            Task<ViewerRenderResult> thumbnail = Thumbnail(viewer, 9);
            var text = viewer.ExtractTextAsync(1);
            Task<ViewerRenderResult> visible = viewer.RenderVisible([new(2, new(96, 96))])[0];
            Assert.True(old.IsCanceled);
            Assert.False(text.IsCanceled);
            engine.Release.Set();
            using ViewerRenderResult foreground = await visible;
            await text;
            using ViewerRenderResult background = await thumbnail;
            Assert.Equal(["R0", "R2", "T1", "R9"], engine.WorkOrder);
        }
        finally { engine.Release.Set(); }
    }

    [Fact]
    public async Task DocumentReplacementRejectsActiveAndPendingText()
    {
        FakeEngine engine = new() { BlockText = true };
        await using PdfViewerRenderer viewer = new(() => engine);
        await viewer.OpenAsync("first");
        var active = viewer.ExtractTextAsync(0);
        try
        {
            await engine.Started.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            var pending = viewer.ExtractTextAsync(1);
            Task<int> opened = viewer.OpenAsync("second");
            Assert.True(active.IsCanceled);
            Assert.True(pending.IsCanceled);
            Assert.Equal(0, engine.ClosedDocuments);
            engine.Release.Set();
            await opened;
            await viewer.ExtractTextAsync(0);
            Assert.Equal(2, engine.TextCount);
            Assert.Equal(1, engine.ClosedDocuments);
        }
        finally { engine.Release.Set(); }
    }

    [Fact]
    public async Task RapidTextDemandHasOneReplaceableSlotAndDoesNotCancelRaster()
    {
        FakeEngine engine = new() { BlockText = true };
        await using PdfViewerRenderer viewer = new(() => engine);
        await viewer.OpenAsync("first");
        var previous = viewer.ExtractTextAsync(0);
        try
        {
            await engine.Started.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            Task<ViewerRenderResult> visible = viewer.RenderVisible([new(9, new(96, 96))])[0];
            viewer.CancelThumbnails();
            Assert.False(previous.IsCanceled);
            for (int page = 1; page < 1000; page++)
            {
                var next = viewer.ExtractTextAsync(page % 10);
                Assert.True(previous.IsCanceled);
                previous = next;
            }
            Assert.False(visible.IsCanceled);
            engine.Release.Set();
            using ViewerRenderResult result = await visible;
            await previous;
            Assert.Equal(["T0", "R9", "T9"], engine.WorkOrder);
        }
        finally { engine.Release.Set(); }
    }

    [Fact]
    public async Task ShutdownWaitsForActiveTextBeforeClosingSession()
    {
        FakeEngine engine = new() { BlockText = true };
        PdfViewerRenderer viewer = new(() => engine);
        await viewer.OpenAsync("first");
        var text = viewer.ExtractTextAsync(0);
        try
        {
            await engine.Started.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            Task shutdown = viewer.DisposeAsync().AsTask();
            Assert.True(text.IsCanceled);
            Assert.False(shutdown.IsCompleted);
            Assert.Equal(0, engine.ClosedDocuments);
            engine.Release.Set();
            await shutdown;
            Assert.Equal(1, engine.ClosedDocuments);
        }
        finally { engine.Release.Set(); await viewer.DisposeAsync(); }
    }

    [Fact]
    public async Task SearchUsesRasterInteractionGeometryScanThumbnailPriority()
    {
        FakeEngine engine = new() { BlockFirst = true };
        await using PdfViewerRenderer viewer = new(() => engine);
        await viewer.OpenAsync("first");
        var initial = viewer.RenderVisible([new(0, new(96, 96))])[0];
        try
        {
            await engine.Started.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            long generation = viewer.BeginSearch();
            var thumbnail = Thumbnail(viewer, 9);
            var scan = viewer.SearchPageAsync(generation, 3, "d", 10);
            var geometry = viewer.SearchGeometryAsync(generation, 2);
            var interaction = viewer.ExtractTextAsync(1);
            var foreground = viewer.RenderVisible([new(4, new(96, 96))])[0];
            Assert.True(initial.IsCanceled);
            engine.Release.Set();
            using ViewerRenderResult rendered = await foreground;
            await interaction;
            await geometry;
            Assert.Single((await scan).Matches);
            using ViewerRenderResult thumb = await thumbnail;
            Assert.Equal(["R0", "R4", "T1", "T2", "T3", "R9"], engine.WorkOrder);
        }
        finally { engine.Release.Set(); }
    }

    [Fact]
    public async Task QueryReplacementAndCancellationRejectOldNativeScanResults()
    {
        FakeEngine engine = new() { BlockText = true };
        await using PdfViewerRenderer viewer = new(() => engine);
        await viewer.OpenAsync("first");
        long old = viewer.BeginSearch();
        var scan = viewer.SearchPageAsync(old, 0, "a", 10);
        try
        {
            await engine.Started.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            long current = viewer.BeginSearch();
            Assert.True(scan.IsCanceled);
            Assert.True(viewer.SearchPageAsync(old, 1, "a", 10).IsCanceled);
            var replacement = viewer.SearchPageAsync(current, 1, "b", 10);
            engine.Release.Set();
            Assert.Single((await replacement).Matches);
            Assert.Equal(2, engine.TextCount);
            viewer.CancelSearch();
            Assert.True(viewer.SearchGeometryAsync(current, 1).IsCanceled);
        }
        finally { engine.Release.Set(); }
    }

    [Fact]
    public async Task DocumentReplacementInvalidatesScanAndHighlightRequests()
    {
        FakeEngine engine = new() { BlockText = true };
        await using PdfViewerRenderer viewer = new(() => engine);
        await viewer.OpenAsync("first");
        long old = viewer.BeginSearch();
        var scan = viewer.SearchPageAsync(old, 0, "a", 10);
        try
        {
            await engine.Started.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            var geometry = viewer.SearchGeometryAsync(old, 1);
            Task<int> opened = viewer.OpenAsync("second");
            Assert.True(scan.IsCanceled);
            Assert.True(geometry.IsCanceled);
            engine.Release.Set();
            await opened;
            Assert.Single((await viewer.SearchPageAsync(viewer.BeginSearch(), 0, "a", 10)).Matches);
            Assert.Equal(2, engine.TextCount);
            Assert.Equal(1, engine.ClosedDocuments);
        }
        finally { engine.Release.Set(); }
    }

    [Fact]
    public async Task ScanReusesSelectionCacheAndDoesNotCancelSelection()
    {
        FakeEngine engine = new() { BlockText = true };
        await using PdfViewerRenderer viewer = new(() => engine);
        await viewer.OpenAsync("first");
        var selection = viewer.ExtractTextAsync(0);
        try
        {
            await engine.Started.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            long generation = viewer.BeginSearch();
            var scan = viewer.SearchPageAsync(generation, 0, "a", 10);
            viewer.CancelSearchGeometry();
            Assert.False(selection.IsCanceled);
            engine.Release.Set();
            var text = await selection;
            Assert.Single((await scan).Matches);
            Assert.Same(text, await viewer.SearchGeometryAsync(generation, 0));
            Assert.Equal(1, engine.TextCount);
        }
        finally { engine.Release.Set(); }
    }

    [Fact]
    public async Task ThousandPageScanIsReplenishedAndDoesNotKeepEveryTextPageCached()
    {
        FakeEngine engine = new() { PageCount = 1001 };
        await using PdfViewerRenderer viewer = new(() => engine);
        await viewer.OpenAsync("large");
        Assert.Equal(0, engine.TextCount);
        long generation = viewer.BeginSearch();
        for (int page = 0; page < 1001; page++)
        {
            Assert.Empty((await viewer.SearchPageAsync(generation, page, "no match", 10)).Matches);
            Assert.Equal(page + 1, engine.TextCount);
        }
        await viewer.SearchGeometryAsync(generation, 0);
        Assert.Equal(1002, engine.TextCount); // Page zero was evicted from the bounded shared text cache.
    }

    [Fact]
    public async Task SearchCloseDoesNotCancelPendingInteractionOrRaster()
    {
        FakeEngine engine = new() { BlockText = true };
        await using PdfViewerRenderer viewer = new(() => engine);
        await viewer.OpenAsync("first");
        var scan = viewer.SearchPageAsync(viewer.BeginSearch(), 0, "a", 10);
        try
        {
            await engine.Started.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            var text = viewer.ExtractTextAsync(1);
            var raster = viewer.RenderVisible([new(2, new(96, 96))])[0];
            viewer.CancelSearch();
            Assert.True(scan.IsCanceled);
            Assert.False(text.IsCanceled);
            Assert.False(raster.IsCanceled);
            engine.Release.Set();
            await text;
            using ViewerRenderResult result = await raster;
        }
        finally { engine.Release.Set(); }
    }

    [Fact]
    public async Task RapidQueryChangesDoNotAccumulateBackgroundNativeWork()
    {
        FakeEngine engine = new() { BlockText = true };
        await using PdfViewerRenderer viewer = new(() => engine);
        await viewer.OpenAsync("first");
        var previous = viewer.SearchPageAsync(viewer.BeginSearch(), 0, "a", 10);
        try
        {
            await engine.Started.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            for (int query = 0; query < 1000; query++)
            {
                var next = viewer.SearchPageAsync(viewer.BeginSearch(), 1, "b", 10);
                Assert.True(previous.IsCanceled);
                previous = next;
            }
            engine.Release.Set();
            Assert.Single((await previous).Matches);
            Assert.Equal(2, engine.TextCount);
        }
        finally { engine.Release.Set(); }
    }

    [Fact]
    public async Task OutlineUsesLowerPriorityThanVisibleAndInteractiveWork()
    {
        FakeEngine engine = new() { BlockFirst = true };
        await using PdfViewerRenderer viewer = new(() => engine);
        await viewer.OpenAsync("first");
        Assert.Equal(0, engine.OutlineCount);
        var old = viewer.RenderVisible([new(0, new(96, 96))])[0];
        try
        {
            await engine.Started.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            var outline = viewer.ExtractOutlineAsync();
            Assert.Same(outline, viewer.ExtractOutlineAsync());
            var text = viewer.ExtractTextAsync(1);
            var visible = viewer.RenderVisible([new(2, new(96, 96))])[0];
            Assert.True(old.IsCanceled);
            Assert.False(outline.IsCanceled);
            engine.Release.Set();
            using ViewerRenderResult rendered = await visible;
            await text;
            Assert.Equal("Bookmark", Assert.Single((await outline).Roots).Title);
            Assert.Equal(["R0", "R2", "T1", "O"], engine.WorkOrder);
        }
        finally { engine.Release.Set(); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DocumentReplacementRejectsActiveAndPendingOutlines(bool includePending)
    {
        FakeEngine engine = new() { BlockOutline = true };
        await using PdfViewerRenderer viewer = new(() => engine);
        await viewer.OpenAsync("first");
        var active = viewer.ExtractOutlineAsync();
        try
        {
            await engine.Started.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            viewer.CancelVisible();
            viewer.CancelText();
            viewer.CancelSearch();
            viewer.CancelThumbnails();
            Assert.False(active.IsCanceled);
            Task<Core.Outline.PdfOutline>? pending = null;
            if (includePending)
            {
                viewer.CancelOutline();
                pending = viewer.ExtractOutlineAsync();
            }
            Task<int> opened = viewer.OpenAsync("second");
            Assert.True(active.IsCanceled);
            if (pending is not null) Assert.True(pending.IsCanceled);
            Assert.Equal(0, engine.ClosedDocuments);
            engine.Release.Set();
            await opened;
            Assert.Single((await viewer.ExtractOutlineAsync()).Roots);
            Assert.Equal(2, engine.OutlineCount);
            Assert.Equal(1, engine.ClosedDocuments);
        }
        finally { engine.Release.Set(); }
    }

    [Fact]
    public async Task OutlineFailureDoesNotCloseDocumentOrPreventRendering()
    {
        FakeEngine engine = new() { FailOutline = true };
        await using PdfViewerRenderer viewer = new(() => engine);
        await viewer.OpenAsync("first");
        await Assert.ThrowsAsync<InvalidDataException>(() => viewer.ExtractOutlineAsync());
        Assert.Equal(0, engine.ClosedDocuments);
        using ViewerRenderResult rendered = await viewer.RenderVisible([new(0, new(96, 96))])[0];
        Assert.Equal(1, engine.RenderCount);
    }

    [Fact]
    public async Task ShutdownWaitsForOutlineBeforeReleasingDocument()
    {
        FakeEngine engine = new() { BlockOutline = true };
        PdfViewerRenderer viewer = new(() => engine);
        await viewer.OpenAsync("first");
        var outline = viewer.ExtractOutlineAsync();
        try
        {
            await engine.Started.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            Task shutdown = viewer.DisposeAsync().AsTask();
            Assert.True(outline.IsCanceled);
            Assert.False(shutdown.IsCompleted);
            Assert.Equal(0, engine.ClosedDocuments);
            engine.Release.Set();
            await shutdown;
            Assert.Equal(1, engine.ClosedDocuments);
        }
        finally { engine.Release.Set(); await viewer.DisposeAsync(); }
    }

    [Fact]
    public async Task RasterAndThumbnailCachesSeparateEqualSizedOrientations()
    {
        FakeEngine engine = new();
        await using PdfViewerRenderer viewer = new(() => engine);
        await viewer.OpenAsync("first");
        using ViewerRenderResult normal = await viewer.RenderVisible([new(0, new(96, 96))])[0];
        using ViewerRenderResult turned = await viewer.RenderVisible([new(0, new(96, 96), new(180))])[0];
        using ViewerRenderResult returned = await viewer.RenderVisible([new(0, new(96, 96))])[0];
        Assert.False(turned.FromCache);
        Assert.True(returned.FromCache);
        Assert.NotEqual(normal.Pixels.Pixels.Span[0], turned.Pixels.Pixels.Span[0]);
        foreach (int degrees in new[] { 0, 90, 180, 270 })
        {
            viewer.CancelThumbnails();
            Assert.True(viewer.TryRequestThumbnail(0, out var task, new(degrees)));
            using ViewerRenderResult thumbnail = await task!;
            Assert.False(thumbnail.FromCache);
            Assert.Equal(degrees / 90 * 10, thumbnail.Pixels.Pixels.Span[0]);
        }
        viewer.CancelThumbnails();
        Assert.True(viewer.TryRequestThumbnail(0, out var cached, new(180)));
        using ViewerRenderResult hit = await cached!;
        Assert.True(hit.FromCache);
        Assert.Equal(6, engine.RenderCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RotationChangeRejectsOldOrientationAndDisposesPixels(bool thumbnail)
    {
        FakeEngine engine = new() { BlockFirst = true };
        await using PdfViewerRenderer viewer = new(() => engine);
        await viewer.OpenAsync("first");
        Task<ViewerRenderResult> old = thumbnail ? Thumbnail(viewer, 0) : viewer.RenderVisible([new(0, new(96, 96))])[0];
        try
        {
            await engine.Started.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            Task<ViewerRenderResult> current;
            if (thumbnail)
            {
                viewer.CancelThumbnails();
                Assert.True(viewer.TryRequestThumbnail(0, out var task, new(180)));
                current = task!;
            }
            else current = viewer.RenderVisible([new(0, new(96, 96), new(180))])[0];
            Assert.True(old.IsCanceled);
            engine.Release.Set();
            using ViewerRenderResult result = await current;
            Assert.False(result.FromCache);
            Assert.Equal(20, result.Pixels.Pixels.Span[0]);
            Assert.Equal(1, engine.Owners[0].DisposeCount);
            await viewer.OpenAsync("replacement");
            using ViewerRenderResult fresh = await viewer.RenderVisible([new(0, new(96, 96))])[0];
            Assert.False(fresh.FromCache);
            Assert.Equal(0, fresh.Pixels.Pixels.Span[0]);
        }
        finally { engine.Release.Set(); }
    }

    [Fact]
    public async Task LogicalPageMovesReuseSourceRastersAndSourceText()
    {
        FakeEngine engine = new();
        await using PdfViewerRenderer viewer = new(() => engine);
        await viewer.OpenAsync("source");
        Core.Documents.LogicalPageReference page = new(new(Guid.NewGuid(), 4), new(90));
        PageRenderTarget request = PageRenderTarget.FromLogicalPage(page, new(96, 96), new(90));
        Assert.Equal(4, request.PageIndex);
        Assert.Equal(180, request.Rotation.Degrees);
        using ViewerRenderResult first = await viewer.RenderVisible([request])[0];
        var text = await viewer.ExtractTextAsync(page.SourcePageIndex);
        // Logical position changed; the source identity and rendering parameters did not.
        viewer.CancelVisible();
        using ViewerRenderResult moved = await viewer.RenderVisible([PageRenderTarget.FromLogicalPage(page, new(96, 96), new(90))])[0];
        Assert.True(moved.FromCache);
        Assert.Same(text, await viewer.ExtractTextAsync(page.SourcePageIndex));
        Assert.Equal(1, engine.RenderCount);
        Assert.Equal(1, engine.TextCount);
        Assert.Equal(0, engine.ClosedDocuments);
        using ViewerRenderResult rotated = await viewer.RenderVisible([PageRenderTarget.FromLogicalPage(
            page with { StructuralRotation = new(180) }, new(96, 96), new(90))])[0];
        Assert.False(rotated.FromCache);
        Assert.Equal(2, engine.RenderCount);
    }

    private sealed class FakeEngine : IPdfRenderer
    {
        public bool BlockOutline { get; init; }
        public bool FailOutline { get; init; }
        public int OutlineCount { get; set; }
        public bool BlockText { get; init; }
        public bool FailText { get; init; }
        public int TextCount { get; set; }
        public List<string> WorkOrder { get; } = [];
        public int PageCount { get; init; } = 10;
        public bool BlockGeometry { get; init; }
        public bool BlockFirst { get; init; }
        public bool FailFirst { get; init; }
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ManualResetEventSlim Release { get; } = new();
        public List<TrackingOwner> Owners { get; } = [];
        public List<int> RenderOrder { get; } = [];
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
            public Core.Outline.PdfOutline ExtractOutline()
            {
                engine.OutlineCount++;
                engine.WorkOrder.Add("O");
                if (engine.BlockOutline)
                {
                    engine.Started.TrySetResult();
                    engine.Release.Wait();
                }
                if (engine.FailOutline) throw new InvalidDataException("Test outline failure.");
                return new([new("Bookmark", 0, [])]);
            }
            public Core.Text.PdfTextPage ExtractText(int pageIndex)
            {
                engine.TextCount++;
                engine.WorkOrder.Add($"T{pageIndex}");
                if (engine.BlockText)
                {
                    engine.Started.TrySetResult();
                    engine.Release.Wait();
                }
                if (engine.FailText) throw new InvalidDataException("Test text failure.");
                return new([new((uint)('A' + pageIndex), default)]);
            }
            public int PageCount => engine.PageCount;
            public PdfPageSize GetPageSize(int pageIndex)
            {
                if (engine.BlockGeometry)
                {
                    engine.Started.TrySetResult();
                    engine.Release.Wait();
                }
                return new(72, 72);
            }
            public RenderedPage RenderPage(int pageIndex, int pixelWidth, int pixelHeight, VisualRotation rotation = default)
            {
                engine.RenderCount++;
                engine.WorkOrder.Add($"R{pageIndex}");
                engine.RenderOrder.Add(pageIndex);
                if (engine.RenderCount == 1)
                {
                    engine.Started.TrySetResult();
                    if (engine.BlockFirst) engine.Release.Wait();
                    if (engine.FailFirst) throw new InvalidDataException("Test render failure.");
                }

                TrackingOwner owner = new(pixelWidth * pixelHeight * 4);
                owner.Memory.Span.Fill((byte)(pageIndex + rotation.QuarterTurns * 10));
                engine.Owners.Add(owner);
                return new RenderedPage(pixelWidth, pixelHeight, pixelWidth * 4, RenderedPixelFormat.Bgra32, owner);
            }
            public void Dispose() => engine.ClosedDocuments++;
        }
    }
}
