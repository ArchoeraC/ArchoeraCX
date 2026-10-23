namespace Archoera.Application.JavaRuntime;

/// <summary>
/// Location of Java runtimes installed by the launcher itself (Mojang
/// <c>all.json</c> catalog). Kept next to the other shared roots so a fresh
/// download can be discovered again after a restart.
/// </summary>
public static class ManagedJavaRuntimes
{
    /// <summary>Directory below the data root that owns managed runtimes.</summary>
    public const string DirectoryName = "runtime";

    /// <summary>Root that contains <c>java-&lt;major&gt;</c> directories.</summary>
    public static string Root(string dataRoot)
        => Path.Combine(Path.GetFullPath(dataRoot.Trim()), DirectoryName);

    /// <summary>Directory of one managed runtime.</summary>
    public static string MajorRoot(string dataRoot, int major)
        => Path.Combine(Root(dataRoot), $"java-{major}");
}
