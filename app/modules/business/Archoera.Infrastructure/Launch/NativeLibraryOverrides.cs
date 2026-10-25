using Archoera.Domain.Settings;

namespace Archoera.Infrastructure.Launch;

/// <summary>
/// Turns the developer-mode native-library paths into LWJGL system properties.
/// Only Linux is supported, developer mode must be on, and each file must
/// exist; every other case yields no argument and never fails the launch.
///
/// <para>The arguments are inserted before the user's custom JVM arguments, so
/// an explicit <c>-Dorg.lwjgl.*.libname=...</c> written by the user still wins
/// (later <c>-D</c> values override earlier ones).</para>
/// </summary>
internal static class NativeLibraryOverrides
{
    internal const string GlfwProperty = "org.lwjgl.glfw.libname";
    internal const string OpenAlProperty = "org.lwjgl.openal.libname";
    internal const string SdlProperty = "org.lwjgl.sdl.libname";

    /// <summary>
    /// Builds the <c>-D</c> arguments for the configured overrides. Missing,
    /// relative, malformed or absent files are skipped.
    /// </summary>
    /// <param name="settings">Effective settings (global developer mode plus global paths).</param>
    /// <param name="linux">Platform decision, injected so tests can cover both branches.</param>
    /// <param name="exists">File probe; defaults to <see cref="File.Exists(string)"/>.</param>
    internal static IReadOnlyList<string> BuildArguments(
        AppSettings settings,
        bool linux,
        Func<string, bool>? exists = null)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (!linux || !settings.DeveloperMode) return [];

        var probe = exists ?? File.Exists;
        var arguments = new List<string>(3);
        Add(arguments, GlfwProperty, settings.NativeGlfwPath, probe);
        Add(arguments, OpenAlProperty, settings.NativeOpenALPath, probe);
        Add(arguments, SdlProperty, settings.NativeSdlPath, probe);
        return arguments;
    }

    private static void Add(
        List<string> arguments,
        string property,
        string? configured,
        Func<string, bool> exists)
    {
        var path = NativeLibraryPaths.TryNormalize(configured);
        if (path is null) return;

        bool present;
        try
        {
            present = exists(path);
        }
        catch (Exception exception) when (
            exception is ArgumentException or IOException or UnauthorizedAccessException or
            NotSupportedException)
        {
            // A stale or inaccessible override must not abort the launch.
            present = false;
        }

        if (present)
        {
            arguments.Add($"-D{property}={path}");
        }
    }
}
