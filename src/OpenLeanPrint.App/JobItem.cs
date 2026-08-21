// WPF implicitly imports System.Windows.Shapes, whose Path would clash with
// System.IO.Path - the alias keeps the file-system one unambiguous.
using System.ComponentModel;
using OpenLeanPrint.Core;
using Path = System.IO.Path;

namespace OpenLeanPrint.App;

/// <summary>One PDF in the job pool, held in memory so re-imposing is instant.</summary>
public sealed class JobItem : INotifyPropertyChanged
{
    private PageSelection _pages = PageSelection.All;

    /// <summary>Turns the user asked for, by 1-based page number.</summary>
    public Dictionary<int, int> Rotations { get; } = new();

    public required string FilePath { get; init; }
    public required byte[] Pdf { get; init; }
    public required int PageCount { get; init; }

    /// <summary>Which of this job's pages to print. Defaults to all of them.</summary>
    public PageSelection Pages
    {
        get => _pages;
        set
        {
            _pages = value;
            // The list shows the selection, so it has to hear about the change.
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Pages)));
            NotifySummaryChanged();
        }
    }

    public string Name => Path.GetFileName(FilePath);

    /// <summary>What a screen reader announces for a row; the type name otherwise.</summary>
    public override string ToString() => Name;

    public string Summary
    {
        get
        {
            string pages = PageCount == 1 ? "1 page" : $"{PageCount} pages";
            if (!_pages.IsAll) pages += $" · printing {_pages}";
            if (Rotations.Count > 0) pages += $" · {Rotations.Count} turned";
            return pages;
        }
    }

    /// <summary>The list shows the summary, so it has to hear when it changes.</summary>
    public void NotifySummaryChanged() =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Summary)));

    public event PropertyChangedEventHandler? PropertyChanged;
}
