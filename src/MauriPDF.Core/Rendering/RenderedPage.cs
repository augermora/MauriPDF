using System.Buffers;

namespace MauriPDF.Core.Rendering;

/// <summary>Owns a rendered pixel buffer. Pixel data is valid until this instance is disposed.</summary>
public sealed class RenderedPage : IDisposable
{
    private IMemoryOwner<byte>? _pixelOwner;
    private readonly int _byteLength;

    public RenderedPage(
        int width,
        int height,
        int stride,
        RenderedPixelFormat pixelFormat,
        IMemoryOwner<byte> pixelOwner)
    {
        ArgumentNullException.ThrowIfNull(pixelOwner);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        ArgumentOutOfRangeException.ThrowIfLessThan(stride, checked(width * 4));

        _byteLength = checked(stride * height);
        ArgumentOutOfRangeException.ThrowIfLessThan(pixelOwner.Memory.Length, _byteLength);

        Width = width;
        Height = height;
        Stride = stride;
        PixelFormat = pixelFormat;
        AllocatedBytes = pixelOwner.Memory.Length;
        _pixelOwner = pixelOwner;
    }

    public int Width { get; }

    public int Height { get; }

    public int Stride { get; }

    public RenderedPixelFormat PixelFormat { get; }

    /// <summary>Backing allocation size, including any pool capacity beyond visible pixels.</summary>
    public int AllocatedBytes { get; }

    public ReadOnlyMemory<byte> Pixels => (_pixelOwner ?? throw new ObjectDisposedException(nameof(RenderedPage)))
        .Memory[.._byteLength];

    public void Dispose()
    {
        _pixelOwner?.Dispose();
        _pixelOwner = null;
    }
}
