using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
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

    /// <summary>One entry in the "Sides" box; ToString is what the box shows.</summary>
    private sealed record DuplexChoice(string Label, DuplexMode Mode)
    {
        public override string ToString() => Label;
    }

    private static readonly DuplexChoice[] DuplexChoices =
    {
        new("Printer default", DuplexMode.Default),
        new("Single-sided", DuplexMode.Simplex),
        new("Two-sided, long edge", DuplexMode.LongEdge),
        new("Two-sided, short edge", DuplexMode.ShortEdge),
    };

    private readonly ObservableCollection<JobItem> _jobs = new();
    private readonly ObservableCollection<LayoutProfile> _profiles = new();
    private readonly DispatcherTimer _debounce;

    private byte[]? _imposed;
    private int _sheetIndex;
    private int _sheetCount;
    private CancellationTokenSource? _work;
    private readonly List<CapturedFolderWatcher> _capture = new();
    private readonly CaptureService _service = new();
    private bool _suppressPagesEdit;
    private bool _suppressGridEdit;
    private bool _printerSetupOffered;

    /// <summary>Newest captured job already taken into the pool - see <see cref="AppSettings.LastCollectedUtc"/>.</summary>
    private DateTime _collectedThrough;
    private bool _showOnCapture = true;

    /// <summary>
    /// Jobs that were already waiting when collecting started. They go into the
    /// pool but must not raise the window: at login that would mean a window in
    /// your face for something you printed yesterday.
    /// </summary>
    private readonly HashSet<string> _backlog = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The layout behind the picture, so a click can be traced back to a page.</summary>
    private ImpositionResult? _layout;
    private readonly TrayPresence _tray;
    private bool _exiting;

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
        DuplexBox.ItemsSource = DuplexChoices;
        DuplexBox.SelectedIndex = 0;

        ProfileBox.ItemsSource = _profiles;
        ProfileBox.DisplayMemberPath = nameof(LayoutProfile.Name);

        // Typing in a number box should not re-impose on every keystroke.
        _debounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
        _debounce.Tick += (_, _) =>
        {
            _debounce.Stop();
            _ = RefreshAsync();
        };

        _tray = new TrayPresence();
        _tray.ShowRequested += (_, _) => Dispatcher.Invoke(RestoreFromTray);
        _tray.ExitRequested += (_, _) => Dispatcher.Invoke(ExitForGood);
        _tray.CollectingChanged += (_, collecting) => Dispatcher.Invoke(() => SetCollecting(collecting));
        _tray.ShowOnCaptureChanged += (_, show) => Dispatcher.Invoke(() => SetShowOnCapture(show));

        var settings = AppSettings.Load();
        ApplySettings(settings);
        UpdatePrinterSetupButton();
        UpdateControls();

        // Ask once, after the window is up - a dialog during construction would
        // appear before anything is on screen.
        Loaded += (_, _) => OfferPrinterSetup(settings);

        // "OpenLeanPrint a.pdf b.pdf" (or "Open with…") starts with a filled pool.
        var startupFiles = Environment.GetCommandLineArgs().Skip(1)
            .Where(path => File.Exists(path)).ToList();
        if (startupFiles.Count > 0) Loaded += (_, _) => LoadFiles(startupFiles);
    }

    // ---------- settings ----------

    private void ApplySettings(AppSettings settings)
    {
        _printerSetupOffered = settings.PrinterSetupOffered;
        _collectedThrough = settings.LastCollectedUtc;
        SetShowOnCapture(settings.ShowOnCapture);
        _booklet = settings.Booklet;
        _rows = Math.Max(1, settings.Rows);
        _columns = Math.Max(1, settings.Columns);

        if (Papers.Contains(settings.Paper)) PaperBox.SelectedItem = settings.Paper;
        MarginBox.Text = settings.MarginMm.ToString(CultureInfo.CurrentCulture);
        GutterBox.Text = settings.Gutter.ToString(CultureInfo.CurrentCulture);

        if (settings.Printer is not null && PrinterBox.Items.Contains(settings.Printer))
            PrinterBox.SelectedItem = settings.Printer;

        WatermarkBox.Text = settings.Watermark ?? string.Empty;

        _profiles.Clear();
        foreach (var profile in settings.Profiles) _profiles.Add(profile);

        if (DuplexModes.TryParse(settings.Duplex, out var duplex))
            DuplexBox.SelectedItem = DuplexChoices.FirstOrDefault(choice => choice.Mode == duplex) ?? DuplexChoices[0];

        CheckMatchingPreset();

        // Restoring this means the app picks up where it left off: still collecting.
        if (settings.CollectCapturedJobs) SetCollecting(true);
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
        Duplex = SelectedDuplex().ToString(),
        PrinterSetupOffered = _printerSetupOffered,
        Watermark = string.IsNullOrWhiteSpace(WatermarkBox.Text) ? null : WatermarkBox.Text.Trim(),
        Profiles = _profiles.ToList(),
        CollectCapturedJobs = CollectToggle.IsChecked == true,
        ShowOnCapture = _showOnCapture,
        LastCollectedUtc = _collectedThrough,
    };

    private DuplexMode SelectedDuplex() =>
        DuplexBox.SelectedItem is DuplexChoice choice ? choice.Mode : DuplexMode.Default;

    /// <summary>
    /// Closing the window while collecting only hides it — the whole point of
    /// collecting is that jobs keep arriving. "Exit" in the tray menu is how you
    /// really quit.
    /// </summary>
    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_exiting && _capture.Count > 0)
        {
            e.Cancel = true;
            Hide();
            _tray.Notify("Still collecting",
                         "OpenLeanPrint keeps collecting captured jobs. Double-click the tray icon to bring it back.");
            return;
        }
        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        CurrentSettings().Save();
        StopCollecting();
        _service.Dispose();
        _tray.Dispose();
        base.OnClosed(e);

        // ShutdownMode is OnExplicitShutdown so hiding to the tray cannot end the
        // app; that makes this the one place that ends it.
        Application.Current?.Shutdown();
    }

    /// <summary>
    /// Starts hidden with collecting already on - what the login shortcut uses,
    /// so print jobs are caught without a window appearing at every boot.
    /// </summary>
    public void StartInTray()
    {
        SetCollecting(true);
        UpdateControls();
        // Never shown, so nothing to hide: the tray icon is the whole presence.
    }

    private void RestoreFromTray()
    {
        Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
    }

    private void ExitForGood()
    {
        _exiting = true;
        Close();
    }

    /// <summary>
    /// Lights up the preset button that matches the current layout, if any, and
    /// keeps the grid box showing the same thing. A grid with no preset - 2x3,
    /// say - simply leaves them all unlit.
    /// </summary>
    private void CheckMatchingPreset()
    {
        string tag = _booklet ? "booklet" : NUpGrid.Format(_rows, _columns);
        foreach (var preset in Presets)
            preset.IsChecked = (string)preset.Tag == tag;

        ShowGrid();
    }

    private ToggleButton[] Presets => new[] { Preset1Up, Preset2Up, Preset4Up, Preset9Up, PresetBooklet };

    // ---------- collecting captured jobs ----------

    private void Collect_Click(object sender, RoutedEventArgs e) => SetCollecting(CollectToggle.IsChecked == true);

    /// <summary>Single entry point, so the toolbar toggle and the tray menu cannot drift apart.</summary>
    private void SetCollecting(bool collecting)
    {
        if (collecting) StartCollecting();
        else StopCollecting();

        bool actuallyCollecting = _capture.Count > 0;
        CollectToggle.IsChecked = actuallyCollecting;
        _tray.SetCollecting(actuallyCollecting);
    }

    private void StartCollecting()
    {
        if (_capture.Count > 0) return;

        // Two places, because jobs can arrive from two directions: the Windows
        // service writes to the machine-wide folder (it runs as LocalSystem and
        // has no per-user one), while a console host or this app writes to the
        // per-user folder.
        string[] folders = new[] { CaptureLocations.DefaultFolder, CaptureLocations.SharedFolder }
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

        int skipped = LimitInitialIntake(folders);

        foreach (string folder in folders)
        {
            try
            {
                // Jobs already lying in the folder count too. The capture service
                // runs whether or not this app does, so anything printed while it
                // was closed is waiting there - and passing those over looked
                // exactly like a printer that swallows everything. LastCollectedUtc
                // is what keeps a job from being shown twice.
                var watcher = new CapturedFolderWatcher(folder);
                watcher.JobArrived += (_, path) => Dispatcher.Invoke(() => CollectJob(path));
                watcher.Start(includeExisting: true);
                _capture.Add(watcher);
            }
            catch (Exception ex)
            {
                StatusText.Text = $"Could not watch {folder}: {ex.Message}";
            }
        }

        if (_capture.Count == 0) return;

        // Host the loopback IPP service ourselves when nothing else does, so a
        // copy without the Windows service still captures. Jobs land in a
        // watched folder either way.
        try
        {
            _service.Start();
            StatusText.Text = $"Listening for print jobs on port {_service.Port}.";
        }
        catch (Exception)
        {
            // Normally the Windows service already owns the port - then it does
            // the listening and the watchers feed the pool.
            StatusText.Text = PrinterSetup.IsCaptureServiceRunning()
                ? "Collecting jobs from the OpenLeanPrint service."
                : $"Port {CaptureService.DefaultPort} is in use by something else; watching the capture folders.";
        }

        if (skipped > 0)
            StatusText.Text += $" {skipped} older captured jobs were left in the folder.";
    }

    /// <summary>
    /// Caps what a first start pulls in. A machine that has been capturing for
    /// weeks with nobody looking can have a lot waiting, and filling the pool
    /// with months of history helps no one. Moving the mark past the older jobs
    /// leaves them on disk and out of the pool; the number is reported rather
    /// than swallowed. Returns how many were passed over.
    /// </summary>
    private int LimitInitialIntake(IEnumerable<string> folders)
    {
        const int mostToTakeAtOnce = 20;

        _backlog.Clear();
        var waiting = folders
            .Where(Directory.Exists)
            .SelectMany(folder => Directory.EnumerateFiles(folder, "*.pdf"))
            .Select(path => new FileInfo(path))
            .Where(file => file.LastWriteTimeUtc > _collectedThrough)
            .OrderByDescending(file => file.LastWriteTimeUtc)
            .ToList();

        if (waiting.Count <= mostToTakeAtOnce)
        {
            foreach (var file in waiting) _backlog.Add(file.FullName);
            return 0;
        }

        // The newest one being passed over becomes the new mark, so everything
        // above it - the newest mostToTakeAtOnce - still arrives.
        foreach (var file in waiting.Take(mostToTakeAtOnce)) _backlog.Add(file.FullName);
        _collectedThrough = waiting[mostToTakeAtOnce].LastWriteTimeUtc;
        return waiting.Count - mostToTakeAtOnce;
    }

    /// <summary>Single entry point, so the tray menu and this cannot drift apart.</summary>
    private void SetShowOnCapture(bool show)
    {
        _showOnCapture = show;
        _tray.SetShowOnCapture(show);
    }

    /// <summary>
    /// Takes one captured file into the pool - at most once, ever - and makes it
    /// visible. A job you just printed landing in a hidden window is the same
    /// experience as nothing happening at all.
    /// </summary>
    private void CollectJob(string path)
    {
        var file = new FileInfo(path);
        if (file.Exists)
        {
            if (file.LastWriteTimeUtc <= _collectedThrough) return;

            _collectedThrough = file.LastWriteTimeUtc;
            // Written now rather than on exit: a crash must not show it again.
            CurrentSettings().Save();
        }

        bool wasWaiting = _backlog.Remove(Path.GetFullPath(path));

        int before = _jobs.Count;
        LoadFiles(new[] { path });
        if (_jobs.Count == before) return; // unreadable; LoadFiles has said so

        if (_showOnCapture && !wasWaiting) RestoreFromTray();
        else if (!IsVisible) _tray.Notify("Job collected", Path.GetFileName(path));
    }

    private void StopCollecting()
    {
        foreach (var watcher in _capture) watcher.Dispose();
        _capture.Clear();
        _service.Stop();
    }

    // ---------- drag & drop ----------

    private void Window_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = DroppedPdfs(e.Data).Count > 0 ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void Window_Drop(object sender, DragEventArgs e)
    {
        var paths = DroppedPdfs(e.Data);
        if (paths.Count > 0) LoadFiles(paths);
        e.Handled = true;
    }

    private static List<string> DroppedPdfs(IDataObject data)
    {
        if (!data.GetDataPresent(DataFormats.FileDrop) || data.GetData(DataFormats.FileDrop) is not string[] files)
            return new List<string>();

        return files
            .Where(file => File.Exists(file) &&
                           Path.GetExtension(file).Equals(".pdf", StringComparison.OrdinalIgnoreCase))
            .ToList();
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

    private void JobList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // Show the selected job's own page range without treating it as an edit.
        _suppressPagesEdit = true;
        PagesBox.Text = JobList.SelectedItem is JobItem job && !job.Pages.IsAll ? job.Pages.ToString() : string.Empty;
        _suppressPagesEdit = false;

        UpdateControls();
    }

    /// <summary>Applies the "Pages" box to the selected job, e.g. 1-4,7 or 3-.</summary>
    private void Pages_Changed(object sender, TextChangedEventArgs e)
    {
        if (!IsLoaded || _suppressPagesEdit) return;
        if (JobList.SelectedItem is not JobItem job) return;

        bool valid = PageSelection.TryParse(PagesBox.Text, out var selection);
        // Red while it does not parse - but never silently print the wrong pages.
        PagesBox.Foreground = valid ? SystemColors.ControlTextBrush : System.Windows.Media.Brushes.Firebrick;
        if (!valid) return;

        job.Pages = selection;
        _sheetIndex = 0;
        _debounce.Stop();
        _debounce.Start();
    }

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

    /// <summary>
    /// The grid box is the way to any layout the presets do not cover - 2x3,
    /// 1x4, 4x4. Presets write into it, so the two never disagree.
    /// </summary>
    private void Grid_Changed(object sender, TextChangedEventArgs e)
    {
        if (!IsLoaded || _suppressGridEdit) return;

        bool valid = NUpGrid.TryParse(GridBox.Text, out int rows, out int columns);
        GridBox.Foreground = valid ? SystemColors.ControlTextBrush : System.Windows.Media.Brushes.Firebrick;
        if (!valid) return;

        _rows = rows;
        _columns = columns;
        _booklet = false;
        CheckMatchingPreset();

        _sheetIndex = 0;
        _debounce.Stop();
        _debounce.Start();
    }

    /// <summary>Writes the grid box without it counting as an edit.</summary>
    private void ShowGrid()
    {
        _suppressGridEdit = true;
        GridBox.Text = _booklet ? "2-up" : NUpGrid.Format(_rows, _columns);
        GridBox.IsEnabled = !_booklet;
        GridBox.Foreground = SystemColors.ControlTextBrush;
        _suppressGridEdit = false;
    }

    private void Paper_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded) return;
        _ = RefreshAsync();
    }

    private void Debounced_Changed(object sender, TextChangedEventArgs e)
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
        var selections = _jobs.Select(job => job.Pages).ToList();
        // Copy the turns out of the jobs: the imposition runs off the UI thread.
        var rotations = _jobs
            .Select((job, index) => (index, turns: new Dictionary<int, int>(job.Rotations)))
            .ToDictionary(entry => entry.index, entry => entry.turns);
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
        // A new imposer per run: the watermark is part of its configuration.
        var imposer = new PdfImposer
        {
            Watermark = string.IsNullOrWhiteSpace(WatermarkBox.Text)
                ? null
                : new Watermark { Text = WatermarkBox.Text.Trim() },
        };
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
            var (imposed, sheets, layout) = await Task.Run(() =>
            {
                // Composing in two steps rather than calling ImposeToPdf keeps the
                // layout, which is what lets a right-click find the page it hit.
                var sourcePages = PdfImposer.ReadPageSizes(documents, selections)
                    .Select(page => rotations.TryGetValue(page.DocumentIndex, out var turns) &&
                                    turns.TryGetValue(page.PageIndex + 1, out int degrees)
                        ? page with { Rotation = degrees }
                        : page)
                    .ToList();
                ImpositionResult result = booklet
                    ? new BookletImposer().Impose(sourcePages, paper, PtMargins.UniformMm(marginMm), gutter)
                    : new NUpImposer().Impose(sourcePages, settings);
                byte[] pdf = imposer.Compose(documents, result);
                return (pdf, PdfRasterizer.PageSizes(pdf).Count, result);
            }, token);

            if (token.IsCancellationRequested) return;

            _imposed = imposed;
            _sheetCount = sheets;
            _layout = layout;
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
            _layout = null;
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

    // ---------- removing pages from the preview ----------

    /// <summary>
    /// Right-clicking a page offers to drop it. This is the direct way to say
    /// "not that one"; the Pages box is the same thing spelled out.
    /// </summary>
    private void Preview_RightClick(object sender, MouseButtonEventArgs e)
    {
        var hit = PlacedPageAt(e.GetPosition(PreviewImage));
        if (hit is null) return;

        var job = _jobs[hit.Source.DocumentIndex];
        int pageNumber = hit.Source.PageIndex + 1;

        var menu = new ContextMenu { PlacementTarget = PreviewImage };
        var remove = new MenuItem { Header = $"Remove page {pageNumber} of {job.Name}" };
        remove.Click += (_, _) => RemovePage(job, pageNumber);
        menu.Items.Add(remove);

        var rotate = new MenuItem { Header = $"Turn page {pageNumber} by 90°" };
        rotate.Click += (_, _) => RotatePage(job, pageNumber, 90);
        menu.Items.Add(rotate);

        if (job.Rotations.ContainsKey(pageNumber))
        {
            var straighten = new MenuItem { Header = $"Put page {pageNumber} back upright" };
            straighten.Click += (_, _) => RotatePage(job, pageNumber, 0, absolute: true);
            menu.Items.Add(straighten);
        }

        if (!job.Pages.IsAll)
        {
            var restore = new MenuItem { Header = $"Restore all pages of {job.Name}" };
            restore.Click += (_, _) => SetJobPages(job, PageSelection.All);
            menu.Items.Add(restore);
        }

        menu.IsOpen = true;
        e.Handled = true;
    }

    /// <summary>Which placed page sits under a point on the preview image.</summary>
    private PlacedPage? PlacedPageAt(Point position)
    {
        if (_layout is null || _sheetIndex >= _layout.Sheets.Count) return null;
        if (PreviewImage.ActualWidth <= 0 || PreviewImage.ActualHeight <= 0) return null;

        // The image shows exactly one sheet, so this is a straight scale from
        // control pixels to sheet points.
        var sheet = _layout.Sheets[_sheetIndex];
        double x = position.X / PreviewImage.ActualWidth * sheet.Size.Width;
        double y = position.Y / PreviewImage.ActualHeight * sheet.Size.Height;

        foreach (var placed in sheet.Pages)
        {
            var rect = placed.DestRect;
            if (x >= rect.X && x <= rect.Right && y >= rect.Y && y <= rect.Bottom) return placed;
        }
        return null;
    }

    private void RemovePage(JobItem job, int pageNumber)
    {
        var kept = Enumerable.Range(1, job.PageCount)
            .Where(number => number != pageNumber && job.Pages.Includes(number))
            .ToList();

        // Something has to be left to impose, across the whole pool.
        int remaining = kept.Count + _jobs.Where(other => !ReferenceEquals(other, job))
            .Sum(other => Enumerable.Range(1, other.PageCount).Count(other.Pages.Includes));
        if (remaining == 0)
        {
            StatusText.Text = "That is the last page left - remove the job instead.";
            return;
        }

        SetJobPages(job, PageSelection.FromPages(kept));
    }

    /// <summary>Turns one page, either by a further quarter or back to upright.</summary>
    private void RotatePage(JobItem job, int pageNumber, int degrees, bool absolute = false)
    {
        int current = job.Rotations.TryGetValue(pageNumber, out int existing) ? existing : 0;
        int turned = absolute ? degrees : (current + degrees) % 360;

        if (turned == 0) job.Rotations.Remove(pageNumber);
        else job.Rotations[pageNumber] = turned;

        job.NotifySummaryChanged();
        _ = RefreshAsync();
    }

    private void SetJobPages(JobItem job, PageSelection pages)
    {
        job.Pages = pages;

        if (ReferenceEquals(JobList.SelectedItem, job))
        {
            _suppressPagesEdit = true;
            PagesBox.Text = pages.IsAll ? string.Empty : pages.ToString();
            _suppressPagesEdit = false;
        }

        _sheetIndex = 0;
        _ = RefreshAsync();
    }

    // ---------- layout profiles ----------

    /// <summary>Applying a saved layout: one selection instead of five settings.</summary>
    private void Profile_Selected(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || ProfileBox.SelectedItem is not LayoutProfile profile) return;

        _booklet = profile.Booklet;
        _rows = Math.Max(1, profile.Rows);
        _columns = Math.Max(1, profile.Columns);
        if (Papers.Contains(profile.Paper)) PaperBox.SelectedItem = profile.Paper;
        MarginBox.Text = profile.MarginMm.ToString(CultureInfo.CurrentCulture);
        GutterBox.Text = profile.Gutter.ToString(CultureInfo.CurrentCulture);
        WatermarkBox.Text = profile.Watermark ?? string.Empty;
        if (DuplexModes.TryParse(profile.Duplex, out var duplex))
            DuplexBox.SelectedItem = DuplexChoices.FirstOrDefault(choice => choice.Mode == duplex) ?? DuplexChoices[0];

        CheckMatchingPreset();
        _sheetIndex = 0;
        _debounce.Stop();
        _ = RefreshAsync();
    }

    private void SaveProfile_Click(object sender, RoutedEventArgs e)
    {
        string name = ProfileBox.Text.Trim();
        if (name.Length == 0)
        {
            StatusText.Text = "Type a name for the profile first.";
            return;
        }

        var profile = new LayoutProfile
        {
            Name = name,
            Rows = _rows,
            Columns = _columns,
            Booklet = _booklet,
            Paper = (string)PaperBox.SelectedItem,
            MarginMm = ParseNumber(MarginBox.Text, 0),
            Gutter = ParseNumber(GutterBox.Text, 0),
            Watermark = string.IsNullOrWhiteSpace(WatermarkBox.Text) ? null : WatermarkBox.Text.Trim(),
            Duplex = SelectedDuplex().ToString(),
        };

        // Saving under an existing name replaces it, which is what "Save" means
        // everywhere else.
        int existing = _profiles.ToList().FindIndex(p =>
            string.Equals(p.Name, name, StringComparison.CurrentCultureIgnoreCase));
        if (existing >= 0) _profiles[existing] = profile;
        else _profiles.Add(profile);

        ProfileBox.SelectedItem = profile;
        StatusText.Text = $"Saved the profile “{name}”.";
    }

    private void DeleteProfile_Click(object sender, RoutedEventArgs e)
    {
        if (ProfileBox.SelectedItem is not LayoutProfile profile) return;

        _profiles.Remove(profile);
        ProfileBox.Text = string.Empty;
        StatusText.Text = $"Deleted the profile “{profile.Name}”.";
    }

    // ---------- the virtual printer ----------

    /// <summary>
    /// Creates or removes the Windows printer queue. This needs administrator
    /// rights once, so it goes through a UAC prompt rather than the app running
    /// elevated.
    /// </summary>
    private void PrinterSetup_Click(object sender, RoutedEventArgs e)
    {
        PrinterSetupButton.IsEnabled = false;
        try
        {
            if (PrinterSetup.IsRegistered())
            {
                StatusText.Text = PrinterSetup.Unregister()
                    ? "Removed the OpenLeanPrint printer."
                    : "The printer was not removed.";
            }
            else
            {
                CreatePrinter();
            }
        }
        finally
        {
            PrinterSetupButton.IsEnabled = true;
            UpdatePrinterSetupButton();
        }
    }

    /// <summary>
    /// Creates the printer queue. Windows only accepts an IPP printer while the
    /// service behind it is answering, so the service has to be up *first* -
    /// getting that order wrong is the whole reason this is one method.
    /// </summary>
    private void CreatePrinter()
    {
        if (!_service.IsRunning)
        {
            SetCollecting(true);
            if (!_service.IsRunning)
            {
                StatusText.Text = "Could not start the print service, so the printer cannot be created yet.";
                return;
            }
        }

        StatusText.Text = "Creating the printer…";
        StatusText.Dispatcher.Invoke(() => { }, DispatcherPriority.Render);

        StatusText.Text = PrinterSetup.Register(_service.Port)
            ? "The OpenLeanPrint printer is ready - print to it from any application."
            : "The printer was not created. Administrator rights are needed for that one step.";
    }

    /// <summary>
    /// Offers to finish the setup the installer cannot do: creating the printer
    /// needs the user's own session, so it is asked for once, here.
    /// </summary>
    private void OfferPrinterSetup(AppSettings settings)
    {
        if (settings.PrinterSetupOffered || PrinterSetup.IsRegistered()) return;

        _printerSetupOffered = true;
        var answer = MessageBox.Show(
            this,
            "OpenLeanPrint can add its virtual printer now, so you can print into it from any " +
            "application.\n\nWindows will ask for administrator rights once - creating a printer " +
            "queue requires them.",
            "Set up the virtual printer",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (answer == MessageBoxResult.Yes) CreatePrinter();
        UpdatePrinterSetupButton();
    }

    private void UpdatePrinterSetupButton() =>
        PrinterSetupButton.Content = PrinterSetup.IsRegistered()
            ? "Remove virtual printer"
            : "Set up virtual printer…";

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
            var duplex = SelectedDuplex();
            var report = await Task.Run(() => PdfPrinter.Print(pdf, printer, new PrintOptions
            {
                Dpi = PrintDpi,
                Duplex = duplex,
                JobName = _jobs.Count == 1 ? $"OpenLeanPrint - {_jobs[0].Name}" : "OpenLeanPrint",
            }));
            StatusText.Text = $"Sent {report.Sheets} sheet(s) to \"{report.PrinterName}\" " +
                              $"({string.Join("/", report.PaperNames)})." +
                              (report.DuplexUnsupported ? " This printer cannot print two-sided." : string.Empty);
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
        PreviewTip.Visibility = hasSheets ? Visibility.Visible : Visibility.Hidden;

        if (!hasJobs)
        {
            StatusText.Text = "No jobs yet.";
            return;
        }

        int pages = _jobs.Sum(job => job.Pages.IsAll
            ? job.PageCount
            : Enumerable.Range(1, job.PageCount).Count(job.Pages.Includes));
        string jobWord = _jobs.Count == 1 ? "job" : "jobs";
        string pageWord = pages == 1 ? "page" : "pages";
        string sheetWord = _sheetCount == 1 ? "sheet" : "sheets";
        StatusText.Text = hasSheets
            ? $"{_jobs.Count} {jobWord} · {pages} {pageWord} → {_sheetCount} {sheetWord} · {LayoutDescription()}"
            : $"{_jobs.Count} {jobWord} · {pages} {pageWord}";
    }
}
