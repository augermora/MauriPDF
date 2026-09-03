using System.Buffers;
using MauriPDF.Core.Rendering;
using MauriPDF.Core.Text;
using PDFiumCore;

namespace MauriPDF.Rendering;

internal sealed class PdfiumRenderSession : IPdfRenderSession
{
    private const int BytesPerPixel = 4;
    private const long MaximumBufferBytes = 512L * 1024 * 1024;

    private FpdfDocumentT? _document;
    private IDisposable? _libraryLease;

    public PdfiumRenderSession(FpdfDocumentT document, int pageCount, IDisposable libraryLease)
    {
        _document = document;
        _libraryLease = libraryLease;
        PageCount = pageCount;
    }

    public int PageCount { get; }

    public Core.Outline.PdfOutline ExtractOutline() => new PdfiumOutlineReader(GetDocument(), PageCount).Read();

    public PdfTextPage ExtractText(int pageIndex)
    {
        FpdfDocumentT document = GetDocument();
        ValidatePageIndex(pageIndex);
        FpdfPageT? page = fpdfview.FPDF_LoadPage(document, pageIndex);
        if (page is null) throw new InvalidDataException("PDFium could not load the text page.");
        FpdfTextpageT? text = null;
        try
        {
            text = fpdf_text.FPDFTextLoadPage(page);
            if (text is null) throw new InvalidDataException("PDFium could not read the text layer.");
            int count = fpdf_text.FPDFTextCountChars(text);
            if (count < 0) throw new InvalidDataException("PDFium could not count text characters.");
            if (count > PdfTextPage.MaximumCharacters)
                throw new InvalidDataException($"Text selection is limited to {PdfTextPage.MaximumCharacters:N0} characters per page.");

            // Sample the native affine page transform, including CropBox and intrinsic rotation.
            // The virtual million-unit device is geometry only; no raster is allocated.
            TextPoint origin = ToNormalized(page, 0, 0);
            TextPoint xAxis = ToNormalized(page, 1000, 0);
            TextPoint yAxis = ToNormalized(page, 0, 1000);
            TextPoint Map(double x, double y) => new(
                origin.X + x / 1000 * (xAxis.X - origin.X) + y / 1000 * (yAxis.X - origin.X),
                origin.Y + x / 1000 * (xAxis.Y - origin.Y) + y / 1000 * (yAxis.Y - origin.Y));

            TextCharacter[] characters = new TextCharacter[count];
            for (int index = 0; index < count; index++)
            {
                uint unicode = fpdf_text.FPDFTextGetUnicode(text, index);
                double left = 0, right = 0, bottom = 0, top = 0;
                TextBounds bounds = default;
                if (fpdf_text.FPDFTextGetCharBox(text, index, ref left, ref right, ref bottom, ref top) != 0
                    && double.IsFinite(left) && double.IsFinite(right) && double.IsFinite(bottom) && double.IsFinite(top)
                    && right > left && top > bottom)
                {
                    TextPoint a = Map(left, top), b = Map(right, top), c = Map(left, bottom), d = Map(right, bottom);
                    double minX = Math.Min(Math.Min(a.X, b.X), Math.Min(c.X, d.X));
                    double maxX = Math.Max(Math.Max(a.X, b.X), Math.Max(c.X, d.X));
                    double minY = Math.Min(Math.Min(a.Y, b.Y), Math.Min(c.Y, d.Y));
                    double maxY = Math.Max(Math.Max(a.Y, b.Y), Math.Max(c.Y, d.Y));
                    if (maxX <= 0 || minX >= 1 || maxY <= 0 || minY >= 1) unicode = 0; // Cropped-out glyph.
                    else bounds = new(Math.Clamp(minX, 0, 1), Math.Clamp(minY, 0, 1), Math.Clamp(maxX, 0, 1), Math.Clamp(maxY, 0, 1));
                }
                characters[index] = new(unicode, bounds);
            }
            return new PdfTextPage(characters);
        }
        finally
        {
            try { if (text is not null) fpdf_text.FPDFTextClosePage(text); }
            finally { fpdfview.FPDF_ClosePage(page); }
        }
    }

