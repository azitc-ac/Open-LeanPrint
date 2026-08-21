namespace OpenLeanPrint.Capture.Host;

/// <summary>Writes a captured job to disk, shared by the console host and the service.</summary>
internal static class CapturedJobWriter
{
    /// <summary>
    /// Saves the job and returns the path. Job ids restart when the host does,
    /// so an existing file is never overwritten - that would silently destroy a
    /// document somebody printed.
    /// </summary>
    public static string Save(CapturedJob job, string folder)
    {
        Directory.CreateDirectory(folder);
        string extension = job.IsPdf ? "pdf" : "bin";
        string path = Path.Combine(folder, $"job-{job.JobId:D4}.{extension}");

        int suffix = 1;
        while (File.Exists(path))
            path = Path.Combine(folder, $"job-{job.JobId:D4}-{suffix++}.{extension}");

        File.WriteAllBytes(path, job.Data);
        return path;
    }
}
