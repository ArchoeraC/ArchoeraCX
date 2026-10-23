using System.Text.Json;
using Archoera.Domain.Settings;

namespace Archoera.Infrastructure.Launch;

/// <summary>One display adapter as reported by the C++ `system.gpu` probe.</summary>
/// <param name="Vendor">`nvidia`, `amd`, `intel`, `apple`, `arm`, `virtio` or `unknown`.</param>
/// <param name="Name">Human-readable label; empty when the platform has none.</param>
/// <param name="Discrete">True for a dedicated card, false for an integrated GPU.</param>
/// <param name="DriverLoaded">False when the kernel driver is not bound (e.g. nouveau).</param>
public sealed record GpuInfo(
    string Vendor,
    string Name,
    bool Discrete,
    bool DriverLoaded);

/// <summary>
/// Hardware snapshot the launch module needs. The platform APIs live in the
/// C++ core (`system.gpu`); Dart forwards the payload verbatim and this record
/// only parses it.
/// </summary>
/// <param name="Platform">`linux`, `windows`, `macos` or `unknown`.</param>
/// <param name="Gpus">Adapters in probe order.</param>
/// <param name="PrimaryVendor">Vendor of the boot/default adapter.</param>
/// <param name="Hybrid">True when an integrated and a discrete adapter coexist.</param>
public sealed record HardwareProfile(
    string Platform,
    IReadOnlyList<GpuInfo> Gpus,
    string? PrimaryVendor,
    bool Hybrid)
{
    /// <summary>Empty profile used when the caller passes nothing.</summary>
    public static HardwareProfile Unknown { get; } =
        new("unknown", Array.Empty<GpuInfo>(), null, false);

    public GpuInfo? Find(string vendor) =>
        Gpus.FirstOrDefault(gpu =>
            string.Equals(gpu.Vendor, vendor, StringComparison.OrdinalIgnoreCase));

    /// <summary>Parses the `system.gpu` data object; null when it is unusable.</summary>
    public static HardwareProfile? TryParse(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object) return null;
        var platform = element.TryGetProperty("platform", out var platformElement) &&
                       platformElement.ValueKind == JsonValueKind.String
            ? platformElement.GetString() ?? "unknown"
            : "unknown";

        var gpus = new List<GpuInfo>();
        if (element.TryGetProperty("gpus", out var gpuArray) &&
            gpuArray.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in gpuArray.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object) continue;
                gpus.Add(new GpuInfo(
                    String(item, "vendor") ?? "unknown",
                    String(item, "name") ?? string.Empty,
                    Bool(item, "discrete"),
                    !item.TryGetProperty("driverLoaded", out var driverElement) ||
                    driverElement.ValueKind is not (JsonValueKind.True or JsonValueKind.False) ||
                    driverElement.GetBoolean()));
            }
        }

        var primary = String(element, "primaryVendor");
        var hybrid = Bool(element, "hybrid");
        return new HardwareProfile(platform, gpus, primary, hybrid);
    }

    private static string? String(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) &&
        value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static bool Bool(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) &&
        value.ValueKind is JsonValueKind.True or JsonValueKind.False &&
        value.GetBoolean();
}

/// <summary>
/// JVM arguments and process environment that make the game run better on the
/// machine it was detected on. Everything here is optional: with optimization
/// disabled the plan is empty and the launch line stays minimal.
/// </summary>
/// <param name="JvmArgs">Arguments appended before the user's own JVM args.</param>
/// <param name="Environment">Environment variables for the child JVM.</param>
/// <param name="Notes">Applied items, for logs and the task center.</param>
public sealed record LaunchOptimizationPlan(
    IReadOnlyList<string> JvmArgs,
    IReadOnlyDictionary<string, string> Environment,
    IReadOnlyList<string> Notes)
{
    public static LaunchOptimizationPlan Empty { get; } = new(
        Array.Empty<string>(),
        new Dictionary<string, string>(StringComparer.Ordinal),
        Array.Empty<string>());

    /// <summary>True when the plan changes the launch line at all.</summary>
    public bool IsEmpty => JvmArgs.Count == 0 && Environment.Count == 0;
}

