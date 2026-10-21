using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Archoera.Application.Resources;
using Archoera.Domain.Downloads;
using Archoera.Domain.Resources;

namespace Archoera.Infrastructure.Resources.Modrinth;

/// <summary>
/// Rate-limit state reported by Modrinth (<c>X-Ratelimit-*</c>). Reset is the
/// number of seconds until the window rolls over.
/// </summary>
public sealed record ModrinthRateLimit(int Limit, int Remaining, int ResetSeconds);

/// <summary>
/// Modrinth API v2 client. Read-only: search, project, version. Never downloads
/// files and never mirrors them; CDN URLs are returned verbatim.
/// </summary>
/// <remarks>
/// <para>
/// Only 429 is retried (using <c>Retry-After</c>); every other 4xx fails
/// immediately with a stable code. Nothing here resolves dependencies
/// recursively — <see cref="ResourceVersion.Dependencies"/> is returned for the
/// caller.
/// </para>
/// <para>
/// The <see cref="HttpClient"/> is injected and never disposed by this type.
/// </para>
/// </remarks>
public sealed class ModrinthClient : IResourceProvider
{
    /// <summary>Provider id used in DTOs and logs.</summary>
    public const string ProviderId = "modrinth";

    private static readonly MediaTypeWithQualityHeaderValue AcceptJson =
        new("application/json");

    private readonly HttpClient _httpClient;
    private readonly ModrinthClientOptions _options;
    private ModrinthRateLimit? _lastRateLimit;

    /// <summary>Creates a client over an injected <see cref="HttpClient"/>.</summary>
    public ModrinthClient(HttpClient httpClient, ModrinthClientOptions options)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(options);

