namespace Archoera.Infrastructure.Launch;

internal sealed record LaunchPaths(
    string InstanceRoot,
    string SharedRoot,
    string LibrariesRoot,
    string AssetsRoot,
    string NativesRoot)
{
    /// <summary>
    /// Root that owns <c>versions/</c>, <c>libraries/</c> and <c>assets/</c>
    /// before those directories exist. Instance directories placed under a
    /// <c>versions</c> parent (classic version isolation) share the parent game
    /// root; other instances own their files.
    /// </summary>
    public static string ResolveInstallRoot(string instanceRoot)
    {
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(instanceRoot));
        var parent = Directory.GetParent(root);
        if (parent is not null &&
            string.Equals(
                Path.GetFileName(parent.FullName),
                "versions",
                StringComparison.OrdinalIgnoreCase) &&
            parent.Parent is not null)
        {
            return parent.Parent.FullName;
        }

        return root;
    }

    /// <summary>
    /// True when a <c>versions/</c> child looks like a game instance (mods,
    /// configs, saves) rather than a plain loader profile. The launch profile
    /// fallback must not borrow another instance's version JSON: custom pack
    /// names can contain the game and loader name too.
    /// </summary>
    public static bool LooksLikeInstanceDirectory(string directory)
    {
        foreach (var name in new[]
                 {
                     "mods", "config", "logs", "saves", "resourcepacks",
                     "shaderpacks", "options.txt", ".archoera",
                 })
        {
            var path = Path.Combine(directory, name);
            if (File.Exists(path) || Directory.Exists(path)) return true;
        }

        return false;
    }

    public static LaunchPaths Resolve(string instanceRoot)
    {        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(instanceRoot));
        var parent = Directory.GetParent(root);
        var shared = root;

        if (!Directory.Exists(Path.Combine(root, "libraries")) ||
            !Directory.Exists(Path.Combine(root, "assets")))
        {
            if (parent is not null &&
                string.Equals(Path.GetFileName(parent.FullName), "versions",
                    StringComparison.OrdinalIgnoreCase) &&
                parent.Parent is not null)
            {
                shared = parent.Parent.FullName;
            }
            else if (parent is not null &&
                     Directory.Exists(Path.Combine(parent.FullName, "libraries")))
            {
                shared = parent.FullName;
            }
        }

        var existingNatives = Path.Combine(root, "natives");
        var natives = Directory.Exists(existingNatives) &&
                      Directory.EnumerateFiles(existingNatives).Any()
            ? existingNatives
            : Path.Combine(root, ".archoera", "natives");

        return new LaunchPaths(
            root,
            shared,
            Path.Combine(shared, "libraries"),
            Path.Combine(shared, "assets"),
            natives);
    }
}
