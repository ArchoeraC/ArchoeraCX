namespace Archoera.Infrastructure.Settings;

/// <summary>
/// Resolves platform data directories without touching UI code. The environment
/// override is preferred for development/portable builds.
/// </summary>
public static class AppDataPaths
{
    public const string DataDirEnvironment = "ARCHOERACX_DATA_DIR";
    public const string SettingsFileName = "settings.json";

    public static string ResolveDataRoot()
        => ResolveDataRoot(
            Environment.GetEnvironmentVariable(DataDirEnvironment),
            Environment.GetEnvironmentVariable("XDG_DATA_HOME"),
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            Path.GetTempPath(),
            OperatingSystem.IsWindows(),
            OperatingSystem.IsMacOS());

    /// <summary>
    /// Pure resolution path used by tests. Precedence is the same on every
    /// host: explicit <c>ARCHOERACX_DATA_DIR</c>, then the platform directory
    /// variable, then the user profile, then the OS temporary directory. The
    /// two boolean flags make the Windows/macOS/Linux branches testable
    /// without mutating the process environment.
    /// </summary>
    internal static string ResolveDataRoot(
        string? configured,
        string? xdgDataHome,
        string? appData,
        string? userProfile,
        string? tempPath,
        bool isWindows,
        bool isMacOs)
    {
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return Normalize(configured.Trim());
        }

        var fallback = string.IsNullOrWhiteSpace(tempPath)
            ? Path.GetTempPath()
            : tempPath;

        if (isWindows)
        {
            var baseDirectory = string.IsNullOrWhiteSpace(appData) ? fallback : appData;
            return Normalize(Path.Combine(baseDirectory, "ArchoeraCX"));
        }

        if (isMacOs)
        {
            var home = string.IsNullOrWhiteSpace(userProfile) ? fallback : userProfile;
            return Normalize(Path.Combine(
                home,
                "Library",
                "Application Support",
                "ArchoeraCX"));
        }

        // XDG_DATA_HOME must be absolute. A relative value is ignored, as the
        // specification requires, instead of being resolved against the CWD.
        if (!string.IsNullOrWhiteSpace(xdgDataHome) &&
            Path.IsPathFullyQualified(xdgDataHome.Trim()))
        {
            return Normalize(Path.Combine(xdgDataHome.Trim(), "archoera-cx"));
        }

        var linuxHome = string.IsNullOrWhiteSpace(userProfile) ? fallback : userProfile;
        return Normalize(Path.Combine(
            linuxHome,
            ".local",
            "share",
            "archoera-cx"));
    }

    public static string SettingsPath(string dataRoot)
        => Path.Combine(Normalize(dataRoot), SettingsFileName);

    public static string DefaultInstancesRoot(string dataRoot)
        => Path.Combine(Normalize(dataRoot), "instances");

    public static string Normalize(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return Path.GetFullPath(path.Trim());
    }
}
