using System.Text.Json;
using System.Text.RegularExpressions;
using Archoera.Application.JavaRuntime;

namespace Archoera.Infrastructure.Java.Distributions;

/// <summary>
/// SAP SapMachine JDK archives. SAP publishes GA binaries as GitHub release
/// assets with matching <c>.sha256.txt</c> files; EA assets are excluded.
/// </summary>
internal sealed partial class SapMachineJavaDistributionProvider : JavaDistributionProviderBase
{
    public const string ProviderId = "sapmachine";

    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(10);

    private readonly Uri _api;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<string, CacheEntry> _cache = new(StringComparer.Ordinal);

    public SapMachineJavaDistributionProvider(HttpClient http, Uri? api = null)
        : base(http)
    {
        _api = api ?? new Uri("https://api.github.com/repos/SAP/SapMachine/");
    }

    public override string Id => ProviderId;
    public override string DisplayName => "SAP SapMachine";
    public override string Vendor => "SAP";
    public override string Homepage => "https://sap.github.io/SapMachine/";
    public override bool ListsAllVersions => true;

    public override async Task<IReadOnlyList<int>> GetMajorsAsync(
        JavaDistributionPlatform platform,
        CancellationToken cancellationToken)
    {
        var releases = await LoadAsync(platform, cancellationToken).ConfigureAwait(false);
        return releases
            .Select(release => release.Major)
            .Distinct()
            .OrderBy(major => major)
            .ToArray();
    }

    public override async Task<IReadOnlyList<JavaDistributionRelease>> GetReleasesAsync(
        int major,
        JavaDistributionPlatform platform,
        CancellationToken cancellationToken)
    {
        var releases = await LoadAsync(platform, cancellationToken).ConfigureAwait(false);
        return FinalizeReleases(
            releases
                .Where(release => release.Major == major)
                .Select(release => new JavaDistributionRelease(
                    release.VersionId,
                    release.Major,
                    release.Version,
                    platform.ArchiveType,
                    release.Size,
                    null,
                    IsLts(release.Major),
                    false,
                    release.ReleasedAt)),
            major);
    }

    public override async Task<JavaDistributionArchive> ResolveAsync(
        string versionId,
        JavaDistributionPlatform platform,
        CancellationToken cancellationToken)
    {
        var separator = versionId.IndexOf('|');
        if (separator <= 0 || separator == versionId.Length - 1)
        {
            throw new JavaDistributionException(
                "JAVA_VERSION_UNKNOWN",
                "unknown SapMachine release id");
        }

        var tag = versionId[..separator];
        var assetName = versionId[(separator + 1)..];
        if (!TagPattern().IsMatch(tag) || !ArtifactPattern(platform).IsMatch(assetName))
        {
            throw new JavaDistributionException(
                "JAVA_VERSION_UNKNOWN",
                "unknown SapMachine release id");
        }

        var uri = new Uri(_api, $"releases/tags/{tag}");
        using var document = await JavaDistributionHttp
            .GetJsonAsync(Http, uri, cancellationToken)
            .ConfigureAwait(false);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty("assets", out var assets) ||
            assets.ValueKind != JsonValueKind.Array)
        {
            throw new JavaDistributionException(
                "JAVA_VERSION_UNKNOWN",
                $"SapMachine release '{tag}' is no longer available");
        }

        JsonElement? archiveAsset = null;
        JsonElement? hashAsset = null;
        var hashName = HashAssetName(assetName);
        foreach (var asset in assets.EnumerateArray())
        {
            var name = JsonString(asset, "name");
            if (string.Equals(name, assetName, StringComparison.Ordinal))
            {
                archiveAsset = asset;
            }
            else if (string.Equals(name, hashName, StringComparison.Ordinal))
            {
                hashAsset = asset;
            }
        }

        if (archiveAsset is null || hashAsset is null)
        {
            throw new JavaDistributionException(
                "JAVA_VERSION_UNKNOWN",
                $"SapMachine release '{tag}' no longer publishes {assetName}");
        }

        var parsed = ParseAsset(assetName) ??
            throw new JavaDistributionException(
                "JAVA_CATALOGUE_INVALID",
                $"SapMachine asset '{assetName}' has no parseable version");
        var hashUri = JavaDistributionHttp.RequireAllowedHost(
            JsonString(hashAsset.Value, "browser_download_url"),
            "SapMachine checksum",
            "github.com");
        var hash = await JavaDistributionHttp
            .GetSha256Async(Http, hashUri, cancellationToken)
            .ConfigureAwait(false);
        var archiveUrl = JavaDistributionHttp.RequireAllowedHost(
            JsonString(archiveAsset.Value, "browser_download_url"),
            "SapMachine archive",
            "github.com").AbsoluteUri;

