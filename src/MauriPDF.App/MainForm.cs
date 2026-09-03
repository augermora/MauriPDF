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
    private readonly ContinuousPdfView _viewport;
    private ViewerState? _state;
    private long _requestId;
    private bool _resourcesDisposed;
    private bool _closing;
    private bool _shutdownComplete;

    public MainForm(PdfViewerRenderer renderer)
    {
        _renderer = renderer;
        _viewport = new ContinuousPdfView(renderer);
        _viewport.RenderFailed += ShowError;
        _viewport.SelectionStatusChanged += message => _loading.Text = message;
        _viewport.CurrentPageChanged += index =>
        {
            _state = _state?.GoToPage(index + 1);
            UpdateToolbar();
        };
        _thumbnails = new ThumbnailListView(renderer);
        _thumbnails.PageRequested += index => ChangeState(_state?.GoToPage(index + 1), navigate: true);
        _toggleThumbnails.Click += (_, _) => ToggleThumbnails();
        Text = "MauriPDF";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(850, 600);

        ToolStripButton open = new("Open PDF");
        open.Click += (_, _) => ChooseDocument();
        _previous.Click += (_, _) => ChangeState(_state?.PreviousPage(), navigate: true);
        _next.Click += (_, _) => ChangeState(_state?.NextPage(), navigate: true);
        _zoomOut.Click += (_, _) => ChangeState(_state?.ZoomOut());
        _zoomIn.Click += (_, _) => ChangeState(_state?.ZoomIn());
        _resetZoom.Click += (_, _) => ChangeState(_state?.SetZoom(100));
        _fitPage.Click += (_, _) => ChangeState(_state?.SetFitMode(ViewerZoomMode.FitPage), refit: true);
        _fitWidth.Click += (_, _) => ChangeState(_state?.SetFitMode(ViewerZoomMode.FitWidth));
        _pageNumber.KeyDown += PageNumber_KeyDown;
        _pageNumber.Leave += (_, _) => UpdateToolbar();
        ToolStrip toolbar = new() { GripStyle = ToolStripGripStyle.Hidden };
        toolbar.Items.AddRange([
            open, _toggleThumbnails, new ToolStripSeparator(), _previous, _pageNumber, _totalPages, _next,
            new ToolStripSeparator(), _zoomOut, _resetZoom, _zoomIn, _zoomLabel, _fitPage, _fitWidth, _loading
        ]);
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
        if (keyData == (Keys.Control | Keys.C) && !_pageNumber.Focused)
        {
            _viewport.CopySelection();
            return true;
        }

        if (_pageNumber.Focused && (keyData & Keys.Control) == 0)
        {
            return base.ProcessCmdKey(ref msg, keyData);
        }

        if (!_thumbnails.ContainsFocus && keyData is Keys.PageUp or Keys.PageDown or Keys.Up or Keys.Down)
        {
            if (keyData is Keys.PageUp or Keys.PageDown) _viewport.ScrollViewport(keyData == Keys.PageUp ? -1 : 1);
            else _viewport.ScrollLine(keyData == Keys.Up ? -1 : 1);
            return true;
        }

        ViewerState? requested = keyData switch
        {
            Keys.Left => _state?.PreviousPage(),
            Keys.Right => _state?.NextPage(),
            Keys.Home => _state?.GoToPage(1),
            Keys.End => _state?.GoToPage(_state.PageCount),
            Keys.Control | Keys.Add or Keys.Control | Keys.Oemplus or Keys.Control | Keys.Shift | Keys.Oemplus => _state?.ZoomIn(),
            Keys.Control | Keys.Subtract or Keys.Control | Keys.OemMinus => _state?.ZoomOut(),
            Keys.Control | Keys.D0 or Keys.Control | Keys.NumPad0 => _state?.SetZoom(100),
            _ => null
        };
        if (requested is not null)
        {
            ChangeState(requested, navigate: keyData is Keys.Left or Keys.Right or Keys.Home or Keys.End);
            return true;
        }

        return base.ProcessCmdKey(ref msg, keyData);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !_resourcesDisposed)
        {
            _resourcesDisposed = true;
            CloseDocument();
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
        _viewport.SetDocument(null);
        ++_requestId;
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

        CloseDocument();
        long requestId = ++_requestId;
        _loading.Text = "Opening...";
        try
        {
            var pages = await _renderer.OpenDocumentAsync(dialog.FileName);
            if (_resourcesDisposed || requestId != _requestId)
            {
                return;
            }

            Text = $"MauriPDF — {Path.GetFileName(dialog.FileName)}";
            _loading.Text = string.Empty;
            _state = new ViewerState(pages.Count);
            _viewport.SetDocument(pages);
            _thumbnails.SetDocument(pages.Count);
            UpdateToolbar();
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
            ChangeState(_state.GoToPage(page), navigate: true);
        }
        else
        {
            UpdateToolbar();
        }

        _viewport.Focus();
    }

    private void ChangeState(ViewerState? requested, bool navigate = false, bool refit = false)
    {
        if (requested is null || _resourcesDisposed || _closing) return;
        _state = requested;
        _viewport.ApplyState(requested, navigate, refit);
        UpdateToolbar();
        _viewport.Focus();
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

    private void CloseDocument()
    {
        _thumbnails.SetDocument(0);
        ++_requestId;
        _viewport.SetDocument(null);
        _state = null;
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
    }
}
