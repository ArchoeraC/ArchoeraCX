using Archoera.Infrastructure.Downloads;

namespace Archoera.Tests;

public sealed class DownloadPlanRunnerTests
{
    [Fact]
    public void Release_candidates_never_probe_the_working_directory()
    {
        var cwd = Path.Combine(Path.GetTempPath(), "archoera-cwd-probe");
        var baseDirectory = Path.Combine(Path.GetTempPath(), "archoera-app");

        var candidates = DownloadPlanRunner.BuildDownloaderDirectories(
            baseDirectory,
            cwd,
            includeDevelopmentProbes: false);

        Assert.Equal(baseDirectory, candidates[0]);
        Assert.Equal(Path.Combine(baseDirectory, "lib"), candidates[1]);
        Assert.Contains(Path.Combine(baseDirectory, "Frameworks"), candidates);
        Assert.Contains(
            Path.GetFullPath(Path.Combine(baseDirectory, "..", "Frameworks")),
            candidates);
        Assert.DoesNotContain(
            candidates,
            candidate => candidate.StartsWith(cwd, StringComparison.Ordinal));
    }

    [Fact]
    public void Development_candidates_add_repository_locations_after_the_app_directory()
    {
        var cwd = Path.Combine(Path.GetTempPath(), "archoera-cwd-probe");
        var baseDirectory = Path.Combine(Path.GetTempPath(), "archoera-app");

        var candidates = DownloadPlanRunner.BuildDownloaderDirectories(
            baseDirectory,
            cwd,
            includeDevelopmentProbes: true);

        Assert.Equal(baseDirectory, candidates[0]);
        Assert.Contains(Path.Combine(cwd, "build", "native"), candidates);
        Assert.Contains(
            Path.Combine(cwd, "app", "modules", "downloader", "target", "release"),
            candidates);
    }

    [Fact]
    public async Task Plan_files_are_owner_only_and_cleaned_after_the_retention_window()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "archoera-plan-files",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var now = DateTimeOffset.UtcNow;
            var planPath = Path.Combine(directory, "plan-1-pack.json");
            await DownloadPlanRunner.WritePlanFileAsync(
                planPath,
                "{}"u8.ToArray(),
                CancellationToken.None);
            Assert.True(File.Exists(planPath));

            if (!OperatingSystem.IsWindows())
            {
                var mode = File.GetUnixFileMode(planPath);
                Assert.Equal(
                    UnixFileMode.UserRead | UnixFileMode.UserWrite,
                    mode);
            }

            var expired = Path.Combine(directory, "plan-old-pack.json");
            await File.WriteAllTextAsync(expired, "{}");
            File.SetLastWriteTimeUtc(expired, now.UtcDateTime.AddDays(-8));
            var fresh = Path.Combine(directory, "plan-fresh-pack.json");
            await File.WriteAllTextAsync(fresh, "{}");
            File.SetLastWriteTimeUtc(fresh, now.UtcDateTime.AddHours(-1));
            var unrelated = Path.Combine(directory, "settings.json");
            await File.WriteAllTextAsync(unrelated, "{}");
            File.SetLastWriteTimeUtc(unrelated, now.UtcDateTime.AddDays(-30));

            var removed = DownloadPlanRunner.CleanupExpiredPlans(
                directory,
                now,
                TimeSpan.FromDays(7));

            Assert.Equal(1, removed);
            Assert.False(File.Exists(expired));
            Assert.True(File.Exists(fresh));
            Assert.True(File.Exists(unrelated));
        }
        finally
        {
            try
            {
                Directory.Delete(directory, recursive: true);
            }
            catch
            {
                // best effort on platforms with transient file handles
            }
        }
    }
}
