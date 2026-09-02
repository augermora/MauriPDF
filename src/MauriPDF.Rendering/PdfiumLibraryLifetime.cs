using PDFiumCore;

namespace MauriPDF.Rendering;

internal static class PdfiumLibraryLifetime
{
    private static readonly object SyncRoot = new();
    private static int _referenceCount;

    public static IDisposable Acquire()
    {
        lock (SyncRoot)
        {
            if (_referenceCount == 0)
            {
                fpdfview.FPDF_InitLibrary();
            }

            _referenceCount++;
            return new Lease();
        }
    }

    private sealed class Lease : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            lock (SyncRoot)
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;
                _referenceCount--;

                if (_referenceCount == 0)
                {
                    fpdfview.FPDF_DestroyLibrary();
                }
            }
        }
    }
}
