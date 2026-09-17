using MauriPDF.Core.Documents;
using MauriPDF.Core.Rendering;
using MauriPDF.Core.Viewing;
using Xunit;

namespace MauriPDF.Editing.Tests;

public sealed class DocumentEditingTests
{
    [Fact]
    public void StableIdentityAndBidirectionalMappingsFollowEditedSequence()
    {
        DocumentEditSession edits = new(5);
        EditedDocumentState original = edits.State;
        DocumentPageId e = original.Pages[4].Id, c = original.Pages[2].Id;
        Assert.True(edits.Execute(new MovePageEdit(e, 1)));
        Assert.True(edits.Execute(new DeletePageEdit(c)));
        Assert.Equal([0, 4, 1, 3], edits.State.Pages.Select(page => page.SourcePageIndex));
        Assert.Equal(1, edits.State.LogicalIndex(e));
        Assert.Equal(2, edits.State.LogicalIndexForSource(1));
        Assert.Null(edits.State.LogicalIndexForSource(2));
        Assert.Equal([0, 1, 2, 3, 4], original.Pages.Select(page => page.SourcePageIndex));
        Assert.Equal(original.Pages[0].Id, edits.State.Pages[0].Id);
        Assert.Throws<NotSupportedException>(() => ((IList<LogicalPageReference>)original.Pages).Clear());
        Assert.False(edits.Execute(new DeletePageEdit(new DocumentEditSession(5).State.Pages[0].Id)));
    }

    [Theory]
    [InlineData(0, 0, 1)]
    [InlineData(1, 1, 2)]
    [InlineData(2, 2, 1)]
    [InlineData(0, 2, 2)]
    public void DeletePreservesCurrentIdentityOrChoosesFollowingThenPrevious(int deleted, int current, int expectedSource)
    {
        DocumentEditSession edits = new(3);
        EditedDocumentState before = edits.State;
        Assert.True(edits.Execute(new DeletePageEdit(before.Pages[deleted].Id)));
        int resolved = edits.State.ResolveCurrentPage(before, current);
        Assert.Equal(expectedSource, edits.State.Pages[resolved].SourcePageIndex);
        Assert.True(edits.IsDirty);
        Assert.True(edits.Undo());
        Assert.Equal(before.Pages, edits.State.Pages);
        Assert.False(edits.IsDirty);
        Assert.True(edits.Redo());
        Assert.Null(edits.State.LogicalIndexForSource(deleted));
        Assert.True(edits.IsDirty);
    }

    [Fact]
    public void CannotDeleteFinalPageOrRecordNoOpHistory()
    {
        DocumentEditSession edits = new(1);
        DocumentPageId id = edits.State.Pages[0].Id;
        Assert.False(edits.Execute(new DeletePageEdit(id)));
        Assert.False(edits.Execute(new MovePageEdit(id, -1)));
        Assert.False(edits.Execute(new MovePageEdit(id, 1)));
        Assert.False(edits.Execute(new MovePageEdit(id, 0)));
        Assert.False(edits.IsDirty);
        Assert.False(edits.CanUndo);
        Assert.False(edits.Undo());
        Assert.False(edits.Redo());
    }

    [Theory]
    [InlineData(0, 2)]
    [InlineData(2, 0)]
    [InlineData(1, 0)]
    [InlineData(1, 2)]
    public void MoveKeepsCurrentStablePageAndIsReversible(int from, int to)
    {
        DocumentEditSession edits = new(3);
        EditedDocumentState before = edits.State;
        DocumentPageId id = before.Pages[from].Id;
        Assert.True(edits.Execute(new MovePageEdit(id, to)));
        Assert.Equal(id, edits.State.Pages[to].Id);
        Assert.Equal(to, edits.State.ResolveCurrentPage(before, from));
        Assert.True(edits.Undo());
        Assert.Equal(before.Pages, edits.State.Pages);
        Assert.True(edits.Redo());
        Assert.Equal(to, edits.State.LogicalIndex(id));
    }

    [Theory]
    [InlineData(true, 90)]
    [InlineData(false, 270)]
    public void StructuralRotationIsPerPageUndoableAndDoesNotChangeSequence(bool clockwise, int expected)
    {
        DocumentEditSession edits = new(3);
        EditedDocumentState before = edits.State;
        DocumentPageId id = before.Pages[1].Id;
        Assert.True(edits.Execute(new RotatePageEdit(id, clockwise)));
        Assert.Equal(expected, edits.State.Pages[1].StructuralRotation.Degrees);
        Assert.Equal(0, edits.State.Pages[0].StructuralRotation.Degrees);
        Assert.True(edits.State.HasSameSequence(before));
        Assert.True(edits.Undo());
        Assert.Equal(0, edits.State.Pages[1].StructuralRotation.Degrees);
        Assert.False(edits.IsDirty);
        Assert.True(edits.Redo());
        Assert.Equal(expected, edits.State.Pages[1].StructuralRotation.Degrees);
    }

