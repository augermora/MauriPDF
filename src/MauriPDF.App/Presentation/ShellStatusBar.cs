namespace MauriPDF.App.Presentation;

/// <summary>Reserve space for navigation/mode before giving remaining width to the status message.</summary>
internal sealed class ShellStatusBar : UserControl
{
    private readonly Label _message = new() { Dock = DockStyle.Fill, AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft };
    private readonly Label _zoom = new() { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter };
    private readonly Label _mode = new() { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter };
    private readonly ToolTip _tooltip = new();
    private readonly TableLayoutPanel _table;
    private readonly ToolStrip _pages;
    private bool _arranging;
    private bool _narrow;

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
        ToolStrip pages = _pages = new() { Dock = DockStyle.Fill, GripStyle = ToolStripGripStyle.Hidden, TabStop = true,
            BackColor = MauriPdfTheme.Status, Renderer = new ShellStripRenderer(), Padding = Padding.Empty };
        pages.Items.Add(new ToolStripLabel("Page"));
        pages.Items.AddRange(navigation);
        table.Controls.Add(_message, 0, 0); table.Controls.Add(pages, 1, 0);
        table.Controls.Add(_zoom, 2, 0); table.Controls.Add(_mode, 3, 0);
        Controls.Add(table);
        SetMessage("Ready — open a local PDF");
        AccessibleName = "Document status and page navigation";
        _zoom.AccessibleName = "Zoom"; _mode.AccessibleName = "Display mode";
        _zoom.AutoEllipsis = _mode.AutoEllipsis = true;
    }

    public void SetMessage(string? message)
    {
        _message.Text = string.IsNullOrEmpty(message) ? "Ready" : message;
        _tooltip.SetToolTip(_message, _message.Text);
    }
    public void SetView(string zoom, string mode)
    {
        _zoom.Text = zoom; _mode.Text = mode;
        _tooltip.SetToolTip(_zoom, zoom); _tooltip.SetToolTip(_mode, mode);
    }
    public void ApplyTheme()
    {
        BackColor = _pages.BackColor = MauriPdfTheme.Status;
        _table.BackColor = _message.BackColor = _zoom.BackColor = _mode.BackColor = MauriPdfTheme.Status;
        ForeColor = _pages.ForeColor = MauriPdfTheme.Ink;
        Invalidate(true);
    }
    private void UpdateMetrics()
    {
        if (_arranging) return;
        _arranging = true;
        try
        {
            float scale = DeviceDpi / 96F;
            bool narrow = ClientSize.Width < DisplayMetrics.Scale(620, DeviceDpi);
            if (_narrow != narrow)
            {
                _narrow = narrow;
                _table.SuspendLayout();
                _table.Controls.Clear();
                _table.RowStyles.Clear();
                _table.RowCount = narrow ? 2 : 1;
                _table.RowStyles.Add(new(SizeType.Percent, 50));
                if (narrow) _table.RowStyles.Add(new(SizeType.Percent, 50));
                _table.Controls.Add(_message, 0, 0); _table.SetColumnSpan(_message, narrow ? 4 : 1);
                _table.Controls.Add(_pages, narrow ? 0 : 1, narrow ? 1 : 0); _table.SetColumnSpan(_pages, narrow ? 2 : 1);
                _table.Controls.Add(_zoom, 2, narrow ? 1 : 0); _table.Controls.Add(_mode, 3, narrow ? 1 : 0);
                _table.ResumeLayout();
            }
            Height = DisplayMetrics.Scale(narrow ? 56 : MauriPdfTheme.StatusHeight, DeviceDpi);
            _table.ColumnStyles[1].Width = narrow ? 0 : 190 * scale;
            _table.ColumnStyles[2].Width = (narrow ? 100 : 156) * scale;
            _table.ColumnStyles[3].Width = 110 * scale;
            foreach (ToolStripTextBox editor in _pages.Items.OfType<ToolStripTextBox>()) editor.Width = DisplayMetrics.Scale(55, DeviceDpi);
            Padding = new Padding(DisplayMetrics.Scale(10, DeviceDpi), DisplayMetrics.Scale(2, DeviceDpi), DisplayMetrics.Scale(10, DeviceDpi), DisplayMetrics.Scale(2, DeviceDpi));
        }
        finally { _arranging = false; }
    }
    protected override void OnResize(EventArgs e) { base.OnResize(e); if (_table is not null) UpdateMetrics(); }
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
