using System.Text.Json;
using Archoera.Application.JavaRuntime;
using Archoera.Domain.Downloads;

namespace Archoera.Infrastructure.Java.Distributions;

/// <summary>
/// BellSoft Liberica JDK. The discovery API returns every GA build with size
/// and SHA-1; the archive itself is hosted on GitHub releases.
/// </summary>
internal sealed class LibericaJavaDistributionProvider : JavaDistributionProviderBase
{
    public const string ProviderId = "liberica";

    private static readonly Uri Api = new("https://api.bell-sw.com/v1/liberica/");

    public LibericaJavaDistributionProvider(HttpClient http)
        : base(http)
    {
    }

    public override string Id => ProviderId;
    public override string DisplayName => "BellSoft Liberica JDK";
    public override string Vendor => "BellSoft";
    public override string Homepage => "https://bell-sw.com/libericajdk/";
    public override bool ListsAllVersions => true;

    public override async Task<IReadOnlyList<int>> GetMajorsAsync(
        JavaDistributionPlatform platform,
        CancellationToken cancellationToken)
    {
        using var document = await JavaDistributionHttp
            .GetJsonAsync(Http, new Uri(Api, "versions"), cancellationToken)
            .ConfigureAwait(false);
        if (document.RootElement.ValueKind != JsonValueKind.Array)
        {
            throw new JavaDistributionException(
                "JAVA_CATALOGUE_INVALID",
                "Liberica returned an unexpected versions document");
        }

        var majors = new SortedSet<int>();
        foreach (var item in document.RootElement.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object ||
                !Bool(item, "GA") ||
                !item.TryGetProperty("featureVersion", out var feature) ||
                feature.ValueKind != JsonValueKind.Number ||
                !feature.TryGetInt32(out var major) ||
                major is < 8 or > 99)
            {
                continue;
            }

            majors.Add(major);
        }

        return majors.ToArray();
    }

    public override async Task<IReadOnlyList<JavaDistributionRelease>> GetReleasesAsync(
        int major,
        JavaDistributionPlatform platform,
        CancellationToken cancellationToken)
    {
        var uri = ReleasesUri(platform, major, version: null);
        using var document = await JavaDistributionHttp
            .GetJsonAsync(Http, uri, cancellationToken)
            .ConfigureAwait(false);
        if (document.RootElement.ValueKind != JsonValueKind.Array)
        {
            throw new JavaDistributionException(
                "JAVA_CATALOGUE_INVALID",
                "Liberica returned an unexpected releases document");
        }

        var items = new List<(JsonElement Item, string Version, bool Latest)>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in document.RootElement.EnumerateArray())
        {
            if (!IsUsable(item, platform)) continue;
            var version = String(item, "version");
            if (version is null || !seen.Add(version)) continue;
            items.Add((item, version, Bool(item, "latestInFeatureVersion")));
        }

        // Liberica has several patch lines for one update; the API marks the
        // official newest as latestInFeatureVersion. Keep that first, then sort
        // the remaining builds newest-first.
        items.Sort((left, right) =>
        {
            if (left.Latest != right.Latest) return left.Latest ? -1 : 1;
            return VersionKey(right.Version).CompareTo(VersionKey(left.Version));
        });

        var releases = items
            .Select(item => new JavaDistributionRelease(
                item.Version,
                major,
                item.Version,
                platform.ArchiveType,
                Long(item.Item, "size"),
                null,
                IsLts(major),
                false,
                null))
            .ToList();
        return FinalizeReleases(releases, major, preserveOrder: true);
    }

    public override async Task<JavaDistributionArchive> ResolveAsync(
        string versionId,
        JavaDistributionPlatform platform,
        CancellationToken cancellationToken)
    {
        if (!JavaDistributionHttp.IsSafeVersionId(versionId))
        {
            throw new JavaDistributionException(
                "JAVA_VERSION_UNKNOWN",
                "unknown Liberica version id");
        }

        var major = ParseMajorFromVersion(versionId);
        if (major == 0)
        {
            throw new JavaDistributionException(
                "JAVA_VERSION_UNKNOWN",
                "unknown Liberica version id");
        }

        var uri = ReleasesUri(platform, major, versionId);
        using var document = await JavaDistributionHttp
            .GetJsonAsync(Http, uri, cancellationToken)
            .ConfigureAwait(false);
        if (document.RootElement.ValueKind != JsonValueKind.Array)
        {
            throw new JavaDistributionException(
                "JAVA_CATALOGUE_INVALID",
                "Liberica returned an unexpected releases document");
        }

        foreach (var item in document.RootElement.EnumerateArray())
        {
            if (!IsUsable(item, platform) ||
                !string.Equals(
                    String(item, "version"),
                    versionId,
                    StringComparison.Ordinal))
            {
                continue;
            }

            var url = JavaDistributionHttp.RequireAllowedHost(
                String(item, "downloadUrl"),
                "Liberica archive",
                "github.com").AbsoluteUri;
            return BuildArchive(
                versionId,
                versionId,
                major,
                url,
                String(item, "sha1"),
                Long(item, "size"),
                platform.ArchiveType,
                platform,
                DownloadHashAlgorithm.Sha1);
        }

        throw new JavaDistributionException(
            "JAVA_VERSION_UNKNOWN",
            $"Liberica no longer publishes '{versionId}' for " +
            $"{platform.Os} {platform.Arch}");
    }

    private static Uri ReleasesUri(
        JavaDistributionPlatform platform,
        int major,
        string? version)
    {
        var query =
            $"releases?version-feature={major}&bitness=64" +
            $"&os={platform.Os}&arch={(platform.Arch == "aarch64" ? "arm" : "x86")}" +
            $"&package-type={(platform.IsWindows ? "zip" : "tar.gz")}" +
            "&bundle-type=jdk&output=json";
        if (version is not null)
        {
            query += $"&version={Uri.EscapeDataString(version)}";
        }

        return new Uri(Api, query);
    }

    private static bool IsUsable(JsonElement item, JavaDistributionPlatform platform)
    {
        if (item.ValueKind != JsonValueKind.Object ||
            !Bool(item, "GA") ||
            String(item, "downloadUrl") is null ||
            JavaDistributionHttp.ParseHash(
                DownloadHashAlgorithm.Sha1,
                String(item, "sha1")) is null ||
            Long(item, "size") <= 0)
        {
            return false;
        }

        var url = String(item, "downloadUrl");
        return url is not null &&
               url.EndsWith(
                   platform.IsWindows ? ".zip" : ".tar.gz",
                   StringComparison.OrdinalIgnoreCase);
    }

    private static int ParseMajorFromVersion(string version)
    {
        var end = version.IndexOf('.');
        var head = end < 0 ? version : version[..end];
        return int.TryParse(head, out var major) && major is >= 8 and <= 99
            ? major
            : 0;
    }

    private static string? String(JsonElement element, string name)
        => element.ValueKind == JsonValueKind.Object &&
           element.TryGetProperty(name, out var value) &&
           value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static long Long(JsonElement element, string name)
        => element.ValueKind == JsonValueKind.Object &&
           element.TryGetProperty(name, out var value) &&
           value.ValueKind == JsonValueKind.Number &&
           value.TryGetInt64(out var number)
            ? number
            : 0;

    private static bool Bool(JsonElement element, string name)
        => element.ValueKind == JsonValueKind.Object &&
           element.TryGetProperty(name, out var value) &&
           value.ValueKind is JsonValueKind.True or JsonValueKind.False &&
           value.GetBoolean();
}
