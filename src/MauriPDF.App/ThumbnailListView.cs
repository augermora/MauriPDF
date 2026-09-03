using System.Globalization;
using MauriPDF.Rendering;

namespace MauriPDF.App;

/// <summary>Native virtual list: no per-page controls or eagerly allocated items/images.</summary>
internal sealed class ThumbnailListView : ListView
{
    private const int RowHeight = 244;
    private const int MaximumVisibleImages = 32;
    private readonly PdfViewerRenderer _renderer;
    private readonly Dictionary<int, Bitmap> _images = [];
    private readonly HashSet<int> _failed = [];
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 100 };
    // Details-view owner drawing does not set native row height. An EMPTY image list
    // supplies that metric only; never add a Bitmap (ImageList can defer copying it).
    private readonly ImageList _rowSizer = new() { ImageSize = new Size(1, RowHeight), ColorDepth = ColorDepth.Depth32Bit };
    private int _generation;
    private int _current = -1;
    private int _lastTop = -1;
    private int _lastCount = -1;
    private bool _active = true;
    private bool _selecting;
    private bool _ready;
    private bool _disposed;
    private Core.Viewing.VisualRotation _rotation;

    public ThumbnailListView(PdfViewerRenderer renderer)
    {
        _renderer = renderer;
        Dock = DockStyle.Fill;
        View = View.Details;
        HeaderStyle = ColumnHeaderStyle.None;
        VirtualMode = true;
        OwnerDraw = true;
        FullRowSelect = true;
        MultiSelect = false;
        HideSelection = false;
        AccessibleName = "Page thumbnails";
        Columns.Add("Page", 170);
        SmallImageList = _rowSizer;
        RetrieveVirtualItem += (_, e) => e.Item = new ListViewItem((e.ItemIndex + 1).ToString(CultureInfo.InvariantCulture));
        _timer.Tick += LoadVisible;
        _ready = true;
    }

    public event Action<int>? PageRequested;

    public void SetDocument(int pageCount)
    {
        _rotation = default;
        CancelGeneration();
        ClearImages();
        _failed.Clear();
        _current = -1;
        _selecting = true;
        try
        {
            SelectedIndices.Clear();
            VirtualListSize = pageCount;
        }
        finally { _selecting = false; }
        _lastTop = _lastCount = -1;
        Invalidate();
        CheckVisibleRange();
    }

    public void SetActive(bool active)
    {
        _active = active;
        CancelGeneration();
        if (!active) ClearImages();
        _lastTop = _lastCount = -1;
        CheckVisibleRange();
    }

    public void SetRotation(Core.Viewing.VisualRotation rotation)
    {
        if (_rotation == rotation) return;
        _rotation = rotation;
        CancelGeneration();
        ClearImages();
        _failed.Clear();
        _lastTop = _lastCount = -1;
        Invalidate();
        CheckVisibleRange();
    }

    public void SetCurrentPage(int pageIndex)
    {
        if (_current == pageIndex || pageIndex < 0 || pageIndex >= VirtualListSize) return;
        _current = pageIndex;
        _selecting = true;
        try
        {
            SelectedIndices.Clear();
            Items[pageIndex].Selected = true;
            Items[pageIndex].Focused = true;
            EnsureVisible(pageIndex);
        }
        finally { _selecting = false; }
        Invalidate();
        CheckVisibleRange();
    }

    protected override void OnSelectedIndexChanged(EventArgs e)
    {
        base.OnSelectedIndexChanged(e);
        if (!_selecting && SelectedIndices.Count > 0) PageRequested?.Invoke(SelectedIndices[0]);
    }

    protected override void OnDrawColumnHeader(DrawListViewColumnHeaderEventArgs e) => e.DrawDefault = true;
    protected override void OnDrawItem(DrawListViewItemEventArgs e) { }

    protected override void OnDrawSubItem(DrawListViewSubItemEventArgs e)
    {
        bool selected = e.ItemIndex == _current;
        e.Graphics.FillRectangle(selected ? SystemBrushes.Highlight : SystemBrushes.Window, e.Bounds);
        Rectangle area = new(e.Bounds.X + 8, e.Bounds.Y + 6, Math.Max(1, e.Bounds.Width - 16), RowHeight - 30);
        if (_images.TryGetValue(e.ItemIndex, out Bitmap? image))
        {
            double scale = Math.Min(1, Math.Min((double)area.Width / image.Width, (double)area.Height / image.Height));
            int width = Math.Max(1, (int)(image.Width * scale));
            int height = Math.Max(1, (int)(image.Height * scale));
            e.Graphics.DrawImage(image, new Rectangle(area.X + (area.Width - width) / 2, area.Y, width, height));
        }
        else
        {
            Rectangle placeholder = new(area.X + Math.Max(0, (area.Width - 120) / 2), area.Y, Math.Min(120, area.Width), 165);
            e.Graphics.FillRectangle(SystemBrushes.Control, placeholder);
            e.Graphics.DrawRectangle(SystemPens.ControlDark, placeholder);
        }

        TextRenderer.DrawText(e.Graphics, (e.ItemIndex + 1).ToString(CultureInfo.InvariantCulture), Font,
            new Rectangle(e.Bounds.X, e.Bounds.Bottom - 24, e.Bounds.Width, 22),
            selected ? SystemColors.HighlightText : SystemColors.WindowText,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        CheckVisibleRange();
    }

    protected override void WndProc(ref Message m)
    {
        base.WndProc(ref m);
        // Native scrolling, wheel, and resize all update demand, never render in WndProc.
        if (m.Msg is 0x115 or 0x20A or 0x5) CheckVisibleRange();
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        if (_ready && Columns.Count > 0) Columns[0].Width = Math.Max(1, ClientSize.Width - SystemInformation.VerticalScrollBarWidth - 4);
    }

    private void CheckVisibleRange()
    {
        if (!_ready || _disposed || !_active || !IsHandleCreated || VirtualListSize == 0) return;
        int top = TopItem?.Index ?? 0;
        int count = Math.Min(MaximumVisibleImages, Math.Min(VirtualListSize - top, ClientSize.Height / RowHeight + 2));
        if (top == _lastTop && count == _lastCount) return;
        _lastTop = top;
        _lastCount = count;
        CancelGeneration();
        // Retain bitmap copies for this small viewport range only.
        foreach (int index in _images.Keys.Where(index => index < top || index >= top + count).ToArray())
        {
            _images[index].Dispose();
            _images.Remove(index);
        }
        _failed.RemoveWhere(index => index < top || index >= top + count);
        _timer.Start();
    }

    private async void LoadVisible(object? sender, EventArgs e)
    {
        _timer.Stop();
        int generation = _generation;
        int end = _lastTop + _lastCount;
        // Sequential demand avoids a document-wide task list; the worker also bounds its pending slot.
        for (int index = _lastTop; index < end; index++)
        {
            if (_disposed || !_active || generation != _generation) return;
            if (_images.ContainsKey(index) || _failed.Contains(index)) continue;
            try
            {
                if (!_renderer.TryRequestThumbnail(index, out Task<ViewerRenderResult>? task, _rotation)) continue;
                using ViewerRenderResult result = await task!;
                if (_disposed || !_active || generation != _generation) return;
                Bitmap bitmap = WinFormsImageConverter.CreateBitmap(result.Pixels);
                if (!_images.TryAdd(index, bitmap)) bitmap.Dispose();
                Invalidate();
            }
            catch (OperationCanceledException) { return; }
            catch (Exception exception)
            {
                if (_disposed || generation != _generation) return;
                _failed.Add(index);
                System.Diagnostics.Debug.WriteLine($"Thumbnail {index + 1} failed: {exception.Message}");
            }
        }
    }

    private void CancelGeneration()
    {
        _generation++;
        _timer.Stop();
        _renderer.CancelThumbnails();
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
            CancelGeneration();
            _timer.Dispose();
            ClearImages();
            SmallImageList = null;
            _rowSizer.Dispose();
        }
        base.Dispose(disposing);
    }
}
