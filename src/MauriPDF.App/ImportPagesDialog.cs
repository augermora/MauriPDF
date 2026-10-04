using MauriPDF.App.Presentation;

namespace MauriPDF.App;

internal sealed class ImportPagesDialog : Form
{
    private readonly CheckBox _all = new() { Text = "All pages", Checked = true, AutoSize = true };
    private readonly NumericUpDown _first = new() { Minimum = 1, Value = 1, Width = 90, AccessibleName = "First source page" };
    private readonly NumericUpDown _last = new() { Minimum = 1, Value = 1, Width = 90, AccessibleName = "Last source page" };
    private readonly ComboBox _position = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 240, AccessibleName = "Insertion position" };
    public ImportPagesDialog(int count, MauriPdfTheme theme)
    {
        Text = "Insert pages"; Font = theme.Body;
        AutoScaleMode = AutoScaleMode.Dpi; AutoScaleDimensions = new SizeF(96, 96);
        FormBorderStyle = FormBorderStyle.FixedDialog; StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = MinimizeBox = false; ShowInTaskbar = false;
        AutoSize = true; AutoSizeMode = AutoSizeMode.GrowAndShrink;
        FlowLayoutPanel panel = new() { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(16) };
        _first.Maximum = _last.Maximum = count; _last.Value = count;
        _first.Enabled = _last.Enabled = false;
        _all.CheckedChanged += (_, _) => _first.Enabled = _last.Enabled = !_all.Checked;
        _position.Items.AddRange(["Before current page", "After current page", "Beginning", "End"]);
        _position.SelectedIndex = 1;
        panel.Controls.Add(new Label { Text = $"Source PDF: {count:N0} pages", AutoSize = true });
        panel.Controls.Add(_all);
        FlowLayoutPanel range = new() { AutoSize = true };
        range.Controls.AddRange([new Label { Text = "From", AutoSize = true }, _first, new Label { Text = "to", AutoSize = true }, _last]);
        panel.Controls.Add(range);
        panel.Controls.Add(new Label { Text = "Insert at", AutoSize = true }); panel.Controls.Add(_position);
        FlowLayoutPanel buttons = new() { AutoSize = true };
        Button insert = new() { Text = "Insert pages", AutoSize = true };
        Button cancel = new() { Text = "Cancel", AutoSize = true, DialogResult = DialogResult.Cancel };
        insert.Click += (_, _) =>
        {
            if (!_all.Checked && _last.Value < _first.Value)
            { MessageBox.Show(this, "The last page must not precede the first page.", "Insert pages"); return; }
            DialogResult = DialogResult.OK;
        };
        buttons.Controls.AddRange([insert, cancel]); panel.Controls.Add(buttons); Controls.Add(panel);
        AcceptButton = insert; CancelButton = cancel;
    }
    public int First => _all.Checked ? 0 : (int)_first.Value - 1;
    public int Count => _all.Checked ? (int)_last.Maximum : (int)_last.Value - (int)_first.Value + 1;
    public int InsertionIndex(int current, int total) => _position.SelectedIndex switch { 0 => current, 1 => current + 1, 2 => 0, _ => total };
}
