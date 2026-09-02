using System.Globalization;
using MauriPDF.Core.Rendering;
using MauriPDF.Core.Viewing;

namespace MauriPDF.App;

internal sealed class MainForm : Form
{
    private readonly IPdfRenderer _renderer;
    private readonly ToolStripButton _previous = new("<") { ToolTipText = "Previous page" };
    private readonly ToolStripButton _next = new(">") { ToolTipText = "Next page" };
    private readonly ToolStripTextBox _pageNumber = new() { AutoSize = false, Width = 55, AccessibleName = "Page number" };
    private readonly ToolStripLabel _totalPages = new("/ 0");
    private readonly ToolStripButton _zoomOut = new("−") { ToolTipText = "Zoom out" };
    private readonly ToolStripButton _zoomIn = new("+") { ToolTipText = "Zoom in" };
    private readonly ToolStripButton _resetZoom = new("100%") { ToolTipText = "Reset to 100%" };
    private readonly ToolStripLabel _zoomLabel = new();
    private readonly ToolStripButton _fitPage = new("Fit Page");
    private readonly ToolStripButton _fitWidth = new("Fit Width");
    private readonly Panel _viewport = new()
    {
        AutoScroll = true, BackColor = Color.DarkGray, Dock = DockStyle.Fill, TabStop = true
    };
    private readonly PictureBox _pageView = new() { SizeMode = PictureBoxSizeMode.Normal, TabStop = false };
    private readonly System.Windows.Forms.Timer _resizeTimer = new() { Interval = 150 };
    private IPdfRenderSession? _session;
    private ViewerState? _state;
    private bool _rendering;
    private bool _resourcesDisposed;

    public MainForm(IPdfRenderer renderer)
    {
        _renderer = renderer;
        Text = "MauriPDF";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(850, 600);

        ToolStripButton open = new("Open PDF");
        open.Click += (_, _) => ChooseDocument();
        _previous.Click += (_, _) => ChangeState(_state?.PreviousPage());
        _next.Click += (_, _) => ChangeState(_state?.NextPage());
        _zoomOut.Click += (_, _) => ChangeState(_state?.ZoomOut());
        _zoomIn.Click += (_, _) => ChangeState(_state?.ZoomIn());
        _resetZoom.Click += (_, _) => ChangeState(_state?.SetZoom(100));
        _fitPage.Click += (_, _) => ChangeState(_state?.SetFitMode(ViewerZoomMode.FitPage));
        _fitWidth.Click += (_, _) => ChangeState(_state?.SetFitMode(ViewerZoomMode.FitWidth));
        _pageNumber.KeyDown += PageNumber_KeyDown;
        _pageNumber.Leave += (_, _) => UpdateToolbar();
        _viewport.Resize += (_, _) => ScheduleFitRender();
        // Focus the scrollable surface so wheel input works after using the toolbar.
        _pageView.MouseDown += (_, _) => _viewport.Focus();
        _resizeTimer.Tick += (_, _) =>
        {
            _resizeTimer.Stop();
            ChangeState(_state, resetScroll: false);
        };

        ToolStrip toolbar = new() { GripStyle = ToolStripGripStyle.Hidden };
        toolbar.Items.AddRange([
            open, new ToolStripSeparator(), _previous, _pageNumber, _totalPages, _next,
            new ToolStripSeparator(), _zoomOut, _resetZoom, _zoomIn, _zoomLabel, _fitPage, _fitWidth
        ]);
        _viewport.Controls.Add(_pageView);
        Controls.Add(_viewport);
        Controls.Add(toolbar);
        UpdateToolbar();
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == (Keys.Control | Keys.O))
        {
            ChooseDocument();
            return true;
        }

        // Preserve normal cursor movement and Home/End while editing the page number.
        if (_pageNumber.Focused && (keyData & Keys.Control) == 0)
        {
            return base.ProcessCmdKey(ref msg, keyData);
        }

        ViewerState? requested = keyData switch
        {
            Keys.Left or Keys.PageUp => _state?.PreviousPage(),
            Keys.Right or Keys.PageDown => _state?.NextPage(),
            Keys.Home => _state?.GoToPage(1),
            Keys.End => _state?.GoToPage(_state.PageCount),
            Keys.Control | Keys.Add or Keys.Control | Keys.Oemplus or Keys.Control | Keys.Shift | Keys.Oemplus => _state?.ZoomIn(),
            Keys.Control | Keys.Subtract or Keys.Control | Keys.OemMinus => _state?.ZoomOut(),
            Keys.Control | Keys.D0 or Keys.Control | Keys.NumPad0 => _state?.SetZoom(100),
            _ => null
        };
        if (requested is not null)
        {
            ChangeState(requested);
            return true;
        }

