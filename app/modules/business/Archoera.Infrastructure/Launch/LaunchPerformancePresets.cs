using Archoera.Domain.Settings;

namespace Archoera.Infrastructure.Launch;

/// <summary>
/// Linux-only performance presets (GameMode/MangoHud wrapper commands and
/// Zink environment). They are deliberately separate from
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
    internal const string MangoHudCommand = "mangohud";
    internal const string MangoHudVariable = "MANGOHUD";
    internal const string ZinkLoaderVariable = "MESA_LOADER_DRIVER_OVERRIDE";
    internal const string ZinkGalliumVariable = "GALLIUM_DRIVER";
    internal const string ZinkDriver = "zink";

    /// <summary>
    /// Prepends the requested wrapper commands on Linux. GameMode runs
    /// outermost (<c>gamemoderun mangohud ...</c>) and MangoHud comes next;
    /// a command already present in the user wrapper is not duplicated, and
    /// the original list is returned unchanged when nothing is added.
    /// </summary>
    internal static IReadOnlyList<string> ComposeWrapper(
        IReadOnlyList<string> wrapper,
        bool gameMode,
        bool mangoHud,
        bool linux)
    {
        ArgumentNullException.ThrowIfNull(wrapper);
        if (!linux) return wrapper;

        var prefix = new List<string>(2);
        if (gameMode && !Contains(wrapper, GameModeCommand))
        {
            prefix.Add(GameModeCommand);
        }
        if (mangoHud && !Contains(wrapper, MangoHudCommand))
        {
            prefix.Add(MangoHudCommand);
        }
        if (prefix.Count == 0) return wrapper;

        var result = new List<string>(prefix.Count + wrapper.Count);
        result.AddRange(prefix);
        result.AddRange(wrapper);
        return result;

        static bool Contains(IReadOnlyList<string> tokens, string command)
        {
            for (var i = 0; i < tokens.Count; i++)
            {
                if (string.Equals(tokens[i], command, StringComparison.Ordinal))
                {
                    return true;
                }
            }
            return false;
        }
    }

    /// <summary>
    /// Builds the preset environment layer. It is merged before the user's
    /// global/instance environment, so an explicit user value always wins.
    /// Non-Linux platforms get an empty layer. <c>MANGOHUD=1</c> is kept in
    /// addition to the wrapper because Vulkan-layer setups use it directly;
    /// the wrapper is still what enables the OpenGL overlay.
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
