using System.Collections.ObjectModel;
using System.Runtime.InteropServices;

namespace Archoera.Domain.Mojang;

/// <summary>Operating system names used by Mojang rule objects.</summary>
public enum MojangSystemOs
{
    Linux,
    Windows,
    MacOs,
}

/// <summary>Architecture values used by Mojang rule objects.</summary>
public enum MojangSystemArch
{
    X86,
    X64,
    Arm64,
    Arm,
}

/// <summary>
/// Platform description used to evaluate version/library/argument rules. It is
/// deliberately independent from the host OS so a plan can be produced for
/// another platform (for diagnostics or a remote install).
/// </summary>
public sealed record MojangPlatform
{
    private static readonly IReadOnlyDictionary<string, bool> NoFeatures =
        new ReadOnlyDictionary<string, bool>(
            new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase));

    /// <summary>Target operating system.</summary>
    public MojangSystemOs Os { get; init; } = MojangSystemOs.Linux;

    /// <summary>Target architecture.</summary>
    public MojangSystemArch Arch { get; init; } = MojangSystemArch.X64;

    /// <summary>
    /// Host kernel version when a rule uses <c>os.version</c>. Optional because
    /// most rules do not inspect it and tests should not depend on the runner.
    /// </summary>
    public string? OsVersion { get; init; }

    /// <summary>
    /// Launch-time feature flags (<c>has_custom_resolution</c>,
    /// <c>is_demo_user</c>, ...). Missing entries evaluate to <see langword="false"/>.
    /// </summary>
    public IReadOnlyDictionary<string, bool> Features { get; init; } = NoFeatures;

    /// <summary>Mojang's wire name: <c>linux</c>, <c>windows</c> or <c>osx</c>.</summary>
    public string OsName => Os switch
    {
        MojangSystemOs.Linux => "linux",
        MojangSystemOs.Windows => "windows",
        MojangSystemOs.MacOs => "osx",
        _ => throw new ArgumentOutOfRangeException(nameof(Os)),
    };

    /// <summary>Mojang's wire name: <c>x86</c>, <c>x86_64</c>, <c>arm64</c> or <c>arm</c>.</summary>
    public string ArchName => Arch switch
    {
        MojangSystemArch.X86 => "x86",
        MojangSystemArch.X64 => "x86_64",
        MojangSystemArch.Arm64 => "arm64",
        MojangSystemArch.Arm => "arm",
        _ => throw new ArgumentOutOfRangeException(nameof(Arch)),
    };

    /// <summary>
    /// Value substituted for <c>${arch}</c> in legacy <c>natives</c> maps
    /// (32/64). Mojang only ever used those two values.
    /// </summary>
    public string NativeArchBits => Arch is MojangSystemArch.X64 or MojangSystemArch.Arm64
        ? "64"
        : "32";

    /// <summary>Linux x86_64, the most common CI/desktop target.</summary>
    public static MojangPlatform LinuxX64 => new()
    {
        Os = MojangSystemOs.Linux,
        Arch = MojangSystemArch.X64,
    };

    /// <summary>Windows x86_64.</summary>
    public static MojangPlatform WindowsX64 => new()
    {
        Os = MojangSystemOs.Windows,
        Arch = MojangSystemArch.X64,
    };

    /// <summary>macOS x86_64.</summary>
    public static MojangPlatform MacOsX64 => new()
    {
        Os = MojangSystemOs.MacOs,
        Arch = MojangSystemArch.X64,
    };

    /// <summary>macOS arm64 (Apple Silicon).</summary>
    public static MojangPlatform MacOsArm64 => new()
    {
        Os = MojangSystemOs.MacOs,
        Arch = MojangSystemArch.Arm64,
    };

    /// <summary>Creates a platform, copying the feature map defensively.</summary>
    public static MojangPlatform Create(
        MojangSystemOs os,
        MojangSystemArch arch,
        string? osVersion = null,
        IReadOnlyDictionary<string, bool>? features = null)
        => new()
        {
            Os = os,
            Arch = arch,
            OsVersion = osVersion,
            Features = CopyFeatures(features),
        };

    /// <summary>True when <paramref name="name"/> is enabled for this launch.</summary>
    public bool HasFeature(string name)
        => Features.TryGetValue(name, out var value) && value;

    /// <summary>
    /// Maps the current runtime. FreeBSD (and other Unix hosts) use the Linux
    /// rules because that is what Minecraft's launcher does.
    /// </summary>
    public static MojangPlatform Current()
    {
        var os = OperatingSystem.IsWindows()
            ? MojangSystemOs.Windows
            : OperatingSystem.IsMacOS()
                ? MojangSystemOs.MacOs
                : MojangSystemOs.Linux;

        var arch = RuntimeInformation.OSArchitecture switch
        {
            Architecture.X86 => MojangSystemArch.X86,
            Architecture.X64 => MojangSystemArch.X64,
            Architecture.Arm64 => MojangSystemArch.Arm64,
            Architecture.Arm => MojangSystemArch.Arm,
            _ => throw new PlatformNotSupportedException(
                $"unsupported OS architecture {RuntimeInformation.OSArchitecture}"),
        };

        return Create(os, arch);
    }

    private static IReadOnlyDictionary<string, bool> CopyFeatures(
        IReadOnlyDictionary<string, bool>? features)
    {
        if (features is null || features.Count == 0)
        {
            return NoFeatures;
        }

        return new ReadOnlyDictionary<string, bool>(
            new Dictionary<string, bool>(features, StringComparer.OrdinalIgnoreCase));
    }
}

/// <summary>Well-known feature keys used by Minecraft version JSON.</summary>
public static class MojangFeatures
{
    public const string HasCustomResolution = "has_custom_resolution";
    public const string IsDemoUser = "is_demo_user";
    public const string HasQuickPlaysSupport = "has_quick_plays_support";
    public const string IsQuickPlaySingleplayer = "is_quick_play_singleplayer";
    public const string IsQuickPlayMultiplayer = "is_quick_play_multiplayer";
    public const string IsQuickPlayRealms = "is_quick_play_realms";
}
