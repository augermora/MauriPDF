using System.Buffers;
using MauriPDF.Core.Rendering;
using Xunit;

namespace MauriPDF.Core.Tests;

public sealed class AssemblyTests
{
    [Fact]
    public void CoreAssemblyHasExpectedName()
    {
        Assert.Equal("MauriPDF.Core", typeof(Core.AssemblyMarker).Assembly.GetName().Name);
    }

    [Fact]
    public void RenderedPageReleasesItsPixelsOnDispose()
    {
        RenderedPage page = new(1, 1, 4, RenderedPixelFormat.Bgra32, MemoryPool<byte>.Shared.Rent(4));

        page.Dispose();

        Assert.Throws<ObjectDisposedException>(() => page.Pixels);
    }
}
