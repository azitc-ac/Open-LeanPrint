namespace OpenLeanPrint.Capture;

/// <summary>Where captured print jobs live by default.</summary>
public static class CaptureLocations
{
    /// <summary>
    /// Per-user data folder for captured jobs
    /// (<c>%LOCALAPPDATA%\OpenLeanPrint\captured</c> on Windows).
    /// <para>
    /// Deliberately *not* a folder next to the executable or inside the repo:
    /// captured jobs are the user's real documents, and a working directory can
    /// easily be a source tree or a cloud-synced folder. Override it with
    /// <c>--out</c> when a different location is wanted.
    /// </para>
    /// </summary>
    public static string DefaultFolder { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData,
                                  Environment.SpecialFolderOption.Create),
        "OpenLeanPrint", "captured");
}
