using System.Collections.ObjectModel;
using MauriPDF.Core.Documents;

namespace MauriPDF.Editing;

/// <summary>Session-long metadata/path registry. Rendering separately owns the corresponding native sessions.</summary>
public sealed class DocumentSourceRegistry
{
    public const int MaximumSources = 64;
    public const int MaximumSourcePages = 250_000;
    private readonly Dictionary<Guid, PdfSourceDocument> _sources = [];
    public DocumentSourceRegistry() => Sources = new ReadOnlyDictionary<Guid, PdfSourceDocument>(_sources);
    public IReadOnlyDictionary<Guid, PdfSourceDocument> Sources { get; }
    public void ValidateAddition(PdfSourceDocument source)
    {
        if (_sources.TryGetValue(source.Id, out PdfSourceDocument? existing))
        {
            if (!ReferenceEquals(existing, source)) throw new ArgumentException("Source identity is already registered.", nameof(source));
            return;
        }
        if (_sources.Count >= MaximumSources || _sources.Values.Sum(value => value.PageCount) + (long)source.PageCount > MaximumSourcePages)
            throw new InvalidOperationException("The composition source limit has been reached (64 sources / 250,000 source pages).");
    }
    internal void Add(PdfSourceDocument source) { ValidateAddition(source); _sources.TryAdd(source.Id, source); }
    public void ProtectDestination(string destination)
    {
        foreach (PdfSourceDocument source in _sources.Values) FilePathIdentity.RejectSourceAlias(source.Path, destination);
    }
    public PdfSourceDocument? FindPath(string path) => _sources.Values.FirstOrDefault(source => FilePathIdentity.SameFile(source.Path, path));
}