        return BuildArchive(
            versionId,
            parsed.Version,
            parsed.Major,
            archiveUrl,
            hash,
            JsonLong(archiveAsset.Value, "size"),
            platform.ArchiveType,
            platform);
    }

    private async Task<IReadOnlyList<SapRelease>> LoadAsync(
        JavaDistributionPlatform platform,
        CancellationToken cancellationToken)
    {
        var key = $"{platform.Os}-{platform.Arch}";
        if (TryGet(key, out var cached)) return cached;

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (TryGet(key, out cached)) return cached;

            var releases = new List<SapRelease>();
            for (var page = 1; page <= 3; page++)
            {
                var uri = new Uri(_api, $"releases?per_page=100&page={page}");
                using var document = await JavaDistributionHttp
                    .GetJsonAsync(Http, uri, cancellationToken)
                    .ConfigureAwait(false);
                if (document.RootElement.ValueKind != JsonValueKind.Array)
                {
                    throw new JavaDistributionException(
                        "JAVA_CATALOGUE_INVALID",
                        "GitHub returned an unexpected SapMachine release document");
                }

                var count = 0;
                foreach (var item in document.RootElement.EnumerateArray())
                {
                    count++;
                    var tag = JsonString(item, "tag_name");
                    var releasedAt = JsonString(item, "published_at");
                    if (string.IsNullOrWhiteSpace(tag) ||
                        !item.TryGetProperty("assets", out var assets) ||
                        assets.ValueKind != JsonValueKind.Array)
                    {
                        continue;
                    }

                    foreach (var asset in assets.EnumerateArray())
                    {
                        var name = JsonString(asset, "name");
                        if (name is null || !ArtifactPattern(platform).IsMatch(name))
                        {
                            continue;
                        }

                        var parsed = ParseAsset(name);
                        var size = JsonLong(asset, "size");
                        if (parsed is null ||
                            parsed.Value.Major is < 8 or > 99 ||
                            size <= 0)
                        {
                            continue;
                        }

                        releases.Add(new SapRelease(
                            $"{tag}|{name}",
                            parsed.Value.Major,
                            parsed.Value.Version,
                            size,
                            releasedAt));
                    }
                }

                if (count < 100) break;
            }

            var result = releases
                .GroupBy(release => release.VersionId, StringComparer.Ordinal)
                .Select(group => group.First())
                .OrderByDescending(release => VersionKey(release.Version))
                .ToArray();
            _cache[key] = new CacheEntry(DateTimeOffset.UtcNow.Add(CacheTtl), result);
            return result;
        }
        finally
        {
            _gate.Release();
        }
    }

    private bool TryGet(string key, out IReadOnlyList<SapRelease> releases)
    {
        if (_cache.TryGetValue(key, out var entry) &&
            entry.Expires > DateTimeOffset.UtcNow)
        {
            releases = entry.Releases;
            return true;
        }

        releases = [];
        return false;
    }

    private static (int Major, string Version)? ParseAsset(string name)
    {
        var match = ArtifactPatternForParse().Match(name);
        if (!match.Success) return null;
        var version = match.Groups["version"].Value;
        var head = version.Split('.', 2)[0];
        return int.TryParse(head, out var major)
            ? (major, version)
            : null;
    }

    private static Regex ArtifactPattern(JavaDistributionPlatform platform)
        => Platforms.GetOrAdd(platform, CreateArtifactPattern);

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<
        JavaDistributionPlatform,
        Regex> Platforms = new();

    private static Regex CreateArtifactPattern(JavaDistributionPlatform platform)
    {
        var suffix = platform.Os switch
        {
            "windows" => $"_windows-{platform.Arch}_bin.zip",
            "macos" => $"_macos-{platform.Arch}_bin.tar.gz",
            _ => $"_linux-{platform.Arch}_bin.tar.gz",
        };
        return new Regex(
            $@"^sapmachine-jdk-(?<major>\d+)(?:\.\d+)+" +
            Regex.Escape(suffix) + "$",
            RegexOptions.CultureInvariant);
    }

    private static string HashAssetName(string assetName)
    {
        var extension = assetName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)
            ? ".zip"
            : ".tar.gz";
        return assetName[..^extension.Length] + ".sha256.txt";
    }

    [GeneratedRegex(
        @"^sapmachine-jdk-(?<version>\d+(?:\.\d+)+)_",
        RegexOptions.CultureInvariant)]
    private static partial Regex ArtifactPatternForParse();

    [GeneratedRegex("^[A-Za-z0-9._-]+$")]
    private static partial Regex TagPattern();

    private static string? JsonString(JsonElement element, string name)
        => element.ValueKind == JsonValueKind.Object &&
           element.TryGetProperty(name, out var value) &&
           value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static long JsonLong(JsonElement element, string name)
        => element.ValueKind == JsonValueKind.Object &&
           element.TryGetProperty(name, out var value) &&
           value.ValueKind == JsonValueKind.Number &&
           value.TryGetInt64(out var number)
            ? number
            : 0;

    private sealed record SapRelease(
        string VersionId,
        int Major,
        string Version,
        long Size,
        string? ReleasedAt);

    private sealed record CacheEntry(
        DateTimeOffset Expires,
        IReadOnlyList<SapRelease> Releases);
}
