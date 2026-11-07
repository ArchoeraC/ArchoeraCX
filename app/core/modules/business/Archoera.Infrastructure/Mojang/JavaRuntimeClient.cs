using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using Archoera.Application.JavaRuntime;
using Archoera.Domain.Downloads;
using Archoera.Domain.Java;

namespace Archoera.Infrastructure.Mojang;

/// <summary>
/// Metadata-only Mojang Java-runtime client. It fetches <c>all.json</c> and the
/// per-platform runtime manifest, verifies the published SHA-1/size and parses
/// with <see cref="JavaRuntimeJson"/>; it never downloads a runtime file and
/// never decompresses LZMA. The injected <see cref="HttpClient"/> is not
/// disposed by this type.
/// </summary>
/// <remarks>
/// 429 and 5xx/transport failures are retried with capped backoff (429 honours
/// <c>Retry-After</c>). Other 4xx responses fail immediately with a stable
/// <see cref="JavaRuntimeErrorCode"/>.
/// </remarks>
public sealed class JavaRuntimeClient : IJavaRuntimeMetadataClient
{
    private static readonly MediaTypeWithQualityHeaderValue AcceptJson = new("application/json");

    private readonly HttpClient _httpClient;
    private readonly MojangClientOptions _options;
    private readonly JavaRuntimeCatalogSource _source;

    /// <summary>Creates a client over an injected <see cref="HttpClient"/>.</summary>
    /// <param name="httpClient">Transport; owned by the caller.</param>
    /// <param name="options">User agent, timeout, retry and size policy.</param>
    /// <param name="source">Optional catalog base/path override.</param>
    public JavaRuntimeClient(
        HttpClient httpClient,
        MojangClientOptions options,
        JavaRuntimeCatalogSource? source = null)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(options);

        _httpClient = httpClient;
        _options = options.Normalize();
        _source = source ?? new JavaRuntimeCatalogSource();
    }

    /// <inheritdoc />
    public async Task<JavaRuntimeCatalog> GetCatalogAsync(
        CancellationToken cancellationToken = default)
    {
        var uri = _source.Resolve();
        var bytes = await SendBytesAsync(
            uri,
            "java runtime catalog",
            expectedSha1: null,
            expectedSize: null,
            cancellationToken).ConfigureAwait(false);

        using var document = ParseJson(bytes, "java runtime catalog");
        return JavaRuntimeJson.ParseCatalog(document.RootElement);
    }

    /// <inheritdoc />
    public async Task<JavaRuntimeManifest> GetRuntimeManifestAsync(
        JavaRuntimeComponent component,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(component);

        var what = string.IsNullOrWhiteSpace(component.Name)
            ? "java runtime manifest"
            : $"java runtime manifest '{component.Name}'";

        if (component.Manifest is null)
        {
            throw new JavaRuntimeException(
                JavaRuntimeErrorCode.InvalidMetadata,
                $"{what}: catalog entry has no manifest reference");
        }

        var reference = component.Manifest;
        if (string.IsNullOrWhiteSpace(reference.Url))
        {
            throw new JavaRuntimeException(
                JavaRuntimeErrorCode.InvalidMetadata,
                $"{what}: manifest URL is missing");
        }

        if (string.IsNullOrWhiteSpace(reference.Sha1))
        {
            throw new JavaRuntimeException(
                JavaRuntimeErrorCode.MissingHash,
                $"{what}: sha1 is missing; refusing to trust an unverifiable manifest");
        }

        if (reference.Size is null)
        {
            throw new JavaRuntimeException(
                JavaRuntimeErrorCode.MissingSize,
                $"{what}: size is missing; the catalog must publish it");
        }

        var uri = ValidateUrl(reference.Url, what);
        var bytes = await SendBytesAsync(
            uri,
            what,
            reference.Sha1,
            reference.Size,
            cancellationToken).ConfigureAwait(false);

        using var document = ParseJson(bytes, what);
        return JavaRuntimeJson.ParseManifest(document.RootElement);
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

                throw new JavaRuntimeException(
                    JavaRuntimeErrorCode.Network,
                    $"{what}: request timed out after {_options.RequestTimeout.TotalSeconds:0.#}s");
            }
            catch (HttpRequestException exception)
            {
                if (attempt < _options.MaxRetries)
                {
                    await Task.Delay(Backoff(attempt), cancellationToken).ConfigureAwait(false);
                    continue;
                }

                throw new JavaRuntimeException(
                    JavaRuntimeErrorCode.Network,
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

                    throw new JavaRuntimeException(
                        JavaRuntimeErrorCode.RateLimited,
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

                    throw new JavaRuntimeException(
                        JavaRuntimeErrorCode.Network,
                        $"{what}: Mojang returned HTTP {status}",
                        status);
                }

                if (status == (int)HttpStatusCode.NotFound)
                {
                    throw new JavaRuntimeException(
                        JavaRuntimeErrorCode.NotFound,
                        $"{what}: not found (404)",
                        status);
                }

                if (!response.IsSuccessStatusCode)
                {
                    throw new JavaRuntimeException(
                        JavaRuntimeErrorCode.InvalidResponse,
                        $"{what}: unexpected HTTP {status}",
                        status);
                }

                var contentLength = response.Content.Headers.ContentLength;
                if (contentLength is { } length && length > _options.MaxMetadataBytes)
                {
                    throw new JavaRuntimeException(
                        JavaRuntimeErrorCode.InvalidResponse,
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

                    throw new JavaRuntimeException(
                        JavaRuntimeErrorCode.Network,
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

                    throw new JavaRuntimeException(
                        JavaRuntimeErrorCode.Network,
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

                    throw new JavaRuntimeException(
                        JavaRuntimeErrorCode.Network,
                        $"{what}: response stream failed: {exception.Message}",
                        status,
                        innerException: exception);
                }

                if (bytes.LongLength > _options.MaxMetadataBytes)
                {
                    throw new JavaRuntimeException(
                        JavaRuntimeErrorCode.InvalidResponse,
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
            throw new JavaRuntimeException(
                JavaRuntimeErrorCode.HashMismatch,
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
            throw new JavaRuntimeException(
                JavaRuntimeErrorCode.HashMismatch,
                $"{what}: sha1 mismatch (expected {expectedSha1.Trim()}, got {actual})",
                status);
        }
    }

    private static Uri ValidateUrl(string? raw, string what)
    {
        var issue = DownloadPlanPaths.TryValidateUrl(raw, out var uri);
        if (issue is not null || uri is null)
        {
            throw new JavaRuntimeException(
                JavaRuntimeErrorCode.InvalidResponse,
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
            throw new JavaRuntimeException(
                JavaRuntimeErrorCode.InvalidResponse,
                $"{what}: malformed JSON: {exception.Message}",
                innerException: exception);
        }
        catch (ArgumentException exception)
        {
            throw new JavaRuntimeException(
                JavaRuntimeErrorCode.InvalidResponse,
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
