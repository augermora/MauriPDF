using MauriPDF.Core.Search;
using MauriPDF.Core.Text;
using Xunit;

namespace MauriPDF.Core.Tests;

public sealed class DocumentSearchTests
{
    [Theory]
    [InlineData("Cat cat CAT", "cat", 3)]
    [InlineData("é É", "É", 2)]
    [InlineData("aaa", "aa", 1)]
    [InlineData("a\r\nb", "a b", 0)]
    [InlineData("a\r\nb", "a\nb", 0)]
    [InlineData("a\r\nb", "a\r\nb", 1)]
    [InlineData("é", "e\u0301", 0)]
    [InlineData("anything", "", 0)]
    public void OrdinalMatchingPreservesWhitespaceAndDoesNotNormalize(string text, string query, int count)
    {
        Assert.Equal(count, Search(text).Find(0, query, 100).Matches.Count);
    }

    [Fact]
    public void Utf16MatchesMapToLogicalIndicesIncludingSurrogateContinuationAndUnmappedEntries()
    {
        PdfTextPage page = new([new(0, default), new(65, default), new(0xD83D, default), new(0xDE00, default), new(66, default), new(65, default)]);
        SearchablePageText text = new(page);
        Assert.Equal("A😀BA", text.Text);
        Assert.Equal(new SearchMatch(4, 2, 5), Assert.Single(text.Find(4, "😀b", 10).Matches));
        Assert.Equal([new SearchMatch(4, 1, 2), new(4, 5, 6)], text.Find(4, "a", 10).Matches);
        Assert.Empty(text.Find(0, "\uD83D", 10).Matches);
        Assert.Empty(text.Find(0, "\uDE00", 10).Matches);
    }

    [Fact]
    public void EmptyTextAndResultCapsAreExplicit()
    {
        Assert.Empty(Search("").Find(0, "x", 10).Matches);
        PageSearchResult capped = Search("aaaa").Find(0, "a", 3);
        Assert.Equal(3, capped.Matches.Count);
        Assert.True(capped.Truncated);
        Assert.False(Search("aaa").Find(0, "a", 3).Truncated);
        Assert.True(Search("a").Find(0, "a", 0).Truncated);
    }

    [Fact]
    public void ProgressiveStatePreservesActiveResultAndDoesNotWrapEarly()
    {
        DocumentSearchState state = new("a", 3);
        state.Append(0, Search("aa").Find(0, "a", 10));
        Assert.Equal(0, state.ActiveIndex);
        state.Move(true);
        Assert.Equal(0, state.ActiveIndex);
        state.Move(false);
        state.Move(false);
        Assert.Equal(1, state.ActiveIndex);
        SearchMatch? active = state.Active;
        state.Append(1, Search("a").Find(1, "a", 10));
        Assert.Equal(active, state.Active);
        state.Move(false);
        Assert.Equal(2, state.ActiveIndex);
        state.Append(2, Search("").Find(2, "a", 10));
        Assert.True(state.Complete);
        state.Move(false);
        Assert.Equal(0, state.ActiveIndex);
        state.Move(true);
        Assert.Equal(2, state.ActiveIndex);
        Assert.Equal((0, 2), state.MatchesOnPage(0));
        Assert.Equal((2, 3), state.MatchesOnPage(1));
        Assert.Equal((3, 3), state.MatchesOnPage(2));
    }

    [Fact]
    public void CancelledStateRejectsLateProgressAndQueryReplacementIsIndependent()
    {
        DocumentSearchState old = new("cat", 2);
        old.Cancel();
        old.Append(0, Search("cat").Find(0, "cat", 10));
        Assert.True(old.Cancelled);
        Assert.Equal(0, old.Count);
        DocumentSearchState current = new("dog", 2);
        current.Append(0, Search("cat dog").Find(0, "dog", 10));
        Assert.Equal(new SearchMatch(0, 4, 7), current.Active);
    }

    [Fact]
    public void TruncationAndSkippedPagesAreRepresentedWithoutClaimingFullScan()
    {
        DocumentSearchState state = new("a", 1001);
        state.Append(0, new(Array.Empty<SearchMatch>(), false), failed: true);
        state.Append(1, Search("aa").Find(1, "a", 1));
        Assert.True(state.Truncated);
        Assert.True(state.Complete);
        Assert.Equal(2, state.PagesScanned);
        Assert.Equal(1, state.FailedPages);
        Assert.Equal(1, state.Count);
    }

    [Fact]
    public void ThousandPagesAccumulateOnlyLogicalResultsInDocumentOrder()
    {
        DocumentSearchState state = new("a", 1001);
        for (int page = 0; page < 1001; page++) state.Append(page, Search("a").Find(page, "a", 10));
        Assert.True(state.Complete);
        Assert.Equal(1001, state.Count);
        for (int index = 0; index < state.Count; index++) Assert.Equal(new SearchMatch(index, 0, 1), state[index]);
    }

    [Fact]
    public void MatchGeometryUsesLogicalMappingRatherThanUtf16Offsets()
    {
        PdfTextPage page = new([new(0x1F600, new(.1, .2, .3, .4)), new(65, new(.4, .2, .5, .4))]);
        SearchMatch match = Assert.Single(new SearchablePageText(page).Find(0, "a", 10).Matches);
        Assert.Equal(1, match.Start);
        Assert.Equal(new TextBounds(390, 180, 490, 380), TextCoordinateTransform.ToDisplay(page[match.Start].Bounds, -10, -20, 1000, 1000));
    }

    [Fact]
    public void GlobalResultCapStopsAtDeterministicDocumentPrefix()
    {
        DocumentSearchState state = new("a", 1001);
        state.Append(0, Search(new string('a', 10001)).Find(0, "a", DocumentSearchState.MaximumResults));
        Assert.Equal(10000, state.Count);
        Assert.True(state.Truncated);
        Assert.True(state.Complete);
        Assert.Equal(1, state.PagesScanned);
        state.Move(true);
        Assert.Equal(new SearchMatch(0, 9999, 10000), state.Active);
    }

    [Fact]
    public void OrdinalCaseMatchingDoesNotAdoptTurkishCultureRules()
    {
        var previous = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.GetCultureInfo("tr-TR");
            Assert.Equal(2, Search("i I ı İ").Find(0, "i", 10).Matches.Count);
        }
        finally { System.Globalization.CultureInfo.CurrentCulture = previous; }
    }

    private static SearchablePageText Search(string text) => new(new PdfTextPage(
        text.EnumerateRunes().Select(rune => new TextCharacter((uint)rune.Value, default)).ToArray()));
}
