using MauriPDF.Core.Rendering;
using MauriPDF.Core.Viewing;
using MauriPDF.Rendering;
using MauriPDF.Core.Documents;

namespace MauriPDF.App;

/// <summary>One painted surface, two native scrollbars, and a bounded set of exclusively owned Bitmaps.</summary>
internal sealed partial class ContinuousPdfView : Control
{
    private const int ScrollSteps = 1_000_000;
    private readonly PdfViewerRenderer _renderer;
    private readonly VScrollBar _vertical = new() { Dock = DockStyle.Right };
    private readonly HScrollBar _horizontal = new() { Dock = DockStyle.Bottom };
    private readonly System.Windows.Forms.Timer _demandTimer = new() { Interval = 60 };
    private readonly System.Windows.Forms.Timer _resizeTimer = new() { Interval = 150 };
    private readonly Dictionary<int, Bitmap> _images = [];
    private readonly HashSet<int> _failed = [];
    private IReadOnlyList<PdfPageSize>? _sizes;
    private IReadOnlyList<LogicalPageReference>? _logicalPages;
    private ViewerState? _state;
    private ContinuousPageLayout? _layout;
    private PageRange _range = new(0, -1);
    private double _top, _left;
    private int _layoutViewportHeight;
    private int _layoutViewportWidth;
    private long _generation;
    private bool _disposed;
    private bool _ready;

    public ContinuousPdfView(PdfViewerRenderer renderer)
    {
        _renderer = renderer;
        Dock = DockStyle.Fill;
        BackColor = Presentation.MauriPdfTheme.Workspace;
        TabStop = true;
        AccessibleName = "PDF document";
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
            | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
        Controls.Add(_vertical);
        Controls.Add(_horizontal);
        _vertical.Scroll += (_, e) =>
        {
            _searchNavigation = null;
            MoveTo(ScrollOffset(e, _top - MinTop, MaxTop - MinTop, ViewHeight) + MinTop, _left);
            e.NewValue = _vertical.Value;
        };
        _horizontal.Scroll += (_, e) =>
        {
            _searchNavigation = null;
            MoveTo(_top, ScrollOffset(e, _left, MaxLeft, ViewWidth));
            e.NewValue = _horizontal.Value;
        };
        _demandTimer.Tick += (_, _) => { _demandTimer.Stop(); LoadVisible(); };
        _resizeTimer.Tick += (_, _) => { _resizeTimer.Stop(); RebuildLayout(); };
        InitializeSelection();
        _ready = true;
    }

    public event Action<int>? CurrentPageChanged;
    public event Action<Exception>? RenderFailed;
    public event Action? PresentationLayoutChanged;
    public double? DisplayedZoomPercent
    {
        get
        {
            if (_layout is null || _state is null || _sizes is null || !_layout.ContainsPage(_state.PageIndex)) return null;
            PdfPageSize size = _layout.RotationForPage(_state.PageIndex).EffectiveSize(_sizes[SourcePageIndex(_state.PageIndex)]);
            return _layout[_state.PageIndex].Width / (size.WidthPoints * 96.0 / 72) * 100;
        }
    }
    private int ViewWidth => Math.Max(1, ClientSize.Width - _vertical.Width);
    private int ViewHeight => Math.Max(1, ClientSize.Height - _horizontal.Height);
    private double MinTop => _layout?.MinimumScrollTop(ViewHeight) ?? 0;
    private double MaxTop => _layout?.MaximumScrollTop(ViewHeight) ?? 0;
    private double MaxLeft => Math.Max(0, (_layout?.Width ?? 0) - ViewWidth);

    public void SetDocument(IReadOnlyList<PdfPageSize>? sizes, IReadOnlyList<LogicalPageReference>? logicalPages = null)
    {
        SetSearch(null, 0);
        ClearSelection();
        CancelDemand();
        _resizeTimer.Stop();
        ClearImages();
        _failed.Clear();
        _sizes = sizes;
        _logicalPages = logicalPages;
        _state = sizes is { Count: > 0 } ? new ViewerState(logicalPages?.Count ?? sizes.Count) : null;
        _layout = null;
        _range = new(0, -1);
        _top = _left = 0;
        RebuildLayout();
    }

    public int SourcePageIndex(int logicalIndex) => _logicalPages?[logicalIndex].SourcePageIndex ?? logicalIndex;

    public void ApplyEditedPages(IReadOnlyList<LogicalPageReference> pages, ViewerState state, bool sequenceChanged)
    {
        // Capture against the OLD layout; remap the anchor to the stable current page's NEW index.
        ReadingAnchor? anchor = _layout?.CaptureAnchor(_top, _layoutViewportHeight, _left, _layoutViewportWidth);
        if (anchor.HasValue) anchor = anchor.Value with { PageIndex = state.PageIndex };
        _draggingText = false;
        Capture = false;
        if (sequenceChanged) ClearSelection();
        CancelDemand();
        ClearImages();
        _logicalPages = pages;
        _state = state;
        _searchNavigation = null;
        RebuildLayout(anchor);
    }

