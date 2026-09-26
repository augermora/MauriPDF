namespace MauriPDF.App;

internal enum SaveConflictChoice { Cancel, OverwriteOrRecreate, SaveAs }

internal sealed class SaveConflictDialog : Form
{
    public SaveConflictDialog(bool missing)
    {
        Text = "MauriPDF";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = MinimizeBox = false;
        ClientSize = new Size(450, 145);
        Label message = new()
        {
            AutoSize = false, Location = new Point(16, 15), Size = new Size(418, 65),
            Text = missing
                ? "The saved PDF no longer exists. Recreate it, choose another location, or cancel."
                : "The saved PDF changed outside MauriPDF. Overwrite it, choose another location, or cancel."
        };
        Button primary = new() { Text = missing ? "Recreate" : "Overwrite", Location = new Point(128, 96), Size = new Size(95, 30) };
        Button saveAs = new() { Text = "Save As...", Location = new Point(229, 96), Size = new Size(95, 30) };
        Button cancel = new() { Text = "Cancel", Location = new Point(330, 96), Size = new Size(95, 30), DialogResult = DialogResult.Cancel };
        primary.Click += (_, _) => { Choice = SaveConflictChoice.OverwriteOrRecreate; DialogResult = DialogResult.OK; };
        saveAs.Click += (_, _) => { Choice = SaveConflictChoice.SaveAs; DialogResult = DialogResult.OK; };
        Controls.AddRange([message, primary, saveAs, cancel]);
        AcceptButton = primary;
        CancelButton = cancel;
    }

    public SaveConflictChoice Choice { get; private set; }
}