    private static TextPoint ToNormalized(FpdfPageT page, double x, double y)
    {
        const int units = 1_000_000;
        int deviceX = 0, deviceY = 0;
        if (fpdfview.FPDF_PageToDevice(page, 0, 0, units, units, 0, x, y, ref deviceX, ref deviceY) == 0)
            throw new InvalidDataException("PDFium could not map text coordinates.");
        return new((double)deviceX / units, (double)deviceY / units);
    }

    public PdfPageSize GetPageSize(int pageIndex)
    {
        FpdfDocumentT document = GetDocument();
        ValidatePageIndex(pageIndex);

        double width = 0;
        double height = 0;
        int succeeded = fpdfview.FPDF_GetPageSizeByIndex(document, pageIndex, ref width, ref height);

        if (succeeded == 0 || !double.IsFinite(width) || !double.IsFinite(height) || width <= 0 || height <= 0)
        {
            throw new InvalidDataException($"PDFium could not read the dimensions of page {pageIndex + 1}.");
        }

        return new PdfPageSize(width, height);
    }

    public unsafe RenderedPage RenderPage(int pageIndex, int pixelWidth, int pixelHeight, Core.Viewing.VisualRotation rotation = default)
    {
        FpdfDocumentT document = GetDocument();
        ValidatePageIndex(pageIndex);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(pixelWidth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(pixelHeight);

        int stride = checked(pixelWidth * BytesPerPixel);
        int byteLength = checked(stride * pixelHeight);
        if (byteLength > MaximumBufferBytes)
        {
            throw new ArgumentOutOfRangeException(nameof(pixelWidth), "The requested render exceeds the 512 MiB safety limit.");
        }

        FpdfPageT? page = fpdfview.FPDF_LoadPage(document, pageIndex);
        if (page is null)
        {
            throw new InvalidDataException($"PDFium could not load page {pageIndex + 1}.");
        }

        IMemoryOwner<byte>? pixelOwner = null;
        FpdfBitmapT? bitmap = null;
        MemoryHandle pinnedPixels = default;

        try
        {
            pixelOwner = MemoryPool<byte>.Shared.Rent(byteLength);
            pinnedPixels = pixelOwner.Memory[..byteLength].Pin();

            bitmap = fpdfview.FPDFBitmapCreateEx(
                pixelWidth,
                pixelHeight,
                (int)FPDFBitmapFormat.BGRA,
                new IntPtr(pinnedPixels.Pointer),
                stride);

            if (bitmap is null)
            {
                throw new InvalidOperationException("PDFium could not allocate a render bitmap.");
            }

            _ = fpdfview.FPDFBitmapFillRect(bitmap, 0, 0, pixelWidth, pixelHeight, uint.MaxValue);
            fpdfview.FPDF_RenderPageBitmap(
                bitmap,
                page,
                0,
                0,
                pixelWidth,
                pixelHeight,
                rotation.QuarterTurns,
                (int)RenderFlags.RenderAnnotations);

            RenderedPage result = new(
                pixelWidth,
                pixelHeight,
                stride,
                RenderedPixelFormat.Bgra32,
                pixelOwner);

            pixelOwner = null;
            return result;
        }
        finally
        {
            try
            {
                if (bitmap is not null)
                {
                    fpdfview.FPDFBitmapDestroy(bitmap);
                }
            }
            finally
            {
                // Destroy the native bitmap before unpinning its borrowed backing memory.
                pinnedPixels.Dispose();
                pixelOwner?.Dispose();
                fpdfview.FPDF_ClosePage(page);
            }
        }
    }

    public void Dispose()
    {
        try
        {
            if (_document is not null)
            {
                fpdfview.FPDF_CloseDocument(_document);
            }
        }
        finally
        {
            _document = null;
            _libraryLease?.Dispose();
            _libraryLease = null;
        }
    }

    private FpdfDocumentT GetDocument()
    {
        return _document ?? throw new ObjectDisposedException(nameof(PdfiumRenderSession));
    }

    private void ValidatePageIndex(int pageIndex)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(pageIndex);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(pageIndex, PageCount);
    }
}
