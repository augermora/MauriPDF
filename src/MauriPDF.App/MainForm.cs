using System.Globalization;
using MauriPDF.Core.Viewing;
using MauriPDF.Rendering;

namespace MauriPDF.App;

internal sealed class MainForm : Form
{
    private readonly PdfViewerRenderer _renderer;
    private readonly ThumbnailListView _thumbnails;
    private readonly SplitContainer _split = new()
    {
        Dock = DockStyle.Fill, Size = new Size(850, 550), Panel1MinSize = 170,
        Panel2MinSize = 200, SplitterDistance = 190
    };
    private readonly ToolStripButton _toggleThumbnails = new("Thumbnails") { Checked = true, ToolTipText = "Show/hide thumbnails (F4)" };
    private readonly ToolStripLabel _loading = new();
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
    private ViewerState? _state;
    private ViewerState? _displayedState;
    private bool _updatingImage;
    private long _requestId;
    private bool _resourcesDisposed;
    private bool _closing;
    private bool _shutdownComplete;

    public MainForm(PdfViewerRenderer renderer)
    {
        _renderer = renderer;
        _thumbnails = new ThumbnailListView(renderer);
        _thumbnails.PageRequested += index => ChangeState(_state?.GoToPage(index + 1));
        _toggleThumbnails.Click += (_, _) => ToggleThumbnails();
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
            open, _toggleThumbnails, new ToolStripSeparator(), _previous, _pageNumber, _totalPages, _next,
            new ToolStripSeparator(), _zoomOut, _resetZoom, _zoomIn, _zoomLabel, _fitPage, _fitWidth, _loading
        ]);
        _viewport.Controls.Add(_pageView);
        _split.Panel1.Controls.Add(_thumbnails);
        _split.Panel2.Controls.Add(_viewport);
        Controls.Add(_split);
        Controls.Add(toolbar);
        UpdateToolbar();
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.F4)
        {
            ToggleThumbnails();
            return true;
        }
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

    protected override async void OnFormClosing(FormClosingEventArgs e)
    {
        base.OnFormClosing(e);
        if (_shutdownComplete || e.Cancel) return;
        e.Cancel = true;
        if (_closing) return;
        _closing = true;
        _thumbnails.SetActive(false);
        ++_requestId;
        _resizeTimer.Stop();
        _loading.Text = "Closing...";
        try
        {
            // Keep the message loop alive until canceled await continuations have disposed their results.
            await _renderer.DisposeAsync();
            await Task.Yield();
        }
        catch (Exception exception)
        {
            ShowError(exception);
        }
        finally
        {
            _shutdownComplete = true;
            Close();
        }
    }

    private async void ChooseDocument()
    {
        if (_closing) return;
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

        CloseDocument(keepImage: true);
        long requestId = ++_requestId;
        _loading.Text = "Opening...";
        try
        {
            int pageCount = await _renderer.OpenAsync(dialog.FileName);
            if (_resourcesDisposed || requestId != _requestId)
            {
                return;
            }

            Text = $"MauriPDF — {Path.GetFileName(dialog.FileName)}";
            _thumbnails.SetDocument(pageCount);
            ChangeState(new ViewerState(pageCount));
        }
        catch (OperationCanceledException)
        {
            // Superseded opens/renders are normal, not user-facing errors.
        }
        catch (Exception exception)
        {
            if (!_resourcesDisposed && requestId == _requestId)
            {
                _loading.Text = string.Empty;
                ShowError(exception);
            }
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

    private async void ChangeState(ViewerState? requested, bool resetScroll = true)
    {
        if (requested is null || _resourcesDisposed || _closing)
        {
            return;
        }

        _resizeTimer.Stop();
        long requestId = ++_requestId;
        // Navigation advances from the requested state, not the last completed page.
        _state = requested;
        UpdateToolbar();
        _loading.Text = "Rendering...";
        try
        {
            // Recover the full client area independent of the CURRENT bitmap's scrollbars.
            int width = _viewport.ClientSize.Width + (_viewport.VerticalScroll.Visible ? SystemInformation.VerticalScrollBarWidth : 0);
            int height = _viewport.ClientSize.Height + (_viewport.HorizontalScroll.Visible ? SystemInformation.HorizontalScrollBarHeight : 0);
            using ViewerRenderResult result = await _renderer.RenderAsync(
                requested, Math.Max(1, width), Math.Max(1, height), SystemInformation.VerticalScrollBarWidth);
            // Also guard after await: completion may have been posted before a newer UI request.
            if (_resourcesDisposed || requestId != _requestId)
            {
                return;
            }

            Bitmap nextImage = WinFormsImageConverter.CreateBitmap(result.Pixels);
            _updatingImage = true;
            try
            {
                Image? previousImage = _pageView.Image;
                _pageView.Image = nextImage;
                _pageView.Size = nextImage.Size;
                previousImage?.Dispose();
            }
            finally
            {
                _updatingImage = false;
            }

            _displayedState = requested;
            System.Diagnostics.Debug.WriteLine($"Displayed page {requested.PageIndex + 1}: {(result.FromCache ? "cache" : "fresh render")}");
            if (resetScroll)
            {
                _viewport.AutoScrollPosition = Point.Empty;
                _viewport.Focus();
            }

            UpdateToolbar();
        }
        catch (OperationCanceledException)
        {
            // No error dialog for canceled/obsolete work.
        }
        catch (Exception exception)
        {
            if (!_resourcesDisposed && requestId == _requestId)
            {
                _state = _displayedState ?? requested;
                UpdateToolbar();
                _loading.Text = string.Empty;
                ShowError(exception);
            }
        }
        finally
        {
            if (!_resourcesDisposed && requestId == _requestId)
            {
                _loading.Text = string.Empty;
            }
        }
    }

    private void ScheduleFitRender()
    {
        if (_closing || _updatingImage || _state is null || _state.ZoomMode == ViewerZoomMode.Manual)
        {
            return;
        }

        _resizeTimer.Stop();
        _resizeTimer.Start();
    }

    private void UpdateToolbar()
    {
        if (_state is not null) _thumbnails.SetCurrentPage(_state.PageIndex);
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

    private void CloseDocument(bool keepImage = false)
    {
        _thumbnails.SetDocument(0);
        ++_requestId;
        _resizeTimer.Stop();
        if (!keepImage)
        {
            Image? image = _pageView.Image;
            _pageView.Image = null;
            image?.Dispose();
            _pageView.Size = Size.Empty;
            _viewport.AutoScrollPosition = Point.Empty;
        }
        _state = null;
        _displayedState = null;
        _loading.Text = string.Empty;
        UpdateToolbar();
        Text = "MauriPDF";
    }

    private void ShowError(Exception exception)
    {
        MessageBox.Show(this, $"MauriPDF could not open or render this page.\n\n{exception.Message}",
            "MauriPDF", MessageBoxButtons.OK, MessageBoxIcon.Error);
    }

    private void ToggleThumbnails()
    {
        if (_closing) return;
        _split.Panel1Collapsed = !_split.Panel1Collapsed;
        _toggleThumbnails.Checked = !_split.Panel1Collapsed;
        _thumbnails.SetActive(!_split.Panel1Collapsed);
        ScheduleFitRender();
    }
}
