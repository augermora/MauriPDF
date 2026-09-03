using MauriPDF.Core.Text;
using Xunit;

namespace MauriPDF.Rendering.Tests;

public sealed class TextPageCacheTests
{
    [Fact]
    public void ByteBudgetEvictsLeastRecentlyUsedButUiReferenceRemainsValid()
    {
        PdfTextPage page = new([new(65, default)]);
        TextPageCache cache = new(page.EstimatedBytes * 2);
        cache.Store(1, 0, page);
        cache.Store(1, 1, page);
        Assert.Same(page, cache.Get(1, 0));
        cache.Store(1, 2, page);
        Assert.Null(cache.Get(1, 1));
        Assert.Equal(page.EstimatedBytes * 2, cache.RetainedBytes);
        Assert.Equal(65u, page[0].Unicode);
    }

    [Fact]
    public void DocumentKeyAndClearPreventCrossDocumentReuse()
    {
        TextPageCache cache = new();
        cache.Store(1, 0, new([]));
        Assert.Null(cache.Get(2, 0));
        cache.Clear();
        Assert.Equal(0, cache.Count);
        Assert.Equal(0, cache.RetainedBytes);
        Assert.Null(cache.Get(1, 0));
    }

    [Fact]
    public void EmptyPagesAreStillCountBoundedAndOversizedEntriesBypassCache()
    {
        TextPageCache cache = new();
        for (int page = 0; page < 1000; page++) cache.Store(1, page, new([]));
        Assert.Equal(16, cache.Count);
        TextPageCache small = new(1);
        small.Store(1, 0, new([]));
        Assert.Equal(0, small.Count);
    }
}
