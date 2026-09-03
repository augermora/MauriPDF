using System.Text;
using MauriPDF.Core.Text;
using Xunit;

namespace MauriPDF.Core.Tests;

public sealed class TextSelectionTests
{
    [Fact]
    public void PageDataIsImmutableAndKeepsCharacterOrder()
    {
        TextCharacter[] input = [new(65, new(.1, .2, .2, .3))];
        PdfTextPage page = new(input);
        input[0] = default;
        Assert.Equal(65u, page[0].Unicode);
        Assert.Equal(168, page.EstimatedBytes);
    }

    [Theory]
    [InlineData(1, 4)]
    [InlineData(4, 1)]
    public void ForwardAndBackwardSelectionCopyOnlyHalfOpenRange(int anchor, int active)
    {
        TextSelection selection = new(new(0, anchor), new(0, active));
        Assert.Equal("bcd", selection.Reconstruct(new Dictionary<int, PdfTextPage> { [0] = Page("abcdef") }));
        Assert.Equal((1, 4), selection.RangeForPage(0, 6));
        Assert.Equal((0, 0), selection.RangeForPage(1, 6));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MultipageSelectionPreservesExplicitNewlinesAndPageSeparation(bool backward)
    {
        TextPosition a = new(0, 1), b = new(2, 2);
        TextSelection selection = backward ? new(b, a) : new(a, b);
        Dictionary<int, PdfTextPage> pages = new() { [0] = Page("abc"), [1] = Page("line\r\nnext"), [2] = Page("xyz") };
        Assert.Equal("bc\r\n\r\nline\r\nnext\r\n\r\nxy", selection.Reconstruct(pages));
        pages.Remove(1);
        Assert.Null(selection.Reconstruct(pages));
    }

    [Fact]
    public void UnicodeScalarsIncludingSupplementaryCharactersArePreserved()
    {
        PdfTextPage page = Page("café Ω 中文 😀\r\n");
        TextSelection selection = new(new(0, 0), new(0, page.Count));
        Assert.Equal("café Ω 中文 😀\r\n", selection.Reconstruct(new Dictionary<int, PdfTextPage> { [0] = page }));
    }

    [Fact]
    public void PdfiumSurrogateEntriesBecomeOneSelectableScalarWithoutChangingIndices()
    {
        PdfTextPage page = new([new(0xD83D, new(.1, .2, .2, .3)), new(0xDE00, new(.1, .2, .2, .3)), new(65, default)]);
        Assert.Equal(3, page.Count);
        Assert.Equal(0x1F600u, page[0].Unicode);
        Assert.Equal(0u, page[1].Unicode);
        StringBuilder text = new();
        page.AppendText(text, 0, 1);
        Assert.Equal("😀", text.ToString());
    }

    [Fact]
    public void EmptyPageAndEmptySelectionProduceNoCopy()
    {
        PdfTextPage page = new([]);
        Assert.Null(page.HitTest(new(.5, .5), 600, 800, double.PositiveInfinity));
        Assert.Equal(string.Empty, new TextSelection(new(0, 0), new(0, 0)).Reconstruct(new Dictionary<int, PdfTextPage>()));
    }

    [Fact]
    public void HitTestingUsesDisplayDistanceAndCaretMidpoints()
    {
        PdfTextPage page = new([new(65, new(.1, .2, .2, .3)), new(66, new(.2, .2, .3, .3))]);
        Assert.Equal(0, page.HitTest(new(.11, .25), 100, 100, 6));
        Assert.Equal(1, page.HitTest(new(.19, .25), 100, 100, 6));
        Assert.Equal(2, page.HitTest(new(.4, .25), 100, 100, double.PositiveInfinity));
        Assert.Null(page.HitTest(new(.4, .8), 100, 100, 6));
    }

    [Fact]
    public void CaretDirectionFollowsSimpleRtlAndRotatedRuns()
    {
        PdfTextPage rtl = new([new(65, new(.3, .2, .4, .3)), new(66, new(.2, .2, .3, .3))]);
        Assert.Equal(0, rtl.HitTest(new(.39, .25), 100, 100, 6));
        Assert.Equal(1, rtl.HitTest(new(.31, .25), 100, 100, 6));
        PdfTextPage vertical = new([new(65, new(.1, .1, .2, .2)), new(66, new(.1, .2, .2, .3))]);
        Assert.Equal(0, vertical.HitTest(new(.15, .11), 100, 100, 6));
        Assert.Equal(1, vertical.HitTest(new(.15, .19), 100, 100, 6));
    }

    [Theory]
    [InlineData(200, 300)]
    [InlineData(800, 1200)]
    [InlineData(1600, 2400)]
    public void ZoomAndScrollTransformRoundTripPreservesLogicalSelection(int width, int height)
    {
        TextBounds glyph = new(.1, .2, .2, .3);
        TextBounds mapped = TextCoordinateTransform.ToDisplay(glyph, -70, -120, width, height);
        TextPoint point = TextCoordinateTransform.ToPage(mapped.Left + width * .01, mapped.Top + height * .05, -70, -120, width, height);
        PdfTextPage page = new([new(65, glyph)]);
        Assert.Equal(0, page.HitTest(point, width, height, 6));
        Assert.Equal(.11, point.X, 10);
        Assert.Equal(.25, point.Y, 10);
    }

    [Fact]
    public void BoundsAndSelectionLimitsAreEnforced()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new PdfTextPage(new TextCharacter[PdfTextPage.MaximumCharacters + 1]));
        Assert.Null(new TextSelection(new(0, 0), new(16, 1)).Reconstruct(new Dictionary<int, PdfTextPage>()));
        Assert.False(new TextBounds(0, 0, double.NaN, 1).HasArea);
    }

    private static PdfTextPage Page(string text) => new(text.EnumerateRunes().Select(rune => new TextCharacter((uint)rune.Value, default)).ToArray());
}
