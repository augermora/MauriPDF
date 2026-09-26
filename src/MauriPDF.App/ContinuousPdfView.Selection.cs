using System.Runtime.InteropServices;
using MauriPDF.Core.Text;

namespace MauriPDF.App;

internal sealed partial class ContinuousPdfView
{
    private readonly Dictionary<int, PdfTextPage> _textPages = [];
    private readonly SolidBrush _selectionBrush = new(Color.FromArgb(85, 40, 120, 230));
    private readonly ContextMenuStrip _textMenu = new();
    private TextSelection? _selection;
    private TextPosition? _textAnchor;
    private PageTextPoint? _anchorPoint;
    private PageTextPoint? _endPoint;
    private long _interaction;
    private long? _loadingInteraction;
    private bool _draggingText;
    private bool _selectionReady;

    public event Action<string>? SelectionStatusChanged;
    public event Action? SelectionAvailabilityChanged;
    public bool CanCopySelection => _selectionReady && _selection is { IsEmpty: false };

    private readonly record struct PageTextPoint(int Page, TextPoint Point, int Width, int Height);

    private void InitializeSelection()
    {
        ToolStripItem copy = _textMenu.Items.Add("Copy", null, (_, _) => CopySelection());
        _textMenu.Opening += (_, _) => copy.Enabled = _selectionReady && _selection is { IsEmpty: false };
        ContextMenuStrip = _textMenu;
    }

