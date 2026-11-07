using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Archoera.Application.Loaders;

namespace Archoera.Infrastructure.Loaders;

/// <summary>Published SHA-1 and byte size of one Maven artifact.</summary>
/// <param name="Sha1">Lower-case 40-character SHA-1 from the <c>.sha1</c> sidecar.</param>
/// <param name="Size">Content-Length reported by the artifact URL.</param>
public sealed record MavenArtifactMetadata(string Sha1, long Size);

/// <summary>
/// Reads the official Maven <c>.sha1</c> sidecar and the HTTP
/// <c>Content-Length</c> for a Forge/NeoForge artifact. No authentication, no
/// URL guessing and no third-party mirrors: the caller supplies the exact
/// artifact URL and the injected <see cref="HttpClient"/>.
/// </summary>
/// <remarks>
/// When HEAD is rejected or omits <c>Content-Length</c>, a one-byte range GET is
/// used and the total is read from <c>Content-Range</c>. A missing size fails
/// with <see cref="ForgeDownloadErrorCode.MissingSize"/> instead of emitting a
/// plan the Rust executor would reject.
/// </remarks>
public sealed class MavenSidecarClient
{
    private const int MaxSidecarBytes = 4_096;

    private readonly HttpClient _httpClient;
    private readonly string? _userAgent;

