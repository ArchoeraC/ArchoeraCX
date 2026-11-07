using System.Text.Json;
using Archoera.Application.Packs;
using Archoera.Domain.Downloads;

namespace Archoera.Infrastructure.Packs.Platform;

/// <summary>
/// Public Technic Platform / Solder API. Solder manifests provide direct mod
/// URLs, sizes and MD5 digests; MD5 is accepted for this legacy platform only.
/// </summary>
internal sealed class TechnicPackClient : IPlatformPackProvider, IPlatformPackArchiveProvider
{
    public const string ProviderId = "technic";
    public string Id => ProviderId;
    public string DisplayName => "Technic";

    private const int MaxMetadataBytes = 16 * 1024 * 1024;
    private const string Build = "archoeracx";

    private readonly HttpClient _http;
    private readonly Uri _base;

    public TechnicPackClient(HttpClient? http = null, Uri? baseAddress = null)
    {
        _http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(45) };
        _base = baseAddress ?? new Uri("https://api.technicpack.net/");
    }

    public async Task<(IReadOnlyList<PlatformPackSummary> Items, int Total)> SearchAsync(
        string query,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var path = string.IsNullOrWhiteSpace(query)
            ? $"trending?build={Build}"
            : $"search?q={Uri.EscapeDataString(query.Trim())}&build={Build}";
        var root = await GetJsonAsync(path, cancellationToken).ConfigureAwait(false);
        var items = new List<PlatformPackSummary>();
        if (!root.TryGetProperty("modpacks", out var array) ||
            array.ValueKind != JsonValueKind.Array)
        {
            return (items, 0);
        }

        var all = array.EnumerateArray()
            .Where(item => item.ValueKind == JsonValueKind.Object)
            .ToArray();
        foreach (var item in all.Skip(Math.Max(0, page * pageSize)).Take(pageSize))
        {
            var id = String(item, "id");
            var name = String(item, "name");
            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            items.Add(new PlatformPackSummary(
                ProviderId,
                String(item, "slug") ?? id,
                name,
                string.Empty,
                String(item, "iconUrl"),
                null,
                0,
                null,
                String(item, "url")));
        }

        return (items, all.Length);
    }

    public async Task<IReadOnlyList<PlatformPackVersion>> GetVersionsAsync(
        string packSlug,
        CancellationToken cancellationToken)
    {
        var detail = await GetJsonAsync(
                $"modpack/{Uri.EscapeDataString(packSlug)}?build={Build}",
                cancellationToken)
            .ConfigureAwait(false);
        var solder = String(detail, "solder");
        if (string.IsNullOrWhiteSpace(solder))
        {
            // Non-Solder Technic packs only publish the current version and a
            // direct zip. Show that version so the UI is not empty; install
            // reads the archive's bin metadata to detect the loader.
            var current = String(detail, "version");
            return string.IsNullOrWhiteSpace(current)
                ? []
                : [
                    new PlatformPackVersion(
                        ProviderId,
                        packSlug,
                        current,
                        current,
                        String(detail, "minecraft") ?? string.Empty,
                        "unknown",
                        string.Empty,
                        DirectArchive: true),
                ];
        }

        var solderRoot = new Uri(solder.EndsWith('/') ? solder : solder + "/");
        var meta = await GetJsonAsync(
                new Uri(solderRoot, $"modpack/{Uri.EscapeDataString(packSlug)}"),
                cancellationToken)
            .ConfigureAwait(false);
        var builds = new List<string>();
        if (meta.TryGetProperty("builds", out var buildsElement) &&
            buildsElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var build in buildsElement.EnumerateArray())
            {
                if (build.ValueKind == JsonValueKind.String &&
                    build.GetString() is { Length: > 0 } value)
                {
                    builds.Add(value);
                }
            }
        }

        if (builds.Count == 0)
        {
            var single = String(meta, "latest") ?? String(meta, "recommended");
            if (!string.IsNullOrWhiteSpace(single)) builds.Add(single);
        }

        var fallbackGame = String(detail, "minecraft") ?? string.Empty;
        using var gate = new SemaphoreSlim(6);
        var tasks = builds.Take(100).Select(async build =>
        {
            await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var manifest = await GetJsonAsync(
                        new Uri(
                            solderRoot,
                            $"modpack/{Uri.EscapeDataString(packSlug)}/{Uri.EscapeDataString(build)}"),
                        cancellationToken)
                    .ConfigureAwait(false);
                return new PlatformPackVersion(
                    ProviderId,
                    packSlug,
                    build,
                    build,
                    String(manifest, "minecraft") ?? fallbackGame,
                    string.IsNullOrWhiteSpace(String(manifest, "forge")) ? "vanilla" : "forge",
                    String(manifest, "forge") ?? string.Empty);
            }
            catch (Exception exception) when (
                exception is HttpRequestException or IOException or JsonException or
                PlatformPackException)
            {
                return new PlatformPackVersion(
                    ProviderId,
                    packSlug,
                    build,
                    build,
                    fallbackGame,
                    "forge",
                    string.Empty);
            }
            finally
            {
                gate.Release();
            }
        });

        return (await Task.WhenAll(tasks).ConfigureAwait(false))
            .Where(version => version is not null)
            .Select(version => version!)
            .ToArray();
    }

    /// <summary>
    /// Non-Solder packs expose a direct zip and no per-file manifest. Return
    /// that archive so the installer can download it and detect the legacy
    /// Technic layout (<c>bin/modpack.jar</c> or <c>bin/version.json</c>).
    /// </summary>
    public async Task<PlatformPackArchive?> ResolveArchiveAsync(
        string packId,
        string versionId,
        CancellationToken cancellationToken)
    {
        var detail = await GetJsonAsync(
                $"modpack/{Uri.EscapeDataString(packId)}?build={Build}",
                cancellationToken)
            .ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(String(detail, "solder")))
        {
            return null;
        }

        var url = String(detail, "url");
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps)
        {
            throw new PlatformPackException(
                "PLATFORM_METADATA_INVALID",
                "Technic did not return a valid HTTPS pack archive");
        }

        var size = await GetArchiveSizeAsync(uri, cancellationToken)
            .ConfigureAwait(false);
        var version = String(detail, "version") ?? versionId;
        return new PlatformPackArchive(
            uri.AbsoluteUri,
            version,
            String(detail, "displayName") ?? String(detail, "name") ?? packId,
            String(detail, "description") ?? string.Empty,
            String(detail, "minecraft") ?? string.Empty,
            size);
    }

    /// <summary>
    /// HEAD first, then a one-byte Range request. Some CDNs only expose the
    /// total size on a Range response; neither request reads the body.
    /// </summary>
    private async Task<long> GetArchiveSizeAsync(
        Uri uri,
        CancellationToken cancellationToken)
    {
        var timedOut = false;
        var networkFailed = false;
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(20));
            using var head = new HttpRequestMessage(HttpMethod.Head, uri);
            using var response = await _http
                .SendAsync(
                    head,
                    HttpCompletionOption.ResponseHeadersRead,
                    timeout.Token)
                .ConfigureAwait(false);
            if (response.IsSuccessStatusCode &&
                response.Content.Headers.ContentLength is { } length &&
                length > 0)
            {
                return length;
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            timedOut = true;
        }
        catch (Exception exception) when (
            exception is HttpRequestException or IOException)
        {
            networkFailed = true;
            // Fall through to the Range probe.
        }

        if (!timedOut)
        {
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken);
                timeout.CancelAfter(TimeSpan.FromSeconds(20));
                using var request = new HttpRequestMessage(HttpMethod.Get, uri);
                request.Headers.Range =
                    new System.Net.Http.Headers.RangeHeaderValue(0, 0);
                using var response = await _http
                    .SendAsync(
                        request,
                        HttpCompletionOption.ResponseHeadersRead,
                        timeout.Token)
                    .ConfigureAwait(false);
                if (response.IsSuccessStatusCode)
                {
                    var total = response.Content.Headers.ContentRange?.Length;
                    if (total is > 0) return total.Value;
                    if (response.Content.Headers.ContentLength is { } length &&
                        length > 0)
                    {
                        // A 200 means the server ignored Range but still reported
                        // the full body length; a 206 reports one byte plus the
                        // total in Content-Range above.
                        return length;
                    }
                }
            }
            catch (OperationCanceledException) when (
                !cancellationToken.IsCancellationRequested)
            {
                timedOut = true;
            }
            catch (Exception exception) when (
                exception is HttpRequestException or IOException)
            {
                networkFailed = true;
                // Report the shared network error below.
            }
        }

        if (timedOut || networkFailed)
        {
            throw new PlatformPackException(
                "PLATFORM_NETWORK_FAILED",
                $"{uri.Host} could not be reached while probing the pack archive");
        }

        throw new PlatformPackException(
            "PLATFORM_METADATA_INVALID",
            "Technic pack archive size is unavailable");
    }

    public async Task<ModpackIndex> BuildIndexAsync(
        string packSlug,
        string build,
        CancellationToken cancellationToken)
    {
        var detail = await GetJsonAsync(
                $"modpack/{Uri.EscapeDataString(packSlug)}?build={Build}",
                cancellationToken)
            .ConfigureAwait(false);
        var solder = String(detail, "solder")
            ?? throw new PlatformPackException(
                "PLATFORM_UNSUPPORTED",
                "This Technic pack publishes only a direct zip and no Solder manifest; install it locally for now.");
        var solderRoot = new Uri(solder.EndsWith('/') ? solder : solder + "/");
        var manifest = await GetJsonAsync(
                new Uri(
                    solderRoot,
                    $"modpack/{Uri.EscapeDataString(packSlug)}/{Uri.EscapeDataString(build)}"),
                cancellationToken)
            .ConfigureAwait(false);

        var gameVersion = String(manifest, "minecraft")
            ?? String(detail, "minecraft")
            ?? throw new PlatformPackException(
                "PACK_LOADER_METADATA_FAILED",
                "Technic manifest has no Minecraft version");
        var forgeVersion = String(manifest, "forge") ?? string.Empty;
        var files = new List<ModpackFile>();
        var seen = new HashSet<string>(Archoera.Domain.PathSemantics.Comparer);
        if (manifest.TryGetProperty("mods", out var mods) &&
            mods.ValueKind == JsonValueKind.Array)
        {
            foreach (var mod in mods.EnumerateArray())
            {
                if (mod.ValueKind != JsonValueKind.Object) continue;
                var url = String(mod, "url");
                var md5 = String(mod, "md5")?.ToLowerInvariant();
                var size = Long(mod, "filesize") ?? 0;
                if (string.IsNullOrWhiteSpace(url) ||
                    md5 is not { Length: 32 } ||
                    !md5.All(IsLowerHex) ||
                    size <= 0)
                {
                    continue;
                }

                var fileName = FileNameFromUrl(url);
                if (string.IsNullOrWhiteSpace(fileName)) continue;
                var path = $"mods/{fileName}";
                var issue = DownloadPlanPaths.TryNormalizeDest(path, out var normalized);
                if (issue is not null)
                {
                    throw new PlatformPackException(
                        PackErrorCode.Invalid,
                        $"{path}: {issue.Message}");
                }

                if (!seen.Add(normalized)) continue;
                files.Add(new ModpackFile(
                    normalized,
                    null,
                    null,
                    size,
                    [url],
                    false,
                    false,
                    md5));
            }
        }

        if (files.Count == 0)
        {
            throw new PlatformPackException(
                PackErrorCode.Invalid,
                "Technic manifest contains no usable mods");
        }

        var name = String(detail, "displayName") ?? String(detail, "name") ?? packSlug;
        var summary = String(detail, "description") ?? string.Empty;
        var loader = string.IsNullOrWhiteSpace(forgeVersion) ? "vanilla" : "forge";
        var dependencies = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["minecraft"] = gameVersion,
        };
        if (loader == "forge") dependencies["forge"] = forgeVersion;
        return new ModpackIndex(
            1,
            "minecraft",
            build,
            name,
            summary,
            files,
            dependencies,
            new ModpackLoader(loader, forgeVersion));
    }

    private async Task<JsonElement> GetJsonAsync(
        string path,
        CancellationToken cancellationToken)
        => await GetJsonAsync(new Uri(_base, path), cancellationToken).ConfigureAwait(false);

    private async Task<JsonElement> GetJsonAsync(
        Uri uri,
        CancellationToken cancellationToken)
    {
        HttpResponseMessage response;
        try
        {
            response = await _http
                .GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new PlatformPackException(
                "PLATFORM_NETWORK_FAILED",
                $"{uri.Host} timed out");
        }
        catch (HttpRequestException exception)
        {
            throw new PlatformPackException(
                "PLATFORM_NETWORK_FAILED",
                $"{uri.Host} is unreachable: {exception.Message}");
        }

        using var responseScope = response;
        if (!response.IsSuccessStatusCode)
        {
            throw new PlatformPackException(
                "PLATFORM_NETWORK_FAILED",
                $"Technic returned {(int)response.StatusCode}");
        }

        await using var stream = await response.Content
            .ReadAsStreamAsync(cancellationToken)
            .ConfigureAwait(false);
        using var memory = new MemoryStream();
        var buffer = new byte[16 * 1024];
        while (true)
        {
            var read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read == 0) break;
            if (memory.Length + read > MaxMetadataBytes)
            {
                throw new PlatformPackException(
                    "PLATFORM_METADATA_INVALID",
                    "Technic response is too large");
            }

            memory.Write(buffer, 0, read);
        }

        using var document = JsonDocument.Parse(memory.ToArray());
        return document.RootElement.Clone();
    }

    private static string? FileNameFromUrl(string url)
    {
        try
        {
            var path = new Uri(url).AbsolutePath;
            var name = path[(path.LastIndexOf('/') + 1)..];
            return Uri.UnescapeDataString(name);
        }
        catch (UriFormatException)
        {
            return null;
        }
    }

    private static string? String(JsonElement root, string name)
        => root.ValueKind == JsonValueKind.Object &&
           root.TryGetProperty(name, out var element) &&
           element.ValueKind == JsonValueKind.String
            ? element.GetString()
            : null;

    private static long? Long(JsonElement root, string name)
        => root.ValueKind == JsonValueKind.Object &&
           root.TryGetProperty(name, out var element) &&
           element.ValueKind == JsonValueKind.Number &&
           element.TryGetInt64(out var value)
            ? value
            : null;

    private static bool IsLowerHex(char value)
        => value is (>= '0' and <= '9') or (>= 'a' and <= 'f');
}
