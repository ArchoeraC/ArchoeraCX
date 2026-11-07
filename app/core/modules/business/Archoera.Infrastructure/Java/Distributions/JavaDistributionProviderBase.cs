using Archoera.Application.JavaRuntime;
using Archoera.Domain.Downloads;

namespace Archoera.Infrastructure.Java.Distributions;

/// <summary>
/// Shared catalogue behaviour for JDK vendors. Recommendation is computed in
/// one place: the newest build of the provider's recommended major, otherwise
/// the newest LTS build, otherwise the newest build overall.
/// </summary>
internal abstract class JavaDistributionProviderBase : IJavaDistributionProvider
{
    /// <summary>Minecraft currently targets Java 21 on every supported loader.</summary>
    public const int MinecraftRecommendedMajor = 21;

    private static readonly HashSet<int> LtsMajors = [8, 11, 17, 21, 25];

    protected JavaDistributionProviderBase(HttpClient http)
    {
        Http = http;
    }

    protected HttpClient Http { get; }

    public abstract string Id { get; }
    public abstract string DisplayName { get; }
    public abstract string Vendor { get; }

    /// <summary>Short label for the provider chips; the full name stays in the tooltip.</summary>
    public virtual string ShortName => Id switch
    {
        "adoptium" => "Adoptium",
        "zulu" => "Zulu",
        "microsoft" => "Microsoft",
        "graalvm" => "GraalVM",
        "corretto" => "Corretto",
        "liberica" => "Liberica",
        "sapmachine" => "SapMachine",
        "dragonwell" => "Dragonwell",
        _ => DisplayName,
    };

    public abstract string Homepage { get; }
    public abstract bool ListsAllVersions { get; }
    public virtual int RecommendedMajor => MinecraftRecommendedMajor;

    public abstract Task<IReadOnlyList<int>> GetMajorsAsync(
        JavaDistributionPlatform platform,
        CancellationToken cancellationToken);

    public abstract Task<IReadOnlyList<JavaDistributionRelease>> GetReleasesAsync(
        int major,
        JavaDistributionPlatform platform,
        CancellationToken cancellationToken);

    public abstract Task<JavaDistributionArchive> ResolveAsync(
        string versionId,
        JavaDistributionPlatform platform,
        CancellationToken cancellationToken);

    protected static bool IsLts(int major) => LtsMajors.Contains(major);

    /// <summary>
    /// Sorts newest-first by dotted numeric version, then marks exactly one
    /// build as recommended so the page never shows two "recommended" rows.
    /// </summary>
    protected static IReadOnlyList<JavaDistributionRelease> FinalizeReleases(
        IEnumerable<JavaDistributionRelease> releases,
        int major,
        bool preserveOrder = false)
    {
        var filtered = releases
            .Where(release => release.Major == major && release.Size >= 0);
        var ordered = preserveOrder
            ? filtered.ToList()
            : filtered
                .OrderByDescending(release => VersionKey(release.Version))
                .ThenByDescending(release => release.Version, StringComparer.Ordinal)
                .ToList();

        if (ordered.Count == 0) return ordered;

        var recommendedIndex = ordered.FindIndex(release => release.Lts);
        if (recommendedIndex < 0) recommendedIndex = 0;

        for (var index = 0; index < ordered.Count; index++)
        {
            ordered[index] = ordered[index] with
            {
                Lts = IsLts(ordered[index].Major),
                Recommended = index == recommendedIndex,
                LatestPatch = index == 0,
            };
        }

        return ordered;
    }

    protected static long VersionKey(string version)
    {
        // Vendors mix dotted versions with build suffixes ("21.0.13+9",
        // "25i4-25.0.4.1.1"). Normalize separators and read the leading digits
        // of each segment so a +build is not mistaken for "not a number".
        long key = 0;
        var normalized = version;
        // The initial GA build is published as "21+35"; it predates every
        // "21.0.x" update, so give it the missing patch/update positions.
        var plus = normalized.IndexOf('+');
        if (plus > 0 && !normalized[..plus].Contains('.'))
        {
            normalized = $"{normalized[..plus]}.0.0+{normalized[(plus + 1)..]}";
        }

        normalized = normalized.Replace('+', '.').Replace('-', '.');
        var parts = normalized.Split('.', StringSplitOptions.RemoveEmptyEntries);
        // Always consume six positions: 21.0.13+9 must outrank 21.0.12.1+1
        // even though the latter has one more numeric segment.
        for (var index = 0; index < 6; index++)
        {
            var part = 0;
            if (index < parts.Length)
            {
                var digits = new string(parts[index].TakeWhile(char.IsAsciiDigit).ToArray());
                part = int.TryParse(digits, out var parsed) ? parsed : 0;
            }

            key = (key * 1000) + Math.Clamp(part, 0, 999);
        }

        return key;
    }

    protected static int ParseMajor(string? text)
        => int.TryParse(text, out var major) && major is >= 8 and <= 99 ? major : 0;

    protected static bool MatchesPlatformArchive(
        string? archiveType,
        JavaDistributionPlatform platform)
        => string.Equals(archiveType, platform.ArchiveType, StringComparison.OrdinalIgnoreCase);

    protected JavaDistributionArchive BuildArchive(
        string versionId,
        string version,
        int major,
        string? url,
        string? sha256,
        long size,
        string? archiveType,
        JavaDistributionPlatform platform,
        DownloadHashAlgorithm hashAlgorithm = DownloadHashAlgorithm.Sha256)
    {
        if (major is < 8 or > 99)
        {
            throw new JavaDistributionException(
                "JAVA_VERSION_UNKNOWN",
                $"{DisplayName} returned an unsupported Java major");
        }

        if (!MatchesPlatformArchive(archiveType, platform))
        {
            throw new JavaDistributionException(
                "JAVA_PLATFORM_UNSUPPORTED",
                $"{DisplayName} does not publish a {platform.ArchiveType} build for " +
                $"{platform.Os} {platform.Arch}");
        }

        var normalizedHash = JavaDistributionHttp.ParseHash(hashAlgorithm, sha256);
        if (normalizedHash is null)
        {
            throw new JavaDistributionException(
                "JAVA_HASH_MISSING",
                $"{DisplayName} did not publish a {hashAlgorithm} digest for {versionId}");
        }

        if (size <= 0)
        {
            throw new JavaDistributionException(
                "JAVA_SIZE_MISSING",
                $"{DisplayName} did not publish the archive size for {versionId}");
        }

        return new JavaDistributionArchive(
            Id,
            major,
            version,
            JavaDistributionHttp.RequireHttps(url, $"{DisplayName} archive").AbsoluteUri,
            hashAlgorithm,
            normalizedHash,
            size,
            archiveType!.ToLowerInvariant());
    }
}
