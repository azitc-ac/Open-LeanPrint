namespace OpenLeanPrint.Core;

/// <summary>
/// A single source page as captured from a print job, identified by which
/// document and page index it came from and how big it is.
/// </summary>
/// <param name="DocumentIndex">Index of the owning <see cref="PrintDocument"/> within a <see cref="PrintJobPool"/>.</param>
/// <param name="PageIndex">Zero-based page index within the owning document.</param>
/// <param name="Size">Media size of the source page in points.</param>
public sealed record SourcePage(int DocumentIndex, int PageIndex, PtSize Size)
{
    /// <summary>
    /// A turn the user asked for, in degrees clockwise (0, 90, 180 or 270).
    /// Setting it also switches auto-rotation off for this page: once someone
    /// has said which way up they want it, the engine should not argue.
    /// </summary>
    public int Rotation { get; init; }
}

/// <summary>
/// One captured print job: an ordered list of pages plus a little metadata.
/// A document maps to a single "print" action from a source application.
/// </summary>
public sealed class PrintDocument
{
    public PrintDocument(string title, string? sourceApplication = null)
    {
        Title = title ?? string.Empty;
        SourceApplication = sourceApplication;
    }

    /// <summary>Human-readable job title (usually the document/window title).</summary>
    public string Title { get; }

    /// <summary>Name of the application that produced the job, if known.</summary>
    public string? SourceApplication { get; }

    /// <summary>Media sizes of the pages, in order.</summary>
    public IReadOnlyList<PtSize> PageSizes => _pageSizes;
    private readonly List<PtSize> _pageSizes = new();

    public int PageCount => _pageSizes.Count;

    public void AddPage(PtSize size) => _pageSizes.Add(size);
}

/// <summary>
/// A pool of captured print jobs waiting to be reviewed, combined and printed.
/// This is what powers "summarise print jobs": the user prints from several
/// applications, the jobs collect here, and are then imposed and sent to a real
/// printer as one operation.
/// </summary>
public sealed class PrintJobPool
{
    private readonly List<PrintDocument> _documents = new();

    public IReadOnlyList<PrintDocument> Documents => _documents;

    /// <summary>Adds a captured job and returns its document index in the pool.</summary>
    public int Add(PrintDocument document)
    {
        _documents.Add(document);
        return _documents.Count - 1;
    }

    public void Clear() => _documents.Clear();

    /// <summary>Total number of source pages across all pooled documents.</summary>
    public int TotalPageCount
    {
        get
        {
            int total = 0;
            foreach (var doc in _documents)
                total += doc.PageCount;
            return total;
        }
    }

    /// <summary>
    /// Flattens every page of every pooled document, in document then page order,
    /// into the sequence the imposition engine consumes.
    /// </summary>
    public IReadOnlyList<SourcePage> Flatten()
    {
        var pages = new List<SourcePage>(TotalPageCount);
        for (int d = 0; d < _documents.Count; d++)
        {
            var doc = _documents[d];
            for (int p = 0; p < doc.PageCount; p++)
                pages.Add(new SourcePage(d, p, doc.PageSizes[p]));
        }
        return pages;
    }
}
