using Archoera.Domain.Instances;
using Archoera.Domain.Settings;
using Archoera.Infrastructure.SystemInfo;

namespace Archoera.Infrastructure.Launch;

/// <summary>
/// Resolved heap for one launch. <see cref="RecommendedMb"/> is what automatic
/// sizing would pick even in manual mode, so the UI can show it next to the
/// user's own value and warn when the two drift apart.
/// </summary>
/// <param name="Mode">`auto` or `manual`.</param>
/// <param name="MinMb">`-Xms` value.</param>
/// <param name="MaxMb">`-Xmx` value.</param>
/// <param name="RecommendedMb">Heap automatic sizing would choose.</param>
/// <param name="SystemTotalMb">Installed RAM, 0 when unknown.</param>
/// <param name="SystemAvailableMb">Available RAM, null when unknown.</param>
/// <param name="ModCount">Enabled mods found in the instance.</param>
/// <param name="WarningCode">
/// `LOW_MEMORY`, `BELOW_RECOMMENDED`, `OVER_SYSTEM`, `OVER_AVAILABLE` or
/// `SYSTEM_LOW_MEMORY`. The UI maps the code to localized text; the English
/// message is only for logs and the task center.
/// </param>
/// <param name="WarningMessage">Human-readable warning, null when none.</param>
public sealed record LaunchMemoryPlan(
    string Mode,
    int MinMb,
    int MaxMb,
    int RecommendedMb,
    long SystemTotalMb,
    long? SystemAvailableMb,
    int ModCount,
    string? WarningCode,
    string? WarningMessage)
{
    /// <summary>True when the resolved heap is worth telling the user about.</summary>
    public bool HasWarning => WarningCode is not null;
}

/// <summary>
/// Heap sizing shared by the launch path and the `launch.memory` preview.
///
/// The automatic value follows what the game actually needs: the version
/// baseline, the loader overhead, and the enabled mod count, then it is capped
/// by the RAM the machine really has. Manual values are always honoured; they
/// only produce a warning so the user can still force a tiny heap.
/// </summary>
public static class LaunchMemoryPlanner
{
    /// <summary>Lowest heap the launcher will ever hand to the JVM.</summary>
    public const int FloorMb = 512;

    /// <summary>Lower bound for an automatic value.</summary>
    public const int AutoFloorMb = 1024;

    /// <summary>Upper bound for an automatic value; manual mode is not capped.</summary>
    public const int AutoCeilingMb = 8192;

    /// <summary>Warning codes, mirrored by the Dart UI.</summary>
    public static class WarningCodes
    {
        public const string LowMemory = "LOW_MEMORY";
        public const string BelowRecommended = "BELOW_RECOMMENDED";
        public const string OverSystem = "OVER_SYSTEM";
        public const string OverAvailable = "OVER_AVAILABLE";
        public const string SystemLowMemory = "SYSTEM_LOW_MEMORY";
    }

    /// <summary>
    /// Resolves the heap for <paramref name="instance"/> using the effective
    /// settings document (global values already folded with overrides).
    /// </summary>
    public static LaunchMemoryPlan Resolve(
        AppSettings settings,
        GameInstanceRecord instance,
        string? instanceRoot = null,
        (long TotalMb, long? AvailableMb)? memory = null)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(instance);

        var (totalMb, availableMb) = memory ?? SystemModuleService.ReadMemory();
        var root = instanceRoot ?? instance.RootPath;
        // The directory is authoritative (it reflects enable/disable), but the
        // instance index is a usable fallback when the folder is unreachable.
        var modCount = Directory.Exists(Path.Combine(root, "mods"))
            ? CountEnabledMods(root)
            : instance.ModCount;
        var recommended = Recommend(instance, modCount, totalMb);
        // An explicit per-instance heap is a manual decision: honouring it is
        // the whole point of the override, and the UI warns next to it.
        var overridden = instance.Overrides is { } instanceOverrides &&
                         (instanceOverrides.MemoryMinMb is not null ||
                          instanceOverrides.MemoryMaxMb is not null);
        var manual = overridden ||
                     string.Equals(
                         settings.MemoryMode,
                         AppSettings.MemoryModes.Manual,
                         StringComparison.Ordinal);

        var minMb = manual ? settings.MemoryMinMb : Math.Min(recommended, AutoFloorMb);
        var maxMb = manual ? settings.MemoryMaxMb : recommended;
        if (minMb < FloorMb) minMb = FloorMb;
        if (maxMb < minMb) maxMb = minMb;

