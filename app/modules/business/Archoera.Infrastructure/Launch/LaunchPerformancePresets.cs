using Archoera.Domain.Settings;

namespace Archoera.Infrastructure.Launch;

/// <summary>
/// Linux-only performance presets (GameMode wrapper, MangoHud and Zink
/// environment). They are deliberately separate from
/// <see cref="LaunchOptimizationPlanner"/>: the optimization plan may add GPU
/// variables of its own, while these switches mirror the user's explicit
/// choices even when automatic optimization is off.
///
/// <para>The platform decision is a parameter rather than
/// <see cref="OperatingSystem.IsLinux"/> so the behaviour can be tested on any
/// host; callers pass the real platform once.</para>
/// </summary>
internal static class LaunchPerformancePresets
{
    internal const string GameModeCommand = "gamemoderun";
    internal const string MangoHudVariable = "MANGOHUD";
    internal const string ZinkLoaderVariable = "MESA_LOADER_DRIVER_OVERRIDE";
    internal const string ZinkGalliumVariable = "GALLIUM_DRIVER";
    internal const string ZinkDriver = "zink";

    /// <summary>
    /// Prepends <c>gamemoderun</c> to the wrapper tokens on Linux unless it is
    /// already the first token. The result composes with a user wrapper, for
    /// example <c>gamemoderun mangohud java ...</c>.
    /// </summary>
    internal static IReadOnlyList<string> ComposeWrapper(
        IReadOnlyList<string> wrapper,
        bool gameMode,
        bool linux)
    {
        ArgumentNullException.ThrowIfNull(wrapper);
        if (!linux || !gameMode) return wrapper;
        if (wrapper.Count > 0 &&
            string.Equals(wrapper[0], GameModeCommand, StringComparison.Ordinal))
        {
            return wrapper;
        }

        var result = new List<string>(wrapper.Count + 1) { GameModeCommand };
        result.AddRange(wrapper);
        return result;
    }

    /// <summary>
    /// Builds the preset environment layer. It is merged before the user's
    /// global/instance environment, so an explicit user value always wins.
    /// Non-Linux platforms get an empty layer.
    /// </summary>
    internal static Dictionary<string, string> BuildEnvironment(
        bool mangoHud,
        bool zink,
        bool linux)
    {
        var environment = LaunchEnvironmentRules.CreateMap();
        if (!linux) return environment;

        if (mangoHud)
        {
            environment[MangoHudVariable] = "1";
        }

        if (zink)
        {
            environment[ZinkLoaderVariable] = ZinkDriver;
            environment[ZinkGalliumVariable] = ZinkDriver;
        }

        return environment;
    }
}
