using MauriPDF.App.Presentation;

namespace MauriPDF.App;

internal sealed partial class MainForm
{
    private readonly MauriPdfTheme _theme = new();
    private readonly CommandIcons _icons = new();
    private RibbonHost? _ribbon;
    private ToolStrip? _commandSources;
    private bool _shellDisposed;
    private bool _copyRefreshPending;
    private EmptyDocumentView? _welcome;
    private readonly ToolStripButton _find = new("Find") { ToolTipText = "Find text (Ctrl+F)" };
    private readonly ToolStripButton _copy = new("Copy") { ToolTipText = "Copy selected text (Ctrl+C)" };
    private readonly ToolStripButton _showThumbnails = new("Thumbnails");
    private readonly ToolStripButton _showOutline = new("Outline");
    private ShellStatusBar? _statusBar;

    private void InitializeShell()
    {
        AutoScaleDimensions = new SizeF(96, 96);
        AutoScaleMode = AutoScaleMode.Dpi;
        Font = _theme.Body;
        BackColor = MauriPdfTheme.Background;
        ForeColor = MauriPdfTheme.Ink;
        ClientSize = new Size(1180, 780);
        _find.Click += (_, _) => _searchBar.OpenSearch();
        _print.Click += async (_, _) => await PrintAsync();
        _copy.Click += (_, _) => _viewport.CopySelection();
        _showThumbnails.Click += (_, _) => ShowNavigationTab(0);
        _showOutline.Click += (_, _) => ShowNavigationTab(1);

        // Existing ToolStripItems are the lightweight command sources. They own their original
        // handlers and availability, while ribbon bindings borrow them. This strip is never shown.
        _commandSources = new ToolStrip();
        _commandSources.Items.AddRange([_fileMenu, _open, _save, _saveAs, _print, _toggleThumbnails,
            _zoomOut, _resetZoom, _zoomIn, _zoomLabel, _fitPage, _fitWidth, _displayMode,
            _rotateLeft, _rotateRight, _pageEdits, _find, _copy, _showThumbnails, _showOutline]);
        _ribbon = new RibbonHost(_theme, _icons);

        var file = _ribbon.AddTab("File");
        var files = _ribbon.AddGroup(file, "Document");
        _ribbon.Add(files, _open, "Open PDF", CommandIcon.Open, true);
        _ribbon.Add(files, _save, "Save", CommandIcon.Save, true);
        _ribbon.Add(files, _saveAs, "Save As", CommandIcon.SaveAs, true);
        var output = _ribbon.AddGroup(file, "Paper & output");
        _ribbon.Add(output, _print, "Print", CommandIcon.Print, true);

        var home = _ribbon.AddTab("Home");
        var document = _ribbon.AddGroup(home, "Document");
        _ribbon.Add(document, _open, "Open PDF", CommandIcon.Open, true);
        _ribbon.Add(document, _save, "Save", CommandIcon.Save, true);
        _ribbon.Add(document, _print, "Print", CommandIcon.Print, true);
        var history = _ribbon.AddGroup(home, "History");
        _ribbon.Add(history, _undo, "Undo", CommandIcon.Undo);
        _ribbon.Add(history, _redo, "Redo", CommandIcon.Redo);
        var text = _ribbon.AddGroup(home, "Text");
        _ribbon.Add(text, _find, "Find text", CommandIcon.Search);
        _ribbon.Add(text, _copy, "Copy selection", CommandIcon.Copy);
        var fit = _ribbon.AddGroup(home, "Page fit");
        _ribbon.Add(fit, _fitWidth, "Fit width", CommandIcon.FitWidth);
        _ribbon.Add(fit, _fitPage, "Fit page", CommandIcon.FitPage);

        var pages = _ribbon.AddTab("Pages");
        var organize = _ribbon.AddGroup(pages, "Page order");
        _ribbon.Add(organize, _moveEarlier, "Move earlier", CommandIcon.Earlier);
        _ribbon.Add(organize, _moveLater, "Move later", CommandIcon.Later);
        var rotate = _ribbon.AddGroup(pages, "Rotate page • edit");
        _ribbon.Add(rotate, _rotatePageClockwise, "Clockwise", CommandIcon.RotateRight);
        _ribbon.Add(rotate, _rotatePageCounterClockwise, "Rotate left", CommandIcon.RotateLeft);
        var delete = _ribbon.AddGroup(pages, "Remove");
        _ribbon.Add(delete, _deletePage, "Delete page", CommandIcon.Delete, true);

        var view = _ribbon.AddTab("View");
        var zoom = _ribbon.AddGroup(view, "Zoom");
        _ribbon.Add(zoom, _zoomIn, "Zoom in", CommandIcon.ZoomIn);
        _ribbon.Add(zoom, _zoomOut, "Zoom out", CommandIcon.ZoomOut);
        var sizing = _ribbon.AddGroup(view, "Sizing");
        _ribbon.Add(sizing, _resetZoom, "Actual size", CommandIcon.SinglePage);
        _ribbon.Add(sizing, _fitPage, "Fit page", CommandIcon.FitPage);
        _ribbon.Add(sizing, _fitWidth, "Fit width", CommandIcon.FitWidth);
        var mode = _ribbon.AddGroup(view, "Display");
        _ribbon.Add(mode, _continuousMode, "Continuous", CommandIcon.Continuous);
        _ribbon.Add(mode, _singlePageMode, "Single page", CommandIcon.SinglePage);
        var rotation = _ribbon.AddGroup(view, "Rotate view • not saved");
        _ribbon.Add(rotation, _rotateLeft, "Rotate left", CommandIcon.RotateLeft);
        _ribbon.Add(rotation, _rotateRight, "Rotate right", CommandIcon.RotateRight);
        var navigation = _ribbon.AddGroup(view, "Navigation pane");
        _ribbon.Add(navigation, _toggleThumbnails, "Toggle sidebar", CommandIcon.Sidebar);
        _ribbon.Add(navigation, _showThumbnails, "Thumbnails", CommandIcon.Thumbnails);
        _ribbon.Add(navigation, _showOutline, "Outline", CommandIcon.Outline);

        var tools = _ribbon.AddTab("Tools");
        var reading = _ribbon.AddGroup(tools, "Reading tools");
        _ribbon.Add(reading, _find, "Find text", CommandIcon.Search);
        _ribbon.Add(reading, _copy, "Copy selection", CommandIcon.Copy);
        _ribbon.SelectTab(1);

        Label brand = new() { Text = "MauriPDF", Font = _theme.Title, ForeColor = MauriPdfTheme.Panel,
            BackColor = MauriPdfTheme.Ink, Dock = DockStyle.Top, Height = MauriPdfTheme.HeaderHeight,
            Padding = new Padding(12, 0, 0, 0), TextAlign = ContentAlignment.MiddleLeft };
        _split.BorderStyle = BorderStyle.None;
        _split.BackColor = MauriPdfTheme.Border;
        _split.SplitterWidth = 4;
        _split.SplitterDistance = MauriPdfTheme.SidebarWidth;
        _split.FixedPanel = FixedPanel.Panel1;
        _split.Panel1.BackColor = MauriPdfTheme.Group;
        _split.Panel1.Padding = new Padding(5, 0, 5, 5);
        _navigationTabs.Font = _theme.Body;
        _navigationTabs.Padding = new Point(12, 6);
        foreach (TabPage tab in _navigationTabs.TabPages) tab.BackColor = MauriPdfTheme.Panel;
        Label heading = new() { Text = "Document navigation", Dock = DockStyle.Top, Height = 38,
            Font = _theme.Heading, ForeColor = MauriPdfTheme.Ink, BackColor = MauriPdfTheme.Group,
            TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(12, 0, 0, 0) };
        _split.Panel1.Controls.Add(heading);

        _previous.Text = "‹"; _next.Text = "›";
        _previous.AccessibleName = "Previous page"; _next.AccessibleName = "Next page";
        _pageNumber.BorderStyle = BorderStyle.FixedSingle;
        _statusBar = new ShellStatusBar(_theme, _previous, _pageNumber, _totalPages, _next);
        _loading.TextChanged += (_, _) => _statusBar.SetMessage(_loading.Text);
        _viewport.PresentationLayoutChanged += UpdateStatusView;

        _searchBar.BackColor = MauriPdfTheme.Panel;
        _searchBar.ForeColor = MauriPdfTheme.Ink;
        _searchBar.Font = _theme.Body;
        _searchBar.Renderer = new ShellStripRenderer();
        _searchBar.Padding = new Padding(8, 4, 8, 4);
        _welcome = new EmptyDocumentView(_theme, _icons, _open);
        _viewport.Controls.Add(_welcome);
        Controls.Add(_split);
        Controls.Add(_searchBar);
        Controls.Add(_ribbon);
        Controls.Add(brand);
        Controls.Add(_statusBar);
    }

