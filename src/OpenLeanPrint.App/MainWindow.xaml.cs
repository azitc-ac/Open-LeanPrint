using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using OpenLeanPrint.Capture;
using OpenLeanPrint.Compose;
using OpenLeanPrint.Core;
using OpenLeanPrint.Core.Imposition;
using OpenLeanPrint.Print;
// WPF implicitly imports System.Windows.Shapes, whose Path would clash with
// System.IO.Path - the alias keeps the file-system one unambiguous.
using Path = System.IO.Path;

namespace OpenLeanPrint.App;

/// <summary>
/// The whole app in one window: a job pool on the left, the imposed sheet in
/// the middle, and the actions that let it leave the app along the bottom.
/// Every change re-imposes in the background and repaints the preview, so what
/// you see is what the printer gets.
/// </summary>
public partial class MainWindow : Window
{
    /// <summary>Preview resolution. Enough to read the layout, cheap to render.</summary>
    private const int PreviewDpi = 120;

    /// <summary>Resolution the printer path rasterises at.</summary>
    private const int PrintDpi = 200;

    private static readonly string[] Papers = { "A4", "A5", "A3", "A6", "Letter", "Legal", "Tabloid" };

    private readonly ObservableCollection<JobItem> _jobs = new();
    private readonly PdfImposer _imposer = new();
    private readonly DispatcherTimer _debounce;

    private byte[]? _imposed;
    private int _sheetIndex;
    private int _sheetCount;
    private CancellationTokenSource? _work;
    private CapturedFolderWatcher? _capture;

    private int _rows = 2;
    private int _columns = 2;
    private bool _booklet;

    public MainWindow()
    {
        InitializeComponent();

        JobList.ItemsSource = _jobs;
        PaperBox.ItemsSource = Papers;
        PaperBox.SelectedIndex = 0;

        PrinterBox.ItemsSource = PdfPrinter.InstalledPrinters();
        PrinterBox.SelectedItem = PdfPrinter.DefaultPrinter();

        // Typing in a number box should not re-impose on every keystroke.
        _debounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
        _debounce.Tick += (_, _) =>
        {
            _debounce.Stop();
            _ = RefreshAsync();
        };

        ApplySettings(AppSettings.Load());
        UpdateControls();

        // "OpenLeanPrint a.pdf b.pdf" (or "Open with…") starts with a filled pool.
        var startupFiles = Environment.GetCommandLineArgs().Skip(1)
            .Where(path => File.Exists(path)).ToList();
        if (startupFiles.Count > 0) Loaded += (_, _) => LoadFiles(startupFiles);
    }

    // ---------- settings ----------

    private void ApplySettings(AppSettings settings)
    {
        _booklet = settings.Booklet;
        _rows = Math.Max(1, settings.Rows);
        _columns = Math.Max(1, settings.Columns);

        if (Papers.Contains(settings.Paper)) PaperBox.SelectedItem = settings.Paper;
        MarginBox.Text = settings.MarginMm.ToString(CultureInfo.CurrentCulture);
        GutterBox.Text = settings.Gutter.ToString(CultureInfo.CurrentCulture);

        if (settings.Printer is not null && PrinterBox.Items.Contains(settings.Printer))
            PrinterBox.SelectedItem = settings.Printer;

        CheckMatchingPreset();

        // Restoring this means the app picks up where it left off: still collecting.
        if (settings.CollectCapturedJobs)
        {
            CollectToggle.IsChecked = true;
            StartCollecting();
        }
    }

    private AppSettings CurrentSettings() => new()
    {
        Rows = _rows,
        Columns = _columns,
        Booklet = _booklet,
        Paper = (string)PaperBox.SelectedItem,
        MarginMm = ParseNumber(MarginBox.Text, 0),
        Gutter = ParseNumber(GutterBox.Text, 0),
        Printer = PrinterBox.SelectedItem as string,
        CollectCapturedJobs = CollectToggle.IsChecked == true,
    };

    protected override void OnClosed(EventArgs e)
    {
        CurrentSettings().Save();
        StopCollecting();
        base.OnClosed(e);
    }