    public void ApplyState(ViewerState state, bool navigate = false, bool refit = false)
    {
        bool scaleChanged = _state is null || state.ZoomMode != _state.ZoomMode || state.ZoomPercent != _state.ZoomPercent;
        bool orientationChanged = _state?.Rotation != state.Rotation;
        bool modeChanged = _state?.DisplayMode != state.DisplayMode;
        bool singlePageChanged = state.DisplayMode == ViewerDisplayMode.SinglePage && _state?.PageIndex != state.PageIndex;
        navigate |= _state?.PageIndex != state.PageIndex;
        if (navigate || modeChanged) _searchNavigation = null;
        if (orientationChanged || modeChanged || singlePageChanged)
        {
            _draggingText = false;
            Capture = false;
            CancelDemand();
            ClearImages();
        }
        _state = state;
        if (scaleChanged || refit || orientationChanged || modeChanged || singlePageChanged) RebuildLayout();
        if (navigate && _layout is not null) MoveTo(_layout.ScrollTarget(state.PageIndex, ViewHeight), _left);
    }

    private void NavigatePage(int pageIndex)
    {
        if (_state is null) return;
        ApplyState(_state.GoToPage(pageIndex + 1), navigate: true);
        CurrentPageChanged?.Invoke(_state!.PageIndex);
    }

    public void ScrollViewport(int direction)
    {
        _searchNavigation = null;
        if (_layout?.PageScrollNavigates(ViewHeight) == true && _state is not null)
        {
            NavigatePage(_state.PageIndex + Math.Sign(direction));
            return;
        }
        MoveTo(_top + direction * ViewHeight * 0.9, _left);
    }
    public void ScrollLine(int direction)
    {
        _searchNavigation = null;
        MoveTo(_top + direction * 48, _left);
    }

    private void RebuildLayout(ReadingAnchor? editedAnchor = null)
    {
        if (!_ready || _disposed) return;
        ReadingAnchor? anchor = editedAnchor ?? _layout?.CaptureAnchor(_top, _layoutViewportHeight, _left, _layoutViewportWidth);
        try
        {
            ContinuousPageLayout? next = _sizes is not null && _state is not null
                ? new ContinuousPageLayout(_sizes, _state, ViewWidth, ViewHeight, _logicalPages) : null;
            CancelDemand();
            _failed.Clear();
            _layout = next;
            _layoutViewportHeight = ViewHeight;
            _layoutViewportWidth = ViewWidth;
            _range = new(0, -1);
            if (anchor.HasValue && next is not null && next.ContainsPage(anchor.Value.PageIndex))
            {
                _top = next.RestoreAnchor(anchor.Value, ViewHeight);
                _left = next.RestoreHorizontalAnchor(anchor.Value, ViewWidth);
            }
            else if (next is not null && _state is not null)
            {
                _top = next.ScrollTarget(_state.PageIndex, ViewHeight);
                _left = 0;
            }
            MoveTo(_top, _left);
            PresentationLayoutChanged?.Invoke();
        }
        catch (Exception exception) { RenderFailed?.Invoke(exception); }
    }

    private static double ScrollOffset(ScrollEventArgs e, double current, double maximum, int viewport) => e.Type switch
    {
        ScrollEventType.SmallIncrement => current + 48,
        ScrollEventType.SmallDecrement => current - 48,
        ScrollEventType.LargeIncrement => current + viewport * 0.9,
        ScrollEventType.LargeDecrement => current - viewport * 0.9,
        ScrollEventType.EndScroll => current,
        _ => maximum * e.NewValue / ScrollSteps
    };

    private void MoveTo(double top, double left)
    {
        _top = Math.Clamp(top, MinTop, MaxTop);
        _left = Math.Clamp(left, 0, MaxLeft);
        UpdateScrollBar(_vertical, _top - MinTop, MaxTop - MinTop, ViewHeight);
        UpdateScrollBar(_horizontal, _left, MaxLeft, ViewWidth);
        if (_layout is not null && _state is not null)
        {
            int current = _layout.CurrentPage(_top, ViewHeight);
            if (current != _state.PageIndex)
            {
                _state = _state.GoToPage(current + 1);
                CurrentPageChanged?.Invoke(current);
            }
        }
        CheckDemand();
        RefreshSearchGeometry();
        if (_draggingText) UpdateDragPoint(PointToClient(Cursor.Position));
        Invalidate();
    }

    private static void UpdateScrollBar(ScrollBar bar, double offset, double maximum, int viewport)
    {
        bar.Enabled = maximum > 0;
        bar.Minimum = 0;
        bar.LargeChange = Math.Max(1, (int)(ScrollSteps * viewport / (maximum + viewport)));
        bar.Maximum = ScrollSteps + bar.LargeChange - 1;
        bar.Value = maximum == 0 ? 0 : (int)Math.Round(offset / maximum * ScrollSteps);
    }