        _httpClient = httpClient;
        _options = options.Normalize();
    }

    /// <inheritdoc />
    public string Id => ProviderId;

    /// <summary>Most recent <c>X-Ratelimit-*</c> values, if any.</summary>
    public ModrinthRateLimit? LastRateLimit => Volatile.Read(ref _lastRateLimit);

    /// <inheritdoc />
    public async Task<ResourceSearchResult> SearchAsync(
        ResourceSearchRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateSearch(request);

        using var document = await SendJsonAsync(
            BuildSearchPath(request),
            cancellationToken).ConfigureAwait(false);

        return ModrinthJson.ParseSearch(document.RootElement, request);
    }

    /// <inheritdoc />
    public async Task<ResourceProject> GetProjectAsync(
        string projectIdOrSlug,
        CancellationToken cancellationToken = default)
    {
        var id = RequireId(projectIdOrSlug, nameof(projectIdOrSlug));

        using var document = await SendJsonAsync(
            $"project/{Uri.EscapeDataString(id)}",
            cancellationToken).ConfigureAwait(false);

        return ModrinthJson.ParseProject(document.RootElement);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ResourceVersion>> GetVersionsAsync(
        string projectIdOrSlug,
        ResourceKind kind,
        ResourceTarget target,
        CancellationToken cancellationToken = default)
    {
        var id = RequireId(projectIdOrSlug, nameof(projectIdOrSlug));
        ArgumentNullException.ThrowIfNull(target);

        if (!target.IsCompleteFor(kind))
        {
            throw new ResourceProviderException(
                ResourceErrorCode.InvalidRequest,
                $"target is incomplete for {kind.ToWire()}: " +
                "set the instance loader (mods) or shader loader (shaders)");
        }

        var path = $"project/{Uri.EscapeDataString(id)}/version{BuildVersionQuery(kind, target)}";

        using var document = await SendJsonAsync(path, cancellationToken)
            .ConfigureAwait(false);

        var versions = ModrinthJson.ParseVersions(document.RootElement);
        var compatible = new List<ResourceVersion>();
        foreach (var version in versions)
        {
            if (ResourceCompatibility.IsCompatible(version, kind, target))
            {
                compatible.Add(version);
            }
        }

        // Never fall back to "latest": an empty list means no compatible build.
        // NEXT STEP: dependency recursion lives above this layer (docs/08 §3.3).
        return compatible;
    }

    /// <inheritdoc />
    public async Task<ResourceVersion> GetVersionAsync(
        string versionId,
        CancellationToken cancellationToken = default)
    {
        var id = RequireId(versionId, nameof(versionId));

        using var document = await SendJsonAsync(
            $"version/{Uri.EscapeDataString(id)}",
            cancellationToken).ConfigureAwait(false);

        return ModrinthJson.ParseVersion(document.RootElement);
    }

    /// <summary>
    /// Lists every version without compatibility filtering. Used for modpacks,
    /// whose target game/loader is carried by the selected version itself.
    /// </summary>
    public async Task<IReadOnlyList<ResourceVersion>> GetAllVersionsAsync(
        string projectIdOrSlug,
        CancellationToken cancellationToken = default)
    {
        var id = RequireId(projectIdOrSlug, nameof(projectIdOrSlug));
        using var document = await SendJsonAsync(
            $"project/{Uri.EscapeDataString(id)}/version",
            cancellationToken).ConfigureAwait(false);
        return ModrinthJson.ParseVersions(document.RootElement);
    }

    /// <summary>
    /// Builds a one-file plan for a selected version/file. Convenience wrapper
    /// around <see cref="ResourceDownloadPlanBuilder"/>; the caller still owns
    /// project/version selection and the absolute root.
    /// </summary>
    public DownloadPlan BuildDownloadPlan(
        string root,
        ResourceKind kind,
        ResourceTarget target,
        ResourceVersion version,
        ResourceFile? file = null)
        => ResourceDownloadPlanBuilder.Build(root, kind, target, version, file);

    private static void ValidateSearch(ResourceSearchRequest request)
    {
        if (request.Page < 0)
        {
            throw new ResourceProviderException(
                ResourceErrorCode.InvalidRequest,
                "page must not be negative");
        }

        if (request.PageSize is < 1 or > 100)
        {
            throw new ResourceProviderException(
                ResourceErrorCode.InvalidRequest,
                "pageSize must be between 1 and 100");
        }

        if ((long)request.Page * request.PageSize > 10_000)
        {
            throw new ResourceProviderException(
                ResourceErrorCode.InvalidRequest,
                "page * pageSize exceeds the Modrinth offset window (10000)");
        }

        if (request.Target is not null && !request.Target.IsCompleteFor(request.Kind))
        {
            throw new ResourceProviderException(
                ResourceErrorCode.InvalidRequest,
                $"target is incomplete for {request.Kind.ToWire()}");
        }
    }

    private static string RequireId(string? value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ResourceProviderException(
                ResourceErrorCode.InvalidRequest,
                $"{parameterName} is required");
        }

        return value.Trim();
    }

    private static string BuildSearchPath(ResourceSearchRequest request)
    {
        var parameters = new List<string>
        {
            "query=" + Uri.EscapeDataString(request.Query ?? string.Empty),
            "index=" + request.Sort.ToWire(),
            "offset=" + (request.Page * request.PageSize),
            "limit=" + request.PageSize,
            "facets=" + Uri.EscapeDataString(BuildSearchFacets(request)),
        };

        return "search?" + string.Join('&', parameters);
    }

    private static string BuildSearchFacets(ResourceSearchRequest request)
    {
        var groups = new List<IReadOnlyList<string>>
        {
            new[] { $"project_type:{request.Kind.ToWire()}" },
        };

        var loaders = new List<string>();
        var versions = new List<string>();

        if (request.Target is { } target)
        {
            if (!string.IsNullOrWhiteSpace(target.GameVersion))
            {
                versions.Add(target.GameVersion.Trim());
            }

            switch (request.Kind)
            {
                case ResourceKind.Mod when target.NormalizedLoader is { } loader:
                    loaders.Add(loader);
                    break;
                case ResourceKind.Shader when target.NormalizedShaderLoader is { } shader:
                    loaders.Add(shader);
                    break;
                case ResourceKind.ResourcePack:
                    loaders.Add("minecraft");
                    break;
                case ResourceKind.Modpack when target.NormalizedLoader is { } packLoader:
                    loaders.Add(packLoader);
                    break;
            }
        }

        foreach (var loader in request.Loaders)
        {
            AddNormalized(loaders, loader);
        }

        foreach (var version in request.GameVersions)
        {
            if (!string.IsNullOrWhiteSpace(version))
            {
                versions.Add(version.Trim());
            }
        }

        var loaderFacets = loaders
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(loader => $"categories:{loader}")
            .ToArray();

        if (loaderFacets.Length > 0)
        {
            groups.Add(loaderFacets);
        }

        var versionFacets = versions
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(version => $"versions:{version}")
            .ToArray();

        if (versionFacets.Length > 0)
        {
            groups.Add(versionFacets);
        }

        foreach (var category in request.Categories)
        {
            if (!string.IsNullOrWhiteSpace(category))
            {
                groups.Add(new[] { $"categories:{category.Trim()}" });
            }
        }

        return WriteJsonArrayOfArrays(groups);
    }

    private static string BuildVersionQuery(ResourceKind kind, ResourceTarget target)
    {
        var loaders = new List<string>();
        switch (kind)
        {
            case ResourceKind.Mod when target.NormalizedLoader is { } loader:
                loaders.Add(loader);
                break;
            case ResourceKind.Shader when target.NormalizedShaderLoader is { } shader:
                loaders.Add(shader);
                break;
            case ResourceKind.ResourcePack:
                loaders.Add("minecraft");
                break;
            case ResourceKind.Modpack when target.NormalizedLoader is { } packLoader:
                loaders.Add(packLoader);
                break;
        }

        var parts = new List<string>
        {
            "game_versions=" + Uri.EscapeDataString(
                WriteJsonArray([target.GameVersion.Trim()])),
        };

        if (loaders.Count > 0)
        {
            parts.Add("loaders=" + Uri.EscapeDataString(WriteJsonArray(loaders)));
        }

        return "?" + string.Join('&', parts);
    }

    private static void AddNormalized(List<string> values, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            values.Add(value.Trim().ToLowerInvariant());
        }
    }

    private static string WriteJsonArray(IReadOnlyList<string> values)
    {
        var buffer = new MemoryStream(64);
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartArray();
            foreach (var value in values)
            {
                writer.WriteStringValue(value);
            }

            writer.WriteEndArray();
        }

        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    private static string WriteJsonArrayOfArrays(IReadOnlyList<IReadOnlyList<string>> groups)
    {
        var buffer = new MemoryStream(128);
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartArray();
            foreach (var group in groups)
            {
                writer.WriteStartArray();
                foreach (var value in group)
                {
                    writer.WriteStringValue(value);
                }

                writer.WriteEndArray();
            }

            writer.WriteEndArray();
        }

        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    private async Task<JsonDocument> SendJsonAsync(
        string relativePath,
        CancellationToken cancellationToken)
    {
        var uri = new Uri(_options.BaseAddress, relativePath);

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
                throw new ResourceProviderException(
                    ResourceErrorCode.Network,
                    $"Modrinth request timed out after {_options.RequestTimeout.TotalSeconds:0}s");
            }
            catch (HttpRequestException exception)
            {
                throw new ResourceProviderException(
                    ResourceErrorCode.Network,
                    $"Modrinth request failed: {exception.Message}",
                    innerException: exception);
            }

            using (response)
            {
                CaptureRateLimit(response);
                var status = (int)response.StatusCode;

                if (status == (int)HttpStatusCode.TooManyRequests)
                {
                    var retryAfter = ReadRetryAfter(response);
                    if (attempt < _options.RateLimitRetries)
                    {
                        await Task.Delay(retryAfter, cancellationToken).ConfigureAwait(false);
                        continue;
                    }

                    throw new ResourceProviderException(
                        ResourceErrorCode.RateLimited,
                        $"Modrinth rate limit exceeded (retry after " +
                        $"{retryAfter.TotalSeconds:0.#}s)",
                        status,
                        retryAfter);
                }

                if (status == (int)HttpStatusCode.Forbidden)
                {
                    throw new ResourceProviderException(
                        ResourceErrorCode.DistributionDenied,
                        "Modrinth denied access to this resource (403)",
                        status);
                }

                if (status is (int)HttpStatusCode.Unauthorized
                    or (int)HttpStatusCode.ProxyAuthenticationRequired)
                {
                    throw new ResourceProviderException(
                        ResourceErrorCode.ProviderAuth,
                        "Modrinth credentials were rejected",
                        status);
                }

                if (status == (int)HttpStatusCode.NotFound)
                {
                    throw new ResourceProviderException(
                        ResourceErrorCode.NotFound,
                        "Modrinth resource was not found (404)",
                        status);
                }

                if (status >= 500)
                {
                    // No automatic retry in this slice; 5xx is reported as a
                    // transient network failure so the UI can offer "retry".
                    throw new ResourceProviderException(
                        ResourceErrorCode.Network,
                        $"Modrinth returned HTTP {status}",
                        status);
                }

                if (!response.IsSuccessStatusCode)
                {
                    throw new ResourceProviderException(
                        ResourceErrorCode.InvalidResponse,
                        $"Modrinth returned unexpected HTTP {status}",
                        status);
                }

                try
                {
                    var stream = await response.Content
                        .ReadAsStreamAsync(timeout.Token)
                        .ConfigureAwait(false);

                    return await JsonDocument
                        .ParseAsync(stream, cancellationToken: timeout.Token)
                        .ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    throw new ResourceProviderException(
                        ResourceErrorCode.Network,
                        $"Modrinth response was not read within " +
                        $"{_options.RequestTimeout.TotalSeconds:0}s",
                        status);
                }
                catch (IOException exception)
                {
                    throw new ResourceProviderException(
                        ResourceErrorCode.Network,
                        $"Modrinth response stream failed: {exception.Message}",
                        status,
                        innerException: exception);
                }
                catch (JsonException exception)
                {
                    throw new ResourceProviderException(
                        ResourceErrorCode.InvalidResponse,
                        $"Modrinth returned malformed JSON: {exception.Message}",
                        status,
                        innerException: exception);
                }
            }
        }
    }

    private void CaptureRateLimit(HttpResponseMessage response)
    {
        var limit = ReadIntHeader(response, "X-Ratelimit-Limit");
        var remaining = ReadIntHeader(response, "X-Ratelimit-Remaining");
        var reset = ReadIntHeader(response, "X-Ratelimit-Reset");

        if (limit is null && remaining is null && reset is null)
        {
            return;
        }

        var value = new ModrinthRateLimit(limit ?? 0, remaining ?? 0, reset ?? 0);
        Interlocked.Exchange(ref _lastRateLimit, value);
    }

    private static int? ReadIntHeader(HttpResponseMessage response, string name)
    {
        if (!response.Headers.TryGetValues(name, out var values))
        {
            return null;
        }

        foreach (var value in values)
        {
            if (int.TryParse(value, out var parsed))
            {
                return parsed;
            }
        }

        return null;
    }

    private TimeSpan ReadRetryAfter(HttpResponseMessage response)
    {
        var header = response.Headers.RetryAfter;
        TimeSpan delay;

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
            delay = _options.DefaultRetryAfter;
        }

        if (delay < TimeSpan.Zero)
        {
            delay = TimeSpan.Zero;
        }

        if (delay > _options.MaxRetryAfter)
        {
            delay = _options.MaxRetryAfter;
        }

        return delay;
    }
}
