using MauriPDF.Core.Rendering;
using MauriPDF.Pdfium;
using PDFiumCore;

namespace MauriPDF.Rendering;

public sealed class PdfiumRenderer : IPdfRenderer
{
    private IDisposable? _libraryLease = PdfiumRuntime.Acquire();

    public IPdfRenderSession Open(string filePath)
    {
        ObjectDisposedException.ThrowIf(_libraryLease is null, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        string fullPath = Path.GetFullPath(filePath);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException("The PDF file was not found.", fullPath);
        }

        IDisposable sessionLease = PdfiumRuntime.Acquire();
        using IDisposable nativeCall = PdfiumRuntime.Enter();
        FpdfDocumentT? document = null;
        try
        {
            document = fpdfview.FPDF_LoadDocument(fullPath, null!);
            if (document is null)
            {
                throw CreateLoadException(fullPath, fpdfview.FPDF_GetLastError());
            }

            int pageCount = fpdfview.FPDF_GetPageCount(document);
            if (pageCount <= 0)
            {
                throw new InvalidDataException("The PDF does not contain any readable pages.");
            }

            PdfiumRenderSession session = new(document, pageCount, sessionLease);
            document = null;
            return session;
        }
        catch
        {
            if (document is not null)
            {
                fpdfview.FPDF_CloseDocument(document);
            }

            sessionLease.Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        _libraryLease?.Dispose();
        _libraryLease = null;
    }

    private static InvalidDataException CreateLoadException(string filePath, ulong errorCode)
    {
        string reason = errorCode switch
        {
            2 => "The file could not be accessed.",
            3 => "The file is not a valid or supported PDF.",
            4 => "The PDF is password protected.",
            5 => "The PDF uses an unsupported security scheme.",
            6 => "A required page could not be loaded.",
            _ => "PDFium could not open the document."
        };

        return new InvalidDataException($"{reason} File: {filePath}");
    }
}
