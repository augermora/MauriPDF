using MauriPDF.Core.Documents;
using MauriPDF.Core.Rendering;
using Xunit;

namespace MauriPDF.Editing.Tests;

public sealed class MultiSourceDocumentTests
{
    private static readonly int[] ExpectedRange = [1, 2, 3];
    private static PdfSourceDocument Source(int count = 3) => new(Guid.NewGuid(), "source.pdf",
        Enumerable.Repeat(new PdfPageSize(100, 200), count).ToArray());

    [Theory]
    [InlineData(0)] // Beginning / before first.
    [InlineData(1)] // After first / before second.
    [InlineData(2)] // After second / before last.
    [InlineData(3)] // End.
    public void RangeIsOneAtomicEditWithStableIdentityAndCurrentPage(int position)
    {
        PdfSourceDocument a = Source(), b = Source(5);
        DocumentEditSession edits = new(a);
        var before = edits.State;
        DocumentPageId current = before.Pages[1].Id;
        edits.Import(b, 1, 3, position, current);
        Assert.Equal(6, edits.State.Pages.Count);
        Assert.Equal(ExpectedRange, edits.State.Pages.Skip(position).Take(3).Select(p => p.SourcePageIndex));
        Assert.All(edits.State.Pages.Skip(position).Take(3), p => Assert.Equal(b.Id, p.SourceDocumentId));
        DocumentPageId imported = edits.State.Pages[position].Id;
        Assert.Equal(imported, edits.SuggestedCurrentPageId);
        Assert.Equal(1, edits.UndoCount);
        Assert.True(edits.IsDirty);
        var plan = edits.CreateMaterializationPlan();
        Assert.True(edits.Undo());
        Assert.Equal(before.Pages, edits.State.Pages);
        Assert.False(edits.IsDirty);
        Assert.Equal(current, edits.SuggestedCurrentPageId);
        Assert.Equal(2, edits.Sources.Sources.Count); // Undo must retain its source.
        Assert.Equal(6, plan.Pages.Count); // Snapshot does not rewind.
        Assert.True(edits.Redo());
        Assert.Equal(imported, edits.SuggestedCurrentPageId);
        edits.MarkSavedBaseline(edits.State.Revision);
        Assert.False(edits.IsDirty);
        edits.Undo(); Assert.True(edits.IsDirty);
        edits.Redo(); Assert.False(edits.IsDirty);
    }

    [Fact]
    public void RepeatedPhysicalPagesHaveDistinctInstancesAndSupportEveryStructuralEdit()
    {
        PdfSourceDocument a = Source(), b = Source();
        DocumentEditSession edits = new(a);
        edits.Import(b, 0, 3, 3, edits.State.Pages[0].Id);
        DocumentPageId first = edits.State.Pages[3].Id;
        edits.Import(b, 0, 1, 0, first);
        DocumentPageId duplicate = edits.State.Pages[0].Id;
        Assert.NotEqual(first, duplicate);
        Assert.Equal(first.Source, duplicate.Source);
        Assert.NotEqual(edits.State.Pages[1].Source, duplicate.Source);
        Assert.True(edits.Execute(new MovePageEdit(first, 1)));
        Assert.Equal(first, edits.State.Pages[1].Id);
        Assert.True(edits.Execute(new RotatePageEdit(first, true)));
        Assert.Equal(90, edits.State.Pages[1].StructuralRotation.Degrees);
        Assert.True(edits.Execute(new DeletePageEdit(first)));
        Assert.Null(edits.State.LogicalIndex(first));
        edits.Undo();
        Assert.Equal(first, edits.State.Pages[1].Id);
        Assert.Equal(90, edits.State.Pages[1].StructuralRotation.Degrees);
        Assert.Equal(2, edits.Sources.Sources.Count);
    }

    [Theory]
    [InlineData(-1, 1, 0)]
    [InlineData(0, 0, 0)]
    [InlineData(2, 2, 0)]
    [InlineData(0, 1, -1)]
    [InlineData(0, 1, 4)]
    public void InvalidImportLeavesStateRegistryHistoryAndBaselineUntouched(int first, int count, int position)
    {
        DocumentEditSession edits = new(Source());
        var before = edits.State;
        Assert.Throws<ArgumentOutOfRangeException>(() => edits.Import(Source(), first, count, position, before.Pages[0].Id));
        Assert.Same(before, edits.State);
        Assert.Single(edits.Sources.Sources);
        Assert.False(edits.IsDirty);
        Assert.False(edits.CanUndo);
        Assert.Equal(0, edits.EditGeneration);
    }

    [Fact]
    public void OriginalBookmarkCannotResolveToReimportedInstance()
    {
        PdfSourceDocument a = Source();
        DocumentEditSession edits = new(a);
        DocumentPageId bookmark = edits.State.Pages[0].Id;
        edits.Import(a, 0, 1, 0, bookmark);
        edits.Execute(new DeletePageEdit(bookmark));
        Assert.Null(edits.State.LogicalIndex(new(a.Id, 0)));
        Assert.Equal(0, edits.State.Pages[0].SourcePageIndex);
        Assert.NotEqual(bookmark, edits.State.Pages[0].Id);
    }

    [Fact]
    public void LargeImportRetainsOnlyMetadataAndCompactSingleHistoryEntry()
    {
        DocumentEditSession edits = new(Source());
        PdfSourceDocument large = Source(1001);
        edits.Import(large, 0, large.PageCount, 3, edits.State.Pages[0].Id);
        Assert.Equal(1004, edits.State.Pages.Count);
        Assert.Equal(1, edits.UndoCount);
        Assert.Equal(1001, edits.Sources.Sources[large.Id].Pages.Count);
        Assert.Equal(1004, edits.CreateMaterializationPlan().Pages.Count);
    }

    [Fact]
    public void ConflictingSourceIdentityCannotPublishAnEditAndPlansRequireMultiSourceDescriptors()
    {
        PdfSourceDocument a = Source(), b = Source();
        DocumentEditSession edits = new(a);
        var before = edits.State;
        var conflict = new PdfSourceDocument(a.Id, "different.pdf", b.Pages);
        Assert.Throws<ArgumentException>(() => edits.Import(conflict, 0, 1, 0, before.Pages[0].Id));
        Assert.Same(before, edits.State);
        Assert.Single(edits.Sources.Sources);
        Assert.False(edits.IsDirty);
        edits.Import(b, 0, 1, 1, before.Pages[0].Id);
        Assert.Throws<ArgumentException>(() => PdfMaterializationPlan.From(edits.State));
        Assert.Equal(2, edits.CreateMaterializationPlan().Sources.Count);
    }
}
