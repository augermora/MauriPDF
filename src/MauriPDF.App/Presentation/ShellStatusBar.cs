namespace MauriPDF.App.Presentation;

/// <summary>Reserve space for navigation/mode before giving remaining width to the status message.</summary>
internal sealed class ShellStatusBar : UserControl
{
    private readonly Label _message = new() { Dock = DockStyle.Fill, AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft };
    private readonly Label _zoom = new() { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter };
    private readonly Label _mode = new() { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter };
    private readonly ToolTip _tooltip = new();
    private readonly TableLayoutPanel _table;

    public ShellStatusBar(MauriPdfTheme theme, params ToolStripItem[] navigation)
    {
        Dock = DockStyle.Bottom; Height = MauriPdfTheme.StatusHeight;
        AutoScaleMode = AutoScaleMode.Dpi; AutoScaleDimensions = new SizeF(96, 96);
        BackColor = MauriPdfTheme.Status; ForeColor = MauriPdfTheme.Muted; Font = theme.Body;
        Padding = new Padding(10, 2, 10, 2);
        TableLayoutPanel table = _table = new() { Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 1, Margin = Padding.Empty };
        table.ColumnStyles.Add(new(SizeType.Percent, 100));
        table.ColumnStyles.Add(new(SizeType.Absolute, 190));
        table.ColumnStyles.Add(new(SizeType.Absolute, 156));
        table.ColumnStyles.Add(new(SizeType.Absolute, 100));
        ToolStrip pages = new() { Dock = DockStyle.Fill, GripStyle = ToolStripGripStyle.Hidden,
            BackColor = MauriPdfTheme.Status, Renderer = new ShellStripRenderer(), Padding = Padding.Empty };
        pages.Items.Add(new ToolStripLabel("Page"));
        pages.Items.AddRange(navigation);
        table.Controls.Add(_message, 0, 0); table.Controls.Add(pages, 1, 0);
        table.Controls.Add(_zoom, 2, 0); table.Controls.Add(_mode, 3, 0);
        Controls.Add(table);
        SetMessage("Ready — open a local PDF");
    }

    public void SetMessage(string? message)
    {
        _message.Text = string.IsNullOrEmpty(message) ? "Ready" : message;
        _tooltip.SetToolTip(_message, _message.Text);
    }
    public void SetView(string zoom, string mode) { _zoom.Text = zoom; _mode.Text = mode; }
    private void UpdateMetrics()
    {
        float scale = DeviceDpi / 96F;
        Height = (int)(MauriPdfTheme.StatusHeight * scale);
        _table.ColumnStyles[1].Width = 190 * scale;
        _table.ColumnStyles[2].Width = 156 * scale;
        _table.ColumnStyles[3].Width = 110 * scale;
    }
    protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); UpdateMetrics(); }
    protected override void OnDpiChangedAfterParent(EventArgs e) { base.OnDpiChangedAfterParent(e); UpdateMetrics(); }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        using Pen pen = new(MauriPdfTheme.Border);
        e.Graphics.DrawLine(pen, 0, 0, Width, 0);
    }
    protected override void Dispose(bool disposing) { if (disposing) _tooltip.Dispose(); base.Dispose(disposing); }
}
