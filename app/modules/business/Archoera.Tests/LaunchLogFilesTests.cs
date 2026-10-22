using Archoera.Infrastructure.Launch;

namespace Archoera.Tests;

/// <summary>
/// Bounded launch-log retention: common launchers keep a handful of logs and
/// drop the oldest instead of writing one unbounded file per session.
/// </summary>
public sealed class LaunchLogFilesTests
{
    [Fact]
    public void Prune_keeps_only_the_newest_logs()
    {
        var directory = TempDirectory();
        try
        {
            var start = DateTime.UtcNow.AddHours(-20);
            var names = new List<string>();
            for (var index = 0; index < 15; index++)
            {
                var name = $"launch-20260101-1200{index:D2}.log";
                var path = Path.Combine(directory, name);
                File.WriteAllText(path, "line\n");
                File.SetLastWriteTimeUtc(path, start.AddMinutes(index));
                names.Add(name);
            }

            File.WriteAllText(Path.Combine(directory, "notes.txt"), "keep me");

            LaunchLogFiles.Prune(directory, maxFiles: 5, maxTotalBytes: long.MaxValue);

            var remaining = Directory
                .GetFiles(directory, "launch-*.log")
                .Select(Path.GetFileName)
                .ToHashSet(StringComparer.Ordinal);
            Assert.Equal(5, remaining.Count);
            foreach (var name in names.Take(10))
            {
                Assert.DoesNotContain(name, remaining);
            }

            foreach (var name in names.Skip(10))
            {
                Assert.Contains(name, remaining);
            }

            Assert.True(File.Exists(Path.Combine(directory, "notes.txt")));
        }
        finally
        {
            Delete(directory);
        }
    }

    [Fact]
    public void Prune_enforces_the_size_budget_but_keeps_the_newest()
    {
        var directory = TempDirectory();
        try
        {
            var start = DateTime.UtcNow.AddHours(-3);
            for (var index = 0; index < 3; index++)
            {
                var path = Path.Combine(directory, $"launch-20260101-12000{index}.log");
                File.WriteAllText(path, new string('x', 100));
                File.SetLastWriteTimeUtc(path, start.AddMinutes(index));
            }

            LaunchLogFiles.Prune(directory, maxFiles: 10, maxTotalBytes: 150);

            var remaining = Directory.GetFiles(directory, "launch-*.log");
            var single = Assert.Single(remaining);
            Assert.Equal("launch-20260101-120002.log", Path.GetFileName(single));

            // A single oversized newest log is still kept for inspection.
            var newest = Path.Combine(directory, "launch-20260101-130000.log");
            File.WriteAllText(newest, new string('y', 1000));
            File.SetLastWriteTimeUtc(newest, start.AddHours(2));
            LaunchLogFiles.Prune(directory, maxFiles: 10, maxTotalBytes: 150);

            Assert.Equal(newest, Assert.Single(Directory.GetFiles(directory, "launch-*.log")));
        }
        finally
        {
            Delete(directory);
        }
    }

    [Fact]
    public void Writer_stops_at_the_limit_and_marks_the_file()
    {
        var directory = TempDirectory();
        try
        {
            var path = Path.Combine(directory, "launch-20260101-120000.log");
            using var writer = new LaunchLogWriter(path, maxBytes: 1024);
            for (var index = 0; index < 100; index++)
            {
                writer.WriteLine(new string('x', 100));
            }

            writer.Complete(1);
            writer.WriteLine("after close");

            var text = File.ReadAllText(path);
            Assert.Contains("[archoera] log truncated after 1 KiB", text);
            Assert.EndsWith("[exited with code 1]" + Environment.NewLine, text);
            Assert.DoesNotContain("after close", text);
            Assert.True(new FileInfo(path).Length < 2048);
        }
        finally
        {
            Delete(directory);
        }
    }

    private static string TempDirectory()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            "archoera-launch-files-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void Delete(string path)
    {
        try
        {
            if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
        }
        catch
        {
            // best effort
        }
    }
}
