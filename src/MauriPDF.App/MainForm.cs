using System.Globalization;
using MauriPDF.Core.Viewing;
using MauriPDF.Rendering;
using MauriPDF.Editing;

namespace MauriPDF.App;

internal sealed class MainForm : Form
{
    private readonly PdfViewerRenderer _renderer;
    private readonly IPdfDocumentMaterializer _materializer;
    private readonly ThumbnailListView _thumbnails;
    private readonly OutlineView _outline = new();
    private readonly TabControl _navigationTabs = new() { Dock = DockStyle.Fill };
    private readonly SplitContainer _split = new()
    {
        Dock = DockStyle.Fill, Size = new Size(850, 550), Panel1MinSize = 170,
        Panel2MinSize = 200, SplitterDistance = 190
    };
    private readonly ToolStripButton _toggleThumbnails = new("Sidebar") { Checked = true, ToolTipText = "Show/hide navigation sidebar (F4)" };
    private readonly ToolStripLabel _loading = new();
    private readonly ToolStripButton _open = new("Open PDF");
    private readonly ToolStripButton _save = new("Save") { ToolTipText = "Save (Ctrl+S)" };
    private readonly ToolStripDropDownButton _fileMenu = new("File");
    private readonly ToolStripMenuItem _openMenu = new("Open...") { ShortcutKeyDisplayString = "Ctrl+O" };
    private readonly ToolStripMenuItem _saveMenu = new("Save") { ShortcutKeyDisplayString = "Ctrl+S" };
    private readonly ToolStripMenuItem _saveAsMenu = new("Save As...") { ShortcutKeyDisplayString = "Ctrl+Shift+S" };
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
    private readonly ToolStripDropDownButton _displayMode = new("Continuous");
    private readonly ToolStripMenuItem _continuousMode = new("Continuous");
    private readonly ToolStripMenuItem _singlePageMode = new("Single Page");
    private readonly ToolStripButton _rotateLeft = new("↶") { ToolTipText = "Rotate counter-clockwise 90° (view only)", AccessibleName = "Rotate counter-clockwise" };
    private readonly ToolStripButton _rotateRight = new("↷") { ToolTipText = "Rotate clockwise 90° (view only)", AccessibleName = "Rotate clockwise" };
    private readonly ContinuousPdfView _viewport;
    private readonly DocumentSearchBar _searchBar;
    private DocumentEditSession? _edits;
    private string? _documentName;
    private readonly ToolStripDropDownButton _pageEdits = new("Pages");
    private readonly ToolStripMenuItem _deletePage = new("Delete current page");
    private readonly ToolStripMenuItem _moveEarlier = new("Move page earlier");
    private readonly ToolStripMenuItem _moveLater = new("Move page later");
    private readonly ToolStripMenuItem _rotatePageClockwise = new("Rotate page clockwise (edit)");
    private readonly ToolStripMenuItem _rotatePageCounterClockwise = new("Rotate page counter-clockwise (edit)");
    private readonly ToolStripMenuItem _undo = new("Undo") { ShortcutKeyDisplayString = "Ctrl+Z" };
    private readonly ToolStripMenuItem _redo = new("Redo") { ShortcutKeyDisplayString = "Ctrl+Y" };
    private readonly ToolStripButton _saveAs = new("Save As") { ToolTipText = "Save edited pages to a new PDF (Ctrl+Shift+S)" };
    private ViewerState? _state;
    private string? _sourcePath;
    private string? _savedDocumentPath;
    private SavedFileIdentity? _savedDocumentIdentity;
    private long _requestId;
    private bool _resourcesDisposed;
    private bool _closing;
    private bool _shutdownComplete;
    private bool _saving;