        var (code, message) = manual
            ? ManualWarning(maxMb, recommended, totalMb, availableMb)
            : AutoWarning(maxMb, totalMb);

        return new LaunchMemoryPlan(
            manual ? AppSettings.MemoryModes.Manual : AppSettings.MemoryModes.Auto,
            minMb,
            maxMb,
            recommended,
            totalMb,
            availableMb,
            modCount,
            code,
            message);
    }

    /// <summary>
    /// Heap the game would get automatically: version baseline + loader + mods,
    /// rounded to 256 MB and capped by installed RAM.
    /// </summary>
    public static int Recommend(GameInstanceRecord instance, int modCount, long totalMb)
    {
        var baseline = VersionBaselineMb(instance.GameVersion);
        var loader = LoaderOverheadMb(instance.Loader);
        var mods = Math.Min(Math.Max(modCount, 0), 400) * 8;
        var value = baseline + loader + mods;

        // Leave room for the OS and the launcher itself: 2 GB on normal
        // machines, half the RAM on small ones.
        if (totalMb > 0)
        {
            var cap = totalMb >= 8192 ? totalMb - 2048 : totalMb / 2;
            if (cap < AutoFloorMb) cap = AutoFloorMb;
            value = Math.Min(value, (int)Math.Min(cap, int.MaxValue));
        }

        value = Math.Clamp(value, AutoFloorMb, AutoCeilingMb);
        return RoundUpTo256(value);
    }

    /// <summary>Counts enabled mod jars (`mods/*.jar`), ignoring disabled ones.</summary>
    public static int CountEnabledMods(string instanceRoot)
    {
        if (string.IsNullOrWhiteSpace(instanceRoot)) return 0;
        try
        {
            var mods = Path.Combine(instanceRoot, "mods");
            if (!Directory.Exists(mods)) return 0;
            // Top-level only: `*.jar.disabled` does not match `*.jar`, and a
            // nested directory is not loaded by the game either.
            return Directory.EnumerateFiles(mods, "*.jar").Count();
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            return 0;
        }
    }

    private static int VersionBaselineMb(string gameVersion)
    {
        if (!TryParseVersion(gameVersion, out var major, out var minor)) return 2048;
        if (major > 1) return 2048; // 26.x snapshots behave like modern releases
        if (minor >= 18) return 2048;
        if (minor >= 13) return 1536;
        return 1024;
    }

    private static int LoaderOverheadMb(string loader) =>
        loader.Trim().ToLowerInvariant() switch
        {
            "forge" or "neoforge" => 512,
            "fabric" or "quilt" => 256,
            _ => 0,
        };

    private static (string? Code, string? Message) ManualWarning(
        int maxMb,
        int recommendedMb,
        long totalMb,
        long? availableMb)
    {
        if (totalMb > 0 && maxMb > totalMb - 1024)
        {
            return (
                WarningCodes.OverSystem,
                $"heap {maxMb} MB leaves too little RAM on a {totalMb} MB machine");
        }

        if (maxMb < AutoFloorMb)
        {
            return (
                WarningCodes.LowMemory,
                $"heap {maxMb} MB is below {AutoFloorMb} MB; modern modded games may crash");
        }

        if (availableMb is { } available && available > 0 && maxMb + 768L > available)
        {
            return (
                WarningCodes.OverAvailable,
                $"heap {maxMb} MB is close to the {available} MB currently available");
        }

        if (maxMb * 10 < recommendedMb * 6)
        {
            return (
                WarningCodes.BelowRecommended,
                $"heap {maxMb} MB is below the recommended {recommendedMb} MB");
        }

        return (null, null);
    }

    private static (string? Code, string? Message) AutoWarning(int maxMb, long totalMb)
    {
        if (totalMb > 0 && totalMb < 4096)
        {
            return (
                WarningCodes.SystemLowMemory,
                $"{totalMb} MB of RAM is low for modded Minecraft; heap capped at {maxMb} MB");
        }

        return (null, null);
    }

    private static int RoundUpTo256(int value) => (value + 255) / 256 * 256;

    private static bool TryParseVersion(string value, out int major, out int minor)
    {
        major = 0;
        minor = 0;
        if (string.IsNullOrWhiteSpace(value)) return false;
        var parts = value.Split('.');
        if (parts.Length < 2) return false;
        return int.TryParse(parts[0], out major) && int.TryParse(parts[1], out minor);
    }
}