    [Fact]
    public void StructuralRotationNormalizesAndComposesAllThreeContributors()
    {
        Assert.Equal(270, new StructuralPageRotation(-450).Degrees);
        Assert.Equal(90, new StructuralPageRotation(810).Degrees);
        Assert.Throws<ArgumentOutOfRangeException>(() => new StructuralPageRotation(1));
        for (int intrinsic = 0; intrinsic < 360; intrinsic += 90)
            for (int structural = 0; structural < 360; structural += 90)
                for (int visual = 0; visual < 360; visual += 90)
                {
                    StructuralPageRotation edit = new(structural);
                    VisualRotation view = new(visual);
                    VisualRotation device = edit.Compose(view);
                    Assert.Equal((structural + visual) % 360, device.Degrees);
                    Assert.Equal((intrinsic + structural + visual) % 360, new VisualRotation(intrinsic + device.Degrees).Degrees);
                    PdfPageSize sourceSize = new VisualRotation(intrinsic).EffectiveSize(new(200, 300));
                    Assert.Equal(new VisualRotation(intrinsic + structural + visual).EffectiveSize(new(200, 300)), device.EffectiveSize(sourceSize));
                    Assert.Equal(edit, edit.Clockwise().CounterClockwise());
                }
    }

    [Fact]
    public void MultipleEditsUndoToBaselineRedoAndDivergentBranchAreDeterministic()
    {
        DocumentEditSession edits = new(5);
        EditedDocumentState original = edits.State;
        edits.Execute(new MovePageEdit(original.Pages[4].Id, 1));
        edits.Execute(new DeletePageEdit(original.Pages[2].Id));
        edits.Execute(new RotatePageEdit(original.Pages[3].Id, true));
        EditedDocumentState final = edits.State;
        Assert.Equal([0, 4, 1, 3], final.Pages.Select(page => page.SourcePageIndex));
        for (int index = 0; index < 3; index++) Assert.True(edits.Undo());
        Assert.Equal(original.Pages, edits.State.Pages);
        Assert.False(edits.IsDirty);
        Assert.Equal(6, edits.EditGeneration);
        for (int index = 0; index < 3; index++) Assert.True(edits.Redo());
        Assert.Equal(final.Pages, edits.State.Pages);
        Assert.Equal(final.Revision, edits.State.Revision);
        Assert.Equal(9, edits.EditGeneration);
        edits.Undo();
        edits.Execute(new RotatePageEdit(original.Pages[0].Id, false));
        Assert.False(edits.CanRedo);
        Assert.NotEqual(final.Revision, edits.State.Revision);
        Assert.Equal(0, edits.State.Pages[^1].StructuralRotation.Degrees);
    }

    [Fact]
    public void BoundedHistoryDoesNotConfuseEmptyUndoWithCleanBaseline()
    {
        DocumentEditSession edits = new(1001);
        DocumentPageId id = edits.State.Pages[500].Id;
        for (int index = 0; index < 125; index++) edits.Execute(new RotatePageEdit(id, true));
        Assert.Equal(DocumentEditSession.MaximumHistory, edits.UndoCount);
        for (int index = 0; index < 100; index++) Assert.True(edits.Undo());
        Assert.False(edits.CanUndo);
        Assert.True(edits.IsDirty); // The original baseline is older than the retained history.
        Assert.Equal(25, edits.State.Revision);
        Assert.Equal(100, edits.RedoCount);
        for (int index = 0; index < 100; index++) Assert.True(edits.Redo());
        Assert.Equal(1001, edits.State.Pages.Count);
        Assert.Equal(125, edits.State.Revision);
    }

    [Fact]
    public void ViewOnlyActionsLeaveEditBaselineUntouchedAndReplacementIsFresh()
    {
        DocumentEditSession edits = new(3);
        ViewerState view = new ViewerState(3).RotateClockwise().SetZoom(300).SetDisplayMode(ViewerDisplayMode.SinglePage).GoToPage(2);
        Assert.False(edits.IsDirty);
        Assert.Equal(0, edits.EditGeneration);
        DocumentPageId id = edits.State.Pages[1].Id;
        edits.Execute(new RotatePageEdit(id, true));
        Assert.Equal(180, edits.State.Pages[1].DisplayRotation(view.Rotation).Degrees);
        DocumentEditSession replacement = new(3);
        Assert.NotEqual(edits.State.SourceDocumentId, replacement.State.SourceDocumentId);
        Assert.False(replacement.IsDirty);
        Assert.False(replacement.CanUndo);
        Assert.All(replacement.State.Pages, page => Assert.Equal(0, page.StructuralRotation.Degrees));
    }

