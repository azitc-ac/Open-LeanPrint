using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;

namespace OpenLeanPrint.Capture;

/// <summary>
/// Housekeeping for the folder captured jobs land in.
/// <para>
/// The folder is a hand-over, not an archive: the service writes a job, the app
/// picks it up, and after that the file is only worth keeping in case the app
/// never got to it. Nothing used to remove them, so it grew with every page
/// anyone ever printed - in a folder nobody looks at.
/// </para>
/// </summary>
public static class CapturedFolder
{
    /// <summary>Jobs older than this are removed. A week survives a holiday weekend.</summary>
    public static readonly TimeSpan DefaultMaxAge = TimeSpan.FromDays(7);

    /// <summary>Total size the folder is allowed to reach before the oldest go.</summary>
    public const long DefaultMaxBytes = 500L * 1024 * 1024;

    /// <summary>Only files the capture host itself writes are ever touched.</summary>
    private static readonly string[] Patterns = { "job-*.pdf", "job-*.bin" };

    /// <summary>What a pass removed.</summary>
    public sealed record PruneResult(int Files, long Bytes)
    {
        public bool RemovedAnything => Files > 0;
    }

    /// <summary>
    /// Removes captured jobs that are older than <paramref name="maxAge"/>, then
    /// the oldest remaining ones until the folder fits in
    /// <paramref name="maxBytes"/>. A file that cannot be deleted is left alone:
    /// housekeeping must never be the reason capturing stops.
    /// </summary>
    public static PruneResult Prune(string folder, TimeSpan maxAge, long maxBytes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folder);
        if (!Directory.Exists(folder)) return new PruneResult(0, 0);

        var jobs = Patterns
            .SelectMany(pattern => Directory.EnumerateFiles(folder, pattern))
            .Select(path => new FileInfo(path))
            .Where(file => file.Exists)
            .OrderBy(file => file.LastWriteTimeUtc)
            .ToList();

        int files = 0;
        long bytes = 0;
        long total = jobs.Sum(file => file.Length);
        DateTime cutoff = DateTime.UtcNow - maxAge;

        foreach (var job in jobs)
        {
            bool tooOld = maxAge > TimeSpan.Zero && job.LastWriteTimeUtc < cutoff;
            bool tooMany = maxBytes > 0 && total > maxBytes;
            if (!tooOld && !tooMany) continue;

            long size = job.Length;
            try
            {
                job.Delete();
            }
            catch (Exception)
            {
                // Locked, or not ours to delete - the next pass can try again.
                continue;
            }

            files++;
            bytes += size;
            total -= size;
        }

        return new PruneResult(files, bytes);
    }

    /// <summary>Prunes with the defaults.</summary>
    public static PruneResult Prune(string folder) => Prune(folder, DefaultMaxAge, DefaultMaxBytes);

    /// <summary>
    /// Is this file a captured job - one of ours, in one of the capture folders?
    /// <para>
    /// The question that matters before deleting anything. The pool also holds
    /// PDFs the user dragged in from their own documents, and those must survive
    /// being printed no matter what: deleting a captured hand-over is
    /// housekeeping, deleting someone's file is data loss.
    /// </para>
    /// </summary>
    public static bool Holds(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;

        string? folder;
        try
        {
            folder = Path.GetDirectoryName(Path.GetFullPath(path));
        }
        catch (Exception)
        {
            return false; // not a path we can reason about, so not ours
        }

        if (folder is null) return false;

        return Same(folder, CaptureLocations.DefaultFolder) || Same(folder, CaptureLocations.SharedFolder);

        static bool Same(string a, string b) =>
            string.Equals(a.TrimEnd(Path.DirectorySeparatorChar),
                          Path.GetFullPath(b).TrimEnd(Path.DirectorySeparatorChar),
                          StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Lets ordinary users manage what is in the machine-wide capture folder.
    /// <para>
    /// Without this, whether you can delete your own captured jobs depends on
    /// who happened to create the folder first: <c>C:\ProgramData</c> grants
    /// Users read and create but not delete, so files written by the service
    /// (LocalSystem) cannot be removed by the person who printed them. Measured,
    /// not assumed - on one machine the folder had been created by the app and
    /// everything was deletable, which is exactly the kind of accident that
    /// hides a problem.
    /// </para>
    /// <para>
    /// The trade-off is the one already noted for this folder: it is shared, so
    /// on a machine with several users they can see - and now also remove - each
    /// other's captured jobs. Running without the service keeps everything
    /// per-user instead.
    /// </para>
    /// </summary>
    /// <returns>True if the rule was applied.</returns>
    [SupportedOSPlatform("windows")]
    public static bool AllowUsersToManage(string folder)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folder);

        try
        {
            var directory = new DirectoryInfo(folder);
            directory.Create();

            var security = directory.GetAccessControl();
            security.AddAccessRule(new FileSystemAccessRule(
                new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, domainSid: null),
                FileSystemRights.Modify | FileSystemRights.Synchronize,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                PropagationFlags.None,
                AccessControlType.Allow));
            directory.SetAccessControl(security);
            return true;
        }
        catch (Exception)
        {
            // Only an administrator or the owner may change a DACL. Failing here
            // costs deletability, not capturing.
            return false;
        }
    }
}
