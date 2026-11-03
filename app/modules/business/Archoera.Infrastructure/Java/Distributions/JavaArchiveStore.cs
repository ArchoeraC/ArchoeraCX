using Archoera.Application.JavaRuntime;

namespace Archoera.Infrastructure.Java.Distributions;

/// <summary>
/// Moves a verified archive beside the install target when the user opted out
/// of automatic deletion. Names are provider/version based so a later install
/// never overwrites an older kept archive.
/// </summary>
internal static class JavaArchiveStore
{
    /// <summary>
    /// Moves the verified archive next to the install target. The name is
    /// provider/version based so a later install cannot overwrite it.
    /// </summary>
    public static string Keep(
        string archivePath,
        string parent,
        string providerId,
        string version,
        string archiveType)
    {
        var extension = archiveType == "zip" ? ".zip" : ".tar.gz";
        var stem = $"{providerId}-{JavaInstallTarget.VersionSlug(version)}";
        var destination = UniquePath(Path.Combine(parent, stem + extension));
        File.Move(archivePath, destination);
        return destination;
    }

    private static string UniquePath(string path)
    {
        if (!File.Exists(path) && !Directory.Exists(path)) return path;

        var directory = Path.GetDirectoryName(path)!;
        var name = Path.GetFileName(path);
        var suffix = name.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase)
            ? ".tar.gz"
            : Path.GetExtension(name);
        var stem = name[..^suffix.Length];
        for (var index = 2; index < 1000; index++)
        {
            var candidate = Path.Combine(directory, $"{stem}-{index}{suffix}");
            if (!File.Exists(candidate) && !Directory.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new JavaDistributionException(
            "JAVA_INSTALL_FAILED",
            "cannot allocate a unique archive name");
    }
}
