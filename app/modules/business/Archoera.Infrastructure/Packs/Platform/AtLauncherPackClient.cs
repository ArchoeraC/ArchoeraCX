using System.Text;
using System.Text.Json;
using Archoera.Application.Packs;
using Archoera.Domain.Downloads;

namespace Archoera.Infrastructure.Packs.Platform;

/// <summary>
/// ATLauncher public GraphQL API. The launcher endpoint is unauthenticated;
/// pack versions expose the same Configs JSON that ATLauncher uses, including
/// direct file URLs and SHA/MD5 digests.
/// </summary>
internal sealed class AtLauncherPackClient : IPlatformPackProvider
{
    public const string ProviderId = "atlauncher";
    public string Id => ProviderId;
    public string DisplayName => "ATLauncher";

    private const int MaxMetadataBytes = 32 * 1024 * 1024;
    private const string UserAgent =
        "ATLauncher/3.4.32.0 [archoeracx/unknown] (+https://atlauncher.com)";

    private readonly HttpClient _http;
    private readonly Uri _endpoint;

    public AtLauncherPackClient(HttpClient? http = null, Uri? endpoint = null)
    {
        _http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(120) };
        _endpoint = endpoint ?? new Uri("https://api.atlauncher.com/v2/graphql");
    }

    public async Task<(IReadOnlyList<PlatformPackSummary> Items, int Total)> SearchAsync(
        string query,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        // Keep the catalogue query minimal: ATL descriptions are large and the
        // GraphQL endpoint is slow from many regions. The list only needs the
        // identity fields; version metadata is loaded on the pack page.
        var selection = "id name safeName";
        var field = string.IsNullOrWhiteSpace(query)
            ? $"packs(first: {pageSize}, skip: {page * pageSize}) {{ {selection} }}"
            : $"searchPacks(query: {JsonString(query.Trim())}, first: {pageSize}, skip: {page * pageSize}) {{ {selection} }}";
        var root = await QueryAsync(
                $"query {{ {field} }}",
                variables: null,
                cancellationToken)
            .ConfigureAwait(false);
        var arrayName = string.IsNullOrWhiteSpace(query) ? "packs" : "searchPacks";
        var items = new List<PlatformPackSummary>();
        if (!root.TryGetProperty(arrayName, out var array) ||
            array.ValueKind != JsonValueKind.Array)
        {
            return (items, 0);
        }

        foreach (var item in array.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object) continue;
            var id = String(item, "id");
            var name = String(item, "name");
            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(name)) continue;
            var safeName = String(item, "safeName");
            items.Add(new PlatformPackSummary(
                ProviderId,
                id,
                name,
                String(item, "description") ?? string.Empty,
                null,
                "ATLauncher",
                0,
                null,
                safeName is null
                    ? "https://atlauncher.com/packs"
                    : $"https://atlauncher.com/pack/{safeName}"));
        }

        return (items, items.Count + page * pageSize);
    }

    public async Task<IReadOnlyList<PlatformPackVersion>> GetVersionsAsync(
        string packId,
        CancellationToken cancellationToken)
    {
        var root = await QueryAsync(
                """
                query PackVersions($pack: PackInput!) {
                  pack(pack: $pack) {
                    id
                    name
                    versions(first: 8) {
                      id
                      version
                      name
                      minecraftVersion
                      isRecommended
                      publishedAt
                      json { minecraft loader { type metadata { version } } }
                    }
                  }
                }
                """,
                new Dictionary<string, object?> { ["pack"] = new PackInput(int.Parse(packId)) },
                cancellationToken)
            .ConfigureAwait(false);
        var versions = new List<PlatformPackVersion>();
        if (!root.TryGetProperty("pack", out var pack) ||
            pack.ValueKind != JsonValueKind.Object ||
            !pack.TryGetProperty("versions", out var array) ||
            array.ValueKind != JsonValueKind.Array)
        {
            return versions;
        }

        foreach (var version in array.EnumerateArray())
        {
            if (version.ValueKind != JsonValueKind.Object) continue;
            var versionId = String(version, "version")
                ?? String(version, "id");
            if (string.IsNullOrWhiteSpace(versionId)) continue;
            var label = String(version, "name")
                ?? String(version, "version")
                ?? versionId;
            var (loader, loaderVersion) = version.TryGetProperty("json", out var json) &&
                                          json.ValueKind == JsonValueKind.Object
                ? ReadLoader(json)
                : ("unknown", string.Empty);
            versions.Add(new PlatformPackVersion(
                ProviderId,
                packId,
                versionId,
                label,
                (json.ValueKind == JsonValueKind.Object ? String(json, "minecraft") : null)
                    ?? String(version, "minecraftVersion")
                    ?? string.Empty,
                loader,
                loaderVersion,
                "release",
                Date(version, "publishedAt")));
        }

        return versions;
    }

    public async Task<ModpackIndex> BuildIndexAsync(
        string packId,
        string versionId,
        CancellationToken cancellationToken)
    {
        var root = await QueryAsync(
                """
                query PackVersion($pack: PackInput!, $version: String!) {
                  packVersion(pack: $pack, version: $version) {
                    version
                    name
                    minecraftVersion
                    pack { id name description }
                    json {
                      minecraft
                      loader { type metadata { version loader rawVersion } }
                      mods {
                        name file url md5 filesize client server type optional
                      }
                    }
                  }
                }
                """,
                new Dictionary<string, object?>
                {
                    ["pack"] = new PackInput(int.Parse(packId)),
                    ["version"] = versionId,
                },
                cancellationToken)
            .ConfigureAwait(false);
        if (!root.TryGetProperty("packVersion", out var version) ||
            version.ValueKind != JsonValueKind.Object ||
            !version.TryGetProperty("json", out var json) ||
            json.ValueKind != JsonValueKind.Object)
        {
            throw new PlatformPackException(
                "PACK_LOADER_METADATA_FAILED",
                "ATLauncher version has no Configs JSON");
        }

        var game = String(json, "minecraft")
            ?? String(version, "minecraftVersion")
            ?? throw new PlatformPackException(
                "PACK_LOADER_METADATA_FAILED",
                "ATLauncher version has no Minecraft version");
        var (loader, loaderVersion) = ReadLoader(json);
        var files = new List<ModpackFile>();
        var seen = new HashSet<string>(Archoera.Domain.PathSemantics.Comparer);
        if (json.TryGetProperty("mods", out var mods) &&
            mods.ValueKind == JsonValueKind.Array)
        {
            foreach (var mod in mods.EnumerateArray())
            {
                if (mod.ValueKind != JsonValueKind.Object) continue;
                if (Bool(mod, "optional")) continue;
                var fileName = String(mod, "file");
                var url = String(mod, "url");
                var size = Long(mod, "filesize") ?? 0;
                if (string.IsNullOrWhiteSpace(fileName) ||
                    string.IsNullOrWhiteSpace(url) ||
                    size <= 0)
                {
                    continue;
                }

                var rawPath = Combine(FolderFor(String(mod, "type")), fileName);
                var issue = DownloadPlanPaths.TryNormalizeDest(rawPath, out var path);
                if (issue is not null)
                {
                    throw new PlatformPackException(
                        PackErrorCode.Invalid,
                        $"{rawPath}: {issue.Message}");
                }

                if (!seen.Add(path)) continue;
                var (sha1, sha512, md5) = ReadHashes(mod);
                if (md5 is null) continue;
                files.Add(new ModpackFile(
                    path,
                    sha1,
                    sha512,
                    size,
                    [ResolveUrl(url)],
                    !Bool(mod, "client"),
                    !Bool(mod, "server"),
                    md5));
            }
        }

        if (files.Count == 0)
        {
            throw new PlatformPackException(
                PackErrorCode.Invalid,
                "ATLauncher Configs JSON contains no downloadable files");
        }

        var pack = version.TryGetProperty("pack", out var packElement) &&
                   packElement.ValueKind == JsonValueKind.Object
            ? packElement
            : default;
        var name = String(pack, "name") ?? packId;
        var summary = String(pack, "description") ?? string.Empty;
        var dependencies = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["minecraft"] = game,
        };
        if (loader != "vanilla")
        {
            dependencies[LoaderDependency(loader)] = loaderVersion;
        }

        return new ModpackIndex(
            1,
            "minecraft",
            String(version, "version") ?? versionId,
            name,
            summary,
            files,
            dependencies,
            new ModpackLoader(loader, loaderVersion));
    }

    private async Task<JsonElement> QueryAsync(
        string query,
        IReadOnlyDictionary<string, object?>? variables,
        CancellationToken cancellationToken)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("query", query);
            if (variables is null)
            {
                writer.WriteNull("variables");
            }
            else
            {
                writer.WriteStartObject("variables");
                foreach (var pair in variables)
                {
                    writer.WritePropertyName(pair.Key);
                    switch (pair.Value)
                    {
                        case int number:
                            writer.WriteNumberValue(number);
                            break;
                        case string text:
                            writer.WriteStringValue(text);
                            break;
                        case PackInput pack:
                            writer.WriteStartObject();
                            writer.WriteNumber("id", pack.Id);
                            writer.WriteEndObject();
                            break;
                        case null:
                            writer.WriteNullValue();
                            break;
                        default:
                            throw new PlatformPackException(
                                "PLATFORM_METADATA_INVALID",
                                "unsupported GraphQL variable type");
                    }
                }

                writer.WriteEndObject();
            }

            writer.WriteEndObject();
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, _endpoint)
        {
            Content = new StringContent(
                Encoding.UTF8.GetString(stream.ToArray()),
                Encoding.UTF8,
                "application/json"),
        };
        request.Headers.TryAddWithoutValidation("User-Agent", UserAgent);
        using var response = await _http
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new PlatformPackException(
                "PLATFORM_NETWORK_FAILED",
                $"ATLauncher returned {(int)response.StatusCode}");
        }

        await using var responseStream = await response.Content
            .ReadAsStreamAsync(cancellationToken)
            .ConfigureAwait(false);
        using var memory = new MemoryStream();
        var buffer = new byte[16 * 1024];
        while (true)
        {
            var read = await responseStream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read == 0) break;
            if (memory.Length + read > MaxMetadataBytes)
            {
                throw new PlatformPackException(
                    "PLATFORM_METADATA_INVALID",
                    "ATLauncher response is too large");
            }

            memory.Write(buffer, 0, read);
        }

        using var document = JsonDocument.Parse(memory.ToArray());
        var root = document.RootElement;
        if (root.TryGetProperty("errors", out var errors) &&
            errors.ValueKind == JsonValueKind.Array &&
            errors.GetArrayLength() > 0)
        {
            throw new PlatformPackException(
                "PLATFORM_METADATA_INVALID",
                "ATLauncher GraphQL returned an error");
        }

        return root.TryGetProperty("data", out var data)
            ? data.Clone()
            : throw new PlatformPackException(
                "PLATFORM_METADATA_INVALID",
                "ATLauncher GraphQL returned no data");
    }

    private static (string Loader, string Version) ReadLoader(JsonElement json)
    {
        if (!json.TryGetProperty("loader", out var loader) ||
            loader.ValueKind != JsonValueKind.Object)
        {
            return ("vanilla", string.Empty);
        }

        var type = (String(loader, "type") ?? string.Empty).ToLowerInvariant();
        var version = string.Empty;
        if (loader.TryGetProperty("metadata", out var metadata) &&
            metadata.ValueKind == JsonValueKind.Object)
        {
            version = String(metadata, "version")
                ?? String(metadata, "rawVersion")
                ?? String(metadata, "loader")
                ?? string.Empty;
        }

        return type switch
        {
            "fabric" => ("fabric", version),
            "quilt" => ("quilt", version),
            "forge" => ("forge", version),
            "neoforge" => ("neoforge", version),
            "" or "vanilla" => ("vanilla", string.Empty),
            _ => throw new PlatformPackException(
                PackErrorCode.LoaderUnsupported,
                $"ATLauncher loader '{type}' is not supported"),
        };
    }

    private static string LoaderDependency(string loader) => loader switch
    {
        "fabric" => "fabric-loader",
        "quilt" => "quilt-loader",
        "forge" => "forge",
        "neoforge" => "neoforge",
        _ => loader,
    };

    private static string FolderFor(string? type) => type?.ToUpperInvariant() switch
    {
        "RESOURCEPACK" => "resourcepacks",
        _ => "mods",
    };

    private static string ResolveUrl(string url)
        => Uri.TryCreate(url, UriKind.Absolute, out var absolute)
            ? absolute.AbsoluteUri
            : "https://download.nodecdn.net/containers/atl/" + url.TrimStart('/');

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

    private static (string? Sha1, string? Sha512, string? Md5) ReadHashes(JsonElement mod)
    {
        // The ATL GraphQL schema only exposes MD5; the stronger digests are
        // not part of its public Mod type.
        var md5 = Hex(String(mod, "md5"), 32);
        return (null, null, md5);
    }

    private static string? Hex(string? value, int length)
    {
        if (value is null || value.Length != length) return null;
        var lower = value.ToLowerInvariant();
        return lower.All(IsLowerHex) ? lower : null;
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
        var value = String(root, name);
        return DateTimeOffset.TryParse(value, out var parsed) ? parsed : null;
    }

    private static bool IsLowerHex(char value)
        => value is (>= '0' and <= '9') or (>= 'a' and <= 'f');

    private static string JsonString(string value)
        => "\"" + JsonEncodedText.Encode(value).ToString() + "\"";

    private sealed record PackInput(int Id);
}