    private void CheckDemand()
    {
        if (_layout is null || _disposed) return;
        PageRange next = VisiblePageDemand.Select(_layout, _top, ViewHeight);
        if (next == _range) return;
        CancelDemand();
        _range = next;
        foreach (int index in _images.Keys.ToArray())
        {
            Bitmap image = _images[index];
            RenderSize target = _layout.ContainsPage(index) ? VisiblePageDemand.Target(_layout[index], Math.Max(1, next.Count)) : default;
            if (index < next.First || index > next.Last || image.Width != target.Width || image.Height != target.Height)
            {
                image.Dispose();
                _images.Remove(index);
            }
        }
        _failed.RemoveWhere(index => index < next.First || index > next.Last);
        _demandTimer.Start();
    }

    private void LoadVisible()
    {
        if (_layout is null || _disposed) return;
        List<(int LogicalIndex, PageRenderTarget Request)> targets = [];
        for (int index = _range.First; index <= _range.Last; index++)
        {
            if (!_images.ContainsKey(index) && !_failed.Contains(index))
            {
                RenderSize size = VisiblePageDemand.Target(_layout[index], _range.Count);
                PageRenderTarget request = _logicalPages is null ? new(index, size, _state!.Rotation)
                    : PageRenderTarget.FromLogicalPage(_logicalPages[index], size, _state!.Rotation);
                targets.Add((index, request));
            }
        }
        if (targets.Count == 0) return;
        int current = _layout.CurrentPage(_top, ViewHeight);
        targets.Sort((a, b) => Math.Abs(a.LogicalIndex - current).CompareTo(Math.Abs(b.LogicalIndex - current)));
        long generation = _generation;
        IReadOnlyList<Task<ViewerRenderResult>> tasks = _renderer.RenderVisible(targets.Select(target => target.Request).ToArray());
        for (int index = 0; index < tasks.Count; index++) ReceivePage(targets[index].LogicalIndex, tasks[index], generation);
    }

    private async void ReceivePage(int index, Task<ViewerRenderResult> task, long generation)
    {
        try
        {
            using ViewerRenderResult result = await task;
            if (_disposed || generation != _generation || _layout is null) return;
            Bitmap bitmap = WinFormsImageConverter.CreateBitmap(result.Pixels);
            if (!_images.TryAdd(index, bitmap)) bitmap.Dispose();
            Invalidate(PageRectangle(index));
        }
        catch (OperationCanceledException) { }
        catch (Exception exception)
        {
            if (_disposed || generation != _generation) return;
            bool firstFailure = _failed.Count == 0;
            _failed.Add(index);
            if (firstFailure) RenderFailed?.Invoke(exception);
        }
    }

    private Rectangle PageRectangle(int index)
    {
        PageGeometry page = _layout![index];
        return new((int)Math.Clamp(_layout.Left(index, ViewWidth) - _left, int.MinValue / 2, int.MaxValue / 2),
            (int)Math.Clamp(page.Top - _top, int.MinValue / 2, int.MaxValue / 2), page.Width, page.Height);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (_layout is null) return;
        e.Graphics.SetClip(new Rectangle(0, 0, ViewWidth, ViewHeight));
        PageRange visible = _layout.Visible(_top, ViewHeight);
        for (int index = visible.First; index <= visible.Last; index++)
        {
            Rectangle bounds = PageRectangle(index);
            e.Graphics.FillRectangle(Brushes.White, bounds);
            if (_images.TryGetValue(index, out Bitmap? image)) e.Graphics.DrawImage(image, bounds);
            e.Graphics.DrawRectangle(SystemPens.ControlDark, bounds);
            PaintSearch(e.Graphics, index, bounds);
            PaintSelection(e.Graphics, index, bounds); // Mouse selection is always the top overlay.
        }
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        _searchNavigation = null;
        base.OnMouseWheel(e);
        int lines = SystemInformation.MouseWheelScrollLines;
        double distance = lines < 0 ? ViewHeight * 0.9 : lines * 16;
        MoveTo(_top - e.Delta / 120.0 * distance, _left);
    }
    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        if (!_ready || _disposed) return;
        MoveTo(_top, _left);
        _resizeTimer.Stop();
        _resizeTimer.Start();
    }

    private void CancelDemand()
    {
        _generation++;
        _demandTimer.Stop();
        _renderer.CancelVisible();
    }
    private void ClearImages()
    {
        foreach (Bitmap image in _images.Values) image.Dispose();
        _images.Clear();
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing && !_disposed)
        {
            _disposed = true;
            DisposeSearch();
            DisposeSelection();
            CancelDemand();
            _demandTimer.Dispose();
            _resizeTimer.Dispose();
            ClearImages();
        }
        base.Dispose(disposing);
    }
}
