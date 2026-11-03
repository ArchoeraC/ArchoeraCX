using System.Runtime.InteropServices;
using Archoera.Domain.Downloads;

namespace Archoera.Application.JavaRuntime;

/// <summary>
/// Target capability a JDK vendor must match before it can be offered. Java
/// majors are not portable across OS/architecture, so every catalogue request
/// carries one triple instead of filtering in the UI. The installer also
/// accepts foreign triples for manual downloads after the caller confirms the
/// incompatibility.
/// </summary>
/// <param name="Os">Wire name: <c>linux</c>, <c>windows</c> or <c>macos</c>.</param>
/// <param name="Arch">Wire name: <c>x64</c> or <c>aarch64</c>.</param>
/// <param name="ArchiveType">Preferred binary archive for this host.</param>
public sealed record JavaDistributionPlatform(string Os, string Arch, string ArchiveType)
{
    private static readonly JavaDistributionPlatform[] SupportedPlatforms =
    [
        new("linux", "x64", "tar.gz"),
        new("linux", "aarch64", "tar.gz"),
        new("windows", "x64", "zip"),
        new("windows", "aarch64", "zip"),
        new("macos", "x64", "tar.gz"),
        new("macos", "aarch64", "tar.gz"),
    ];

    public bool IsWindows => string.Equals(Os, "windows", StringComparison.Ordinal);
    public bool IsMacOs => string.Equals(Os, "macos", StringComparison.Ordinal);

    /// <summary>True when this triple can run on the machine hosting the app.</summary>
    public bool IsCurrentHost
    {
        get
        {
            try
            {
                var host = Current();
                return string.Equals(Os, host.Os, StringComparison.Ordinal) &&
                       string.Equals(Arch, host.Arch, StringComparison.Ordinal);
            }
            catch (JavaDistributionException)
            {
                return false;
            }
        }
    }

    /// <summary>
    /// Targets the UI may request, including platforms that cannot run on this
    /// machine (the page warns before installing those).
    /// </summary>
    public static IReadOnlyList<JavaDistributionPlatform> Supported => SupportedPlatforms;

    /// <summary>
    /// Parses a wire triple. The archive defaults to the platform's normal
    /// format and mismatches are rejected instead of silently corrected.
    /// </summary>
    public static JavaDistributionPlatform Parse(
        string? os,
        string? arch,
        string? archive = null)
    {
        var normalizedOs = os?.Trim().ToLowerInvariant();
        var normalizedArch = arch?.Trim().ToLowerInvariant();
        var expectedArchive = normalizedOs == "windows" ? "zip" : "tar.gz";
        var normalizedArchive = string.IsNullOrWhiteSpace(archive)
            ? expectedArchive
            : archive.Trim().ToLowerInvariant();

        if (normalizedOs is not ("linux" or "windows" or "macos") ||
            normalizedArch is not ("x64" or "aarch64") ||
            string.Equals(
                normalizedArchive,
                expectedArchive,
                StringComparison.Ordinal) == false)
        {
            throw new JavaDistributionException(
                "JAVA_PLATFORM_UNSUPPORTED",
                $"unsupported Java platform {os}/{arch}/{archive}");
        }

        var platform = new JavaDistributionPlatform(
            normalizedOs,
            normalizedArch,
            normalizedArchive);
        if (!SupportedPlatforms.Contains(platform))
        {
            throw new JavaDistributionException(
                "JAVA_PLATFORM_UNSUPPORTED",
                $"unsupported Java platform {os}/{arch}/{archive}");
        }

        return platform;
    }

    /// <summary>
    /// Maps the running process to a supported catalogue target. Java 8 x86
    /// builds exist, but Minecraft has not run on 32-bit Java for years; the
    /// UI only offers what the host can actually execute.
    /// </summary>
    public static JavaDistributionPlatform Current()
    {
        var os = OperatingSystem.IsWindows()
            ? "windows"
            : OperatingSystem.IsMacOS()
                ? "macos"
                : "linux";

        var arch = RuntimeInformation.OSArchitecture switch
        {
            Architecture.X64 => "x64",
            Architecture.Arm64 => "aarch64",
            _ => throw new JavaDistributionException(
                "JAVA_PLATFORM_UNSUPPORTED",
                $"unsupported Java architecture {RuntimeInformation.OSArchitecture}"),
        };

        return new JavaDistributionPlatform(os, arch, os == "windows" ? "zip" : "tar.gz");
    }
}

