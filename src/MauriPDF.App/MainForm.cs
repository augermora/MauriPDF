using MauriPDF.Core.Rendering;

namespace MauriPDF.App;

internal sealed class MainForm : Form
{
    private const double ScreenDpi = 96;
    private const double PdfPointsPerInch = 72;

    private readonly IPdfRenderer _renderer;
    private readonly ToolStripLabel _pageCountLabel = new("No document open");
    private readonly PictureBox _pageView = new()
    {
        BackColor = Color.DarkGray,
        SizeMode = PictureBoxSizeMode.Normal
    };

    private IPdfRenderSession? _session;

    public MainForm(IPdfRenderer renderer)
    {
        _renderer = renderer;

        Text = "MauriPDF";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(800, 600);

        ToolStripButton openButton = new("Open PDF");
        openButton.Click += OpenButton_Click;

        ToolStrip toolStrip = new();
        toolStrip.Items.Add(openButton);
        toolStrip.Items.Add(new ToolStripSeparator());
        toolStrip.Items.Add(_pageCountLabel);

        Panel viewport = new()
        {
            AutoScroll = true,
            BackColor = Color.DarkGray,
            Dock = DockStyle.Fill
        };
        viewport.Controls.Add(_pageView);

        Controls.Add(viewport);
        Controls.Add(toolStrip);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            CloseDocument();
        }

        base.Dispose(disposing);
    }

    private void OpenButton_Click(object? sender, EventArgs e)
    {
        using OpenFileDialog dialog = new()
        {
            CheckFileExists = true,
            Filter = "PDF documents (*.pdf)|*.pdf|All files (*.*)|*.*",
            Title = "Open PDF"
        };

        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        OpenDocument(dialog.FileName);
    }

    private void OpenDocument(string filePath)
    {
        CloseDocument();

        try
        {
            _session = _renderer.Open(filePath);
            PdfPageSize pageSize = _session.GetPageSize(0);
            int pixelWidth = checked((int)Math.Ceiling(pageSize.WidthPoints * ScreenDpi / PdfPointsPerInch));
            int pixelHeight = checked((int)Math.Ceiling(pageSize.HeightPoints * ScreenDpi / PdfPointsPerInch));

            using RenderedPage renderedPage = _session.RenderPage(0, pixelWidth, pixelHeight);
            _pageView.Image = WinFormsImageConverter.CreateBitmap(renderedPage);
            _pageView.Size = _pageView.Image.Size;
            _pageCountLabel.Text = $"{_session.PageCount} page{(_session.PageCount == 1 ? string.Empty : "s")}";
            Text = $"MauriPDF — {Path.GetFileName(filePath)}";
        }
        catch (Exception exception)
        {
            CloseDocument();
            MessageBox.Show(
                this,
                $"MauriPDF could not open this PDF.\n\n{exception.Message}",
                "Open PDF",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    private void CloseDocument()
    {
        Image? previousImage = _pageView.Image;
        _pageView.Image = null;
        previousImage?.Dispose();

        _session?.Dispose();
        _session = null;

        _pageView.Size = Size.Empty;
        _pageCountLabel.Text = "No document open";
        Text = "MauriPDF";
    }
}
