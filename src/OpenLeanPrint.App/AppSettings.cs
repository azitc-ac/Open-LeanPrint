using System.IO;
using System.Text.Json;
using Path = System.IO.Path;

namespace OpenLeanPrint.App;

/// <summary>A named layout you can come back to — "4-up draft", "booklet".</summary>
public sealed record LayoutProfile
{
    public string Name { get; init; } = string.Empty;
    public int Rows { get; init; } = 2;
    public int Columns { get; init; } = 2;
    public bool Booklet { get; init; }
    public string Paper { get; init; } = "A4";
    public double MarginMm { get; init; } = 8;

    /// <summary>Gutter in points, like the rest of the geometry. The app shows millimetres.</summary>
    public double Gutter { get; init; } = 6;
    public bool PageBorders { get; init; }
    public string? Watermark { get; init; }
    public string Duplex { get; init; } = "Default";
}

/// <summary>
/// What the app remembers between runs: the layout you last used, the printer
/// you picked, whether it was collecting captured jobs, and your saved layouts.
/// </summary>
public sealed record AppSettings
{
    public int Rows { get; init; } = 2;
    public int Columns { get; init; } = 2;
    public bool Booklet { get; init; }
    public string Paper { get; init; } = "A4";
    public double MarginMm { get; init; } = 8;

    /// <summary>Gutter in points, like the rest of the geometry. The app shows millimetres.</summary>
    public double Gutter { get; init; } = 6;
    public bool PageBorders { get; init; }
    public string? Printer { get; init; }
    public string Duplex { get; init; } = "Default";
    public string? Watermark { get; init; }
    /// <summary>
    /// On by default. The app exists to receive what you print into the virtual
    /// printer; a pool that stays empty after printing is indistinguishable from
    /// a broken printer.
    /// </summary>
    public bool CollectCapturedJobs { get; init; } = true;

    /// <summary>Bring the window up when a job arrives, the way a print dialog would.</summary>
    public bool ShowOnCapture { get; init; } = true;

    /// <summary>
    /// Write time of the newest captured job that has already been in the pool.
    /// The capture service runs with no app open, so jobs wait in the folder;
    /// this is what separates "waiting to be shown" from "shown once already".
    /// </summary>
    public DateTime LastCollectedUtc { get; init; }

    /// <summary>Whether the app has already offered to create the printer.</summary>
    public bool PrinterSetupOffered { get; init; }
    public List<LayoutProfile> Profiles { get; init; } = new();

    /// <summary><c>%APPDATA%\OpenLeanPrint\settings.json</c>.</summary>
    public static string FilePath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData,
                                  Environment.SpecialFolderOption.Create),
        "OpenLeanPrint", "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new AppSettings();
        }
        catch (Exception)
        {
            // Unreadable or corrupt settings are not worth a startup failure -
            // fall back to defaults and overwrite them on the next save.
        }
        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            string? directory = Path.GetDirectoryName(FilePath);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, JsonOptions));
        }
        catch (Exception)
        {
            // Losing preferences is annoying; crashing on exit is worse.
        }
    }

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
}