/// <summary>Static identity of one registered JDK vendor.</summary>
/// <param name="Id">Stable provider id used by the API and the UI.</param>
/// <param name="Name">Full product name shown in tooltips.</param>
/// <param name="ShortName">Compact label used by provider buttons.</param>
/// <param name="Vendor">Company or project that publishes the builds.</param>
/// <param name="Homepage">Public project page for attribution.</param>
/// <param name="ListsAllVersions">
/// True when the vendor metadata exposes historical builds; false when only
/// the latest build of each major is published.
/// </param>
/// <param name="RecommendedMajor">Default major for Minecraft use.</param>
public sealed record JavaDistributionProviderInfo(
    string Id,
    string Name,
    string ShortName,
    string Vendor,
    string Homepage,
    bool ListsAllVersions,
    int RecommendedMajor);

/// <summary>One selectable JDK build. The URL/hash stay inside C#.</summary>
/// <param name="VersionId">Opaque provider-native id; never a URL.</param>
/// <param name="Major">Java feature release.</param>
/// <param name="Version">Human-readable full version.</param>
/// <param name="ArchiveType"><c>tar.gz</c> or <c>zip</c>.</param>
/// <param name="Size">Declared archive size, or zero when the list API omits it.</param>
/// <param name="Sha256">Published SHA-256, or null when it is resolved on install.</param>
/// <param name="Lts">True for a long-term-support feature release.</param>
/// <param name="Recommended">True for the build the page preselects.</param>
/// <param name="ReleasedAt">ISO-8601 date from the vendor catalogue, if any.</param>
/// <param name="LatestPatch">
/// True for the newest patch of this feature release. Older builds may contain
/// known CVEs and require an explicit opt-in before installation.
/// </param>
public sealed record JavaDistributionRelease(
    string VersionId,
    int Major,
    string Version,
    string ArchiveType,
    long Size,
    string? Sha256,
    bool Lts,
    bool Recommended,
    string? ReleasedAt,
    bool LatestPatch = false);

/// <summary>Resolved archive metadata. Only this record reaches the plan builder.</summary>
/// <param name="Provider">Provider id for diagnostics.</param>
/// <param name="Major">Java feature release.</param>
/// <param name="Version">Full version shown to the user.</param>
/// <param name="Url">HTTPS archive URL.</param>
/// <param name="HashAlgorithm">Digest algorithm published by the vendor.</param>
/// <param name="Hash">Required lower-case digest.</param>
/// <param name="Size">Exact archive size in bytes.</param>
/// <param name="ArchiveType"><c>tar.gz</c> or <c>zip</c>.</param>
public sealed record JavaDistributionArchive(
    string Provider,
    int Major,
    string Version,
    string Url,
    DownloadHashAlgorithm HashAlgorithm,
    string Hash,
    long Size,
    string ArchiveType);

/// <summary>Outcome of a completed install.</summary>
/// <param name="Provider">Provider id.</param>
/// <param name="Version">Installed version.</param>
/// <param name="Major">Java feature release.</param>
/// <param name="Home">JDK home containing <c>bin/java</c>; empty when not extracted.</param>
/// <param name="Executable">Absolute Java executable path; empty when not extracted.</param>
/// <param name="Bytes">Archive size that was downloaded.</param>
/// <param name="Archive">Kept archive path, or null when it was deleted.</param>
/// <param name="Extracted">False when the user asked to download only.</param>
public sealed record JavaDistributionInstallResult(
    string Provider,
    string Version,
    int Major,
    string Home,
    string Executable,
    long Bytes,
    string? Archive,
    bool Extracted);

/// <summary>
/// Stable, user-safe failure from a JDK catalogue or archive. Handlers map
/// <see cref="Code"/> straight into the error envelope.
/// </summary>
public sealed class JavaDistributionException(string code, string message)
    : Exception(message)
{
    public string Code { get; } = code;
}
