// WPF implicitly imports System.Windows.Shapes, whose Path would clash with
// System.IO.Path - the alias keeps the file-system one unambiguous.
using Path = System.IO.Path;

namespace OpenLeanPrint.App;

/// <summary>One PDF in the job pool, held in memory so re-imposing is instant.</summary>
public sealed class JobItem
{
    public required string FilePath { get; init; }
    public required byte[] Pdf { get; init; }
    public required int PageCount { get; init; }

    public string Name => Path.GetFileName(FilePath);

    public string Summary => PageCount == 1 ? "1 page" : $"{PageCount} pages";
}
