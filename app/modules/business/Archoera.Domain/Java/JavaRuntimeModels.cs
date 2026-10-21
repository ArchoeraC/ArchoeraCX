using System.Collections.ObjectModel;
using Archoera.Domain.Downloads;
using Archoera.Domain.Mojang;

namespace Archoera.Domain.Java;

/// <summary>
/// One download object (<c>raw</c> or <c>lzma</c>) inside a Mojang runtime
/// manifest. The catalog and manifest parsers keep absent fields as
/// <see langword="null"/> so the plan builder can report
/// <c>MISSING_HASH</c>/<c>MISSING_SIZE</c> instead of guessing.
/// </summary>
/// <param name="Url">Absolute HTTPS URL.</param>
/// <param name="Sha1">Published SHA-1, lower-case hex, when present.</param>
/// <param name="Size">Published size in bytes, when present.</param>
public sealed record JavaRuntimeDownloadRef(string Url, string? Sha1, long? Size);

/// <summary>Version metadata attached to one runtime catalog entry.</summary>
/// <param name="Name">Runtime version, for example <c>21.0.7</c>.</param>
/// <param name="Released">Publication timestamp, when the catalog provides one.</param>
public sealed record JavaRuntimeVersion(string Name, DateTimeOffset? Released);

/// <summary>
/// One entry under <c>all.json[platform][component][]</c>.
/// </summary>
/// <param name="PlatformKey">Top-level catalog platform key, for example <c>linux</c>.</param>
/// <param name="Name">Component name, for example <c>java-runtime-delta</c>.</param>
/// <param name="Availability">
/// Mojang availability string (<c>available</c>, ...). Missing/empty is treated
/// as available.
/// </param>
/// <param name="Manifest">Reference to the platform runtime manifest.</param>
/// <param name="Version">Runtime version metadata.</param>
public sealed record JavaRuntimeComponent(
    string PlatformKey,
    string Name,
    string? Availability,
    JavaRuntimeDownloadRef Manifest,
    JavaRuntimeVersion Version)
{
    /// <summary>True when the entry is not explicitly marked unavailable.</summary>
    public bool IsAvailable
        => string.IsNullOrWhiteSpace(Availability)
            || string.Equals(Availability.Trim(), "available", StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// The parsed <c>all.json</c> document: platform key → component → versions.
/// Empty component arrays (notably under <c>gamecore</c>) are preserved as
/// "no entries", never as "the whole catalog is unavailable".
/// </summary>
public sealed record JavaRuntimeCatalog
{
    private static readonly IReadOnlyDictionary<string, IReadOnlyList<JavaRuntimeComponent>>
        NoPlatforms = new ReadOnlyDictionary<string, IReadOnlyList<JavaRuntimeComponent>>(
            new Dictionary<string, IReadOnlyList<JavaRuntimeComponent>>(StringComparer.Ordinal));

    /// <summary>An empty catalog (useful for default values and tests).</summary>
    public static JavaRuntimeCatalog Empty { get; } = new(NoPlatforms);

    /// <summary>Copies the platform map defensively.</summary>
    public JavaRuntimeCatalog(
        IReadOnlyDictionary<string, IReadOnlyList<JavaRuntimeComponent>> platforms)
    {
        ArgumentNullException.ThrowIfNull(platforms);

        var copy = new Dictionary<string, IReadOnlyList<JavaRuntimeComponent>>(
            StringComparer.Ordinal);

        foreach (var (platformKey, components) in platforms)
        {
            if (string.IsNullOrWhiteSpace(platformKey))
            {
                continue;
            }

            copy[platformKey] = components is null
                ? []
                : Array.AsReadOnly(components.Where(static item => item is not null).ToArray());
        }

        Platforms = new ReadOnlyDictionary<string, IReadOnlyList<JavaRuntimeComponent>>(copy);
    }

    /// <summary>Platform key → all catalog entries for that platform.</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<JavaRuntimeComponent>> Platforms { get; }

    /// <summary>True when <paramref name="platformKey"/> is present (even with empty arrays).</summary>
    public bool HasPlatform(string? platformKey)
        => !string.IsNullOrWhiteSpace(platformKey)
            && Platforms.ContainsKey(platformKey.Trim());

    /// <summary>Entries for a platform; empty when the platform is absent.</summary>
    public IReadOnlyList<JavaRuntimeComponent> GetComponents(string? platformKey)
    {
        if (string.IsNullOrWhiteSpace(platformKey))
        {
            return [];
        }

        return Platforms.TryGetValue(platformKey.Trim(), out var components)
            ? components
            : [];
    }

    /// <summary>Distinct component names present for a platform, ordinal-sorted.</summary>
    public IReadOnlyList<string> GetComponentNames(string? platformKey)
        => GetComponents(platformKey)
            .Select(static component => component.Name)
            .Where(static name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.Ordinal)
            .ToArray();

    /// <summary>
    /// Selects one component deterministically. Entries explicitly marked
    /// unavailable are used only when no available entry exists; remaining
    /// candidates are ordered by <c>version.released</c> (newest first), then
    /// by version name (ordinal, descending), then by manifest URL (ordinal,
    /// ascending) so ties never depend on JSON property order.
    /// </summary>
    /// <returns><see langword="null"/> when the platform/component is absent.</returns>
    public JavaRuntimeComponent? Select(string? platformKey, string? componentName)
    {
        if (string.IsNullOrWhiteSpace(componentName))
        {
            return null;
        }

        var candidates = GetComponents(platformKey)
            .Where(component => string.Equals(
                component.Name,
                componentName.Trim(),
                StringComparison.OrdinalIgnoreCase))
            .ToArray();

        if (candidates.Length == 0)
        {
            return null;
        }

        var preferred = candidates.Where(static component => component.IsAvailable).ToArray();
        var pool = preferred.Length > 0 ? preferred : candidates;

        return pool
            .OrderByDescending(static component =>
                component.Version?.Released ?? DateTimeOffset.MinValue)
            .ThenByDescending(
                static component => component.Version?.Name ?? string.Empty,
                StringComparer.Ordinal)
            .ThenBy(
                static component => component.Manifest?.Url ?? string.Empty,
                StringComparer.Ordinal)
            .First();
    }
}

/// <summary>
/// One <c>files[]</c> entry in a Mojang runtime manifest.
/// </summary>
/// <param name="Path">Manifest-relative path using <c>/</c> separators.</param>
/// <param name="Type"><c>file</c>, <c>directory</c> or <c>link</c>.</param>
/// <param name="Executable">Whether the committed file needs the Unix execute bit.</param>
/// <param name="Raw">Uncompressed download, preferred by this slice.</param>
/// <param name="Lzma">LZMA-compressed download; never unpacked by C#/Rust here.</param>
public sealed record JavaRuntimeFileEntry(
    string Path,
    string Type,
    bool Executable,
    JavaRuntimeDownloadRef? Raw,
    JavaRuntimeDownloadRef? Lzma)
{
    /// <summary>True for <c>type=file</c> (case-insensitive).</summary>
    public bool IsFile => string.Equals(Type, "file", StringComparison.OrdinalIgnoreCase);

    /// <summary>True for <c>type=directory</c> (case-insensitive).</summary>
    public bool IsDirectory
        => string.Equals(Type, "directory", StringComparison.OrdinalIgnoreCase);

    /// <summary>True for <c>type=link</c> (case-insensitive).</summary>
    public bool IsLink => string.Equals(Type, "link", StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// A parsed platform runtime manifest (<c>files</c> map). The catalog entry
/// SHA-1/size verifies the raw document; this model only represents its
/// contents.
/// </summary>
public sealed record JavaRuntimeManifest
{
    private static readonly IReadOnlyDictionary<string, JavaRuntimeFileEntry> NoFiles =
        new ReadOnlyDictionary<string, JavaRuntimeFileEntry>(
            new Dictionary<string, JavaRuntimeFileEntry>(StringComparer.Ordinal));

    /// <summary>An empty manifest (useful for default values and tests).</summary>
    public static JavaRuntimeManifest Empty { get; } = new(NoFiles);

    /// <summary>Copies the file map defensively.</summary>
    public JavaRuntimeManifest(IReadOnlyDictionary<string, JavaRuntimeFileEntry> files)
    {
        ArgumentNullException.ThrowIfNull(files);

        var copy = new Dictionary<string, JavaRuntimeFileEntry>(StringComparer.Ordinal);
        foreach (var (path, entry) in files)
        {
            if (entry is not null)
            {
                copy[path] = entry;
            }
        }

        Files = new ReadOnlyDictionary<string, JavaRuntimeFileEntry>(copy);
    }

    /// <summary>Manifest-relative path → entry.</summary>
    public IReadOnlyDictionary<string, JavaRuntimeFileEntry> Files { get; }
}

/// <summary>
/// Mapping from <see cref="MojangPlatform"/> to the top-level <c>all.json</c>
/// platform key. The names are Mojang launcher names, not Java <c>os.name</c>
/// values.
/// </summary>
public static class JavaRuntimePlatform
{
    public const string Linux = "linux";
    public const string LinuxI386 = "linux-i386";
    public const string MacOs = "mac-os";
    public const string MacOsArm64 = "mac-os-arm64";
    public const string WindowsX64 = "windows-x64";
    public const string WindowsX86 = "windows-x86";
    public const string WindowsArm64 = "windows-arm64";
    public const string GameCore = "gamecore";

    /// <summary>
    /// Resolves the catalog key. Linux ARM/ARM64, macOS x86 and Windows ARM32
    /// have no Mojang runtime platform and fail with
    /// <c>UNSUPPORTED_PLATFORM</c>.
    /// </summary>
    /// <exception cref="JavaRuntimeException"><c>UNSUPPORTED_PLATFORM</c>.</exception>
    public static string FromMojang(MojangPlatform platform)
    {
        ArgumentNullException.ThrowIfNull(platform);

        if (TryFromMojang(platform, out var key))
        {
            return key;
        }

        throw new JavaRuntimeException(
            JavaRuntimeErrorCode.UnsupportedPlatform,
            $"no Mojang Java runtime platform for {platform.OsName}/{platform.ArchName}");
    }

    /// <summary>Non-throwing variant of <see cref="FromMojang"/>.</summary>
    public static bool TryFromMojang(MojangPlatform? platform, out string key)
    {
        key = string.Empty;
        if (platform is null)
        {
            return false;
        }

        key = (platform.Os, platform.Arch) switch
        {
            (MojangSystemOs.Linux, MojangSystemArch.X64) => Linux,
            (MojangSystemOs.Linux, MojangSystemArch.X86) => LinuxI386,
            (MojangSystemOs.Windows, MojangSystemArch.X64) => WindowsX64,
            (MojangSystemOs.Windows, MojangSystemArch.X86) => WindowsX86,
            (MojangSystemOs.Windows, MojangSystemArch.Arm64) => WindowsArm64,
            (MojangSystemOs.MacOs, MojangSystemArch.X64) => MacOs,
            (MojangSystemOs.MacOs, MojangSystemArch.Arm64) => MacOsArm64,
            _ => string.Empty,
        };

        return key.Length > 0;
    }
}

/// <summary>
/// Runtime component selected for a Java major version.
/// </summary>
/// <param name="MajorVersion">Requested feature release, for example <c>21</c>.</param>
/// <param name="Component">Mojang component name, for example <c>java-runtime-delta</c>.</param>
/// <param name="IsDefault">True when <see cref="Component"/> came from the built-in table.</param>
public sealed record JavaRuntimeSelection(int MajorVersion, string Component, bool IsDefault)
{
    /// <summary>
    /// Default component table used by the official launcher:
    /// 8 → <c>jre-legacy</c>, 17 → <c>java-runtime-gamma</c>,
    /// 21 → <c>java-runtime-delta</c>, 25 → <c>java-runtime-epsilon</c>.
    /// Any other major requires an explicit component.
    /// </summary>
    /// <exception cref="JavaRuntimeException">
    /// <c>INVALID_REQUEST</c> for a non-positive major or unsafe component, or
    /// <c>UNSUPPORTED_JAVA_VERSION</c> when no explicit component is supplied
    /// for another major.
    /// </exception>
    public static JavaRuntimeSelection ForMajor(int majorVersion, string? component = null)
    {
        if (majorVersion <= 0)
        {
            throw new JavaRuntimeException(
                JavaRuntimeErrorCode.InvalidRequest,
                $"java majorVersion must be positive, got {majorVersion}");
        }

        if (!string.IsNullOrWhiteSpace(component))
        {
            var trimmed = component.Trim();
            if (!DownloadPlanPaths.IsPlainFileName(trimmed))
            {
                throw new JavaRuntimeException(
                    JavaRuntimeErrorCode.InvalidRequest,
                    $"runtime component '{component}' is not a plain name");
            }

            return new JavaRuntimeSelection(majorVersion, trimmed, IsDefault: false);
        }

        var fallback = majorVersion switch
        {
            8 => "jre-legacy",
            17 => "java-runtime-gamma",
            21 => "java-runtime-delta",
            25 => "java-runtime-epsilon",
            _ => null,
        };

        if (fallback is null)
        {
            throw new JavaRuntimeException(
                JavaRuntimeErrorCode.UnsupportedJavaVersion,
                $"Java {majorVersion} has no default Mojang runtime component; " +
                "pass an explicit component");
        }

        return new JavaRuntimeSelection(majorVersion, fallback, IsDefault: true);
    }
}
