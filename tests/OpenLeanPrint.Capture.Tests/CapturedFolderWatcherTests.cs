using OpenLeanPrint.Capture;
using Xunit;

namespace OpenLeanPrint.Capture.Tests;

public class CapturedFolderWatcherTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "olp-watch-" + Guid.NewGuid().ToString("N"));

    public CapturedFolderWatcherTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        try { Directory.Delete(_folder, recursive: true); } catch (IOException) { }
    }

    /// <summary>Waits for <paramref name="count"/> events, or gives up.</summary>
    private static List<string> Collect(CapturedFolderWatcher watcher, int count, Action act,
                                        int timeoutMs = 15000)
    {
        var arrived = new List<string>();
        using var done = new ManualResetEventSlim(false);
        watcher.JobArrived += (_, path) =>
        {
            lock (arrived)
            {
                arrived.Add(path);
                if (arrived.Count >= count) done.Set();
            }
        };

        act();
        done.Wait(timeoutMs);
        lock (arrived) return new List<string>(arrived);
    }

    [Fact]
    public void RaisesJobArrived_ForANewFile()
    {
        using var watcher = new CapturedFolderWatcher(_folder);
        watcher.Start();

        var arrived = Collect(watcher, 1, () =>
            File.WriteAllBytes(Path.Combine(_folder, "job-0001.pdf"), new byte[] { 1, 2, 3 }));

        Assert.Single(arrived);
        Assert.Equal("job-0001.pdf", Path.GetFileName(arrived[0]));
    }

    [Fact]
    public void IgnoresFilesThatDoNotMatchTheFilter()
    {
        using var watcher = new CapturedFolderWatcher(_folder);
        watcher.Start();

        var arrived = Collect(watcher, 1, () =>
        {
            File.WriteAllText(Path.Combine(_folder, "notes.txt"), "not a job");
            File.WriteAllBytes(Path.Combine(_folder, "job-0002.pdf"), new byte[] { 1 });
        });

        Assert.Single(arrived);
        Assert.Equal("job-0002.pdf", Path.GetFileName(arrived[0]));
    }

    [Fact]
    public void IncludeExisting_PicksUpWhatIsAlreadyThere()
    {
        File.WriteAllBytes(Path.Combine(_folder, "old.pdf"), new byte[] { 1, 2 });

        using var watcher = new CapturedFolderWatcher(_folder);
        var arrived = Collect(watcher, 1, () => watcher.Start(includeExisting: true));

        Assert.Single(arrived);
        Assert.Equal("old.pdf", Path.GetFileName(arrived[0]));
    }

    [Fact]
    public void WithoutIncludeExisting_OnlyNewFilesArrive()
    {
        File.WriteAllBytes(Path.Combine(_folder, "old.pdf"), new byte[] { 1, 2 });

        using var watcher = new CapturedFolderWatcher(_folder);
        watcher.Start();

        var arrived = Collect(watcher, 1, () =>
            File.WriteAllBytes(Path.Combine(_folder, "new.pdf"), new byte[] { 3, 4 }));

        Assert.Single(arrived);
        Assert.Equal("new.pdf", Path.GetFileName(arrived[0]));
    }

    [Fact]
    public void Accept_CanVetoAFile()
    {
        using var watcher = new CapturedFolderWatcher(_folder)
        {
            Accept = path => !Path.GetFileName(path).StartsWith("skip", StringComparison.OrdinalIgnoreCase),
        };
        watcher.Start();

        var arrived = Collect(watcher, 1, () =>
        {
            File.WriteAllBytes(Path.Combine(_folder, "skip-me.pdf"), new byte[] { 1 });
            File.WriteAllBytes(Path.Combine(_folder, "take-me.pdf"), new byte[] { 1 });
        });

        Assert.Single(arrived);
        Assert.Equal("take-me.pdf", Path.GetFileName(arrived[0]));
    }

    [Fact]
    public void WaitsUntilAFileIsFinishedBeforeAnnouncingIt()
    {
        using var watcher = new CapturedFolderWatcher(_folder);
        watcher.Start();

        string path = Path.Combine(_folder, "slow.pdf");
        var announced = new ManualResetEventSlim(false);
        watcher.JobArrived += (_, _) => announced.Set();

        // Hold the file open and keep growing it: the watcher must stay quiet.
        using (var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            stream.Write(new byte[1024]);
            stream.Flush();
            Assert.False(announced.Wait(2000), "announced a file that was still being written");

            stream.Write(new byte[1024]);
            stream.Flush();
            Assert.False(announced.Wait(1000), "announced a file that was still growing");
        }

        // Closed now - it must show up.
        Assert.True(announced.Wait(10000), "never announced the finished file");
    }
}
