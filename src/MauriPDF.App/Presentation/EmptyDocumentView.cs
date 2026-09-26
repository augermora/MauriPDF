namespace MauriPDF.App.Presentation;

internal sealed class EmptyDocumentView : UserControl
{
    private readonly CommandIcons _icons;
    private readonly PictureBox _picture;
    private readonly Button _open;
    private bool _layingOut;
    private readonly FlowLayoutPanel _content = new() { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
        FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(24) };

    public EmptyDocumentView(MauriPdfTheme theme, CommandIcons icons, ToolStripItem open)
    {
        _icons = icons;
        Dock = DockStyle.Fill;
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(96, 96);
        BackColor = MauriPdfTheme.Background;
        _content.BackColor = MauriPdfTheme.Panel;
        _content.Controls.Add(_picture = new PictureBox { Image = icons.Get(CommandIcon.SinglePage, 56),
            SizeMode = PictureBoxSizeMode.CenterImage, Size = new(64, 64), BackColor = MauriPdfTheme.Selected,
            Margin = new Padding(0, 0, 0, 18) });
        _content.Controls.Add(new Label { Text = "Make room for your ideas.", Font = theme.WelcomeTitle,
            ForeColor = MauriPdfTheme.Ink, AutoSize = true, Margin = new Padding(0, 0, 0, 8) });
        _content.Controls.Add(new Label { Text = "Read, arrange and print your PDFs.\nEverything starts with a file on your computer.",
            Font = theme.Body, ForeColor = MauriPdfTheme.Muted, AutoSize = true, Margin = new Padding(0, 0, 0, 22) });
        Button button = _open = new() { Text = "Open PDF     Ctrl+O", AccessibleName = "Open PDF", Font = theme.Heading,
            BackColor = MauriPdfTheme.ActionBackground, ForeColor = MauriPdfTheme.ActionText, FlatStyle = FlatStyle.Flat,
            Size = new(188, 38), Margin = new Padding(0, 0, 0, 20) };
        button.FlatAppearance.BorderSize = 0;
        button.Click += (_, _) => open.PerformClick();
        _content.Controls.Add(button);
        _content.Controls.Add(new Label { Text = "LOCAL FILES   /   NO ACCOUNTS   /   YOUR CONTROL", Font = theme.Caption,
            ForeColor = MauriPdfTheme.Muted, AutoSize = true, Margin = Padding.Empty });
        Controls.Add(_content);
        AutoScroll = true;
        TabStop = false;
    }

    public void ApplyTheme()
    {
        BackColor = MauriPdfTheme.Background;
        _content.BackColor = MauriPdfTheme.Panel;
        foreach (Label label in _content.Controls.OfType<Label>()) label.ForeColor = MauriPdfTheme.Ink;
        _picture.BackColor = MauriPdfTheme.Group;
        _open.BackColor = MauriPdfTheme.ActionBackground; _open.ForeColor = MauriPdfTheme.ActionText;
        UpdateMetrics();
    }
    private void UpdateMetrics()
    {
        _picture.Image = _icons.Get(CommandIcon.SinglePage, DisplayMetrics.Scale(56, DeviceDpi));
        _picture.Size = new(DisplayMetrics.Scale(64, DeviceDpi), DisplayMetrics.Scale(64, DeviceDpi));
        _open.Size = new(DisplayMetrics.Scale(188, DeviceDpi), DisplayMetrics.Scale(38, DeviceDpi));
        _content.Padding = new Padding(DisplayMetrics.Scale(24, DeviceDpi));
        PerformLayout();
    }
    protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); UpdateMetrics(); }
    protected override void OnDpiChangedAfterParent(EventArgs e) { base.OnDpiChangedAfterParent(e); UpdateMetrics(); }

    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);
        if (_layingOut || _open is null) return;
        _layingOut = true;
        try
        {
            int gap = DisplayMetrics.Scale(8, DeviceDpi);
            int width = Math.Max(1, ClientSize.Width - gap * 2 - _content.Padding.Horizontal - SystemInformation.VerticalScrollBarWidth);
            foreach (Label label in _content.Controls.OfType<Label>()) label.MaximumSize = new(width, 0);
            _content.Location = new(Math.Max(gap, (ClientSize.Width - _content.Width) / 2), Math.Max(gap, (ClientSize.Height - _content.Height) / 2));
        }
        finally { _layingOut = false; }
    }
}
