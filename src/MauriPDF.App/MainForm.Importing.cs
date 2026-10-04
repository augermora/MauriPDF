using MauriPDF.Core.Documents;
using MauriPDF.Editing;

namespace MauriPDF.App;

internal sealed partial class MainForm
{
    private readonly ToolStripButton _insertPages = new("Insert Pages") { AccessibleName = "Insert pages from another PDF", ToolTipText = "Insert all pages or a contiguous range from a local PDF" };
    private bool _importing;

    private async Task ChooseImportAsync()
    {
        if (DocumentBusy || _closing || _edits is null || _state is null) return;
        using OpenFileDialog dialog = new() { Filter = "PDF documents (*.pdf)|*.pdf", CheckFileExists = true };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        await ImportPagesAsync(dialog.FileName, null);
    }

    // Explicit options are also used by the local smoke harness; normal UI always presents the dialog.
    private async Task ImportPagesAsync(string path, (int First, int Count, int Position)? options)
    {
        if (DocumentBusy || _closing || _resourcesDisposed || _edits is null || _state is null) return;
        DocumentEditSession edits = _edits;
        long generation = _requestId;
        PdfSourceDocument? source = null;
        bool prepared = false, committed = false;
        _importing = true; UpdateToolbar(); _loading.Text = "Importing pages…";
        try
        {
            source = await Task.Run(() => edits.Sources.FindPath(path));
            if (_resourcesDisposed || generation != _requestId || !ReferenceEquals(edits, _edits)) return;
            if (source is null)
            {
                source = await _renderer.PrepareSourceAsync(path, Guid.NewGuid());
                prepared = true;
            }
            if (_resourcesDisposed || generation != _requestId || !ReferenceEquals(edits, _edits)) return;
            edits.Sources.ValidateAddition(source);
            if (!options.HasValue)
            {
                using ImportPagesDialog dialog = new(source.PageCount, _theme);
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                options = (dialog.First, dialog.Count, dialog.InsertionIndex(_state.PageIndex, edits.State.Pages.Count));
            }
            EditedDocumentState before = edits.State;
            edits.Import(source, options.Value.First, options.Value.Count, options.Value.Position, before.Pages[_state.PageIndex].Id);
            committed = true;
            RefreshEditedDocument(before);
            _viewport.ApplyState(_state!, navigate: true);
        }
        catch (OperationCanceledException) { }
        catch (Exception exception)
        {
            if (!_resourcesDisposed) MessageBox.Show(this, $"Could not insert pages.\n\n{exception.Message}", "MauriPDF", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            if (prepared && !committed && source is not null)
            {
                try { await _renderer.ReleaseSourceAsync(source.Id); }
                catch (Exception exception) when (exception is ObjectDisposedException or OperationCanceledException) { }
            }
            _importing = false;
            if (!_resourcesDisposed) { _loading.Text = committed ? "Pages inserted" : string.Empty; UpdateToolbar(); }
        }
    }
}
