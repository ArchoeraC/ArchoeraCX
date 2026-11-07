using System.Text.Json;
using Archoera.Application.JavaRuntime;

namespace Archoera.Infrastructure.Java.Distributions;

/// <summary>
/// Amazon Corretto. AWS publishes a stable "latest" URL per feature release
/// and a matching SHA-256 file; the redirect names the exact build version.
/// Only the LTS lines are enumerated because Corretto's non-LTS lines are not
/// useful for Minecraft.
/// </summary>
internal sealed class CorrettoJavaDistributionProvider : JavaDistributionProviderBase
{
    public const string ProviderId = "corretto";

    private static readonly int[] MajorVersions = [8, 11, 17, 21, 25];

    private static readonly Uri DownloadRoot =
        new("https://corretto.aws/downloads/");

    public CorrettoJavaDistributionProvider(HttpClient http)
        : base(http)
    {
    }

    public override string Id => ProviderId;
    public override string DisplayName => "Amazon Corretto";
    public override string Vendor => "Amazon";
    public override string Homepage => "https://aws.amazon.com/corretto/";
    public override bool ListsAllVersions => false;

    public override Task<IReadOnlyList<int>> GetMajorsAsync(
        JavaDistributionPlatform platform,
        CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<int>>(MajorVersions);

    public override async Task<IReadOnlyList<JavaDistributionRelease>> GetReleasesAsync(
        int major,
        JavaDistributionPlatform platform,
        CancellationToken cancellationToken)
    {
        var release = await ResolveLatestAsync(major, platform, cancellationToken)
            .ConfigureAwait(false);
        return release is null
            ? []
            : FinalizeReleases([release.Value.Release], major);
    }

    public override async Task<JavaDistributionArchive> ResolveAsync(
        string versionId,
        JavaDistributionPlatform platform,
        CancellationToken cancellationToken)
    {
        if (!TryParseVersionId(versionId, platform, out var major))
        {
            throw new JavaDistributionException(
                "JAVA_VERSION_UNKNOWN",
                "unknown Corretto artifact");
        }

        var resolved = await ResolveLatestAsync(major, platform, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new JavaDistributionException(
                "JAVA_VERSION_UNKNOWN",
                $"Corretto has no Java {major} build for {platform.Os} {platform.Arch}");
        if (!string.Equals(
                resolved.Artifact,
                versionId,
                StringComparison.Ordinal))
        {
            throw new JavaDistributionException(
                "JAVA_VERSION_UNKNOWN",
                $"Corretto no longer publishes '{versionId}'");
        }

        var archiveUrl = JavaDistributionHttp.RequireAllowedHost(
            resolved.Url,
            "Corretto archive",
            "corretto.aws").AbsoluteUri;
        return BuildArchive(
            versionId,
            resolved.Release.Version,
            major,
            archiveUrl,
            resolved.Sha256,
            resolved.Size,
            platform.ArchiveType,
            platform);
    }

    private async Task<(JavaDistributionRelease Release, string Artifact, string Url,
        string Sha256, long Size)?> ResolveLatestAsync(
        int major,
        JavaDistributionPlatform platform,
        CancellationToken cancellationToken)
    {
        var artifact = ArtifactName(major, platform);
        var latest = new Uri(DownloadRoot, $"latest/{artifact}");
        using var request = new HttpRequestMessage(HttpMethod.Head, latest);
        using var response = await Http
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new JavaDistributionException(
                "JAVA_CATALOGUE_UNAVAILABLE",
                $"Corretto returned HTTP {(int)response.StatusCode}");
        }

        var finalUri = response.RequestMessage?.RequestUri ?? latest;
        var size = response.Content.Headers.ContentLength ?? 0;
        var version = VersionFromUri(finalUri) ?? major.ToString(
            System.Globalization.CultureInfo.InvariantCulture);
        var shaUri = new Uri(DownloadRoot, $"latest_sha256/{artifact}");
        var sha = await JavaDistributionHttp
            .GetSha256Async(Http, shaUri, cancellationToken)
            .ConfigureAwait(false);

        var release = new JavaDistributionRelease(
            artifact,
            major,
            version,
            platform.ArchiveType,
            size,
            sha,
            IsLts(major),
            false,
            null);
        return (release, artifact, finalUri.AbsoluteUri, sha, size);
    }

    private static string ArtifactName(int major, JavaDistributionPlatform platform)
    {
        var ext = platform.IsWindows ? "zip" : "tar.gz";
        return $"amazon-corretto-{major}-{platform.Arch}-{platform.Os}-jdk.{ext}";
    }

    private static bool TryParseVersionId(
        string versionId,
        JavaDistributionPlatform platform,
        out int major)
    {
        major = 0;
        if (!versionId.StartsWith("amazon-corretto-", StringComparison.Ordinal) ||
            !versionId.EndsWith(
                platform.IsWindows ? ".zip" : ".tar.gz",
                StringComparison.Ordinal))
        {
            return false;
        }

        var rest = versionId["amazon-corretto-".Length..];
        var dash = rest.IndexOf('-');
        if (dash <= 0 || !int.TryParse(rest[..dash], out major)) return false;
        return string.Equals(
            ArtifactName(major, platform),
            versionId,
            StringComparison.Ordinal);
    }

    private static string? VersionFromUri(Uri uri)
    {
        var segments = uri.AbsolutePath.Split(
            '/',
            StringSplitOptions.RemoveEmptyEntries);
        for (var index = 0; index < segments.Length - 1; index++)
        {
            if (string.Equals(segments[index], "resources", StringComparison.Ordinal))
            {
                return segments[index + 1];
            }
        }

        return null;
    }
}