    public MainForm(PdfViewerRenderer renderer, IPdfDocumentMaterializer? materializer = null)
    {
        _renderer = renderer;
        _materializer = materializer ?? new PdfiumDocumentMaterializer();
        _viewport = new ContinuousPdfView(renderer);
        _searchBar = new DocumentSearchBar(renderer, _viewport);
        _viewport.RenderFailed += ShowError;
        _viewport.SelectionStatusChanged += message => _loading.Text = message;
        _viewport.CurrentPageChanged += index =>
        {
            _state = _state?.GoToPage(index + 1);
            UpdateToolbar();
        };
        _thumbnails = new ThumbnailListView(renderer);
        _thumbnails.PageRequested += index => ChangeState(_state?.GoToPage(index + 1), navigate: true);
        _outline.PageRequested += index =>
        {
            int? logical = _edits is null ? index : _edits.State.LogicalIndexForSource(index);
            if (_state is not null && logical.HasValue && logical.Value < _state.PageCount)
                ChangeState(_state.GoToPage(logical.Value + 1), navigate: true, focusViewport: false);
        };
        _toggleThumbnails.Click += (_, _) => ToggleThumbnails();
        Text = "MauriPDF";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(850, 600);

        _open.Click += (_, _) => ChooseDocument();
        _save.Click += async (_, _) => await SaveAsync();
        _saveAs.Click += async (_, _) => await ChooseSaveAsAsync();
        _fileMenu.DropDownItems.AddRange([_openMenu, new ToolStripSeparator(), _saveMenu, _saveAsMenu]);
        _openMenu.Click += (_, _) => ChooseDocument();
        _saveMenu.Click += async (_, _) => await SaveAsync();
        _saveAsMenu.Click += async (_, _) => await ChooseSaveAsAsync();
        _previous.Click += (_, _) => ChangeState(_state?.PreviousPage(), navigate: true);
        _next.Click += (_, _) => ChangeState(_state?.NextPage(), navigate: true);
        _zoomOut.Click += (_, _) => ChangeState(_state?.ZoomOut());
        _zoomIn.Click += (_, _) => ChangeState(_state?.ZoomIn());
        _resetZoom.Click += (_, _) => ChangeState(_state?.SetZoom(100));
        _fitPage.Click += (_, _) => ChangeState(_state?.SetFitMode(ViewerZoomMode.FitPage), refit: true);
        _fitWidth.Click += (_, _) => ChangeState(_state?.SetFitMode(ViewerZoomMode.FitWidth));
        _displayMode.DropDownItems.AddRange([_continuousMode, _singlePageMode]);
        _continuousMode.Click += (_, _) => ChangeState(_state?.SetDisplayMode(ViewerDisplayMode.Continuous));
        _singlePageMode.Click += (_, _) => ChangeState(_state?.SetDisplayMode(ViewerDisplayMode.SinglePage));
        _rotateLeft.Click += (_, _) => ChangeState(_state?.RotateCounterClockwise());
        _rotateRight.Click += (_, _) => ChangeState(_state?.RotateClockwise());
        _pageEdits.DropDownItems.AddRange([_deletePage, _moveEarlier, _moveLater, _rotatePageClockwise,
            _rotatePageCounterClockwise, new ToolStripSeparator(), _undo, _redo]);
        _deletePage.Click += (_, _) => EditCurrent(id => new DeletePageEdit(id));
        _moveEarlier.Click += (_, _) => EditCurrent(id => new MovePageEdit(id, _state!.PageIndex - 1));
        _moveLater.Click += (_, _) => EditCurrent(id => new MovePageEdit(id, _state!.PageIndex + 1));
        _rotatePageClockwise.Click += (_, _) => EditCurrent(id => new RotatePageEdit(id, true));
        _rotatePageCounterClockwise.Click += (_, _) => EditCurrent(id => new RotatePageEdit(id, false));
        _undo.Click += (_, _) => ApplyHistory(undo: true);
        _redo.Click += (_, _) => ApplyHistory(undo: false);
        _pageNumber.KeyDown += PageNumber_KeyDown;
        _pageNumber.Leave += (_, _) => UpdateToolbar();
        ToolStrip toolbar = new() { GripStyle = ToolStripGripStyle.Hidden };
        toolbar.Items.AddRange([
            _fileMenu, _open, _save, _saveAs, _toggleThumbnails, new ToolStripSeparator(), _previous, _pageNumber, _totalPages, _next,
            new ToolStripSeparator(), _zoomOut, _resetZoom, _zoomIn, _zoomLabel, _fitPage, _fitWidth,
            _displayMode, _rotateLeft, _rotateRight, _pageEdits, _loading
        ]);
        TabPage thumbnailsTab = new("Thumbnails");
        thumbnailsTab.Controls.Add(_thumbnails);
        TabPage outlineTab = new("Outline");
        outlineTab.Controls.Add(_outline);
        _navigationTabs.TabPages.AddRange([thumbnailsTab, outlineTab]);
        _navigationTabs.SelectedIndexChanged += (_, _) => UpdateSidebarActivity();
        _split.Panel1.Controls.Add(_navigationTabs);
        _split.Panel2.Controls.Add(_viewport);
        Controls.Add(_split);
        Controls.Add(_searchBar);
        Controls.Add(toolbar);
        UpdateToolbar();
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == (Keys.Control | Keys.F)) { _searchBar.OpenSearch(); return true; }
        if (keyData is Keys.F3 or (Keys.Shift | Keys.F3)) { _searchBar.Navigate(keyData.HasFlag(Keys.Shift)); return true; }
        if (keyData == Keys.Escape && _searchBar.Visible) { _searchBar.CloseSearch(); return true; }
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
        if (keyData == (Keys.Control | Keys.Shift | Keys.S))
        {
            _ = ChooseSaveAsAsync();
            return true;
        }
        if (keyData == (Keys.Control | Keys.S))
        {
            _ = SaveAsync();
            return true;
        }

