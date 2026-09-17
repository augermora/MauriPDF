namespace MauriPDF.App;

internal sealed class DiscardChangesDialog : Form
{
    public DiscardChangesDialog()
    {
        Text = "Discard unsaved changes?";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = MinimizeBox = false;
        ShowInTaskbar = false;
        ClientSize = new Size(400, 140);
        Label message = new()
        {
            Text = "These page edits exist only in memory. Saving is not available yet. Discard them?",
            Location = new Point(16, 16), Size = new Size(368, 60)
        };
        Button discard = new() { Text = "Discard", DialogResult = DialogResult.OK, Location = new Point(208, 96), Size = new Size(80, 28) };
        Button cancel = new() { Text = "Cancel", DialogResult = DialogResult.Cancel, Location = new Point(300, 96), Size = new Size(80, 28) };
        Controls.AddRange([message, discard, cancel]);
        AcceptButton = cancel;
        CancelButton = cancel;
        ActiveControl = cancel;
    }
}
