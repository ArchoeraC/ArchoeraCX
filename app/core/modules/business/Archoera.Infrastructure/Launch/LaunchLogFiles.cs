namespace Archoera.Infrastructure.Launch;

/// <summary>
/// Retention policy for captured game logs.
///
/// Common launchers keep a small, fixed number of launch logs (Prism rotates
/// five launcher logs, PCL2 keeps Log1..Log5) instead of one file per session
/// forever. We do the same per instance: keep the newest
/// <see cref="MaxFiles"/> logs, and prune the oldest until their total size
/// fits <see cref="MaxTotalBytes"/>. The newest log is always kept so a single
/// huge session can still be inspected after the next launch.
/// </summary>
internal static class LaunchLogFiles
{
    /// <summary>Newest launch logs kept per instance.</summary>
    internal const int MaxFiles = 10;

    /// <summary>Total bytes the kept logs may occupy per instance.</summary>
    internal const long MaxTotalBytes = 32L * 1024 * 1024;

    /// <summary>Upper bound for one launch log; output beyond it is dropped.</summary>
    internal const long MaxFileBytes = 64L * 1024 * 1024;

    private const string Pattern = "launch-*.log";

    /// <summary>Creates the log directory and applies retention in one step.</summary>
    public static void Prepare(
        string directory,
        int maxFiles = MaxFiles,
        long maxTotalBytes = MaxTotalBytes)
    {
        Directory.CreateDirectory(directory);
        Prune(directory, maxFiles, maxTotalBytes);
    }

    /// <summary>
    /// Deletes the oldest logs until at most <paramref name="maxFiles"/> remain
    /// and their combined size is within <paramref name="maxTotalBytes"/>.
    /// The newest file is never deleted. Other files in the directory are left
    /// alone, and cleanup failures never abort a launch.
    /// </summary>
    internal static void Prune(string directory, int maxFiles, long maxTotalBytes)
    {
        List<FileInfo> files;
        try
        {
            files = new DirectoryInfo(directory)
                .GetFiles(Pattern, SearchOption.TopDirectoryOnly)
                .OrderByDescending(file => file.LastWriteTimeUtc)
                .ThenByDescending(file => file.Name, StringComparer.Ordinal)
                .ToList();
        }
        catch (Exception exception) when (
            exception is DirectoryNotFoundException or IOException or UnauthorizedAccessException)
        {
            return;
        }

        long total = 0;
        for (var index = 0; index < files.Count; index++)
        {
            var file = files[index];
            long size;
            try
            {
                size = file.Length;
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            // The newest log is kept even when it alone exceeds the budget.
            var keep = index == 0 ||
                       (index < maxFiles && total + size <= maxTotalBytes);
            if (keep)
            {
                total += size;
                continue;
            }

            try
            {
                file.Delete();
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException)
            {
                // Best effort: the next launch tries again.
            }
        }
    }
}
