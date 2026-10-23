using Archoera.Domain.Settings;
using Archoera.Infrastructure.Launch;

namespace Archoera.Tests;

/// <summary>
/// Optimization is opt-in: with the switch off the launch line must stay
/// minimal, and the GPU environment only appears when the C++ probe actually
/// found a usable dedicated adapter.
/// </summary>
public sealed class LaunchOptimizationPlannerTests
{
    private static readonly HardwareProfile NvidiaHybrid = new(
        "linux",
        [new GpuInfo("nvidia", "nvidia (10DE:28E0)", true, true)],
        "intel",
        true);

    private static readonly HardwareProfile AmdHybrid = new(
        "linux",
        [
            new GpuInfo("amd", "amd (1002:73FF)", true, true),
            new GpuInfo("intel", "intel (8086:A788)", false, true),
        ],
        "intel",
        true);

    [Fact]
    public void Disabled_optimization_adds_nothing()
    {
        var plan = LaunchOptimizationPlanner.Build(
            Settings(optimization: false),
            Memory(4096, 32768),
            NvidiaHybrid,
            cores: 16);

        Assert.True(plan.IsEmpty);
        Assert.Empty(plan.JvmArgs);
        Assert.Empty(plan.Environment);
    }

    [Fact]
    public void A_small_heap_does_not_get_the_tuned_flags()
    {
        var plan = LaunchOptimizationPlanner.Build(
            Settings(),
            Memory(2048, 32768),
            hardware: null,
            cores: 8);

        Assert.DoesNotContain("-XX:+UseG1GC", plan.JvmArgs);
        Assert.DoesNotContain("-XX:+AlwaysPreTouch", plan.JvmArgs);
    }

    [Fact]
    public void A_large_heap_gets_tuned_g1_flags_scaled_to_the_machine()
    {
        var plan = LaunchOptimizationPlanner.Build(
            Settings(),
            Memory(4096, 32768),
            hardware: null,
            cores: 16);

        Assert.Contains("-XX:+UseG1GC", plan.JvmArgs);
        Assert.Contains("-XX:G1HeapRegionSize=8M", plan.JvmArgs);
        Assert.Contains("-XX:+ParallelRefProcEnabled", plan.JvmArgs);
        // 8 + (16 - 8) * 5 / 8 = 13 GC threads, a quarter of them concurrent.
        Assert.Contains("-XX:ParallelGCThreads=13", plan.JvmArgs);
        Assert.Contains("-XX:ConcGCThreads=3", plan.JvmArgs);
        // 4 GB heap on a 32 GB machine: pre-touch is affordable.
        Assert.Contains("-XX:+AlwaysPreTouch", plan.JvmArgs);
        Assert.Contains("g1-tuning", plan.Notes);
    }

    [Fact]
    public void Pretouch_is_skipped_when_the_machine_cannot_afford_it()
    {
        var plan = LaunchOptimizationPlanner.Build(
            Settings(),
            Memory(4096, 6144),
            hardware: null,
            cores: 8);

        Assert.DoesNotContain("-XX:+AlwaysPreTouch", plan.JvmArgs);
    }

    [Fact]
    public void A_user_selected_collector_is_not_overridden()
    {
        var plan = LaunchOptimizationPlanner.Build(
            Settings(gcMode: "zgc"),
            Memory(8192, 32768),
            hardware: null,
            cores: 8);

        Assert.DoesNotContain("-XX:+UseG1GC", plan.JvmArgs);
        Assert.Empty(plan.JvmArgs);
    }

    [Fact]
    public void Nvidia_hybrid_gets_prime_offload()
    {
        var plan = LaunchOptimizationPlanner.Build(
            Settings(),
            Memory(4096, 32768),
            NvidiaHybrid,
            cores: 8);

        Assert.Equal("1", plan.Environment["__NV_PRIME_RENDER_OFFLOAD"]);
        Assert.Equal("nvidia", plan.Environment["__GLX_VENDOR_LIBRARY_NAME"]);
        Assert.Contains("prime-offload", plan.Notes);
    }

    [Fact]
    public void A_card_without_its_driver_is_ignored()
    {
        var nouveau = new HardwareProfile(
            "linux",
            [new GpuInfo("nvidia", "nvidia (10DE:28E0)", true, DriverLoaded: false)],
            "intel",
            true);

        var plan = LaunchOptimizationPlanner.Build(
            Settings(),
            Memory(4096, 32768),
            nouveau,
            cores: 8);

        Assert.DoesNotContain("__NV_PRIME_RENDER_OFFLOAD", plan.Environment.Keys);
        Assert.DoesNotContain("prime-offload", plan.Notes);
    }

    [Fact]
    public void Amd_hybrid_uses_dri_prime()
    {
        var plan = LaunchOptimizationPlanner.Build(
            Settings(),
            Memory(4096, 32768),
            AmdHybrid,
            cores: 8);

        Assert.Equal("1", plan.Environment["DRI_PRIME"]);
        Assert.Contains("dri-prime", plan.Notes);
    }

    [Fact]
    public void Windows_uses_the_gpu_preference_note_instead_of_environment()
    {
        var windows = new HardwareProfile(
            "windows",
            [new GpuInfo("nvidia", "NVIDIA GeForce RTX 4060", true, true)],
            "intel",
            true);

        var plan = LaunchOptimizationPlanner.Build(
            Settings(),
            Memory(4096, 32768),
            windows,
            cores: 8);

        Assert.Empty(plan.Environment);
        Assert.Contains("windows-gpu-preference", plan.Notes);
    }

    [Fact]
    public void The_hardware_payload_is_parsed_from_the_core_shape()
    {
        using var document = System.Text.Json.JsonDocument.Parse(
            """
            {"platform":"linux","primaryVendor":"intel","hybrid":true,
             "gpus":[{"vendor":"nvidia","name":"nvidia (10DE:28E0)",
                      "discrete":true,"driverLoaded":true}]}
            """);
        var profile = HardwareProfile.TryParse(document.RootElement);

        Assert.NotNull(profile);
        Assert.Equal("linux", profile!.Platform);
        Assert.True(profile.Hybrid);
        Assert.Equal("intel", profile.PrimaryVendor);
        Assert.Single(profile.Gpus);
        Assert.True(profile.Gpus[0].Discrete);
        Assert.True(profile.Gpus[0].DriverLoaded);
        Assert.NotNull(profile.Find("nvidia"));
    }

    private static AppSettings Settings(
        bool optimization = true,
        string gcMode = "auto") =>
        new(
            InstancesRoot: Path.GetTempPath(),
            DataRoot: Path.GetTempPath(),
            SettingsPath: string.Empty,
            GcMode: gcMode,
            GameOptimization: optimization);

    private static LaunchMemoryPlan Memory(int maxMb, long totalMb) =>
        new(
            Mode: "auto",
            MinMb: 1024,
            MaxMb: maxMb,
            RecommendedMb: maxMb,
            SystemTotalMb: totalMb,
            SystemAvailableMb: totalMb / 2,
            ModCount: 0,
            WarningCode: null,
            WarningMessage: null);
}
