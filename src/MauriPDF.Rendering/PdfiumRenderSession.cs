using System.Buffers;
using MauriPDF.Core.Rendering;
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

    public unsafe RenderedPage RenderPage(int pageIndex, int pixelWidth, int pixelHeight)
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
                0,
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
