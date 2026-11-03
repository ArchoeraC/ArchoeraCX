using System.Text.Json;
using System.Text.RegularExpressions;
using Archoera.Application.JavaRuntime;

namespace Archoera.Infrastructure.Java.Distributions;

/// <summary>
/// Alibaba Dragonwell Standard JDK. Each feature release lives in its own
/// GitHub repository; assets carry a matching <c>.sha256.txt</c> file.
/// Dragonwell does not publish macOS binaries, so that platform stays empty.
/// </summary>
internal sealed partial class DragonwellJavaDistributionProvider : JavaDistributionProviderBase
{
    public const string ProviderId = "dragonwell";

    private static readonly int[] MajorVersions = [8, 11, 17, 21, 25];

    private static readonly Uri GitHub = new("https://api.github.com/");

    public DragonwellJavaDistributionProvider(HttpClient http)
        : base(http)
    {
    }

    public override string Id => ProviderId;
    public override string DisplayName => "Alibaba Dragonwell";
    public override string Vendor => "Alibaba";
    public override string Homepage => "https://dragonwell-jdk.io/";
    public override bool ListsAllVersions => true;

    public override Task<IReadOnlyList<int>> GetMajorsAsync(
        JavaDistributionPlatform platform,
        CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<int>>(
            platform.IsMacOs ? [] : MajorVersions);

    public override async Task<IReadOnlyList<JavaDistributionRelease>> GetReleasesAsync(
        int major,
        JavaDistributionPlatform platform,
        CancellationToken cancellationToken)
    {
        if (platform.IsMacOs) return [];

        var uri = new Uri(
            GitHub,
            $"repos/dragonwell-project/dragonwell{major}/releases?per_page=30");
        using var document = await JavaDistributionHttp
            .GetJsonAsync(Http, uri, cancellationToken)
            .ConfigureAwait(false);
        if (document.RootElement.ValueKind != JsonValueKind.Array)
        {
            throw new JavaDistributionException(
                "JAVA_CATALOGUE_INVALID",
                "GitHub returned an unexpected Dragonwell release document");
        }

        var releases = new List<JavaDistributionRelease>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in document.RootElement.EnumerateArray())
        {
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
                if (name is null || !MatchesPlatform(name, platform)) continue;
                var parsed = ParseAsset(name);
                var size = JsonLong(asset, "size");
                if (parsed is null || parsed.Value.Major != major || size <= 0)
                {
                    continue;
                }

                var versionId = $"{tag}|{name}";
                if (!seen.Add(versionId)) continue;
                releases.Add(new JavaDistributionRelease(
                    versionId,
                    major,
                    parsed.Value.Version,
                    platform.ArchiveType,
                    size,
                    null,
                    IsLts(major),
                    false,
                    releasedAt));
            }
        }

        return FinalizeReleases(releases, major);
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
                "unknown Dragonwell release id");
        }

        var tag = versionId[..separator];
        var assetName = versionId[(separator + 1)..];
        var parsed = ParseAsset(assetName);
        if (!TagPattern().IsMatch(tag) ||
            parsed is null ||
            !MatchesPlatform(assetName, platform))
        {
            throw new JavaDistributionException(
                "JAVA_VERSION_UNKNOWN",
                "unknown Dragonwell release id");
        }

        var uri = new Uri(
            GitHub,
            $"repos/dragonwell-project/dragonwell{parsed.Value.Major}/releases/tags/" +
            Uri.EscapeDataString(tag));
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
                $"Dragonwell release '{tag}' is no longer available");
        }

        JsonElement? archiveAsset = null;
        JsonElement? hashAsset = null;
        var hashName = assetName + ".sha256.txt";
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
                $"Dragonwell release '{tag}' no longer publishes {assetName}");
        }

        var hashUri = JavaDistributionHttp.RequireAllowedHost(
            JsonString(hashAsset.Value, "browser_download_url"),
            "Dragonwell checksum",
            "github.com");
        var hash = await JavaDistributionHttp
            .GetSha256Async(Http, hashUri, cancellationToken)
            .ConfigureAwait(false);
        var archiveUrl = JavaDistributionHttp.RequireAllowedHost(
            JsonString(archiveAsset.Value, "browser_download_url"),
            "Dragonwell archive",
            "github.com").AbsoluteUri;

        return BuildArchive(
            versionId,
            parsed.Value.Version,
            parsed.Value.Major,
            archiveUrl,
            hash,
            JsonLong(archiveAsset.Value, "size"),
            platform.ArchiveType,
            platform);
    }

    private static bool MatchesPlatform(
        string assetName,
        JavaDistributionPlatform platform)
    {
        if (platform.IsMacOs) return false;
        var suffix = platform.Os == "windows"
            ? $"_{platform.Arch}_windows.zip"
            : $"_{platform.Arch}_linux.tar.gz";
        return assetName.StartsWith(
                   "Alibaba_Dragonwell_Standard_",
                   StringComparison.Ordinal) &&
               assetName.EndsWith(suffix, StringComparison.Ordinal);
    }

    private static (int Major, string Version)? ParseAsset(string name)
    {
        var match = ArtifactPattern().Match(name);
        if (!match.Success) return null;
        var version = match.Groups["version"].Value;
        var head = version.Split('.', 2)[0];
        return int.TryParse(head, out var major)
            ? (major, version)
            : null;
    }

    [GeneratedRegex(
        @"^Alibaba_Dragonwell_Standard_(?<version>\d+(?:\.\d+)+)_" +
        @"(?:x64|aarch64)_(?:linux|windows)\.(?:tar\.gz|zip)$",
        RegexOptions.CultureInvariant)]
    private static partial Regex ArtifactPattern();

    [GeneratedRegex("^[A-Za-z0-9._+-]+$")]
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
}