    /// <summary>Creates a client over an injected <see cref="HttpClient"/>.</summary>
    /// <param name="httpClient">Transport; this type never disposes it.</param>
    /// <param name="userAgent">Optional identifiable user agent.</param>
    public MavenSidecarClient(HttpClient httpClient, string? userAgent = null)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        _httpClient = httpClient;
        _userAgent = string.IsNullOrWhiteSpace(userAgent) ? null : userAgent.Trim();
    }

    /// <summary>Fetches the SHA-1 sidecar, then the artifact size.</summary>
    /// <exception cref="ForgeDownloadException">
    /// <c>NOT_FOUND</c>, <c>INVALID_RESPONSE</c>, <c>MISSING_SIZE</c> or
    /// <c>NETWORK_ERROR</c>.
    /// </exception>
    public async Task<MavenArtifactMetadata> GetMetadataAsync(
        Uri artifactUri,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(artifactUri);

        var sha1 = await GetSha1Async(artifactUri, cancellationToken).ConfigureAwait(false);
        var size = await GetSizeAsync(artifactUri, cancellationToken).ConfigureAwait(false);
        return new MavenArtifactMetadata(sha1, size);
    }

    /// <summary>Fetches and validates the artifact's <c>.sha1</c> sidecar.</summary>
    /// <exception cref="ForgeDownloadException">
    /// <c>NOT_FOUND</c>, <c>INVALID_RESPONSE</c> or <c>NETWORK_ERROR</c>.
    /// </exception>
    public async Task<string> GetSha1Async(
        Uri artifactUri,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(artifactUri);

        var sidecarUri = ForgeMavenUrls.Sidecar(artifactUri);
        using var request = new HttpRequestMessage(HttpMethod.Get, sidecarUri);
        AddUserAgent(request);

        using var response = await SendAsync(request, sidecarUri, cancellationToken)
            .ConfigureAwait(false);
        EnsureSuccess(response, sidecarUri, "sha1 sidecar");

        if (response.Content is null)
        {
            throw new ForgeDownloadException(
                ForgeDownloadErrorCode.InvalidResponse,
                $"sha1 sidecar has no body: {sidecarUri}");
        }

        string text;
        try
        {
            await using var stream = await response.Content
                .ReadAsStreamAsync(cancellationToken)
                .ConfigureAwait(false);
            using var buffer = new MemoryStream(MaxSidecarBytes);
            var chunk = new byte[512];
            var total = 0;

            while (true)
            {
                var read = await stream
                    .ReadAsync(chunk.AsMemory(0, chunk.Length), cancellationToken)
                    .ConfigureAwait(false);
                if (read == 0)
                {
                    break;
                }

                total += read;
                if (total > MaxSidecarBytes)
                {
                    throw new ForgeDownloadException(
                        ForgeDownloadErrorCode.InvalidResponse,
                        $"sha1 sidecar is larger than {MaxSidecarBytes} bytes: {sidecarUri}");
                }

                buffer.Write(chunk, 0, read);
            }

            text = Encoding.UTF8.GetString(buffer.GetBuffer(), 0, checked((int)buffer.Length));
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new ForgeDownloadException(
                ForgeDownloadErrorCode.NetworkError,
                $"timed out while reading the sha1 sidecar: {sidecarUri}");
        }
        catch (IOException exception)
        {
            throw new ForgeDownloadException(
                ForgeDownloadErrorCode.NetworkError,
                $"failed to read sha1 sidecar: {sidecarUri}",
                innerException: exception);
        }

        var value = text.Trim().TrimStart('\uFEFF').Trim();
        if (value.Length != 40 || !IsHex(value))
        {
            throw new ForgeDownloadException(
                ForgeDownloadErrorCode.InvalidResponse,
                $"sha1 sidecar is not a 40-character hex digest: {sidecarUri}");
        }

        return value.ToLowerInvariant();
    }

    /// <summary>
    /// Resolves the artifact size from HEAD, falling back to a one-byte range
    /// GET when the server rejects HEAD or omits <c>Content-Length</c>.
    /// </summary>
    /// <exception cref="ForgeDownloadException">
    /// <c>NOT_FOUND</c>, <c>INVALID_RESPONSE</c>, <c>MISSING_SIZE</c> or
    /// <c>NETWORK_ERROR</c>.
    /// </exception>
    public async Task<long> GetSizeAsync(
        Uri artifactUri,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(artifactUri);

        using (var request = new HttpRequestMessage(HttpMethod.Head, artifactUri))
        {
            AddUserAgent(request);
            using var response = await SendAsync(request, artifactUri, cancellationToken)
                .ConfigureAwait(false);

            if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Gone)
            {
                throw NotFound(artifactUri, "artifact");
            }

            if (response.IsSuccessStatusCode)
            {
                if (response.Content?.Headers.ContentLength is { } length && length >= 0)
                {
                    return length;
                }
            }
            else if (response.StatusCode is not (HttpStatusCode.MethodNotAllowed
                                                or HttpStatusCode.NotImplemented))
            {
                throw new ForgeDownloadException(
                    ForgeDownloadErrorCode.InvalidResponse,
                    $"HEAD returned HTTP {(int)response.StatusCode}: {artifactUri}",
                    (int)response.StatusCode);
            }
        }

        return await GetSizeByRangeAsync(artifactUri, cancellationToken).ConfigureAwait(false);
    }

    private async Task<long> GetSizeByRangeAsync(
        Uri artifactUri,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, artifactUri);
        request.Headers.Range = new RangeHeaderValue(0, 0);
        AddUserAgent(request);

        using var response = await SendAsync(request, artifactUri, cancellationToken)
            .ConfigureAwait(false);

        if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Gone)
        {
            throw NotFound(artifactUri, "artifact");
        }

        if (response.StatusCode == HttpStatusCode.PartialContent)
        {
            if (response.Content?.Headers.ContentRange?.Length is { } total && total >= 0)
            {
                return total;
            }

            throw MissingSize(artifactUri, "206 response has no Content-Range total");
        }

        if (response.IsSuccessStatusCode)
        {
            // The server ignored Range and answered 200: Content-Length is the
            // full artifact size (the body itself is never read here).
            if (response.Content?.Headers.ContentLength is { } length && length >= 0)
            {
                return length;
            }

            throw MissingSize(artifactUri, "Content-Length is missing");
        }

        throw new ForgeDownloadException(
            ForgeDownloadErrorCode.InvalidResponse,
            $"range GET returned HTTP {(int)response.StatusCode}: {artifactUri}",
            (int)response.StatusCode);
    }

    private async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        Uri uri,
        CancellationToken cancellationToken)
    {
        try
        {
            return await _httpClient
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException exception)
        {
            throw new ForgeDownloadException(
                ForgeDownloadErrorCode.NetworkError,
                $"request timed out: {uri}",
                innerException: exception);
        }
        catch (HttpRequestException exception)
        {
            throw new ForgeDownloadException(
                ForgeDownloadErrorCode.NetworkError,
                $"request failed: {uri}: {exception.Message}",
                innerException: exception);
        }
    }

    private static void EnsureSuccess(
        HttpResponseMessage response,
        Uri uri,
        string what)
    {
        if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Gone)
        {
            throw NotFound(uri, what);
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new ForgeDownloadException(
                ForgeDownloadErrorCode.InvalidResponse,
                $"{what} returned HTTP {(int)response.StatusCode}: {uri}",
                (int)response.StatusCode);
        }
    }

    private static ForgeDownloadException NotFound(Uri uri, string what)
        => new(
            ForgeDownloadErrorCode.NotFound,
            $"{what} was not found: {uri}",
            (int)HttpStatusCode.NotFound);

    private static ForgeDownloadException MissingSize(Uri uri, string detail)
        => new(
            ForgeDownloadErrorCode.MissingSize,
            $"cannot determine the size of {uri}: {detail}");

    private void AddUserAgent(HttpRequestMessage request)
    {
        if (_userAgent is not null)
        {
            request.Headers.TryAddWithoutValidation("User-Agent", _userAgent);
        }
    }

    private static bool IsHex(string value)
    {
        foreach (var character in value)
        {
            if (!Uri.IsHexDigit(character))
            {
                return false;
            }
        }

        return true;
    }
}
