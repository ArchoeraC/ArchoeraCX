using System.Text.Json;
using Archoera.Application.JavaRuntime;

namespace Archoera.Infrastructure.Java.Distributions;

/// <summary>
/// Eclipse Adoptium (Temurin) JDK binaries. The v3 API publishes every GA
/// build with the exact archive URL, SHA-256 and size, and supports Linux,
/// macOS and Windows on x64/aarch64.
/// </summary>
internal sealed class AdoptiumJavaDistributionProvider : JavaDistributionProviderBase
{
    public const string ProviderId = "adoptium";

    private readonly Uri _api;

    public AdoptiumJavaDistributionProvider(HttpClient http, Uri? api = null)
        : base(http)
    {
        _api = api ?? new Uri("https://api.adoptium.net/");
    }

    public override string Id => ProviderId;
    public override string DisplayName => "Eclipse Temurin (OpenJDK)";
    public override string Vendor => "Adoptium";
    public override string Homepage => "https://adoptium.net/";
    public override bool ListsAllVersions => true;

    public override async Task<IReadOnlyList<int>> GetMajorsAsync(
        JavaDistributionPlatform platform,
        CancellationToken cancellationToken)
    {
        var uri = new Uri(_api, "v3/info/available_releases");
        using var document = await JavaDistributionHttp
            .GetJsonAsync(Http, uri, cancellationToken)
            .ConfigureAwait(false);

        if (!document.RootElement.TryGetProperty("available_releases", out var array) ||
            array.ValueKind != JsonValueKind.Array)
        {
            throw new JavaDistributionException(
                "JAVA_CATALOGUE_INVALID",
                "Adoptium did not return available_releases");
        }

        var majors = new SortedSet<int>();
        foreach (var item in array.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.Number &&
                item.TryGetInt32(out var major) &&
                major is >= 8 and <= 99)
            {
                majors.Add(major);
            }
        }

        return majors.ToArray();
    }

    public override async Task<IReadOnlyList<JavaDistributionRelease>> GetReleasesAsync(
        int major,
        JavaDistributionPlatform platform,
        CancellationToken cancellationToken)
    {
        var uri = new Uri(
            _api,
            $"v3/assets/feature_releases/{major}/ga" +
            $"?os={platform.Os}&architecture={platform.Arch}&image_type=jdk" +
            "&page=0&page_size=50&vendor=eclipse");
        using var document = await JavaDistributionHttp
            .GetJsonAsync(Http, uri, cancellationToken)
            .ConfigureAwait(false);

        if (document.RootElement.ValueKind != JsonValueKind.Array)
        {
            throw new JavaDistributionException(
                "JAVA_CATALOGUE_INVALID",
                "Adoptium returned an unexpected release document");
        }

        var releases = new List<JavaDistributionRelease>();
        foreach (var item in document.RootElement.EnumerateArray())
        {
            var release = ParseRelease(item, platform);
            if (release is not null) releases.Add(release);
        }

        // The Adoptium API already returns newest-first and its ordering is
        // authoritative for patch releases such as 21.0.12.1+1.
        return FinalizeReleases(releases, major, preserveOrder: true);
    }

    public override async Task<JavaDistributionArchive> ResolveAsync(
        string versionId,
        JavaDistributionPlatform platform,
        CancellationToken cancellationToken)
    {
        if (!JavaDistributionHttp.IsSafeVersionId(versionId) ||
            !versionId.StartsWith("jdk-", StringComparison.Ordinal))
        {
            throw new JavaDistributionException(
                "JAVA_VERSION_UNKNOWN",
                "unknown Adoptium release id");
        }

        var uri = new Uri(
            _api,
            $"v3/assets/release_name/eclipse/{Uri.EscapeDataString(versionId)}" +
            $"?os={platform.Os}&architecture={platform.Arch}&image_type=jdk");
        using var document = await JavaDistributionHttp
            .GetJsonAsync(Http, uri, cancellationToken)
            .ConfigureAwait(false);

        var release = ParseRelease(document.RootElement, platform);
        if (release is null ||
            !string.Equals(
                JsonString(document.RootElement, "release_name"),
                versionId,
                StringComparison.Ordinal))
        {
            throw new JavaDistributionException(
                "JAVA_VERSION_UNKNOWN",
                $"Adoptium release '{versionId}' is not available for " +
                $"{platform.Os} {platform.Arch}");
        }

        var binary = FindBinary(document.RootElement, platform);
        if (binary is null ||
            !binary.Value.TryGetProperty("package", out var package) ||
            package.ValueKind != JsonValueKind.Object)
        {
            throw new JavaDistributionException(
                "JAVA_PLATFORM_UNSUPPORTED",
                $"Adoptium does not publish a JDK {release.Major} build for " +
                $"{platform.Os} {platform.Arch}");
        }

        var archiveUrl = JavaDistributionHttp.RequireAllowedHost(
            JsonString(package, "link"),
            "Adoptium archive",
            "github.com").AbsoluteUri;
        return BuildArchive(
            versionId,
            release.Version,
            release.Major,
            archiveUrl,
            JsonString(package, "checksum"),
            JsonLong(package, "size"),
            platform.ArchiveType,
            platform);
    }

    private static JavaDistributionRelease? ParseRelease(
        JsonElement element,
        JavaDistributionPlatform platform)
    {
        if (element.ValueKind != JsonValueKind.Object) return null;

        var releaseName = JsonString(element, "release_name");
        var binary = FindBinary(element, platform);
        if (string.IsNullOrWhiteSpace(releaseName) ||
            binary is null ||
            !binary.Value.TryGetProperty("package", out var package) ||
            package.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var major = 0;
        if (element.TryGetProperty("version_data", out var versionData) &&
            versionData.ValueKind == JsonValueKind.Object)
        {
            major = JsonInt(versionData, "major");
        }

        if (major == 0) return null;

        var version = releaseName.StartsWith("jdk-", StringComparison.Ordinal)
            ? releaseName[4..]
            : releaseName;
        return new JavaDistributionRelease(
            releaseName,
            major,
            version,
            platform.ArchiveType,
            JsonLong(package, "size"),
            JavaDistributionHttp.ParseSha256(JsonString(package, "checksum")),
            IsLts(major),
            false,
            JsonString(element, "timestamp"));
    }

    private static JsonElement? FindBinary(
        JsonElement release,
        JavaDistributionPlatform platform)
    {
        if (!release.TryGetProperty("binaries", out var binaries) ||
            binaries.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var binary in binaries.EnumerateArray())
        {
            if (binary.ValueKind != JsonValueKind.Object) continue;
            if (!string.Equals(
                    JsonString(binary, "image_type"),
                    "jdk",
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!string.Equals(
                    JsonString(binary, "os"),
                    platform.Os,
                    StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(
                    JsonString(binary, "architecture"),
                    platform.Arch,
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            return binary;
        }

        return null;
    }

    private static string? JsonString(JsonElement element, string name)
        => element.ValueKind == JsonValueKind.Object &&
           element.TryGetProperty(name, out var value) &&
           value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static int JsonInt(JsonElement element, string name)
        => element.ValueKind == JsonValueKind.Object &&
           element.TryGetProperty(name, out var value) &&
           value.ValueKind == JsonValueKind.Number &&
           value.TryGetInt32(out var number)
            ? number
            : 0;

    private static long JsonLong(JsonElement element, string name)
        => element.ValueKind == JsonValueKind.Object &&
           element.TryGetProperty(name, out var value) &&
           value.ValueKind == JsonValueKind.Number &&
           value.TryGetInt64(out var number)
            ? number
            : 0;
}
