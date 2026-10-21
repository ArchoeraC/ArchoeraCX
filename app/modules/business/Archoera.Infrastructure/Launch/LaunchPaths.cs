namespace Archoera.Infrastructure.Launch;

internal sealed record LaunchPaths(
    string InstanceRoot,
    string SharedRoot,
    string LibrariesRoot,
    string AssetsRoot,
    string NativesRoot)
{
    public static LaunchPaths Resolve(string instanceRoot)
    {
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(instanceRoot));
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
