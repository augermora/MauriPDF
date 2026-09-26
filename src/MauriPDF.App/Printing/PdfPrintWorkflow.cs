using System.Drawing.Printing;
using MauriPDF.Core.Rendering;
using MauriPDF.Core.Viewing;
using MauriPDF.Editing;
using MauriPDF.Rendering;

namespace MauriPDF.App.Printing;

internal sealed record PdfPrintRequest(string SourcePath, string DocumentName, PdfMaterializationPlan Pages, int CurrentPage);

internal interface IPdfPrintWorkflow
{
    Task<bool> PrintAsync(IWin32Window owner, PdfPrintRequest request);
}

/// <summary>Owns the Windows dialog and one background STA print job. Never touches viewer caches.</summary>
internal sealed class PdfPrintWorkflow : IPdfPrintWorkflow
{
    public async Task<bool> PrintAsync(IWin32Window owner, PdfPrintRequest request)
    {
        using PrintDialog dialog = new()
        {
            AllowSomePages = true, AllowCurrentPage = true, AllowSelection = false,
            AllowPrintToFile = false, UseEXDialog = true
        };
        dialog.PrinterSettings.MinimumPage = 1;
        dialog.PrinterSettings.MaximumPage = request.Pages.Pages.Count;
        dialog.PrinterSettings.FromPage = 1;
        dialog.PrinterSettings.ToPage = request.Pages.Pages.Count;
        if (dialog.ShowDialog(owner) != DialogResult.OK) return false;
        PrinterSettings settings = (PrinterSettings)dialog.PrinterSettings.Clone();
        if (!settings.IsValid) throw new InvalidOperationException("The selected printer is unavailable.");
        (int first, int last) = PrintPageLayout.SelectRange(settings.PrintRange, settings.FromPage,
            settings.ToPage, request.CurrentPage, request.Pages.Pages.Count);

        using CancellationTokenSource cancellation = new();
        using PrintProgressWindow progress = new(cancellation);
        progress.Show(owner);
        Progress<int> report = new(page =>
        {
            if (!progress.IsDisposed) progress.SetPage(page, last + 1);
        });
        TaskCompletionSource completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Thread worker = new(() =>
        {
            try
            {
                using PdfPrintDocument document = new(request, settings, first, last,
                    () => new PdfiumRenderer(), report, cancellation.Token);
                document.Print();
                completion.SetResult();
            }
            catch (Exception exception) { completion.SetException(exception); }
        }) { IsBackground = true, Name = "MauriPDF printing" };
        worker.SetApartmentState(ApartmentState.STA);
        worker.Start();
        try { await completion.Task; return !cancellation.IsCancellationRequested; }
        finally { progress.Close(); }
    }
}

internal sealed class PdfPrintDocument : PrintDocument
{
    private readonly PdfPrintRequest _request;
    private readonly Func<IPdfRenderer> _createRenderer;
    private readonly IProgress<int> _progress;
    private readonly CancellationToken _cancellation;
    private readonly int _first, _last;
    private IPdfRenderer? _renderer;
    private IPdfRenderSession? _session;
    private int _page;

    public PdfPrintDocument(PdfPrintRequest request, PrinterSettings settings, int first, int last,
        Func<IPdfRenderer> createRenderer, IProgress<int> progress, CancellationToken cancellation)
    {
        _request = request; _first = first; _last = last; _createRenderer = createRenderer;
        _progress = progress; _cancellation = cancellation;
        PrinterSettings = settings;
        DocumentName = request.DocumentName;
        OriginAtMargins = false;
        DefaultPageSettings.Margins = new Margins(35, 35, 35, 35);
        PrintController = new StandardPrintController();
    }

    protected override void OnBeginPrint(PrintEventArgs e)
    {
        base.OnBeginPrint(e);
        if (_cancellation.IsCancellationRequested) { e.Cancel = true; return; }
        _page = _first;
        _renderer = _createRenderer();
        _session = _renderer.Open(_request.SourcePath);
    }

