using MauriPDF.Core.Outline;
using Xunit;

namespace MauriPDF.Core.Tests;

public sealed class PdfOutlineTests
{
    [Fact]
    public void SnapshotPreservesHierarchyOrderUnicodeAndCopiesCollections()
    {
        List<PdfOutlineNode> children = [new("Sección 日本語 😀", 1, []), new("Second", null, [])];
        List<PdfOutlineNode> roots = [new("Chapter", 0, children), new("Appendix", 2, [])];
        PdfOutline outline = new(roots);
        children.Clear();
        roots.Clear();
        Assert.Equal(["Chapter", "Appendix"], outline.Roots.Select(node => node.Title));
        Assert.Equal(["Sección 日本語 😀", "Second"], outline.Roots[0].Children.Select(node => node.Title));
        Assert.Equal(1, outline.Roots[0].Children[0].PageIndex);
        Assert.Null(outline.Roots[0].Children[1].PageIndex);
        Assert.Throws<NotSupportedException>(() => ((IList<PdfOutlineNode>)outline.Roots).Clear());
        Assert.Throws<NotSupportedException>(() => ((IList<PdfOutlineNode>)outline.Roots[0].Children).Clear());
    }

    [Fact]
    public void EmptyAndLimitedAreDistinct()
    {
        PdfOutline empty = new([]);
        Assert.Empty(empty.Roots);
        Assert.False(empty.WasLimited);
        Assert.True(new PdfOutline([], wasLimited: true).WasLimited);
    }

    [Fact]
    public void NegativePageDestinationIsRejected() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new PdfOutlineNode("Invalid", -1, []));
}
