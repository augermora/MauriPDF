using MauriPDF.Core.Text;

namespace MauriPDF.Rendering;

/// <summary>Worker-owned LRU of immutable managed text data; independent of raster ownership/budgets.</summary>
public sealed class TextPageCache
{
    public const long DefaultBudgetBytes = 8L * 1024 * 1024;
    public const int MaximumPages = 16;
    private readonly Dictionary<(long Document, int Page), LinkedListNode<Entry>> _entries = [];
    private readonly LinkedList<Entry> _lru = [];
    private readonly long _budget;
    public long RetainedBytes { get; private set; }
    public int Count => _entries.Count;

    public TextPageCache(long budgetBytes = DefaultBudgetBytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(budgetBytes);
        _budget = budgetBytes;
    }

    public PdfTextPage? Get(long document, int page)
    {
        if (!_entries.TryGetValue((document, page), out LinkedListNode<Entry>? node)) return null;
        _lru.Remove(node);
        _lru.AddFirst(node);
        return node.Value.Text;
    }

    public void Store(long document, int page, PdfTextPage text)
    {
        if (_entries.TryGetValue((document, page), out LinkedListNode<Entry>? previous)) Remove(previous);
        if (text.EstimatedBytes > _budget) return;
        while (_lru.Last is not null && (RetainedBytes + text.EstimatedBytes > _budget || Count >= MaximumPages)) Remove(_lru.Last);
        _entries.Add((document, page), _lru.AddFirst(new Entry(document, page, text)));
        RetainedBytes += text.EstimatedBytes;
    }

    public void Clear()
    {
        _entries.Clear();
        _lru.Clear();
        RetainedBytes = 0;
    }

    private void Remove(LinkedListNode<Entry> node)
    {
        _entries.Remove((node.Value.Document, node.Value.Page));
        _lru.Remove(node);
        RetainedBytes -= node.Value.Text.EstimatedBytes;
    }

    private sealed record Entry(long Document, int Page, PdfTextPage Text);
}
