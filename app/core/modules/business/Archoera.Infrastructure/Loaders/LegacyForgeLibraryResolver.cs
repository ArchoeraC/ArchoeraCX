using Archoera.Application.Downloads;
using Archoera.Application.Loaders;
using Archoera.Application.Mojang;
using Archoera.Domain.Downloads;
using Archoera.Domain.Loaders;

namespace Archoera.Infrastructure.Loaders;

/// <summary>One resolved download for a pre-1.7 Forge library.</summary>
/// <param name="Url">Primary URL (the mirror when the source mode is mirror).</param>
/// <param name="Mirrors">Official fallback URLs.</param>
/// <param name="Hash">SHA-1 from the installer checksums or the Maven sidecar.</param>
/// <param name="Size">Content-Length, because legacy metadata has no size.</param>
public sealed record LegacyForgeLibrary(
    string Url,
    IReadOnlyList<string> Mirrors,
    DownloadHash Hash,
    long Size);

/// <summary>
/// Resolves a pre-1.7 Forge library entry (<c>name</c> + optional <c>url</c> +
/// optional <c>checksums</c>). The installer's own GUI streams these without a
/// known size; our executor needs one, so the resolver asks the Maven host.
/// </summary>
public interface ILegacyForgeLibraryResolver
{
    Task<LegacyForgeLibrary> ResolveAsync(
        string name,
        string? repository,
        IReadOnlyList<string> checksums,
        DownloadSourceMode sourceMode,
        CancellationToken cancellationToken);
}

/// <summary>HTTP implementation used by <see cref="ForgeInstallPlanService"/>.</summary>
public sealed class LegacyForgeLibraryResolver : ILegacyForgeLibraryResolver
{
    private static readonly HttpClient SharedHttp = new()
    {
        Timeout = TimeSpan.FromSeconds(60),
    };

    private readonly HttpClient _http;

    public LegacyForgeLibraryResolver(HttpClient? http = null)
    {
        _http = http ?? SharedHttp;
    }

    public async Task<LegacyForgeLibrary> ResolveAsync(
        string name,
        string? repository,
        IReadOnlyList<string> checksums,
        DownloadSourceMode sourceMode,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (!MavenPath.TryParse(name, out var coordinate))
        {
            throw new ForgeDownloadException(
                ForgeDownloadErrorCode.InvalidMetadata,
                $"legacy library coordinate '{name}' is not maven-shaped");
        }

        var relative = coordinate.RelativePath;
        var official = new Uri(NormalizeRepository(repository), relative);
        var mirror = sourceMode == DownloadSourceMode.Mirror
            ? new Uri(ForgeMavenMirror.Bmclapi, relative)
            : null;
        var primary = mirror ?? official;
        IReadOnlyList<string> mirrors = mirror is null ? [] : [official.AbsoluteUri];

        var sha1 = checksums.FirstOrDefault(IsSha1);
        if (sha1 is null)
        {
            sha1 = await FetchSidecarAsync(primary, cancellationToken).ConfigureAwait(false)
                   ?? await FetchSidecarAsync(official, cancellationToken).ConfigureAwait(false);
        }

        if (sha1 is null)
        {
            throw new ForgeDownloadException(
                ForgeDownloadErrorCode.MissingHash,
                $"legacy library '{name}' has no SHA-1 (checksums and .sha1 sidecar are missing)");
        }

        var size = await HeadSizeAsync(primary, cancellationToken).ConfigureAwait(false)
                   ?? await HeadSizeAsync(official, cancellationToken).ConfigureAwait(false)
                   ?? throw new ForgeDownloadException(
                       ForgeDownloadErrorCode.MissingSize,
                       $"legacy library '{name}' has no Content-Length");

        return new LegacyForgeLibrary(
            primary.AbsoluteUri,
            mirrors,
            DownloadHash.Create(DownloadHashAlgorithm.Sha1, sha1),
            size);
    }

    private static Uri NormalizeRepository(string? repository)
    {
        if (string.IsNullOrWhiteSpace(repository))
        {
            return MojangRepositories.MinecraftLibraries;
        }

        var issue = DownloadPlanPaths.TryValidateUrl(repository, out var parsed);
        if (issue is not null || parsed is null)
        {
            throw new ForgeDownloadException(
                ForgeDownloadErrorCode.InvalidMetadata,
                $"legacy library repository is not allowed: {issue?.Message ?? repository}");
        }

        var builder = new UriBuilder(parsed);
        if (!builder.Path.EndsWith('/'))
        {
            builder.Path += "/";
        }

        return builder.Uri;
    }

    private async Task<string?> FetchSidecarAsync(Uri artifact, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await _http
                .GetAsync(artifact.AbsoluteUri + ".sha1", cancellationToken)
                .ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var text = (await response.Content
                    .ReadAsStringAsync(cancellationToken)
                    .ConfigureAwait(false))
                .Trim();
            return IsSha1(text) ? text : null;
        }
        catch (Exception exception) when (
            exception is HttpRequestException or TaskCanceledException or IOException)
        {
            return null;
        }
    }

    private async Task<long?> HeadSizeAsync(Uri artifact, CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Head, artifact);
            using var response = await _http
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);
            return response.IsSuccessStatusCode
                ? response.Content.Headers.ContentLength
                : null;
        }
        catch (Exception exception) when (
            exception is HttpRequestException or TaskCanceledException or IOException)
        {
            return null;
        }
    }

    private static bool IsSha1(string? value)
        => value is { Length: 40 } &&
           value.All(character => char.IsAsciiHexDigit(character));
}