    /// <summary>Lights up the preset button that matches the current layout, if any.</summary>
    private void CheckMatchingPreset()
    {
        string tag = _booklet ? "booklet" : $"{_rows}x{_columns}";
        foreach (var preset in Presets)
            preset.IsChecked = (string)preset.Tag == tag;
    }

    private ToggleButton[] Presets => new[] { Preset1Up, Preset2Up, Preset4Up, Preset9Up, PresetBooklet };

    // ---------- collecting captured jobs ----------

    private void Collect_Click(object sender, RoutedEventArgs e)
    {
        if (CollectToggle.IsChecked == true) StartCollecting();
        else StopCollecting();
    }

    private void StartCollecting()
    {
        if (_capture is not null) return;

        try
        {
            // Only jobs that arrive from now on - the folder may hold older jobs
            // the user has no interest in reprinting.
            _capture = new CapturedFolderWatcher(CaptureLocations.DefaultFolder);
            _capture.JobArrived += (_, path) => Dispatcher.Invoke(() => LoadFiles(new[] { path }));
            _capture.Start();
            StatusText.Text = $"Collecting jobs from {CaptureLocations.DefaultFolder}";
        }
        catch (Exception ex)
        {
            _capture = null;
            CollectToggle.IsChecked = false;
            MessageBox.Show(this, ex.Message, "Could not watch the capture folder",
                            MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void StopCollecting()
    {
        _capture?.Dispose();
        _capture = null;
    }

    // ---------- job pool ----------

    private void AddJobs_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Add PDFs to the job pool",
            Filter = "PDF documents (*.pdf)|*.pdf|All files (*.*)|*.*",
            Multiselect = true,
        };
        if (dialog.ShowDialog(this) != true) return;

        LoadFiles(dialog.FileNames);
    }

    private void LoadFiles(IEnumerable<string> paths)
    {
        var failed = new List<string>();
        foreach (string path in paths)
        {
            try
            {
                byte[] pdf = File.ReadAllBytes(path);
                _jobs.Add(new JobItem
                {
                    FilePath = path,
                    Pdf = pdf,
                    PageCount = PdfImposer.ReadPageSizes(pdf).Count,
                });
            }
            catch (Exception ex)
            {
                // One unreadable file must not lose the others.
                failed.Add($"{Path.GetFileName(path)}: {ex.Message}");
            }
        }

        if (failed.Count > 0)
            MessageBox.Show(this, string.Join(Environment.NewLine, failed), "Could not add every file",
                            MessageBoxButton.OK, MessageBoxImage.Warning);

        if (JobList.SelectedIndex < 0 && _jobs.Count > 0) JobList.SelectedIndex = 0;
        _ = RefreshAsync();
    }

    private void RemoveJob_Click(object sender, RoutedEventArgs e)
    {
        int index = JobList.SelectedIndex;
        if (index < 0) return;

        _jobs.RemoveAt(index);
        JobList.SelectedIndex = Math.Min(index, _jobs.Count - 1);
        _ = RefreshAsync();
    }

    private void Clear_Click(object sender, RoutedEventArgs e)
    {
        if (_jobs.Count == 0) return;

        _jobs.Clear();
        _imposed = null;
        _sheetIndex = 0;
        _sheetCount = 0;
        _ = RefreshAsync();
    }

    private void MoveUp_Click(object sender, RoutedEventArgs e) => Move(-1);

    private void MoveDown_Click(object sender, RoutedEventArgs e) => Move(+1);

    private void Move(int delta)
    {
        int index = JobList.SelectedIndex;
        int target = index + delta;
        if (index < 0 || target < 0 || target >= _jobs.Count) return;

        _jobs.Move(index, target);
        JobList.SelectedIndex = target;
        _ = RefreshAsync();
    }

    private void JobList_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateControls();

    // ---------- layout settings ----------

    private void Preset_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not ToggleButton clicked || clicked.Tag is not string tag) return;

        foreach (var preset in Presets)
            preset.IsChecked = ReferenceEquals(preset, clicked);

