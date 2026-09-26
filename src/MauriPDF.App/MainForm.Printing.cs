using MauriPDF.App.Printing;
using MauriPDF.Editing;

namespace MauriPDF.App;

internal sealed partial class MainForm
{
    private readonly IPdfPrintWorkflow _printer;
    private readonly ToolStripButton _print = new("Print") { ToolTipText = "Print document (Ctrl+P)" };
    private bool _printing;
    private bool DocumentBusy => _saving || _printing;

    private async Task PrintAsync()
    {
        if (DocumentBusy || _closing || _resourcesDisposed || _edits is null || _sourcePath is null || _state is null) return;
        PdfPrintRequest request = new(_sourcePath, Path.GetFileName(_savedDocumentPath ?? _sourcePath),
            PdfMaterializationPlan.From(_edits.State), _state.PageIndex);
        _printing = true;
        _loading.Text = "Printing…";
        UpdateToolbar();
        try
        {
            bool submitted = await _printer.PrintAsync(this, request);
            if (!_resourcesDisposed) _loading.Text = submitted ? "Sent to printer" : "Printing cancelled";
        }
        catch (Exception exception)
        {
            if (!_resourcesDisposed)
            {
                _loading.Text = "Print failed";
                MessageBox.Show(this, $"MauriPDF could not print the document.\n\n{exception.Message}",
                    "MauriPDF", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
        finally
        {
            _printing = false;
            if (!_resourcesDisposed) UpdateToolbar();
        }
    }
}
