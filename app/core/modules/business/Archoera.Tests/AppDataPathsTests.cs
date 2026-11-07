using Archoera.Infrastructure.Settings;

namespace Archoera.Tests;

public sealed class AppDataPathsTests
{
    [Fact]
    public void Explicit_environment_override_wins_everywhere()
    {
        var resolved = AppDataPaths.ResolveDataRoot(
            configured: " /portable/archoera ",
            xdgDataHome: "/xdg",
            appData: "/appdata",
            userProfile: "/home/u",
            tempPath: "/tmp",
            isWindows: true,
            isMacOs: true);

        Assert.Equal(AppDataPaths.Normalize("/portable/archoera"), resolved);
    }

    [Fact]
    public void Windows_uses_the_roaming_appdata_directory()
    {
        var resolved = AppDataPaths.ResolveDataRoot(
            configured: null,
            xdgDataHome: null,
            appData: "/appdata",
            userProfile: "/home/u",
            tempPath: "/tmp",
            isWindows: true,
            isMacOs: false);

        Assert.Equal(
            AppDataPaths.Normalize(Path.Combine("/appdata", "ArchoeraCX")),
            resolved);
    }

    [Fact]
    public void Macos_uses_application_support()
    {
        var resolved = AppDataPaths.ResolveDataRoot(
            configured: null,
            xdgDataHome: null,
            appData: null,
            userProfile: "/Users/example",
            tempPath: "/tmp",
            isWindows: false,
            isMacOs: true);

        Assert.Equal(
            AppDataPaths.Normalize(Path.Combine(
                "/Users/example",
                "Library",
                "Application Support",
                "ArchoeraCX")),
            resolved);
    }

    [Fact]
    public void Linux_uses_xdg_data_home_when_it_is_absolute()
    {
        // Path.IsPathFullyQualified follows the host OS, so use a host-absolute
        // path rather than a literal /xdg path that is not drive-rooted on
        // Windows.
        var xdg = Path.Combine(Path.GetTempPath(), "xdg", "data");
        var resolved = AppDataPaths.ResolveDataRoot(
            configured: null,
            xdgDataHome: xdg,
            appData: null,
            userProfile: "/home/u",
            tempPath: "/tmp",
            isWindows: false,
            isMacOs: false);

        Assert.Equal(
            AppDataPaths.Normalize(Path.Combine(xdg, "archoera-cx")),
            resolved);
    }

    [Fact]
    public void Linux_ignores_a_relative_xdg_data_home()
    {
        var resolved = AppDataPaths.ResolveDataRoot(
            configured: null,
            xdgDataHome: "relative/data",
            appData: null,
            userProfile: "/home/u",
            tempPath: "/tmp",
            isWindows: false,
            isMacOs: false);

        Assert.Equal(
            AppDataPaths.Normalize(Path.Combine(
                "/home/u",
                ".local",
                "share",
                "archoera-cx")),
            resolved);
    }

    [Fact]
    public void Missing_profile_falls_back_to_the_os_temp_directory()
    {
        var resolved = AppDataPaths.ResolveDataRoot(
            configured: null,
            xdgDataHome: null,
            appData: null,
            userProfile: null,
            tempPath: "/tmp",
            isWindows: false,
            isMacOs: false);

        Assert.Equal(
            AppDataPaths.Normalize(Path.Combine(
                "/tmp",
                ".local",
                "share",
                "archoera-cx")),
            resolved);
    }

    [Fact]
    public void Settings_and_default_instances_live_under_the_data_root()
    {
        var root = AppDataPaths.Normalize("/portable/archoera");

        Assert.Equal(
            AppDataPaths.Normalize(Path.Combine(root, "settings.json")),
            AppDataPaths.SettingsPath(root));
        Assert.Equal(
            AppDataPaths.Normalize(Path.Combine(root, "instances")),
            AppDataPaths.DefaultInstancesRoot(root));
    }
}