    private void ShowNavigationTab(int index)
    {
        if (_closing) return;
        _split.Panel1Collapsed = false;
        _toggleThumbnails.Checked = true;
        _navigationTabs.SelectedIndex = index;
        UpdateSidebarActivity();
        UpdateShellCommands();
    }

    private void UpdateShellCommands()
    {
        if (_resourcesDisposed) return;
        if (_welcome is not null) _welcome.Visible = _state is null;
        _print.Enabled = _edits is not null && _sourcePath is not null && !_closing && !DocumentBusy;
        _find.Enabled = _state is not null && !_closing;
        _copy.Enabled = _state is not null && !_closing && _viewport.CanCopySelection;
        _showThumbnails.Enabled = _showOutline.Enabled = _toggleThumbnails.Enabled = !_closing;
        _showThumbnails.Checked = !_split.Panel1Collapsed && _navigationTabs.SelectedIndex == 0;
        _showOutline.Checked = !_split.Panel1Collapsed && _navigationTabs.SelectedIndex == 1;
        UpdateStatusView();
        _ribbon?.RefreshCommands();
    }

    private void UpdateStatusView()
    {
        if (_resourcesDisposed || _statusBar is null) return;
        string zoom = _state is null ? "" : $"{_viewport.DisplayedZoomPercent ?? _state.ZoomPercent:0}%";
        if (_state?.ZoomMode == Core.Viewing.ViewerZoomMode.FitWidth) zoom += " · Fit width";
        if (_state?.ZoomMode == Core.Viewing.ViewerZoomMode.FitPage) zoom += " · Fit page";
        string mode = _state is null ? "No document" : _state.DisplayMode == Core.Viewing.ViewerDisplayMode.Continuous ? "Continuous" : "Single page";
        _statusBar.SetView(zoom, mode);
    }

    private void QueueCopyAvailabilityRefresh()
    {
        if (_resourcesDisposed || !IsHandleCreated || _copyRefreshPending) return;
        _copyRefreshPending = true;
        // Keep command focus/layout changes outside the viewer's active mouse/selection callback.
        BeginInvoke(() =>
        {
            _copyRefreshPending = false;
            if (_resourcesDisposed) return;
            _copy.Enabled = _state is not null && !_closing && _viewport.CanCopySelection;
            _ribbon?.RefreshCommands();
        });
    }
}