        return base.ProcessCmdKey(ref msg, keyData);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !_resourcesDisposed)
        {
            _resourcesDisposed = true;
            _resizeTimer.Stop();
            CloseDocument();
            _resizeTimer.Dispose();
        }

        base.Dispose(disposing);
    }

    private void ChooseDocument()
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

        CloseDocument();
        try
        {
            _session = _renderer.Open(dialog.FileName);
            ViewerState initial = new(_session.PageCount);
            if (ChangeState(initial))
            {
                Text = $"MauriPDF — {Path.GetFileName(dialog.FileName)}";
            }
            else
            {
                CloseDocument();
            }
        }
        catch (Exception exception)
        {
            CloseDocument();
            ShowError(exception);
        }
    }

    private void PageNumber_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode != Keys.Enter)
        {
            return;
        }

        e.SuppressKeyPress = true;
        if (_state is not null && int.TryParse(_pageNumber.Text, out int page))
        {
            ChangeState(_state.GoToPage(page));
        }
        else
        {
            UpdateToolbar();
        }

        _viewport.Focus();
    }

    private bool ChangeState(ViewerState? requested, bool resetScroll = true)
    {
        if (_session is null || requested is null || _rendering)
        {
            return false;
        }

        _resizeTimer.Stop();
        _rendering = true;
        try
        {
            // Recover the full client area independent of the CURRENT bitmap's scrollbars.
            int width = _viewport.ClientSize.Width + (_viewport.VerticalScroll.Visible ? SystemInformation.VerticalScrollBarWidth : 0);
            int height = _viewport.ClientSize.Height + (_viewport.HorizontalScroll.Visible ? SystemInformation.HorizontalScrollBarHeight : 0);
            RenderSize size = RenderSizeCalculator.Calculate(
                _session.GetPageSize(requested.PageIndex), requested, Math.Max(1, width), Math.Max(1, height),
                SystemInformation.VerticalScrollBarWidth);

            if (_state != requested || _pageView.Image is null || _pageView.Image.Width != size.Width || _pageView.Image.Height != size.Height)
            {
                using RenderedPage pixels = _session.RenderPage(requested.PageIndex, size.Width, size.Height);
                Bitmap nextImage = WinFormsImageConverter.CreateBitmap(pixels);
                Image? previousImage = _pageView.Image;
                _pageView.Image = nextImage;
                _pageView.Size = nextImage.Size;
                previousImage?.Dispose();
            }

            _state = requested;
            if (resetScroll)
            {
                _viewport.AutoScrollPosition = Point.Empty;
                _viewport.Focus();
            }

            UpdateToolbar();
            return true;
        }
        catch (Exception exception)
        {
            // Only commit navigation/zoom after a successful render; retain the previous image on failure.
            UpdateToolbar();
            ShowError(exception);
            return false;
        }
        finally
        {
            _rendering = false;
        }
    }

    private void ScheduleFitRender()
    {
        if (_rendering || _state is null || _state.ZoomMode == ViewerZoomMode.Manual)
        {
            return;
        }

        _resizeTimer.Stop();
        _resizeTimer.Start();
    }

    private void UpdateToolbar()
    {
        _previous.Enabled = _state?.CanGoPrevious == true;
        _next.Enabled = _state?.CanGoNext == true;
        _pageNumber.Enabled = _state is not null;
        _pageNumber.Text = _state is null ? string.Empty : (_state.PageIndex + 1).ToString(CultureInfo.InvariantCulture);
        _totalPages.Text = $"/ {_state?.PageCount ?? 0}";
        _zoomOut.Enabled = _state is not null && (_state.ZoomMode != ViewerZoomMode.Manual || _state.ZoomPercent > ViewerState.MinimumZoom);
        _zoomIn.Enabled = _state is not null && (_state.ZoomMode != ViewerZoomMode.Manual || _state.ZoomPercent < ViewerState.MaximumZoom);
        _resetZoom.Enabled = _fitPage.Enabled = _fitWidth.Enabled = _state is not null;
        _fitPage.Checked = _state?.ZoomMode == ViewerZoomMode.FitPage;
        _fitWidth.Checked = _state?.ZoomMode == ViewerZoomMode.FitWidth;
        _zoomLabel.Text = _state?.ZoomMode == ViewerZoomMode.Manual ? $"{_state.ZoomPercent}%" : string.Empty;
    }

    private void CloseDocument()
    {
        _resizeTimer.Stop();
        Image? image = _pageView.Image;
        _pageView.Image = null;
        image?.Dispose();
        _session?.Dispose();
        _session = null;
        _state = null;
        _pageView.Size = Size.Empty;
        _viewport.AutoScrollPosition = Point.Empty;
        UpdateToolbar();
        Text = "MauriPDF";
    }

    private void ShowError(Exception exception)
    {
        MessageBox.Show(this, $"MauriPDF could not open or render this page.\n\n{exception.Message}",
            "MauriPDF", MessageBoxButtons.OK, MessageBoxIcon.Error);
    }
}
