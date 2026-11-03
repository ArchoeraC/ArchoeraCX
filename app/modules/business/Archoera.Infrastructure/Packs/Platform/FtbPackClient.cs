using System.Text;
using System.Text.Json;
using Archoera.Application.Packs;
using Archoera.Domain.Downloads;
using Archoera.Domain.Resources;

namespace Archoera.Infrastructure.Packs.Platform;

/// <summary>
/// Public Feed The Beast modpack API. No account or API key is needed; the
/// version manifest carries direct URLs, paths, sizes and SHA hashes, so packs
/// can be installed through the same validated download plan as Modrinth.
/// </summary>
internal sealed class FtbPackClient : IPlatformPackProvider
{
    public const string ProviderId = "ftb";
    public string Id => ProviderId;
    public string DisplayName => "FTB";

    private const int MaxDetailRequests = 8;
    private const int MaxMetadataBytes = 16 * 1024 * 1024;

    private readonly HttpClient _http;
    private readonly Uri _base;

    public FtbPackClient(HttpClient? http = null, Uri? baseAddress = null)
    {
        _http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(45) };
        _base = baseAddress ??
                new Uri("https://api.feed-the-beast.com/v1/modpacks/public/");
    }

    public async Task<(IReadOnlyList<PlatformPackSummary> Items, int Total)> SearchAsync(
        string query,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var path = string.IsNullOrWhiteSpace(query)
            ? "modpack/featured/1000"
            : $"modpack/search/1000?term={Uri.EscapeDataString(query.Trim())}";
        var root = await GetJsonAsync(path, cancellationToken).ConfigureAwait(false);
        var ids = new List<int>();
        if (root.TryGetProperty("packs", out var packs) &&
            packs.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in packs.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.Number &&
                    item.TryGetInt32(out var id))
                {
                    ids.Add(id);
                }
            }
        }

        var total = ids.Count;
        var slice = ids
            .Skip(Math.Max(0, page * pageSize))
            .Take(pageSize)
            .ToArray();
        using var gate = new SemaphoreSlim(MaxDetailRequests);
        var tasks = slice.Select(async id =>
        {
            await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                return await GetSummaryAsync(id, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (
                exception is HttpRequestException or IOException or JsonException or
                PlatformPackException)
            {
                return null;
            }
            finally
            {
                gate.Release();
            }
        });
        var items = (await Task.WhenAll(tasks).ConfigureAwait(false))
            .Where(item => item is not null)
            .Select(item => item!)
            .ToArray();
        return (items, total);
    }

    public async Task<IReadOnlyList<PlatformPackVersion>> GetVersionsAsync(
        string packId,
        CancellationToken cancellationToken)
    {
        var root = await GetJsonAsync(
                $"modpack/{Uri.EscapeDataString(packId)}",
                cancellationToken)
            .ConfigureAwait(false);
        var versions = new List<PlatformPackVersion>();
        if (!root.TryGetProperty("versions", out var array) ||
            array.ValueKind != JsonValueKind.Array)
        {
            return versions;
        }

        foreach (var version in array.EnumerateArray())
        {
            if (version.ValueKind != JsonValueKind.Object) continue;
            var versionId = String(version, "id") ??
                            Long(version, "id")?.ToString();
            var name = String(version, "name") ?? versionId ?? string.Empty;
            if (string.IsNullOrWhiteSpace(versionId)) continue;
            var (game, loader, loaderVersion) = ReadTargets(version);
            if (string.IsNullOrWhiteSpace(game)) continue;
            versions.Add(new PlatformPackVersion(
                ProviderId,
                packId,
                versionId,
                name,
                game,
                loader,
                loaderVersion,
                String(version, "type") ?? "release",
                Date(version, "released") ?? Date(version, "updated")));
        }

        return versions;
    }

    public async Task<ModpackIndex> BuildIndexAsync(
        string packId,
        string versionId,
        CancellationToken cancellationToken)
    {
        var detail = await GetJsonAsync(
                $"modpack/{Uri.EscapeDataString(packId)}",
                cancellationToken)
            .ConfigureAwait(false);
        var manifest = await GetJsonAsync(
                $"modpack/{Uri.EscapeDataString(packId)}/{Uri.EscapeDataString(versionId)}",
                cancellationToken)
            .ConfigureAwait(false);

        var (game, loader, loaderVersion) = ReadTargets(manifest);
        if (string.IsNullOrWhiteSpace(game))
        {
            throw new PlatformPackException(
                "PACK_LOADER_METADATA_FAILED",
                "FTB version manifest has no Minecraft target");
        }

        var files = new List<ModpackFile>();
        var seen = new HashSet<string>(Archoera.Domain.PathSemantics.Comparer);
        if (manifest.TryGetProperty("files", out var array) &&
            array.ValueKind == JsonValueKind.Array)
        {
            foreach (var file in array.EnumerateArray())
            {
                if (file.ValueKind != JsonValueKind.Object) continue;
                if (Bool(file, "optional")) continue;
                var fileName = String(file, "name");
                var url = String(file, "url");
                var size = Long(file, "size") ?? 0;
                if (string.IsNullOrWhiteSpace(fileName) ||
                    string.IsNullOrWhiteSpace(url) ||
                    size <= 0)
                {
                    continue;
                }

                var rawPath = Combine(
                    String(file, "path") ?? string.Empty,
                    fileName!);
                var issue = DownloadPlanPaths.TryNormalizeDest(rawPath, out var path);
                if (issue is not null)
                {
                    throw new PlatformPackException(
                        issue.Code == DownloadPlanErrorCode.PathEscape
                            ? PackErrorCode.PathEscape
                            : PackErrorCode.ArchiveInvalid,
                        $"{rawPath}: {issue.Message}");
                }

                if (!seen.Add(path)) continue;
                var (sha1, sha512) = ReadHashes(file);
                if (sha1 is null && sha512 is null) continue;

                var downloads = new List<string> { url };
                if (file.TryGetProperty("mirrors", out var mirrors) &&
                    mirrors.ValueKind == JsonValueKind.Array)
                {
                    foreach (var mirror in mirrors.EnumerateArray())
                    {
                        if (mirror.ValueKind == JsonValueKind.String &&
                            mirror.GetString() is { Length: > 0 } value)
                        {
                            downloads.Add(value);
                        }
                    }
                }

                files.Add(new ModpackFile(
                    path,
                    sha1,
                    sha512,
                    size,
                    downloads,
                    Bool(file, "serveronly"),
                    Bool(file, "clientonly")));
            }
        }

        if (files.Count == 0)
        {
            throw new PlatformPackException(
                PackErrorCode.Invalid,
                "FTB version manifest contains no downloadable files");
        }

        var packName = String(detail, "name") ?? packId;
        var versionName = String(manifest, "name") ?? versionId;
        var summary = String(detail, "synopsis")
            ?? String(detail, "description")
            ?? string.Empty;
        var dependencies = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["minecraft"] = game,
        };
        if (!string.IsNullOrWhiteSpace(loader) && loader != "vanilla")
        {
            dependencies[LoaderDependency(loader)] = loaderVersion;
        }

        return new ModpackIndex(
            1,
            "minecraft",
            versionName,
            packName,
            summary,
            files,
            dependencies,
            new ModpackLoader(loader, loaderVersion));
    }

    private async Task<PlatformPackSummary?> GetSummaryAsync(
        int id,
        CancellationToken cancellationToken)
    {
        var root = await GetJsonAsync($"modpack/{id}", cancellationToken)
            .ConfigureAwait(false);
        var name = String(root, "name");
        if (string.IsNullOrWhiteSpace(name)) return null;
        var icon = FindIcon(root);
        var author = FindAuthor(root);
        var slug = String(root, "slug") ?? id.ToString();
        return new PlatformPackSummary(
            ProviderId,
            id.ToString(),
            name,
            String(root, "synopsis") ?? String(root, "description") ?? string.Empty,
            icon,
            author,
            Long(root, "installs") ?? 0,
            Date(root, "updated"),
            $"https://feed-the-beast.com/modpacks/{id}-{slug}");
    }

    private async Task<JsonElement> GetJsonAsync(
        string path,
        CancellationToken cancellationToken)
    {
        using var response = await _http
            .GetAsync(new Uri(_base, path), HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new PlatformPackException(
                "PLATFORM_NETWORK_FAILED",
                $"FTB returned {(int)response.StatusCode}");
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
                    "FTB response is too large");
            }

            memory.Write(buffer, 0, read);
        }

        using var document = JsonDocument.Parse(memory.ToArray());
        return document.RootElement.Clone();
    }

    private static (string Game, string Loader, string LoaderVersion) ReadTargets(
        JsonElement root)
    {
        string game = string.Empty;
        string loader = "vanilla";
        string loaderVersion = string.Empty;
        if (!root.TryGetProperty("targets", out var targets) ||
            targets.ValueKind != JsonValueKind.Array)
        {
            return (game, loader, loaderVersion);
        }

        foreach (var target in targets.EnumerateArray())
        {
            if (target.ValueKind != JsonValueKind.Object) continue;
            var type = (String(target, "type") ?? string.Empty).ToLowerInvariant();
            if (type == "game")
            {
                game = String(target, "version") ?? string.Empty;
            }
            else if (type == "modloader")
            {
                var name = (String(target, "name") ?? string.Empty).ToLowerInvariant();
                loader = name switch
                {
                    "fabric" => "fabric",
                    "quilt" => "quilt",
                    "forge" => "forge",
                    "neoforge" => "neoforge",
                    "vanilla" or "" => "vanilla",
                    _ => throw new PlatformPackException(
                        PackErrorCode.LoaderUnsupported,
                        $"FTB loader '{name}' is not supported"),
                };
                loaderVersion = String(target, "version") ?? string.Empty;
            }
        }

        return (game, loader, loaderVersion);
    }

    private static string LoaderDependency(string loader) => loader switch
    {
        "fabric" => "fabric-loader",
        "quilt" => "quilt-loader",
        "forge" => "forge",
        "neoforge" => "neoforge",
        _ => loader,
    };

    private static string Combine(string path, string name)
    {
        var folder = path.Replace('\\', '/').Trim();
        while (folder.StartsWith("./", StringComparison.Ordinal))
        {
            folder = folder[2..];
        }

        folder = folder.Trim('/');
        var cleanName = name.Replace('\\', '/').TrimStart('/');
        return folder.Length == 0 ? cleanName : $"{folder}/{cleanName}";
    }

    private static (string? Sha1, string? Sha512) ReadHashes(JsonElement file)
    {
        string? sha1 = String(file, "sha1");
        string? sha512 = null;
        if (file.TryGetProperty("hashes", out var hashes) &&
            hashes.ValueKind == JsonValueKind.Object)
        {
            sha1 ??= String(hashes, "sha1");
            sha512 = String(hashes, "sha512");
        }

        return (
            sha1 is { Length: 40 } && sha1.All(IsLowerHex) ? sha1 : null,
            sha512 is { Length: 128 } && sha512.All(IsLowerHex) ? sha512 : null);
    }

    private static string? FindIcon(JsonElement root)
    {
        if (!root.TryGetProperty("art", out var art) ||
            art.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        string? fallback = null;
        foreach (var item in art.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object) continue;
            var url = String(item, "url");
            if (string.IsNullOrWhiteSpace(url)) continue;
            fallback ??= url;
            if (string.Equals(String(item, "type"), "square", StringComparison.OrdinalIgnoreCase))
            {
                return url;
            }
        }

        return fallback;
    }

    private static string? FindAuthor(JsonElement root)
    {
        if (!root.TryGetProperty("authors", out var authors) ||
            authors.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var item in authors.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.Object &&
                String(item, "name") is { Length: > 0 } name)
            {
                return name;
            }
        }

        return null;
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

    private static bool Bool(JsonElement root, string name)
        => root.ValueKind == JsonValueKind.Object &&
           root.TryGetProperty(name, out var element) &&
           element.ValueKind == JsonValueKind.True;

    private static DateTimeOffset? Date(JsonElement root, string name)
    {
        var value = Long(root, name);
        return value is > 0
            ? DateTimeOffset.FromUnixTimeSeconds(value.Value)
            : null;
    }

    private static bool IsLowerHex(char value)
        => value is (>= '0' and <= '9') or (>= 'a' and <= 'f');
}

/// <summary>Stable error used by platform pack providers.</summary>
public sealed class PlatformPackException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}