    protected override void OnPrintPage(PrintPageEventArgs e)
    {
        if (_cancellation.IsCancellationRequested) { e.Cancel = true; return; }
        Graphics graphics = e.Graphics ?? throw new InvalidOperationException("The printer did not provide a drawing surface.");
        RectangleF area = PrintPageLayout.PrintableBounds(e.MarginBounds, e.PageSettings.PrintableArea,
            e.PageSettings.HardMarginX, e.PageSettings.HardMarginY);
        _progress.Report(_page + 1);
        PrintPagePainter.Draw(_session!, _request.Pages.Pages[_page], graphics, area);
        e.HasMorePages = ++_page <= _last && !_cancellation.IsCancellationRequested;
        if (_cancellation.IsCancellationRequested) e.Cancel = true;
        base.OnPrintPage(e);
    }

    protected override void OnEndPrint(PrintEventArgs e)
    {
        try { ReleaseSource(); }
        finally { base.OnEndPrint(e); }
    }

    private void ReleaseSource()
    {
        try { _session?.Dispose(); }
        finally { _session = null; _renderer?.Dispose(); _renderer = null; }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) ReleaseSource();
        base.Dispose(disposing);
    }
}

internal static class PrintPageLayout
{
    public const long MaximumPixels = 8 * 1024 * 1024;

    public static (int First, int Last) SelectRange(PrintRange range, int from, int to, int current, int count)
    {
        if (count <= 0 || current < 0 || current >= count) throw new ArgumentOutOfRangeException(nameof(count));
        return range switch
        {
            PrintRange.AllPages => (0, count - 1),
            PrintRange.CurrentPage => (current, current),
            PrintRange.SomePages when from >= 1 && to >= from && to <= count => (from - 1, to - 1),
            _ => throw new ArgumentException("Choose a valid page range within this document.", nameof(range))
        };
    }

    public static RectangleF PrintableBounds(RectangleF margins, RectangleF printable, float hardX, float hardY)
    {
        RectangleF result = RectangleF.Intersect(margins, printable);
        result.Offset(-hardX, -hardY); // PrintDocument's default graphics origin is the hardware printable origin.
        if (result.Width <= 0 || result.Height <= 0) throw new InvalidOperationException("The printer's page margins leave no printable area.");
        return result;
    }

    public static RectangleF Fit(PdfPageSize page, RectangleF area)
    {
        double scale = Math.Min(area.Width / page.WidthPoints, area.Height / page.HeightPoints);
        float width = (float)(page.WidthPoints * scale), height = (float)(page.HeightPoints * scale);
        return new(area.X + (area.Width - width) / 2, area.Y + (area.Height - height) / 2, width, height);
    }

    public static Size RasterSize(RectangleF bounds)
    {
        // Bounds are hundredths of an inch. Target 300 DPI, reduced for oversized paper.
        double width = Math.Max(1, bounds.Width * 3.0), height = Math.Max(1, bounds.Height * 3.0);
        double scale = Math.Min(1, Math.Sqrt(MaximumPixels / (width * height)));
        return new(Math.Max(1, (int)Math.Floor(width * scale)), Math.Max(1, (int)Math.Floor(height * scale)));
    }
}

internal static class PrintPagePainter
{
    public static void Draw(IPdfRenderSession session, PdfPageMaterialization page, Graphics graphics, RectangleF area)
    {
        VisualRotation structural = new(page.StructuralRotationDegrees);
        PdfPageSize size = structural.EffectiveSize(session.GetPageSize(page.SourcePageIndex));
        RectangleF bounds = PrintPageLayout.Fit(size, area);
        Size pixels = PrintPageLayout.RasterSize(bounds);
        using RenderedPage rendered = session.RenderPage(page.SourcePageIndex, pixels.Width, pixels.Height, structural);
        using Bitmap bitmap = WinFormsImageConverter.CreateBitmap(rendered);
        graphics.DrawImage(bitmap, bounds);
    }
}

internal sealed class PrintProgressWindow : Form
{
    private readonly Label _status = new() { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, Text = "Preparing print job…" };
    public PrintProgressWindow(CancellationTokenSource cancellation)
    {
        Text = "MauriPDF — Printing";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        ControlBox = false; ShowInTaskbar = false;
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(360, 110);
        Button cancel = new() { Text = "Cancel printing", Dock = DockStyle.Bottom, Height = 34 };
        cancel.Click += (_, _) => { cancellation.Cancel(); cancel.Enabled = false; _status.Text = "Cancelling after the current page…"; };
        Controls.Add(_status); Controls.Add(cancel);
    }
    public void SetPage(int page, int total) => _status.Text = $"Printing page {page} of {total}…";
}