/// <summary>
/// Builds the optimization plan from the memory plan, the detected hardware
/// and the effective settings. Pure: no process or registry side effects.
/// </summary>
public static class LaunchOptimizationPlanner
{
    /// <summary>Heap at or above which the tuned G1 flags are worth using.</summary>
    public const int G1TuningFloorMb = 4096;

    public static LaunchOptimizationPlan Build(
        AppSettings settings,
        LaunchMemoryPlan memory,
        HardwareProfile? hardware,
        int cores)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(memory);

        if (!settings.GameOptimization)
        {
            return LaunchOptimizationPlan.Empty;
        }

        var args = new List<string>();
        var environment = new Dictionary<string, string>(StringComparer.Ordinal);
        var notes = new List<string>();

        // "auto" means G1: every modern Minecraft build runs best on it, and
        // Java 8u40+ ships it as the default collector anyway.
        var gc = settings.GcMode == "auto" ? "g1gc" : settings.GcMode;

        if (gc == "g1gc" && memory.MaxMb >= G1TuningFloorMb)
        {
            args.AddRange(G1Tuning(memory.MaxMb));
            if (cores > 8)
            {
                var parallel = 8 + (cores - 8) * 5 / 8;
                args.Add($"-XX:ParallelGCThreads={parallel}");
                args.Add($"-XX:ConcGCThreads={Math.Max(1, parallel / 4)}");
            }

            // Committing the whole heap up front trades startup time for
            // steadier frame times, but only when the RAM is actually there.
            if (memory.SystemTotalMb > 0 &&
                memory.MaxMb + 3072 <= memory.SystemTotalMb)
            {
                args.Add("-XX:+AlwaysPreTouch");
                notes.Add("pretouch");
            }

            notes.Add("g1-tuning");
        }

        if (hardware is not null)
        {
            if (string.Equals(hardware.Platform, "linux", StringComparison.Ordinal))
            {
                var nvidia = hardware.Find("nvidia");
                if (nvidia is { DriverLoaded: true })
                {
                    // PRIME render offload: the GL/Vulkan calls go to the
                    // dedicated card while the compositor keeps the iGPU.
                    environment["__NV_PRIME_RENDER_OFFLOAD"] = "1";
                    environment["__GLX_VENDOR_LIBRARY_NAME"] = "nvidia";
                    environment["__VK_LAYER_NV_optimus"] = "NVIDIA_only";
                    notes.Add("prime-offload");
                }
                else if (hardware.Find("amd") is not null && hardware.Hybrid)
                {
                    environment["DRI_PRIME"] = "1";
                    notes.Add("dri-prime");
                }
            }
            else if (string.Equals(hardware.Platform, "windows", StringComparison.Ordinal) &&
                     hardware.Find("nvidia") is not null)
            {
                notes.Add("windows-gpu-preference");
            }
        }

        return new LaunchOptimizationPlan(args, environment, notes);
    }

    /// <summary>
    /// Aikar-style G1 flags, scaled to the heap. They target 4-32 GB heaps and
    /// keep the collector out of the way of the game thread.
    /// </summary>
    private static IEnumerable<string> G1Tuning(int maxMb)
    {
        var region = maxMb >= 16384 ? "32M" : maxMb >= 8192 ? "16M" : "8M";
        yield return "-XX:+UseG1GC";
        yield return "-XX:+ParallelRefProcEnabled";
        yield return "-XX:MaxGCPauseMillis=200";
        yield return "-XX:+UnlockExperimentalVMOptions";
        yield return "-XX:+DisableExplicitGC";
        yield return "-XX:G1NewSizePercent=30";
        yield return "-XX:G1MaxNewSizePercent=40";
        yield return $"-XX:G1HeapRegionSize={region}";
        yield return "-XX:G1ReservePercent=20";
        yield return "-XX:G1HeapWastePercent=5";
        yield return "-XX:G1MixedGCCountTarget=4";
        yield return "-XX:InitiatingHeapOccupancyPercent=15";
        yield return "-XX:G1MixedGCLiveThresholdPercent=90";
        yield return "-XX:G1RSetUpdatingPauseTimePercent=5";
        yield return "-XX:SurvivorRatio=32";
        yield return "-XX:MaxTenuringThreshold=1";
    }
}
