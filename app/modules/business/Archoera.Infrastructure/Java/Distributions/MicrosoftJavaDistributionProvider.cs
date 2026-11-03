using System.Text.RegularExpressions;
using Archoera.Application.JavaRuntime;

namespace Archoera.Infrastructure.Java.Distributions;

/// <summary>
/// Microsoft Build of OpenJDK. Microsoft publishes only the newest binary
/// archive of each major, linked from the public download page and paired with
/// a <c>.sha256sum.txt</c> file. The page is parsed once per catalogue request;
/// install re-checks that the artifact is still listed before touching aka.ms.
/// </summary>
internal sealed partial class MicrosoftJavaDistributionProvider : JavaDistributionProviderBase
{
    public const string ProviderId = "microsoft";

    private readonly Uri _page;
    private readonly Uri _downloadRoot;

    public MicrosoftJavaDistributionProvider(HttpClient http, Uri? page = null)
        : base(http)
    {
        _page = page ?? new Uri("https://learn.microsoft.com/en-us/java/openjdk/download");
        _downloadRoot = new Uri("https://aka.ms/download-jdk/");
    }

    public override string Id => ProviderId;
    public override string DisplayName => "Microsoft Build of OpenJDK";
    public override string Vendor => "Microsoft";
    public override string Homepage => "https://learn.microsoft.com/java/openjdk/";
    public override bool ListsAllVersions => false;

    public override async Task<IReadOnlyList<int>> GetMajorsAsync(
        JavaDistributionPlatform platform,
        CancellationToken cancellationToken)
    {
        var text = await ReadPageAsync(cancellationToken).ConfigureAwait(false);
        var majors = new SortedSet<int>();
        foreach (var (_, major) in EnumerateArtifacts(text, platform))
        {
            majors.Add(major);
        }

        return majors.ToArray();
    }

    public override async Task<IReadOnlyList<JavaDistributionRelease>> GetReleasesAsync(
        int major,
        JavaDistributionPlatform platform,
        CancellationToken cancellationToken)
    {
        var text = await ReadPageAsync(cancellationToken).ConfigureAwait(false);
        var releases = new List<JavaDistributionRelease>();
        foreach (var (artifact, artifactMajor) in EnumerateArtifacts(text, platform))
        {
            if (artifactMajor != major) continue;
            releases.Add(new JavaDistributionRelease(
                artifact,
                major,
                VersionFromArtifact(artifact),
                platform.ArchiveType,
                0,
                null,
                IsLts(major),
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
        var match = ArtifactPattern(platform).Match(versionId);
        if (!match.Success)
        {
            throw new JavaDistributionException(
                "JAVA_VERSION_UNKNOWN",
                "unknown Microsoft JDK artifact");
        }

        var text = await ReadPageAsync(cancellationToken).ConfigureAwait(false);
        var listed = EnumerateArtifacts(text, platform)
            .Any(item => string.Equals(item.Artifact, versionId, StringComparison.Ordinal));
        if (!listed)
        {
            throw new JavaDistributionException(
                "JAVA_VERSION_UNKNOWN",
                $"Microsoft no longer lists '{versionId}'");
        }

        var artifactUri = new Uri(_downloadRoot, versionId);
        var shaUri = new Uri(_downloadRoot, versionId + ".sha256sum.txt");
        var hash = await JavaDistributionHttp
            .GetSha256Async(Http, shaUri, cancellationToken)
            .ConfigureAwait(false);
        var size = await JavaDistributionHttp
            .GetContentLengthAsync(Http, artifactUri, cancellationToken)
            .ConfigureAwait(false);

        return BuildArchive(
            versionId,
            VersionFromArtifact(versionId),
            ParseMajor(match.Groups["major"].Value),
            artifactUri.AbsoluteUri,
            hash,
            size,
            platform.ArchiveType,
            platform);
    }

    private async Task<string> ReadPageAsync(CancellationToken cancellationToken)
        => await JavaDistributionHttp
            .GetTextAsync(Http, _page, cancellationToken)
            .ConfigureAwait(false);

    private static IEnumerable<(string Artifact, int Major)> EnumerateArtifacts(
        string text,
        JavaDistributionPlatform platform)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (Match match in ArtifactPattern(platform).Matches(text))
        {
            var artifact = match.Value;
            if (!seen.Add(artifact)) continue;
            var major = ParseMajor(match.Groups["major"].Value);
            if (major is >= 8 and <= 99)
            {
                yield return (artifact, major);
            }
        }
    }

    /// <summary>
    /// Matches only full version numbers (at least one dot), excluding the
    /// "microsoft-jdk-25-linux-x64" shortcut links that do not pin a release.
    /// </summary>
    private static Regex ArtifactPattern(JavaDistributionPlatform platform)
        => CachedPatterns.GetOrAdd(platform, CreatePattern);

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<
        JavaDistributionPlatform,
        Regex> CachedPatterns = new();

    private static Regex CreatePattern(JavaDistributionPlatform platform)
    {
        var suffix = platform.Os switch
        {
            "windows" => $"-windows-{platform.Arch}.zip",
            "macos" => $"-macos-{platform.Arch}.tar.gz",
            _ => $"-linux-{platform.Arch}.tar.gz",
        };
        return new Regex(
            $@"microsoft-jdk-(?<major>\d+)(?:\.\d+)+{Regex.Escape(suffix)}",
            RegexOptions.CultureInvariant);
    }

    private static string VersionFromArtifact(string artifact)
    {
        var match = VersionPattern().Match(artifact);
        return match.Success ? match.Groups["version"].Value : artifact;
    }

    [GeneratedRegex(@"^microsoft-jdk-(?<version>\d+(?:\.\d+)+)-")]
    private static partial Regex VersionPattern();
}
