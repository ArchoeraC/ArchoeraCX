using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using Archoera.Application.Mojang;
using Archoera.Domain.Downloads;
using Archoera.Domain.Mojang;

namespace Archoera.Infrastructure.Mojang;

/// <summary>
/// Metadata-only Mojang client. It fetches the version manifest, version JSON
/// and asset index, verifies every published SHA-1/size and parses with
/// <see cref="MojangJson"/>; it never downloads client jars, libraries or
/// assets. The injected <see cref="HttpClient"/> is not disposed by this type.
/// </summary>
/// <remarks>
/// 429 and 5xx/transport failures are retried with capped backoff (429 honours
/// <c>Retry-After</c>). Other 4xx responses fail immediately with a stable code.
/// </remarks>
public sealed class MojangClient : IMojangMetadataClient
{
    private static readonly MediaTypeWithQualityHeaderValue AcceptJson = new("application/json");

    private readonly HttpClient _httpClient;
    private readonly MojangClientOptions _options;

    /// <summary>Creates a client over an injected <see cref="HttpClient"/>.</summary>
    public MojangClient(HttpClient httpClient, MojangClientOptions options)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(options);

        _httpClient = httpClient;
        _options = options.Normalize();
    }

    /// <inheritdoc />
    public async Task<MojangVersionManifest> GetManifestAsync(
        CancellationToken cancellationToken = default)
    {
        var uri = new Uri(_options.BaseAddress, _options.ManifestPath);
        var bytes = await SendBytesAsync(
            uri,
            "version manifest",
            expectedSha1: null,
            expectedSize: null,
            cancellationToken).ConfigureAwait(false);

        using var document = ParseJson(bytes, "version manifest");
        return MojangJson.ParseManifest(document.RootElement);
    }

    /// <inheritdoc />
    public async Task<MojangVersion> GetVersionAsync(
        string versionId,
        CancellationToken cancellationToken = default)
    {
        var id = versionId?.Trim() ?? string.Empty;
        var bytes = await GetVersionJsonAsync(id, cancellationToken)
            .ConfigureAwait(false);

        using var document = ParseJson(bytes, $"version '{id}' JSON");
        var version = MojangJson.ParseVersion(document.RootElement);

        if (!string.Equals(version.Id, id, StringComparison.Ordinal))
        {
            throw new MojangException(
                MojangErrorCode.InvalidResponse,
                $"version JSON id '{version.Id}' does not match requested '{id}'");
        }

        return version;
    }

    /// <inheritdoc />
    public async Task<byte[]> GetVersionJsonAsync(
        string versionId,
        CancellationToken cancellationToken = default)
    {
        var id = versionId?.Trim() ?? string.Empty;
        if (id.Length == 0)
        {
            throw new MojangException(MojangErrorCode.InvalidRequest, "versionId is required");
        }

        var manifest = await GetManifestAsync(cancellationToken).ConfigureAwait(false);

        MojangVersionSummary? summary = null;
        foreach (var candidate in manifest.Versions)
        {
            if (string.Equals(candidate.Id, id, StringComparison.Ordinal))
            {
                summary = candidate;
                break;
            }
        }

        if (summary is null)
        {
            throw new MojangException(
                MojangErrorCode.NotFound,
                $"version '{id}' is not present in the Mojang manifest");
        }

        var uri = ValidateUrl(summary.Url, $"version '{id}' JSON");
        var bytes = await SendBytesAsync(
            uri,
            $"version '{id}' JSON",
            summary.Sha1,
            summary.Size,
            cancellationToken).ConfigureAwait(false);

        using var document = ParseJson(bytes, $"version '{id}' JSON");
        var parsed = MojangJson.ParseVersion(document.RootElement);
        if (!string.Equals(parsed.Id, id, StringComparison.Ordinal))
        {
            throw new MojangException(
                MojangErrorCode.InvalidResponse,
                $"version JSON id '{parsed.Id}' does not match requested '{id}'");
        }

        return bytes;
    }

    /// <inheritdoc />
    public async Task<MojangAssetIndex> GetAssetIndexAsync(
        MojangAssetIndexRef index,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(index);

        var id = index.Id?.Trim() ?? string.Empty;
        if (id.Length == 0)
        {
            throw new MojangException(
                MojangErrorCode.InvalidRequest,
                "asset index id is required");
        }

        if (string.IsNullOrWhiteSpace(index.Sha1))
        {
            throw new MojangException(
                MojangErrorCode.MissingHash,
                $"asset index '{id}': sha1 is missing");
        }

        var uri = ValidateUrl(index.Url, $"asset index '{id}'");
        var bytes = await SendBytesAsync(
            uri,
            $"asset index '{id}'",
            index.Sha1,
            index.Size,
            cancellationToken).ConfigureAwait(false);

        using var document = ParseJson(bytes, $"asset index '{id}'");
        return MojangJson.ParseAssetIndex(document.RootElement, id);
    }

    private async Task<byte[]> SendBytesAsync(
        Uri uri,
        string what,
        string? expectedSha1,
        long? expectedSize,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; ; attempt++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.TryAddWithoutValidation("User-Agent", _options.UserAgent);
            request.Headers.Accept.Add(AcceptJson);

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(_options.RequestTimeout);

            HttpResponseMessage response;
            try
            {
                response = await _httpClient
                    .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                if (attempt < _options.MaxRetries)
                {
                    await Task.Delay(Backoff(attempt), cancellationToken).ConfigureAwait(false);
                    continue;
                }

                throw new MojangException(
                    MojangErrorCode.Network,
                    $"{what}: request timed out after {_options.RequestTimeout.TotalSeconds:0.#}s");
            }
            catch (HttpRequestException exception)
            {
                if (attempt < _options.MaxRetries)
                {
                    await Task.Delay(Backoff(attempt), cancellationToken).ConfigureAwait(false);
                    continue;
                }

                throw new MojangException(
                    MojangErrorCode.Network,
                    $"{what}: request failed: {exception.Message}",
                    innerException: exception);
            }

            using (response)
            {
                var status = (int)response.StatusCode;

                if (status == (int)HttpStatusCode.TooManyRequests)
                {
                    var retryAfter = ReadRetryAfter(response, attempt);
                    if (attempt < _options.MaxRetries)
                    {
                        await Task.Delay(retryAfter, cancellationToken).ConfigureAwait(false);
                        continue;
                    }

                    throw new MojangException(
                        MojangErrorCode.RateLimited,
                        $"{what}: rate limited (retry after {retryAfter.TotalSeconds:0.#}s)",
                        status,
                        retryAfter);
                }

                if (status >= 500)
                {
                    if (attempt < _options.MaxRetries)
                    {
                        await Task.Delay(Backoff(attempt), cancellationToken).ConfigureAwait(false);
                        continue;
                    }

                    throw new MojangException(
                        MojangErrorCode.Network,
                        $"{what}: Mojang returned HTTP {status}",
                        status);
                }

                if (status == (int)HttpStatusCode.NotFound)
                {
                    throw new MojangException(
                        MojangErrorCode.NotFound,
                        $"{what}: not found (404)",
                        status);
                }

                if (!response.IsSuccessStatusCode)
                {
                    throw new MojangException(
                        MojangErrorCode.InvalidResponse,
                        $"{what}: unexpected HTTP {status}",
                        status);
                }

                var contentLength = response.Content.Headers.ContentLength;
                if (contentLength is { } length && length > _options.MaxMetadataBytes)
                {
                    throw new MojangException(
                        MojangErrorCode.InvalidResponse,
                        $"{what}: response of {length} bytes exceeds the " +
                        $"{_options.MaxMetadataBytes}-byte metadata limit",
                        status);
                }

                byte[] bytes;
                try
                {
                    bytes = await response.Content
                        .ReadAsByteArrayAsync(timeout.Token)
                        .ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    if (attempt < _options.MaxRetries)
                    {
                        await Task.Delay(Backoff(attempt), cancellationToken).ConfigureAwait(false);
                        continue;
                    }

                    throw new MojangException(
                        MojangErrorCode.Network,
                        $"{what}: response body was not read within " +
                        $"{_options.RequestTimeout.TotalSeconds:0.#}s",
                        status);
                }
                catch (HttpRequestException exception)
                {
                    if (attempt < _options.MaxRetries)
                    {
                        await Task.Delay(Backoff(attempt), cancellationToken).ConfigureAwait(false);
                        continue;
                    }

                    throw new MojangException(
                        MojangErrorCode.Network,
                        $"{what}: response body failed: {exception.Message}",
                        status,
                        innerException: exception);
                }
                catch (IOException exception)
                {
                    if (attempt < _options.MaxRetries)
                    {
                        await Task.Delay(Backoff(attempt), cancellationToken).ConfigureAwait(false);
                        continue;
                    }

                    throw new MojangException(
                        MojangErrorCode.Network,
                        $"{what}: response stream failed: {exception.Message}",
                        status,
                        innerException: exception);
                }

                if (bytes.LongLength > _options.MaxMetadataBytes)
                {
                    throw new MojangException(
                        MojangErrorCode.InvalidResponse,
                        $"{what}: response of {bytes.LongLength} bytes exceeds the " +
                        $"{_options.MaxMetadataBytes}-byte metadata limit",
                        status);
                }

                Verify(bytes, expectedSha1, expectedSize, what, status);
                return bytes;
            }
        }
    }

    private static void Verify(
        byte[] bytes,
        string? expectedSha1,
        long? expectedSize,
        string what,
        int? status)
    {
        if (expectedSize is { } size && size >= 0 && bytes.LongLength != size)
        {
            throw new MojangException(
                MojangErrorCode.HashMismatch,
                $"{what}: expected {size} bytes, received {bytes.LongLength}",
                status);
        }

        if (string.IsNullOrWhiteSpace(expectedSha1))
        {
            return;
        }

        var actual = Convert.ToHexString(SHA1.HashData(bytes));
        if (!string.Equals(actual, expectedSha1.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            throw new MojangException(
                MojangErrorCode.HashMismatch,
                $"{what}: sha1 mismatch (expected {expectedSha1.Trim()}, got {actual})",
                status);
        }
    }

    private Uri ValidateUrl(string? raw, string what)
    {
        var issue = DownloadPlanPaths.TryValidateUrl(raw, out var uri);
        if (issue is not null || uri is null)
        {
            throw new MojangException(
                MojangErrorCode.InvalidResponse,
                $"{what}: {issue?.Message ?? "invalid URL"}");
        }

        return uri;
    }

    private static JsonDocument ParseJson(byte[] bytes, string what)
    {
        try
        {
            return JsonDocument.Parse(bytes);
        }
        catch (JsonException exception)
        {
            throw new MojangException(
                MojangErrorCode.InvalidResponse,
                $"{what}: malformed JSON: {exception.Message}",
                innerException: exception);
        }
        catch (ArgumentException exception)
        {
            throw new MojangException(
                MojangErrorCode.InvalidResponse,
                $"{what}: malformed UTF-8 JSON: {exception.Message}",
                innerException: exception);
        }
    }

    private TimeSpan ReadRetryAfter(HttpResponseMessage response, int attempt)
    {
        TimeSpan delay;
        var header = response.Headers.RetryAfter;

        if (header?.Delta is { } delta)
        {
            delay = delta;
        }
        else if (header?.Date is { } date)
        {
            delay = date - DateTimeOffset.UtcNow;
        }
        else
        {
            delay = Backoff(attempt);
        }

        if (delay < TimeSpan.Zero)
        {
            delay = TimeSpan.Zero;
        }

        return delay > _options.MaxRetryDelay ? _options.MaxRetryDelay : delay;
    }

    private TimeSpan Backoff(int attempt)
    {
        var exponent = Math.Min(attempt, 8);
        var milliseconds = _options.RetryDelay.TotalMilliseconds * Math.Pow(2, exponent);
        var capped = Math.Min(milliseconds, _options.MaxRetryDelay.TotalMilliseconds);
        return TimeSpan.FromMilliseconds(capped);
    }
}
