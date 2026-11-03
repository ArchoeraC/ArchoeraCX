using System.Text.RegularExpressions;
using Archoera.Application.JavaRuntime;
using Archoera.Domain;

namespace Archoera.Infrastructure.Java.Distributions;

/// <summary>
/// Filesystem side of a JDK install: target validation, executable discovery
/// and atomic placement. Kept apart from the download/cache orchestration.
/// </summary>
internal static partial class JavaInstallTarget
{
    public static string Resolve(
        string? directory,
        string providerId,
        string version,
        string dataRoot,
        string defaultRoot)
    {
        if (string.IsNullOrWhiteSpace(directory))
        {
            return Path.Combine(defaultRoot, $"{providerId}-{VersionSlug(version)}");
        }

        var trimmed = directory.Trim();
        if (!Path.IsPathRooted(trimmed))
        {
            throw new JavaDistributionException(
                "JAVA_DIR_INVALID",
                "install directory must be an absolute path");
        }

        var full = Path.GetFullPath(trimmed);
        var root = Path.GetPathRoot(full);
        if (string.IsNullOrEmpty(root) ||
            string.Equals(full, root, PathSemantics.Comparison))
        {
            throw new JavaDistributionException(
                "JAVA_DIR_INVALID",
                "the filesystem root cannot be used as a JDK directory");
        }

        if (IsSameOrAncestor(full, dataRoot))
        {
            throw new JavaDistributionException(
                "JAVA_DIR_INVALID",
                "install directory must not contain the launcher data root");
        }

        return full;
    }

    public static void EnsureAvailable(string target)
    {
        if (File.Exists(target))
        {
            throw new JavaDistributionException(
                "JAVA_DIR_NOT_EMPTY",
                $"install target is a file: {target}");
        }

        if (!Directory.Exists(target)) return;
        if (IsLink(target))
        {
            throw new JavaDistributionException(
                "JAVA_DIR_INVALID",
                "install target must not be a symlink");
        }

        if (Directory.EnumerateFileSystemEntries(target).Any())
        {
            throw new JavaDistributionException(
                "JAVA_DIR_NOT_EMPTY",
                $"install directory is not empty: {target}");
        }
    }

    public static string? FindJavaHome(
        string extracted,
        JavaDistributionPlatform platform)
    {
        // The archive's target platform decides the executable name. A
        // Windows zip extracted on Linux still contains bin/java.exe.
        var executable = platform.IsWindows ? "java.exe" : "java";
        var queue = new Queue<(string Directory, int Depth)>();
        queue.Enqueue((extracted, 0));

        while (queue.Count > 0)
        {
            var (directory, depth) = queue.Dequeue();
            if (File.Exists(Path.Combine(directory, "bin", executable)))
            {
                return directory;
            }

            var bundleHome = Path.Combine(directory, "Contents", "Home");
            if (!IsLink(bundleHome) &&
                File.Exists(Path.Combine(bundleHome, "bin", executable)))
            {
                return bundleHome;
            }

            if (depth >= 4) continue;
            IEnumerable<string> children;
            try
            {
                children = Directory.EnumerateDirectories(directory);
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            foreach (var child in children)
            {
                if (!IsLink(child)) queue.Enqueue((child, depth + 1));
            }
        }

        return null;
    }

    public static void NormalizeExecutables(string home)
    {
        if (OperatingSystem.IsWindows()) return;
        var bin = Path.Combine(home, "bin");
        if (!Directory.Exists(bin)) return;

        foreach (var file in Directory.EnumerateFiles(bin))
        {
            try
            {
                var mode = File.GetUnixFileMode(file);
                const UnixFileMode execute =
                    UnixFileMode.UserExecute |
                    UnixFileMode.GroupExecute |
                    UnixFileMode.OtherExecute;
                if ((mode & execute) != execute)
                {
                    File.SetUnixFileMode(file, mode | execute);
                }
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException or
                ArgumentException or NotSupportedException)
            {
                // The launch preflight will report an unusable executable.
            }
        }
    }

    public static void MoveIntoPlace(string home, string target)
    {
        if (Directory.Exists(target))
        {
            try
            {
                Directory.Delete(target, recursive: false);
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException)
            {
                throw new JavaDistributionException(
                    "JAVA_INSTALL_FAILED",
                    $"cannot replace install directory: {exception.Message}");
            }
        }

        try
        {
            Directory.Move(home, target);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            throw new JavaDistributionException(
                "JAVA_INSTALL_FAILED",
                $"cannot move the JDK into place: {exception.Message}");
        }
    }

    public static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            // Best-effort cleanup; a later install overwrites the staging name.
        }
    }

    private static bool IsSameOrAncestor(string possibleAncestor, string path)
    {
        var ancestor = possibleAncestor.EndsWith(Path.DirectorySeparatorChar)
            ? possibleAncestor
            : possibleAncestor + Path.DirectorySeparatorChar;
        return string.Equals(possibleAncestor, path, PathSemantics.Comparison) ||
               path.StartsWith(ancestor, PathSemantics.Comparison);
    }

    private static bool IsLink(string path)
    {
        try
        {
            return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    public static string VersionSlug(string version)
    {
        var slug = NonSlugPattern().Replace(version, "-").Trim('-', '.', ' ');
        return slug.Length == 0 ? "unknown" : slug;
    }

    [GeneratedRegex("[^A-Za-z0-9._-]+")]
    private static partial Regex NonSlugPattern();

}
