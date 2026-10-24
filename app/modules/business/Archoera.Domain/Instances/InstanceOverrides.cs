namespace Archoera.Domain.Instances;

using Archoera.Domain.Settings;

/// <summary>
/// Per-instance launch overrides. Every field is optional: <c>null</c> means
/// "follow the global settings document", so an instance only stores what the
/// user actually changed.
/// </summary>
/// <param name="MemoryMinMb">Minimum heap for this instance.</param>
/// <param name="MemoryMaxMb">Maximum heap for this instance.</param>
/// <param name="JvmArgs">Extra JVM arguments for this instance.</param>
/// <param name="GcMode">Garbage collector override.</param>
/// <param name="GameArgs">Extra game arguments for this instance.</param>
/// <param name="JavaPath">Java executable pinned for this instance.</param>
/// <param name="WindowWidth">Game window width override.</param>
/// <param name="WindowHeight">Game window height override.</param>
/// <param name="Fullscreen">Fullscreen override.</param>
/// <param name="WrapperCommand">
/// Wrapper command override. Null inherits the global wrapper; an empty string
/// explicitly disables the global wrapper for this instance.
/// </param>
/// <param name="Environment">
/// Environment override. Null inherits the global environment; an empty object
/// explicitly clears it for this instance.
/// </param>
/// <param name="JoinServerOnLaunch">True to join a server or world after start.</param>
/// <param name="JoinServerAddress">Server address (<c>host[:port]</c> or bracketed IPv6).</param>
/// <param name="JoinWorldName">Single-player world folder to open.</param>
/// <param name="PreLoadCommand">
/// Pre-load command override. Null inherits the global command; an empty
/// string explicitly disables the global command for this instance.
/// </param>
/// <param name="PreLaunchCommand">
/// Pre-launch command override. Null inherits the global command; an empty
/// string explicitly disables the global command for this instance.
/// </param>
/// <param name="PostExitCommand">
/// Post-exit command override. Null inherits the global command; an empty
/// string explicitly disables the global command for this instance.
/// </param>
public sealed record InstanceOverrides(
    int? MemoryMinMb = null,
    int? MemoryMaxMb = null,
    string? JvmArgs = null,
    string? GcMode = null,
    string? GameArgs = null,
    string? JavaPath = null,
    int? WindowWidth = null,
    int? WindowHeight = null,
    bool? Fullscreen = null,
    string? WrapperCommand = null,
    IReadOnlyDictionary<string, string>? Environment = null,
    bool? JoinServerOnLaunch = null,
    string? JoinServerAddress = null,
    string? JoinWorldName = null,
    string? PreLoadCommand = null,
    string? PreLaunchCommand = null,
    string? PostExitCommand = null)
{
    /// <summary>Supported GC modes; mirrors the global settings validation.</summary>
    public static readonly string[] GcModes =
    [
        "auto", "g1gc", "zgc", "parallel", "serial", "shenandoah", "epsilon",
    ];

    public bool IsEmpty =>
        MemoryMinMb is null &&
        MemoryMaxMb is null &&
        JvmArgs is null &&
        GcMode is null &&
        GameArgs is null &&
        JavaPath is null &&
        WindowWidth is null &&
        WindowHeight is null &&
        Fullscreen is null &&
        WrapperCommand is null &&
        Environment is null &&
        JoinServerOnLaunch is null &&
        JoinServerAddress is null &&
        JoinWorldName is null &&
        PreLoadCommand is null &&
        PreLaunchCommand is null &&
        PostExitCommand is null;

    /// <summary>
    /// Folds the overrides onto the global document. The result is what the
    /// launch plan builder should consume; the global settings stay untouched.
    /// </summary>
    public AppSettings ApplyTo(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var memoryMin = MemoryMinMb ?? settings.MemoryMinMb;
        var memoryMax = MemoryMaxMb ?? settings.MemoryMaxMb;
        if (memoryMin > memoryMax) memoryMin = memoryMax;

        var gcMode = GcMode is { } mode && GcModes.Contains(mode)
            ? mode
            : settings.GcMode;

        // Environment layers: optimization is added by the plan builder, then
        // this merged user map (global first, instance last). An explicitly
        // empty instance map clears the global one; a non-empty map merges.
        IReadOnlyDictionary<string, string>? environment = settings.Environment;
        if (Environment is { Count: 0 })
        {
            environment = LaunchEnvironmentRules.Merge();
        }
        else if (Environment is not null)
        {
            environment = LaunchEnvironmentRules.Merge(settings.Environment, Environment);
        }
        else if (settings.Environment is not null)
        {
            environment = LaunchEnvironmentRules.Merge(settings.Environment);
        }

        return settings with
        {
            MemoryMinMb = memoryMin,
            MemoryMaxMb = memoryMax,
            JvmArgs = JvmArgs ?? settings.JvmArgs,
            GcMode = gcMode,
            GameArgs = GameArgs ?? settings.GameArgs,
            WindowWidth = WindowWidth ?? settings.WindowWidth,
            WindowHeight = WindowHeight ?? settings.WindowHeight,
            Fullscreen = Fullscreen ?? settings.Fullscreen,
            JavaPath = JavaPath ?? settings.JavaPath,
            WrapperCommand = WrapperCommand ?? settings.WrapperCommand,
            Environment = environment,
            PreLoadCommand = ResolveCommand(PreLoadCommand, settings.PreLoadCommand),
            PreLaunchCommand = ResolveCommand(PreLaunchCommand, settings.PreLaunchCommand),
            PostExitCommand = ResolveCommand(PostExitCommand, settings.PostExitCommand),
            // A pinned instance Java is always manual; otherwise keep global.
            JavaAuto = JavaPath is null ? settings.JavaAuto : false,
        };
    }

    /// <summary>
    /// An absent instance command inherits the global one; an explicitly empty
    /// instance command disables it. Whitespace is treated as empty because the
    /// settings parser stores it that way.
    /// </summary>
    private static string? ResolveCommand(string? instance, string? global)
    {
        var value = instance ?? global;
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }
}
