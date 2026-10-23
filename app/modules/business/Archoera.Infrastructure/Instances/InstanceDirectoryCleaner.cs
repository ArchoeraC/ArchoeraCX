using Archoera.Domain;
using Archoera.Domain.Instances;

namespace Archoera.Infrastructure.Instances;

/// <summary>
/// Deletes an instance's own game directory, but only when the directory is
/// provably dedicated to that instance. The guard rails matter because
/// <c>rootPath</c> may point at a shared game folder, a configured instance
/// root, or a directory that hosts another instance:
/// <list type="bullet">
///   <item>filesystem roots and symbolic links are never followed or removed;</item>
///   <item>the directory must carry the <c>.archoera/instance.json</c> marker;</item>
///   <item>configured instance roots and any other registered instance path
///         (equal or nested) veto the delete.</item>
/// </list>
/// </summary>
public static class InstanceDirectoryCleaner
{

    /// <summary>
    /// Try to remove <paramref name="rootPath"/> recursively.
    /// Returns true when the directory is gone, otherwise false with a stable
    /// <paramref name="skipReason"/> code from <see cref="InstanceDeleteSkipReason"/>.
    /// </summary>
    public static bool TryDelete(
        string rootPath,
        IReadOnlyList<string> configuredRoots,
        IReadOnlyList<string> otherInstanceRoots,
        out string? skipReason)
    {
        skipReason = null;
        if (string.IsNullOrWhiteSpace(rootPath))
        {
            skipReason = InstanceDeleteSkipReason.UnsafePath;
            return false;
        }

        string full;
        try
        {
            full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(rootPath));
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException
                                              or PathTooLongException)
        {
            skipReason = InstanceDeleteSkipReason.UnsafePath;
            return false;
        }

        if (IsFilesystemRoot(full))
        {
            skipReason = InstanceDeleteSkipReason.UnsafePath;
            return false;
        }

        if (!Directory.Exists(full))
        {
            skipReason = InstanceDeleteSkipReason.Missing;
            return false;
        }

        if (new DirectoryInfo(full).LinkTarget is not null)
        {
            skipReason = InstanceDeleteSkipReason.Symlink;
            return false;
        }

        if (!File.Exists(Path.Combine(full, ".archoera", "instance.json")))
        {
            skipReason = InstanceDeleteSkipReason.NotManaged;
            return false;
        }

        // An instance normally lives inside a configured root
        // (<root>/versions/<name>), so only a root that *is* the instance
        // directory, or that sits inside it, vetoes the delete.
        foreach (var root in configuredRoots)
        {
            if (Contains(full, root))
            {
                skipReason = InstanceDeleteSkipReason.SharedRoot;
                return false;
            }
        }

        // Same rule for sibling instances: never remove a directory that hosts
        // another registered instance.
        foreach (var other in otherInstanceRoots)
        {
            if (Contains(full, other))
            {
                skipReason = InstanceDeleteSkipReason.NestedInstance;
                return false;
            }
        }

        try
        {
            Directory.Delete(full, recursive: true);
        }
        catch (Exception exception) when (exception is IOException
                                              or UnauthorizedAccessException
                                              or DirectoryNotFoundException)
        {
            skipReason = InstanceDeleteSkipReason.Failed;
            return false;
        }

        return !Directory.Exists(full);
    }

    private static bool IsFilesystemRoot(string fullPath)
    {
        var pathRoot = Path.GetPathRoot(fullPath);
        return string.IsNullOrEmpty(pathRoot) ||
               PathSemantics.Comparer.Equals(
                   Path.TrimEndingDirectorySeparator(pathRoot),
                   fullPath);
    }

    /// <summary>
    /// True when <paramref name="candidate"/> is the same directory as
    /// <paramref name="protectedPath"/> or contains it. A protected path nested
    /// deeper than the candidate would be destroyed together with it.
    /// </summary>
    private static bool Contains(string candidate, string protectedPath)
    {
        if (string.IsNullOrWhiteSpace(protectedPath)) return false;

        string other;
        try
        {
            other = Path.TrimEndingDirectorySeparator(Path.GetFullPath(protectedPath));
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException
                                              or PathTooLongException)
        {
            return false;
        }

        return PathSemantics.Comparer.Equals(candidate, other) || IsWithin(other, candidate);
    }

    private static bool IsWithin(string candidate, string ancestor)
        => candidate.Length > ancestor.Length &&
           candidate.StartsWith(ancestor + Path.DirectorySeparatorChar, PathSemantics.Comparison);
}