        if (tag == "booklet")
        {
            _booklet = true;
        }
        else
        {
            var parts = tag.Split('x');
            _booklet = false;
            _rows = int.Parse(parts[0], CultureInfo.InvariantCulture);
            _columns = int.Parse(parts[1], CultureInfo.InvariantCulture);
        }

        _sheetIndex = 0;
        _ = RefreshAsync();
    }

    private void Paper_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded) return;
        _ = RefreshAsync();
    }

    private void Number_Changed(object sender, TextChangedEventArgs e)
    {
        if (!IsLoaded) return;
        _debounce.Stop();
        _debounce.Start();
    }

    /// <summary>Accepts both "8.5" and "8,5" — the box is typed in, not parsed from a file.</summary>
    private static double ParseNumber(string text, double fallback)
    {
        if (double.TryParse(text, NumberStyles.Any, CultureInfo.CurrentCulture, out double value) ||
            double.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out value))
            return value >= 0 ? value : fallback;
        return fallback;
    }

    // ---------- imposition & preview ----------

    private async Task RefreshAsync()
    {
        _work?.Cancel();
        var cancellation = new CancellationTokenSource();
        _work = cancellation;
        var token = cancellation.Token;

        var documents = _jobs.Select(job => job.Pdf).ToList();
        if (documents.Count == 0)
        {
            _imposed = null;
            _sheetCount = 0;
            PreviewImage.Source = null;
            UpdateControls();
            return;
        }

        var paper = PaperSizes.ByName((string)PaperBox.SelectedItem) ?? PaperSizes.A4;
        double marginMm = ParseNumber(MarginBox.Text, 0);
        double gutter = ParseNumber(GutterBox.Text, 0);
        bool booklet = _booklet;
        var settings = ImpositionSettings.NUp(_rows, _columns) with
        {
            SheetSize = paper,
            Margins = PtMargins.UniformMm(marginMm),
            GutterX = gutter,
            GutterY = gutter,
        };

        StatusText.Text = "Imposing…";
        try
        {
            var (imposed, sheets) = await Task.Run(() =>
            {
                byte[] pdf = booklet
                    ? _imposer.ImposeBookletToPdf(documents, paper, PtMargins.UniformMm(marginMm), gutter)
                    : _imposer.ImposeToPdf(documents, settings);
                return (pdf, PdfRasterizer.PageSizes(pdf).Count);
            }, token);

            if (token.IsCancellationRequested) return;

            _imposed = imposed;
            _sheetCount = sheets;
            _sheetIndex = Math.Clamp(_sheetIndex, 0, Math.Max(0, sheets - 1));
            await ShowSheetAsync(token);
        }
        catch (OperationCanceledException)
        {
            // Superseded by a newer change - nothing to report.
        }
        catch (Exception ex)
        {
            _imposed = null;
            _sheetCount = 0;
            PreviewImage.Source = null;
            StatusText.Text = ex.Message;
        }
        finally
        {
            if (ReferenceEquals(_work, cancellation)) UpdateControls();
        }
    }

    private async Task ShowSheetAsync(CancellationToken token)
    {
        byte[]? pdf = _imposed;
        if (pdf is null || _sheetCount == 0) return;

        int index = _sheetIndex;
        byte[] png = await Task.Run(() => PdfRasterizer.RenderPagePng(pdf, index, PreviewDpi), token);
        if (token.IsCancellationRequested) return;

        var image = new BitmapImage();
        using (var stream = new MemoryStream(png))
        {
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad; // decode now so the stream can go
            image.StreamSource = stream;
            image.EndInit();
        }
        PreviewImage.Source = image;
        UpdateControls();
    }

    private void PrevSheet_Click(object sender, RoutedEventArgs e) => StepSheet(-1);

    private void NextSheet_Click(object sender, RoutedEventArgs e) => StepSheet(+1);

    private void StepSheet(int delta)
    {
        int target = _sheetIndex + delta;
        if (target < 0 || target >= _sheetCount) return;

        _sheetIndex = target;
        // Page turns only repaint - no need to impose again.
        _ = ShowSheetAsync(CancellationToken.None);
    }

    // ---------- output ----------

    private async void Print_Click(object sender, RoutedEventArgs e)
    {
        if (_imposed is null) return;
        if (PrinterBox.SelectedItem is not string printer)
        {
            MessageBox.Show(this, "Choose a printer first.", "OpenLeanPrint",
                            MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        byte[] pdf = _imposed;
        PrintButton.IsEnabled = false;
        StatusText.Text = $"Printing to \"{printer}\"…";
        try
        {
            var report = await Task.Run(() => PdfPrinter.Print(pdf, printer, new PrintOptions
            {
                Dpi = PrintDpi,
                JobName = _jobs.Count == 1 ? $"OpenLeanPrint - {_jobs[0].Name}" : "OpenLeanPrint",
            }));
            StatusText.Text = $"Sent {report.Sheets} sheet(s) to \"{report.PrinterName}\" " +
                              $"({string.Join("/", report.PaperNames)}).";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Printing failed", MessageBoxButton.OK, MessageBoxImage.Error);
            UpdateControls();
        }
        finally
        {
            PrintButton.IsEnabled = _imposed is not null;
        }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (_imposed is null) return;

        string suggested = _jobs.Count > 0
            ? $"{Path.GetFileNameWithoutExtension(_jobs[0].Name)}-{LayoutTag()}.pdf"
            : $"openleanprint-{LayoutTag()}.pdf";

        var dialog = new SaveFileDialog
        {
            Title = "Save the imposed PDF",
            Filter = "PDF documents (*.pdf)|*.pdf",
            FileName = suggested,
            DefaultExt = ".pdf",
        };
        if (dialog.ShowDialog(this) != true) return;

        File.WriteAllBytes(dialog.FileName, _imposed);
        StatusText.Text = $"Saved {Path.GetFileName(dialog.FileName)} ({_imposed.Length:N0} bytes).";
    }

    // ---------- ui state ----------

    private string LayoutTag() => _booklet ? "booklet" : $"{_rows}x{_columns}up";

    private string LayoutDescription() =>
        _booklet ? $"booklet on {PaperBox.SelectedItem}" : $"{_rows}×{_columns}-up on {PaperBox.SelectedItem}";

    private void UpdateControls()
    {
        bool hasJobs = _jobs.Count > 0;
        bool hasSheets = _imposed is not null && _sheetCount > 0;
        bool hasSelection = JobList.SelectedIndex >= 0;

        EmptyPoolHint.Visibility = hasJobs ? Visibility.Collapsed : Visibility.Visible;
        SheetShadow.Visibility = hasSheets ? Visibility.Visible : Visibility.Collapsed;
        PreviewHint.Visibility = hasSheets ? Visibility.Collapsed : Visibility.Visible;

        RemoveButton.IsEnabled = hasSelection;
        UpButton.IsEnabled = hasSelection && JobList.SelectedIndex > 0;
        DownButton.IsEnabled = hasSelection && JobList.SelectedIndex < _jobs.Count - 1;
        ClearButton.IsEnabled = hasJobs;
        PrintButton.IsEnabled = hasSheets;
        SaveButton.IsEnabled = hasSheets;
        PrevSheet.IsEnabled = hasSheets && _sheetIndex > 0;
        NextSheet.IsEnabled = hasSheets && _sheetIndex < _sheetCount - 1;

        SheetLabel.Text = hasSheets ? $"Sheet {_sheetIndex + 1} of {_sheetCount}" : "—";

        if (!hasJobs)
        {
            StatusText.Text = "No jobs yet.";
            return;
        }

        int pages = _jobs.Sum(job => job.PageCount);
        string jobWord = _jobs.Count == 1 ? "job" : "jobs";
        StatusText.Text = hasSheets
            ? $"{_jobs.Count} {jobWord} · {pages} pages → {_sheetCount} sheets · {LayoutDescription()}"
            : $"{_jobs.Count} {jobWord} · {pages} pages";
    }
}
