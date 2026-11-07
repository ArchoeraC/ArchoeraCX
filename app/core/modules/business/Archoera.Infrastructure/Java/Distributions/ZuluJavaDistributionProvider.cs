using System.Text.Json;
using Archoera.Application.JavaRuntime;

namespace Archoera.Infrastructure.Java.Distributions;

/// <summary>
/// Azul Zulu JDK archives. The list endpoint is metadata-only (no checksum or
/// size), so the list keeps the package uuid and install re-reads the package
/// detail to obtain the exact URL, SHA-256 and size.
/// </summary>
internal sealed class ZuluJavaDistributionProvider : JavaDistributionProviderBase
{
    public const string ProviderId = "zulu";

    private readonly Uri _api;

    public ZuluJavaDistributionProvider(HttpClient http, Uri? api = null)
        : base(http)
    {
        _api = api ?? new Uri("https://api.azul.com/metadata/v1/zulu/");
    }

    public override string Id => ProviderId;
    public override string DisplayName => "Azul Zulu";
    public override string Vendor => "Azul";
    public override string Homepage => "https://www.azul.com/downloads/";
    public override bool ListsAllVersions => true;

    public override async Task<IReadOnlyList<int>> GetMajorsAsync(
        JavaDistributionPlatform platform,
        CancellationToken cancellationToken)
    {
        var uri = PackagesUri(platform, major: null, latestOnly: true, pageSize: 1000);
        using var document = await JavaDistributionHttp
            .GetJsonAsync(Http, uri, cancellationToken)
            .ConfigureAwait(false);
        if (document.RootElement.ValueKind != JsonValueKind.Array)
        {
            throw new JavaDistributionException(
                "JAVA_CATALOGUE_INVALID",
                "Zulu returned an unexpected package document");
        }

        var majors = new SortedSet<int>();
        foreach (var item in document.RootElement.EnumerateArray())
        {
            if (!IsStandardPackage(item, platform)) continue;
            var major = PackageMajor(item);
            if (major is >= 8 and <= 99) majors.Add(major);
        }

        return majors.ToArray();
    }

    public override async Task<IReadOnlyList<JavaDistributionRelease>> GetReleasesAsync(
        int major,
        JavaDistributionPlatform platform,
        CancellationToken cancellationToken)
    {
        var uri = PackagesUri(platform, major, latestOnly: false, pageSize: 200);
        using var document = await JavaDistributionHttp
            .GetJsonAsync(Http, uri, cancellationToken)
            .ConfigureAwait(false);
        if (document.RootElement.ValueKind != JsonValueKind.Array)
        {
            throw new JavaDistributionException(
                "JAVA_CATALOGUE_INVALID",
                "Zulu returned an unexpected package document");
        }

        var releases = new List<JavaDistributionRelease>();
        foreach (var item in document.RootElement.EnumerateArray())
        {
            if (!IsStandardPackage(item, platform)) continue;
            var uuid = JsonString(item, "package_uuid");
            if (uuid is null || !Guid.TryParse(uuid, out _)) continue;
            var parsedMajor = PackageMajor(item);
            if (parsedMajor != major) continue;

            releases.Add(new JavaDistributionRelease(
                uuid,
                parsedMajor,
                PackageVersion(item),
                platform.ArchiveType,
                0,
                null,
                IsLts(parsedMajor),
                false,
                null));
        }

        return FinalizeReleases(releases, major);
    }

    public override async Task<JavaDistributionArchive> ResolveAsync(
        string versionId,
        JavaDistributionPlatform platform,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(versionId, out var uuid))
        {
            throw new JavaDistributionException(
                "JAVA_VERSION_UNKNOWN",
                "unknown Zulu package id");
        }

        var uri = new Uri(_api, $"packages/{uuid:D}");
        using var document = await JavaDistributionHttp
            .GetJsonAsync(Http, uri, cancellationToken)
            .ConfigureAwait(false);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object || !IsStandardPackage(root, platform))
        {
            throw new JavaDistributionException(
                "JAVA_VERSION_UNKNOWN",
                $"Zulu package '{versionId}' is not a JDK build for " +
                $"{platform.Os} {platform.Arch}");
        }

        var major = PackageMajor(root);
        var archiveUrl = JavaDistributionHttp.RequireAllowedHost(
            JsonString(root, "download_url"),
            "Zulu archive",
            "cdn.azul.com").AbsoluteUri;
        return BuildArchive(
            versionId,
            PackageVersion(root),
            major,
            archiveUrl,
            JsonString(root, "sha256_hash"),
            JsonLong(root, "size"),
            JsonString(root, "archive_type"),
            platform);
    }

    private Uri PackagesUri(
        JavaDistributionPlatform platform,
        int? major,
        bool latestOnly,
        int pageSize)
    {
        var query =
            $"packages/?os={platform.Os}&arch={platform.Arch}&hw_bitness=64" +
            $"&archive_type={platform.ArchiveType}&java_package_type=jdk" +
            $"&release_status=ga&page_size={pageSize}";
        if (major is not null) query += $"&java_version={major.Value}";
        if (latestOnly) query += "&latest=true";
        return new Uri(_api, query);
    }

    /// <summary>
    /// The metadata API returns musl, CRaC, JavaFX and FIPS variants under the
    /// same OS/arch filter. The launcher only wants the portable "CA" JDK.
    /// </summary>
    private static bool IsStandardPackage(
        JsonElement item,
        JavaDistributionPlatform platform)
    {
        var name = JsonString(item, "name");
        if (string.IsNullOrWhiteSpace(name)) return false;
        if (!name.Contains("-ca-jdk", StringComparison.Ordinal)) return false;

        foreach (var marker in ExcludedMarkers)
        {
            if (name.Contains(marker, StringComparison.Ordinal)) return false;
        }

        var suffix = platform.Os switch
        {
            "windows" => $"-win_{platform.Arch}.zip",
            "macos" => $"-macosx_{platform.Arch}.tar.gz",
            _ => $"-linux_{platform.Arch}.tar.gz",
        };
        return name.EndsWith(suffix, StringComparison.Ordinal);
    }

    private static readonly string[] ExcludedMarkers =
        ["musl", "crac", "crs", "fx", "fips", "debug"];

    private static int PackageMajor(JsonElement item)
    {
        if (item.TryGetProperty("java_version", out var array) &&
            array.ValueKind == JsonValueKind.Array)
        {
            foreach (var value in array.EnumerateArray())
            {
                if (value.ValueKind == JsonValueKind.Number &&
                    value.TryGetInt32(out var major))
                {
                    return major;
                }
            }
        }

        return 0;
    }

    private static string PackageVersion(JsonElement item)
    {
        if (item.TryGetProperty("java_version", out var array) &&
            array.ValueKind == JsonValueKind.Array)
        {
            var parts = new List<string>();
            foreach (var value in array.EnumerateArray())
            {
                if (value.ValueKind == JsonValueKind.Number)
                {
                    parts.Add(value.GetRawText());
                }
            }

            if (parts.Count > 0) return string.Join('.', parts);
        }

        var name = JsonString(item, "name") ?? string.Empty;
        var marker = name.IndexOf("-jdk", StringComparison.Ordinal);
        if (marker >= 0)
        {
            var rest = name[(marker + 4)..];
            var dash = rest.IndexOf('-');
            return dash > 0 ? rest[..dash] : rest;
        }

        return string.Empty;
    }

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
}
