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

    /// <summary>
    /// Machine-wide folder for captured jobs
    /// (<c>%ProgramData%\OpenLeanPrint\captured</c>), used by the Windows
    /// service.
    /// <para>
    /// A service runs as LocalSystem, where <see cref="DefaultFolder"/> would
    /// resolve inside <c>config\systemprofile</c> - a folder no user ever sees.
    /// Jobs therefore land somewhere both the service and the app can reach.
    /// </para>
    /// <para>
    /// The consequence is worth knowing: on a machine with several users, one
    /// user's captured documents are readable by the others. Running without the
    /// service keeps everything per-user instead.
    /// </para>
    /// </summary>
    public static string SharedFolder { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData,
                                  Environment.SpecialFolderOption.Create),
        "OpenLeanPrint", "captured");
}
