using System.IO;
using System.Text.Json;
using Path = System.IO.Path;

namespace OpenLeanPrint.App;

/// <summary>
/// What the app remembers between runs: the layout you last used, the printer
/// you picked, and whether it was collecting captured jobs.
/// </summary>
public sealed record AppSettings
{
    public int Rows { get; init; } = 2;
    public int Columns { get; init; } = 2;
    public bool Booklet { get; init; }
    public string Paper { get; init; } = "A4";
    public double MarginMm { get; init; } = 8;
    public double Gutter { get; init; } = 6;
    public string? Printer { get; init; }
    public string Duplex { get; init; } = "Default";
    public bool CollectCapturedJobs { get; init; }

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