    [Fact]
    public void SavedRevisionBecomesCleanAndUndoRedoCrossesThatBaseline()
    {
        DocumentEditSession edits = new(3);
        edits.Execute(new DeletePageEdit(edits.State.Pages[1].Id));
        long savedRevision = edits.State.Revision;
        edits.MarkSavedBaseline(savedRevision);
        Assert.False(edits.IsDirty);
        Assert.True(edits.Undo());
        Assert.True(edits.IsDirty);
        Assert.True(edits.Redo());
        Assert.False(edits.IsDirty);
        edits.Execute(new RotatePageEdit(edits.State.Pages[0].Id, true));
        Assert.True(edits.IsDirty);
        edits.MarkSavedBaseline(edits.State.Revision);
        Assert.False(edits.IsDirty);
        Assert.Throws<InvalidOperationException>(() => edits.MarkSavedBaseline(savedRevision));
    }

    [Fact]
    public void MaterializationPlanCopiesLogicalOrderAndStructuralRotationOnly()
    {
        DocumentEditSession edits = new(4);
        DocumentPageId last = edits.State.Pages[3].Id;
        edits.Execute(new MovePageEdit(last, 0));
        edits.Execute(new DeletePageEdit(edits.State.Pages[2].Id));
        edits.Execute(new RotatePageEdit(last, true));
        ViewerState ignoredView = new ViewerState(3).RotateClockwise().RotateClockwise();
        PdfMaterializationPlan plan = PdfMaterializationPlan.From(edits.State);
        Assert.Equal([3, 0, 2], plan.Pages.Select(page => page.SourcePageIndex));
        Assert.Equal([90, 0, 0], plan.Pages.Select(page => page.StructuralRotationDegrees));
        Assert.Equal(180, ignoredView.Rotation.Degrees);
        Assert.Equal(edits.State.Revision, plan.Revision);
    }

    [Fact]
    public void LargeMaterializationPlanIsOnlyLinearLightweightMetadata()
    {
        DocumentEditSession edits = new(1001);
        edits.Execute(new MovePageEdit(edits.State.Pages[^1].Id, 0));
        edits.Execute(new DeletePageEdit(edits.State.Pages[501].Id));
        PdfMaterializationPlan plan = PdfMaterializationPlan.From(edits.State);
        Assert.Equal(1000, plan.Pages.Count);
        Assert.Equal(1000, plan.Pages[0].SourcePageIndex);
        Assert.Equal(0, plan.Pages[1].SourcePageIndex);
        Assert.All(plan.Pages, page => Assert.Equal(0, page.StructuralRotationDegrees));
    }

    [Theory]
    [InlineData(ViewerDisplayMode.Continuous)]
    [InlineData(ViewerDisplayMode.SinglePage)]
    public void EditedOrderAndStructuralGeometryFeedSharedLayout(ViewerDisplayMode mode)
    {
        DocumentEditSession edits = new(1001);
        DocumentPageId last = edits.State.Pages[1000].Id;
        edits.Execute(new MovePageEdit(last, 0));
        edits.Execute(new RotatePageEdit(last, true));
        edits.Execute(new DeletePageEdit(edits.State.Pages[2].Id));
        PdfPageSize[] sizes = Enumerable.Repeat(new PdfPageSize(200, 300), 1001).ToArray();
        sizes[1000] = new(72, 144);
        ViewerState state = new ViewerState(edits.State.Pages.Count).SetDisplayMode(mode);
        ContinuousPageLayout layout = new(sizes, state, 800, 600, edits.State.Pages);
        Assert.Equal(1000, layout.SourcePageIndex(0));
        Assert.Equal(90, layout.RotationForPage(0).Degrees);
        Assert.Equal(192, layout[0].Width);
        Assert.Equal(96, layout[0].Height);
        Assert.InRange(VisiblePageDemand.Select(layout, layout.ScrollTarget(0, 600), 600).Count, 1, 32);
        Assert.Equal(mode == ViewerDisplayMode.SinglePage ? 1 : 1000, layout.Count);
    }
}
