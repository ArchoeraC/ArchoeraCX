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
public sealed record InstanceOverrides(
    int? MemoryMinMb = null,
    int? MemoryMaxMb = null,
    string? JvmArgs = null,
    string? GcMode = null,
    string? GameArgs = null,
    string? JavaPath = null,
    int? WindowWidth = null,
    int? WindowHeight = null,
    bool? Fullscreen = null)
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
        Fullscreen is null;

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
            // A pinned instance Java is always manual; otherwise keep global.
            JavaAuto = JavaPath is null ? settings.JavaAuto : false,
        };
    }
}
