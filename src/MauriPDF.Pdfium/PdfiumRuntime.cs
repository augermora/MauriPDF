using PDFiumCore;

namespace MauriPDF.Pdfium;

/// <summary>Process-wide lifetime and serialization for PDFium, whose public API is not thread-safe.</summary>
public static class PdfiumRuntime
{
    private static readonly object Gate = new();
    private static int _leases;

    public static IDisposable Acquire()
    {
        lock (Gate)
        {
            if (_leases++ == 0) fpdfview.FPDF_InitLibrary();
            return new LibraryLease();
        }
    }

    public static IDisposable Enter()
    {
        Monitor.Enter(Gate);
        if (_leases == 0)
        {
            Monitor.Exit(Gate);
            throw new InvalidOperationException("PDFium has not been initialized.");
        }
        return new CallLease();
    }

    private sealed class LibraryLease : IDisposable
    {
        private bool _disposed;
        public void Dispose()
        {
            lock (Gate)
            {
                if (_disposed) return;
                _disposed = true;
                if (--_leases == 0) fpdfview.FPDF_DestroyLibrary();
            }
        }
    }

    private sealed class CallLease : IDisposable
    {
        private bool _disposed;
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Monitor.Exit(Gate);
        }
    }
}
