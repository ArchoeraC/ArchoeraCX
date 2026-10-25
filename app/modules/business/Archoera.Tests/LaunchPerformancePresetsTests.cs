using Archoera.Domain.Settings;
using Archoera.Infrastructure.Launch;

namespace Archoera.Tests;

/// <summary>
/// Pure helpers behind the Linux performance presets and the developer-only
/// native-library overrides. The platform flag is a parameter, so both the
/// Linux and non-Linux branches are covered on any host.
/// </summary>
public sealed class LaunchPerformancePresetsTests
{
    [Fact]
    public void GameMode_prepends_gamemoderun_to_an_empty_wrapper()
    {
        var result = LaunchPerformancePresets.ComposeWrapper([], gameMode: true, linux: true);
        Assert.Equal(["gamemoderun"], result);
    }

    [Fact]
    public void GameMode_composes_with_an_existing_wrapper()
    {
        var result = LaunchPerformancePresets.ComposeWrapper(
            ["mangohud", "--dlsym"],
            gameMode: true,
            linux: true);
        Assert.Equal(["gamemoderun", "mangohud", "--dlsym"], result);
    }

    [Fact]
    public void GameMode_is_not_prepended_twice()
    {
        var wrapper = new[] { "gamemoderun", "mangohud" };
        var result = LaunchPerformancePresets.ComposeWrapper(
            wrapper,
            gameMode: true,
            linux: true);

        Assert.Same(wrapper, result);
    }

    [Fact]
    public void GameMode_is_ignored_when_off_or_not_on_linux()
    {
        Assert.Equal(
            ["mangohud"],
            LaunchPerformancePresets.ComposeWrapper(
                ["mangohud"],
                gameMode: false,
                linux: true));
        Assert.Equal(
            ["mangohud"],
            LaunchPerformancePresets.ComposeWrapper(
                ["mangohud"],
                gameMode: true,
                linux: false));
    }

    [Fact]
    public void MangoHud_and_Zink_environment_is_linux_only()
    {
        var linux = LaunchPerformancePresets.BuildEnvironment(
            mangoHud: true,
            zink: true,
            linux: true);
        Assert.Equal("1", linux["MANGOHUD"]);
        Assert.Equal("zink", linux["MESA_LOADER_DRIVER_OVERRIDE"]);
        Assert.Equal("zink", linux["GALLIUM_DRIVER"]);

        var other = LaunchPerformancePresets.BuildEnvironment(
            mangoHud: true,
            zink: true,
            linux: false);
        Assert.Empty(other);

        Assert.Empty(LaunchPerformancePresets.BuildEnvironment(
            mangoHud: false,
            zink: false,
            linux: true));
    }

    [Fact]
    public void Native_overrides_need_developer_mode_linux_and_existing_files()
    {
        var directory = TempDirectory();
        try
        {
            var glfw = CreateFile(directory, "libglfw.so");
            var openAl = CreateFile(directory, "libopenal.so");
            var sdl = CreateFile(directory, "libSDL2.so");
            var settings = Settings() with
            {
                DeveloperMode = true,
                NativeGlfwPath = glfw,
                NativeOpenALPath = openAl,
                NativeSdlPath = sdl,
            };

            var arguments = NativeLibraryOverrides.BuildArguments(settings, linux: true);
            Assert.Equal(
                [
                    $"-Dorg.lwjgl.glfw.libname={glfw}",
                    $"-Dorg.lwjgl.openal.libname={openAl}",
                    $"-Dorg.lwjgl.sdl.libname={sdl}",
                ],
                arguments);

            Assert.Empty(NativeLibraryOverrides.BuildArguments(
                settings with { DeveloperMode = false },
                linux: true));
            Assert.Empty(NativeLibraryOverrides.BuildArguments(settings, linux: false));
        }
        finally
        {
            Delete(directory);
        }
    }

    [Fact]
    public void Missing_relative_or_invalid_native_paths_are_skipped()
    {
        var directory = TempDirectory();
        try
        {
            var present = CreateFile(directory, "libglfw.so");
            var missing = Path.Combine(directory, "missing.so");
            var settings = Settings() with
            {
                DeveloperMode = true,
                NativeGlfwPath = present,
                NativeOpenALPath = missing,
                // Simulates a hand-edited document that bypassed the module
                // validation; it must be ignored instead of aborting a launch.
                NativeSdlPath = "relative/libSDL2.so",
            };

            var arguments = NativeLibraryOverrides.BuildArguments(settings, linux: true);

            Assert.Equal([$"-Dorg.lwjgl.glfw.libname={present}"], arguments);
        }
        finally
        {
            Delete(directory);
        }
    }

    private static AppSettings Settings()
        => new(
            InstancesRoot: Path.GetTempPath(),
            DataRoot: Path.GetTempPath(),
            SettingsPath: string.Empty);

    private static string CreateFile(string directory, string name)
    {
        var path = Path.Combine(directory, name);
        File.WriteAllText(path, "native");
        return path;
    }

    private static string TempDirectory()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            "archoera-launch-performance-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void Delete(string path)
    {
        try
        {
            if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
        }
        catch
        {
            // best effort
        }
    }
}