    public void CopySelection()
    {
        if (!_selectionReady || _selection is null) return;
        string? text = _selection.Reconstruct(_textPages);
        if (string.IsNullOrEmpty(text)) return;
        try { Clipboard.SetText(text, TextDataFormat.UnicodeText); }
        catch (Exception exception) when (exception is ExternalException or ThreadStateException or InvalidOperationException)
        {
            SelectionStatusChanged?.Invoke("Clipboard unavailable. Please try Copy again.");
        }
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        Focus();
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left) return;
        ClearSelection();
        _anchorPoint = LocateTextPoint(e.Location, nearest: false);
        if (_anchorPoint is null) return;
        _endPoint = _anchorPoint;
        _draggingText = true;
        Capture = true;
        ResolveSelection();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_draggingText) UpdateDragPoint(e.Location);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.Button != MouseButtons.Left || !_draggingText) return;
        UpdateDragPoint(e.Location);
        _draggingText = false;
        Capture = false;
        // A pending lazy extraction may still finish this interaction after release.
    }

    protected override void OnMouseCaptureChanged(EventArgs e)
    {
        base.OnMouseCaptureChanged(e);
        if (!Capture) _draggingText = false;
    }

    private void UpdateDragPoint(Point location)
    {
        PageTextPoint? point = LocateTextPoint(location, nearest: true);
        if (point is null || _anchorPoint is null) return;
        if (Math.Abs(point.Value.Page - _anchorPoint.Value.Page) >= TextSelection.MaximumPages)
        {
            SelectionStatusChanged?.Invoke("Selection is limited to 16 consecutive pages.");
            return;
        }
        _endPoint = point;
        ResolveSelection();
    }

    private PageTextPoint? LocateTextPoint(Point point, bool nearest)
    {
        if (_layout is null || _disposed) return null;
        if (!nearest && (point.X < 0 || point.Y < 0 || point.X >= ViewWidth || point.Y >= ViewHeight)) return null;
        double y = _top + Math.Clamp(point.Y, 0, ViewHeight - 1);
        int page = _layout.CurrentPage(y, 0);
        var geometry = _layout[page];
        double left = _layout.Left(page, ViewWidth) - _left;
        double top = geometry.Top - _top;
        if (!nearest && (point.X < left || point.X > left + geometry.Width || point.Y < top || point.Y > top + geometry.Height)) return null;
        var rotation = _layout.RotationForPage(page);
        return new(page, TextCoordinateTransform.ToPage(point.X, point.Y, left, top, geometry.Width, geometry.Height, rotation),
            rotation.SwapsDimensions ? geometry.Height : geometry.Width, rotation.SwapsDimensions ? geometry.Width : geometry.Height);
    }

    private async void ResolveSelection()
    {
        long interaction = _interaction;
        if (_loadingInteraction == interaction || _anchorPoint is not PageTextPoint anchor || _layout is null) return;
        _loadingInteraction = interaction;
        _selectionReady = false;
        try
        {
            if (!_textPages.TryGetValue(anchor.Page, out PdfTextPage? anchorText))
            {
                anchorText = await _renderer.ExtractTextAsync(SourcePageIndex(anchor.Page));
                if (_disposed || interaction != _interaction) return;
                _textPages.Add(anchor.Page, anchorText);
            }
            if (_textAnchor is null)
            {
                int? hit = anchorText.HitTest(anchor.Point, anchor.Width, anchor.Height, 6);
                if (hit is null)
                {
                    ClearSelection(); // Blank or image-only page: no selection and no clipboard action.
                    return;
                }
                _textAnchor = new TextPosition(anchor.Page, hit.Value);
            }

            while (_endPoint is PageTextPoint end)
            {
                int first = Math.Min(anchor.Page, end.Page), last = Math.Max(anchor.Page, end.Page);
                // Only retain the consecutive range actually being selected, never a document scan.
                foreach (int page in _textPages.Keys.Where(page => page < first || page > last).ToArray()) _textPages.Remove(page);
                int missing = -1;
                for (int page = first; page <= last; page++)
                    if (!_textPages.ContainsKey(page)) { missing = page; break; }
                if (missing >= 0)
                {
                    PdfTextPage text = await _renderer.ExtractTextAsync(SourcePageIndex(missing));
                    if (_disposed || interaction != _interaction) return;
                    if (_textPages.Values.Sum(page => page.Count) + text.Count > TextSelection.MaximumCharacters)
                    {
                        ClearSelection();
                        SelectionStatusChanged?.Invoke("Selection is limited to 131,072 retained text characters.");
                        return;
                    }
                    _textPages.Add(missing, text);
                    continue; // Re-read the latest drag endpoint, not a queue of mouse movements.
                }
                PdfTextPage endText = _textPages[end.Page];
                int endOffset = endText.HitTest(end.Point, end.Width, end.Height, double.PositiveInfinity) ?? 0;
                _selection = new TextSelection(_textAnchor.Value, new TextPosition(end.Page, endOffset));
                _selectionReady = true;
                SelectionAvailabilityChanged?.Invoke();
                SelectionStatusChanged?.Invoke(string.Empty);
                Invalidate();
                return;
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception exception)
        {
            if (_disposed || interaction != _interaction) return;
            ClearSelection();
            SelectionStatusChanged?.Invoke($"Text selection unavailable: {exception.Message}");
        }
        finally
        {
            if (_loadingInteraction == interaction) _loadingInteraction = null;
        }
    }

    private void PaintSelection(Graphics graphics, int page, Rectangle display)
    {
        if (_selection is null || !_textPages.TryGetValue(page, out PdfTextPage? text)) return;
        (int start, int end) = _selection.RangeForPage(page, text.Count);
        for (int index = start; index < end; index++)
        {
            TextBounds box = text[index].Bounds;
            if (!box.HasArea || text[index].Unicode is 0 or 10 or 13) continue;
            TextBounds mapped = TextCoordinateTransform.ToDisplay(box, display.Left, display.Top, display.Width, display.Height, _layout!.RotationForPage(page));
            graphics.FillRectangle(_selectionBrush, (float)mapped.Left, (float)mapped.Top,
                (float)(mapped.Right - mapped.Left), (float)(mapped.Bottom - mapped.Top));
        }
    }

    private void ClearSelection()
    {
        _interaction++;
        _renderer.CancelText();
        _selection = null;
        _textAnchor = null;
        _anchorPoint = _endPoint = null;
        _textPages.Clear();
        _selectionReady = false;
        _draggingText = false;
        SelectionAvailabilityChanged?.Invoke();
        Capture = false;
        Invalidate();
    }

    private void DisposeSelection()
    {
        ClearSelection();
        ContextMenuStrip = null;
        _textMenu.Dispose();
        _selectionBrush.Dispose();
    }
}