        if (_searchBar.QueryFocused) return base.ProcessCmdKey(ref msg, keyData);

        if (!_pageNumber.Focused && keyData is (Keys.Control | Keys.Z) or (Keys.Control | Keys.Y))
        {
            ApplyHistory(undo: keyData == (Keys.Control | Keys.Z));
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

        // TreeView owns its ordinary navigation/expand/collapse/activation keys.
        if (_outline.ContainsFocus && (keyData & Keys.Control) == 0)
            return base.ProcessCmdKey(ref msg, keyData);

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
        if (_saving) { _loading.Text = "Saving..."; return; }
        if (_closing) return;
        if (!ConfirmDiscardChanges()) return;
        _closing = true;
        _outline.Clear();
        _renderer.CancelOutline();
        _searchBar.SetDocument(0);
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
        if (_closing || _saving) return;
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

        await OpenDocumentAsync(dialog.FileName);
    }

    private async Task OpenDocumentAsync(string path)
    {
        if (_closing || _saving || !ConfirmDiscardChanges()) return;
        CloseDocument();
        long requestId = ++_requestId;
        _loading.Text = "Opening...";
        try
        {
            var pages = await _renderer.OpenDocumentAsync(path);
            if (_resourcesDisposed || requestId != _requestId)
            {
                return;
            }

            _sourcePath = Path.GetFullPath(path);
            _savedDocumentPath = null;
            _savedDocumentIdentity = null;
            _documentName = Path.GetFileName(path);
            _loading.Text = string.Empty;
            _state = new ViewerState(pages.Count);
            _edits = new DocumentEditSession(pages.Count);
            _searchBar.SetDocument(pages.Count);
            _viewport.SetDocument(pages, _edits.State.Pages);
            _thumbnails.SetDocument(pages.Count, _edits.State.Pages);
            UpdateToolbar();
            await LoadOutlineAsync(requestId);
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

    private async Task LoadOutlineAsync(long requestId)
    {
        _outline.Clear("Loading bookmarks...");
        try
        {
            var outline = await _renderer.ExtractOutlineAsync();
            if (!_resourcesDisposed && !_closing && requestId == _requestId) _outline.SetOutline(outline);
        }
        catch (OperationCanceledException) { }
        catch (Exception)
        {
            // Optional metadata failure must never turn an otherwise readable PDF into an open error.
            if (!_resourcesDisposed && !_closing && requestId == _requestId) _outline.Clear("Bookmarks unavailable");
        }
    }

    private void ChangeState(ViewerState? requested, bool navigate = false, bool refit = false, bool focusViewport = true)
    {
        if (requested is null || _resourcesDisposed || _closing) return;
        _state = requested;
        _viewport.ApplyState(requested, navigate, refit);
        UpdateToolbar();
        if (focusViewport) _viewport.Focus();
    }

    private void UpdateToolbar()
    {
        string? currentName = _savedDocumentPath is null ? _documentName : Path.GetFileName(_savedDocumentPath);
        Text = currentName is null ? "MauriPDF" : $"MauriPDF — {currentName}{(_edits?.IsDirty == true ? " *" : "")}";
        _pageEdits.Enabled = _edits is not null && !_closing && !_saving;
        _deletePage.Enabled = !_saving && _state?.PageCount > 1;
        _moveEarlier.Enabled = !_saving && _state?.CanGoPrevious == true;
        _moveLater.Enabled = !_saving && _state?.CanGoNext == true;
        _undo.Enabled = !_saving && _edits?.CanUndo == true;
        _redo.Enabled = !_saving && _edits?.CanRedo == true;
        _saveAs.Enabled = _edits is not null && !_closing && !_saving;
        _save.Enabled = _edits is not null && !_closing && !_saving;
        _open.Enabled = !_closing && !_saving;
        _saveMenu.Enabled = _save.Enabled;
        _saveAsMenu.Enabled = _saveAs.Enabled;
        _openMenu.Enabled = _open.Enabled;
        _thumbnails.SetRotation(_state?.Rotation ?? default);
        _displayMode.Enabled = _rotateLeft.Enabled = _rotateRight.Enabled = _state is not null;
        _singlePageMode.Checked = _state?.DisplayMode == ViewerDisplayMode.SinglePage;
        _continuousMode.Checked = !_singlePageMode.Checked;
        _displayMode.Text = _singlePageMode.Checked ? "Single Page" : "Continuous";
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
        _edits = null;
        _sourcePath = null;
        _savedDocumentPath = null;
        _savedDocumentIdentity = null;
        _documentName = null;
        _outline.Clear();
        _renderer.CancelOutline();
        _searchBar.SetDocument(0);
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

    private bool ConfirmDiscardChanges()
    {
        if (_edits?.IsDirty != true) return true;
        using DiscardChangesDialog dialog = new();
        return dialog.ShowDialog(this) == DialogResult.OK;
    }

    private void EditCurrent(Func<Core.Documents.DocumentPageId, DocumentEdit> createEdit)
    {
        if (_edits is null || _state is null || _closing || _saving || _resourcesDisposed) return;
        EditedDocumentState before = _edits.State;
        if (_edits.Execute(createEdit(before.Pages[_state.PageIndex].Id))) RefreshEditedDocument(before);
    }

    private void ApplyHistory(bool undo)
    {
        if (_edits is null || _state is null || _closing || _saving || _resourcesDisposed) return;
        EditedDocumentState before = _edits.State;
        if (undo ? _edits.Undo() : _edits.Redo()) RefreshEditedDocument(before);
    }

    private void RefreshEditedDocument(EditedDocumentState before)
    {
        EditedDocumentState edited = _edits!.State;
        int current = edited.ResolveCurrentPage(before, _state!.PageIndex);
        bool sequenceChanged = !edited.HasSameSequence(before);
        _state = _state.RemapPages(edited.Pages.Count, current);
        if (sequenceChanged) _searchBar.PageSequenceChanged(edited.Pages.Count);
        _thumbnails.ApplyEditedPages(edited.Pages);
        _viewport.ApplyEditedPages(edited.Pages, _state, sequenceChanged);
        UpdateToolbar();
    }

    private void ToggleThumbnails()
    {
        if (_closing) return;
        _split.Panel1Collapsed = !_split.Panel1Collapsed;
        _toggleThumbnails.Checked = !_split.Panel1Collapsed;
        UpdateSidebarActivity();
    }

    private void UpdateSidebarActivity() => _thumbnails.SetActive(!_split.Panel1Collapsed && _navigationTabs.SelectedIndex == 0);

    private async Task ChooseSaveAsAsync()
    {
        if (_saving || _closing || _edits is null || _sourcePath is null) return;
        using SaveFileDialog dialog = new()
        {
            AddExtension = true,
            DefaultExt = "pdf",
            Filter = "PDF documents (*.pdf)|*.pdf",
            FileName = _savedDocumentPath is null ? Path.GetFileName(_sourcePath) : Path.GetFileName(_savedDocumentPath),
            InitialDirectory = Path.GetDirectoryName(_savedDocumentPath ?? _sourcePath),
            OverwritePrompt = true,
            Title = "Save MauriPDF document as"
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        await SaveAsAsync(dialog.FileName);
    }

    private async Task SaveAsAsync(string destinationPath)
    {
        if (_saving || _closing || _edits is null || _sourcePath is null) return;
        string destination;
        try { destination = Path.GetFullPath(destinationPath); }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            ShowSaveError(exception);
            return;
        }
        if (string.Equals(destination, _sourcePath, StringComparison.OrdinalIgnoreCase))
        {
            ShowSaveError(new IOException("Save As cannot overwrite the currently open source PDF. Choose a different file."));
            return;
        }

        await PersistAsync(destination, PdfDestinationPolicy.OverwriteApproved, null);
    }

    private async Task SaveAsync()
    {
        if (_saving || _closing || _edits is null || _sourcePath is null) return;
        if (_savedDocumentPath is null) { await ChooseSaveAsAsync(); return; }

        string destination = _savedDocumentPath;
        SavedFileIdentity? expected = _savedDocumentIdentity;
        PdfDestinationConflictKind? conflict = null;
        _saving = true;
        _loading.Text = "Saving...";
        UpdateToolbar();
        try
        {
            if (expected is null || !File.Exists(destination)) conflict = PdfDestinationConflictKind.Missing;
            else
            {
                SavedFileIdentity current = await Task.Run(() => SavedFileIdentity.Capture(destination));
                if (current != expected) conflict = PdfDestinationConflictKind.Changed;
                else if (_edits.IsDirty)
                {
                    await PersistCoreAsync(destination, PdfDestinationPolicy.RequireExpectedIdentity, expected);
                }
                else _loading.Text = "Saved";
            }
        }
        catch (PdfDestinationConflictException exception) { conflict = exception.Kind; }
        catch (Exception exception) { if (!_resourcesDisposed && !_closing) ShowSaveError(exception); }
        finally
        {
            _saving = false;
            if (!_resourcesDisposed) UpdateToolbar();
        }
        if (conflict.HasValue && !_resourcesDisposed && !_closing) await ResolveConflictAsync(conflict.Value, destination);
    }

    private async Task ResolveConflictAsync(PdfDestinationConflictKind conflict, string destination)
    {
        bool missing = conflict == PdfDestinationConflictKind.Missing;
        using SaveConflictDialog dialog = new(missing);
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        if (dialog.Choice == SaveConflictChoice.SaveAs) { await ChooseSaveAsAsync(); return; }
        if (dialog.Choice == SaveConflictChoice.OverwriteOrRecreate)
            await PersistAsync(destination, missing ? PdfDestinationPolicy.RequireMissing : PdfDestinationPolicy.OverwriteApproved, null);
    }

    private async Task PersistAsync(string destination, PdfDestinationPolicy policy, SavedFileIdentity? expected)
    {
        if (_saving || _closing || _edits is null || _sourcePath is null) return;
        _saving = true;
        _loading.Text = "Saving...";
        UpdateToolbar();
        PdfDestinationConflictKind? conflict = null;
        try { await PersistCoreAsync(destination, policy, expected); }
        catch (PdfDestinationConflictException exception) { conflict = exception.Kind; }
        catch (Exception exception) { if (!_resourcesDisposed && !_closing) ShowSaveError(exception); }
        finally
        {
            _saving = false;
            if (!_resourcesDisposed) UpdateToolbar();
        }
        if (conflict.HasValue && !_resourcesDisposed && !_closing) await ResolveConflictAsync(conflict.Value, destination);
    }

    private async Task PersistCoreAsync(string destination, PdfDestinationPolicy policy, SavedFileIdentity? expected)
    {
        DocumentEditSession edits = _edits!;
        PdfMaterializationPlan snapshot = PdfMaterializationPlan.From(edits.State);
        PdfMaterializationRequest request = new(_sourcePath!, destination, snapshot, policy, expected);
        PdfMaterializationResult result = await _materializer.MaterializeAsync(request);
        if (_resourcesDisposed || _closing || !ReferenceEquals(edits, _edits)) return;
        edits.MarkSavedBaseline(snapshot.Revision);
        _savedDocumentPath = destination;
        _savedDocumentIdentity = result.DestinationIdentity;
        _loading.Text = $"Saved {result.PageCount} pages as {Path.GetFileName(destination)}";
    }

    private void ShowSaveError(Exception exception)
    {
        _loading.Text = "Save failed";
        MessageBox.Show(this, $"MauriPDF could not save the PDF.\n\n{exception.Message}", "MauriPDF",
            MessageBoxButtons.OK, MessageBoxIcon.Error);
    }
}
