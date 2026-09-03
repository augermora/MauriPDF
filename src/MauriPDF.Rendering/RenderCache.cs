using System.Buffers;
using MauriPDF.Core.Rendering;

namespace MauriPDF.Rendering;

public readonly record struct RenderCacheKey(long DocumentId, int PageIndex, int Width, int Height);

/// <summary>Single-worker LRU cache. Store transfers ownership; reads return independent disposable copies.</summary>
public sealed class RenderCache : IDisposable
{
    public const long DefaultBudgetBytes = 64L * 1024 * 1024;
    private readonly Dictionary<RenderCacheKey, LinkedListNode<Entry>> _entries = [];
    private readonly LinkedList<Entry> _lru = new();
    private bool _disposed;

    public RenderCache(long budgetBytes = DefaultBudgetBytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(budgetBytes);
        BudgetBytes = budgetBytes;
    }

    public long BudgetBytes { get; }
    public long UsedBytes { get; private set; }
    public int Count => _entries.Count;

    public RenderedPage? GetCopy(RenderCacheKey key)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_entries.TryGetValue(key, out LinkedListNode<Entry>? node))
        {
            return null;
        }

        _lru.Remove(node);
        _lru.AddFirst(node);
        return Copy(node.Value.Pixels);
    }

    public void Store(RenderCacheKey key, RenderedPage pixels)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(pixels);
        if (_entries.TryGetValue(key, out LinkedListNode<Entry>? existing))
        {
            Remove(existing);
        }

        if (pixels.AllocatedBytes > BudgetBytes)
        {
            pixels.Dispose();
            return;
        }

        while (UsedBytes + pixels.AllocatedBytes > BudgetBytes)
        {
            Remove(_lru.Last!);
        }

        LinkedListNode<Entry> node = _lru.AddFirst(new Entry(key, pixels));
        _entries.Add(key, node);
        UsedBytes += pixels.AllocatedBytes;
    }

    public void Clear()
    {
        while (_lru.Last is { } node)
        {
            Remove(node);
        }
    }

    public void Dispose()
    {
        Clear();
        _disposed = true;
    }

    internal static RenderedPage Copy(RenderedPage source)
    {
        IMemoryOwner<byte> owner = MemoryPool<byte>.Shared.Rent(source.Pixels.Length);
        try
        {
            source.Pixels.CopyTo(owner.Memory);
            return new RenderedPage(source.Width, source.Height, source.Stride, source.PixelFormat, owner);
        }
        catch
        {
            owner.Dispose();
            throw;
        }
    }

    private void Remove(LinkedListNode<Entry> node)
    {
        _entries.Remove(node.Value.Key);
        _lru.Remove(node);
        UsedBytes -= node.Value.Pixels.AllocatedBytes;
        node.Value.Pixels.Dispose();
    }

    private sealed record Entry(RenderCacheKey Key, RenderedPage Pixels);
}
