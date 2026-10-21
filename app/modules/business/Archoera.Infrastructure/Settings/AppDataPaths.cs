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
    {
        var configured = Environment.GetEnvironmentVariable(DataDirEnvironment);
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return Normalize(configured);
        }

        if (OperatingSystem.IsWindows())
        {
            var appData = Environment.GetFolderPath(
                Environment.SpecialFolder.ApplicationData);
            return Path.Combine(
                string.IsNullOrWhiteSpace(appData)
                    ? Path.GetTempPath()
                    : appData,
                "ArchoeraCX");
        }

        if (OperatingSystem.IsMacOS())
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "Library",
                "Application Support",
                "ArchoeraCX");
        }

        var xdg = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
        if (!string.IsNullOrWhiteSpace(xdg))
        {
            return Path.Combine(Normalize(xdg), "archoera-cx");
        }

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".local",
            "share",
            "archoera-cx");
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
