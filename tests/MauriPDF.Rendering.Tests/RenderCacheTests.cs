using System.Buffers;
using MauriPDF.Core.Rendering;
using Xunit;

namespace MauriPDF.Rendering.Tests;

public sealed class RenderCacheTests
{
    [Fact]
    public void MissAndHitReturnIndependentOwnership()
    {
        using RenderCache cache = new(16);
        RenderCacheKey key = new(1, 0, 1, 1);
        Assert.Null(cache.GetCopy(key));
        TrackingOwner owner = new(4);
        owner.Memory.Span.Fill(42);
        cache.Store(key, Page(owner));
        using RenderedPage copy = Assert.IsType<RenderedPage>(cache.GetCopy(key));
        Assert.Equal(42, copy.Pixels.Span[0]);
        copy.Dispose();
        Assert.Equal(0, owner.DisposeCount);
        using RenderedPage second = Assert.IsType<RenderedPage>(cache.GetCopy(key));
        Assert.Equal(42, second.Pixels.Span[0]);
        cache.Clear();
        Assert.Equal(1, owner.DisposeCount);
    }

    [Fact]
    public void LruEvictionUsesAllocationBytesAndHitsPromoteEntries()
    {
        using RenderCache cache = new(12);
        TrackingOwner first = new(8);
        TrackingOwner second = new(4);
        TrackingOwner third = new(4);
        cache.Store(new(1, 0, 1, 1), Page(first));
        cache.Store(new(1, 1, 1, 1), Page(second));
        using RenderedPage? hit = cache.GetCopy(new(1, 0, 1, 1));
        cache.Store(new(1, 2, 1, 1), Page(third));
        Assert.Equal(12, cache.UsedBytes);
        Assert.Equal(2, cache.Count);
        Assert.Equal(1, second.DisposeCount);
        Assert.Equal(0, first.DisposeCount);
        Assert.Null(cache.GetCopy(new(1, 1, 1, 1)));
    }

    [Fact]
    public void ReplacementOversizeAndClearReleaseOwnership()
    {
        using RenderCache cache = new(8);
        TrackingOwner old = new(4);
        TrackingOwner replacement = new(8);
        TrackingOwner large = new(16);
        RenderCacheKey key = new(1, 0, 1, 1);
        cache.Store(key, Page(old));
        cache.Store(key, Page(replacement));
        Assert.Equal(1, old.DisposeCount);
        cache.Store(new(1, 1, 1, 1), Page(large));
        Assert.Equal(1, large.DisposeCount);
        Assert.Equal(8, cache.UsedBytes);
        cache.Dispose();
        cache.Dispose();
        Assert.Equal(1, replacement.DisposeCount);
        Assert.Equal(0, cache.UsedBytes);
    }

    [Theory]
    [InlineData(2, 0, 1, 1)]
    [InlineData(1, 1, 1, 1)]
    [InlineData(1, 0, 2, 1)]
    [InlineData(1, 0, 1, 2)]
    public void KeyIncludesDocumentPageAndBothDimensions(long doc, int page, int width, int height)
    {
        using RenderCache cache = new();
        cache.Store(new(1, 0, 1, 1), Page(new TrackingOwner(4)));
        Assert.Null(cache.GetCopy(new(doc, page, width, height)));
    }

    internal static RenderedPage Page(TrackingOwner owner) => new(1, 1, 4, RenderedPixelFormat.Bgra32, owner);
}

internal sealed class TrackingOwner(int size) : IMemoryOwner<byte>
{
    private readonly byte[] _bytes = new byte[size];
    public int DisposeCount { get; private set; }
    public Memory<byte> Memory => _bytes;
    public void Dispose() => DisposeCount++;
}
