namespace MauriPDF.App.Presentation;

internal sealed class EmptyDocumentView : UserControl
{
    private readonly FlowLayoutPanel _content = new() { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
        FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(24) };

    public EmptyDocumentView(MauriPdfTheme theme, CommandIcons icons, ToolStripItem open)
    {
        Dock = DockStyle.Fill;
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(96, 96);
        BackColor = MauriPdfTheme.Background;
        _content.BackColor = MauriPdfTheme.Panel;
        _content.Controls.Add(new PictureBox { Image = icons.Get(CommandIcon.SinglePage, 56),
            SizeMode = PictureBoxSizeMode.CenterImage, Size = new(64, 64), BackColor = MauriPdfTheme.Selected,
            Margin = new Padding(0, 0, 0, 18) });
        _content.Controls.Add(new Label { Text = "Make room for your ideas.", Font = theme.WelcomeTitle,
            ForeColor = MauriPdfTheme.Ink, AutoSize = true, Margin = new Padding(0, 0, 0, 8) });
        _content.Controls.Add(new Label { Text = "Read, arrange and print your PDFs.\nEverything starts with a file on your computer.",
            Font = theme.Body, ForeColor = MauriPdfTheme.Muted, AutoSize = true, Margin = new Padding(0, 0, 0, 22) });
        Button button = new() { Text = "Open PDF     Ctrl+O", AccessibleName = "Open PDF", Font = theme.Heading,
            BackColor = MauriPdfTheme.Accent, ForeColor = Color.White, FlatStyle = FlatStyle.Flat,
            Size = new(188, 38), Margin = new Padding(0, 0, 0, 20) };
        button.FlatAppearance.BorderSize = 0;
        button.Click += (_, _) => open.PerformClick();
        _content.Controls.Add(button);
        _content.Controls.Add(new Label { Text = "LOCAL FILES   /   NO ACCOUNTS   /   YOUR CONTROL", Font = theme.Caption,
            ForeColor = MauriPdfTheme.Muted, AutoSize = true, Margin = Padding.Empty });
        Controls.Add(_content);
    }

    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);
        _content.Location = new(Math.Max(8, (ClientSize.Width - _content.Width) / 2), Math.Max(8, (ClientSize.Height - _content.Height) / 2));
    }
}
