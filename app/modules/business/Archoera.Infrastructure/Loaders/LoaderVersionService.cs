using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Xml.Linq;
using Archoera.Application.Modules;
using Archoera.Infrastructure.Modules;

namespace Archoera.Infrastructure.Loaders;

/// <summary>One loader build offered for a game version.</summary>
public sealed record LoaderVersionInfo(
    string Version,
    bool Stable,
    bool Latest,
    bool Recommended);

/// <summary>Provider endpoints, injectable for tests and mirrors.</summary>
public sealed record LoaderVersionEndpoints(
    Uri Fabric,
    Uri Quilt,
    Uri ForgeMetadata,
    Uri ForgePromotions,
    Uri NeoForgeMetadata)
{
    public static LoaderVersionEndpoints Default { get; } = new(
        new Uri("https://meta.fabricmc.net/v2/"),
        new Uri("https://meta.quiltmc.org/v3/"),
        new Uri("https://maven.minecraftforge.net/net/minecraftforge/forge/maven-metadata.xml"),
        new Uri("https://files.minecraftforge.net/net/minecraftforge/forge/promotions_slim.json"),
        new Uri("https://maven.neoforged.net/releases/net/neoforged/neoforge/maven-metadata.xml"));
}

/// <summary>
/// Lists loader builds for a game version. This owns the provider-specific
/// metadata formats so the UI and C++ hub never parse Maven/JSON themselves.
/// </summary>
public sealed class LoaderVersionService : IModuleMethodHandler
{
    private static readonly string[] SupportedMethods = ["loader.versions"];
    private static readonly MediaTypeWithQualityHeaderValue AcceptJson =
        new("application/json");

    private readonly HttpClient _http;
    private readonly LoaderVersionEndpoints _endpoints;
    private readonly DownloadPolicy _policy;

    public LoaderVersionService(
        HttpClient? httpClient = null,
        LoaderVersionEndpoints? endpoints = null,
        DownloadPolicy? policy = null)
    {
        _policy = policy ?? new DownloadPolicy();
        _http = httpClient ?? new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(25),
        };
        if (httpClient is null)
        {
            _http.DefaultRequestHeaders.UserAgent.ParseAdd(
                "ArchoeraCX/0.0.1+1 (https://github.com/BetaStudio2/ArchoeraCX)");
        }

        _endpoints = endpoints ?? LoaderVersionEndpoints.Default;
    }

    public IReadOnlyCollection<string> Methods => SupportedMethods;

    public async Task<byte[]> HandleAsync(
        string method,
        string? paramsJson,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(method, "loader.versions", StringComparison.Ordinal))
        {
            return ModuleJson.Error("E_UNSUPPORTED", $"unsupported loader method '{method}'");
        }

        try
        {
            await _policy.EnsureEnabledAsync(cancellationToken).ConfigureAwait(false);
            using var document = string.IsNullOrWhiteSpace(paramsJson)
                ? JsonDocument.Parse("{}")
                : JsonDocument.Parse(paramsJson);
            var root = document.RootElement;
            var kind = ReadString(root, "kind")?.ToLowerInvariant() ?? string.Empty;
            var gameVersion = ReadString(root, "gameVersion");
            if (string.IsNullOrWhiteSpace(kind) || string.IsNullOrWhiteSpace(gameVersion))
            {
                return ModuleJson.Error(
                    "E_INVALID",
                    "loader.versions requires kind and gameVersion");
            }

            var items = await ListAsync(kind, gameVersion, cancellationToken)
                .ConfigureAwait(false);
            return ModuleJson.Envelope(writer =>
            {
                writer.WriteString("kind", kind);
                writer.WriteString("gameVersion", gameVersion);
                writer.WriteStartArray("items");
                foreach (var item in items)
                {
                    writer.WriteStartObject();
                    writer.WriteString("version", item.Version);
                    writer.WriteBoolean("stable", item.Stable);
                    writer.WriteBoolean("latest", item.Latest);
                    writer.WriteBoolean("recommended", item.Recommended);
                    writer.WriteEndObject();
                }

                writer.WriteEndArray();
            });
        }
        catch (DownloadPolicyException exception)
        {
            return ModuleJson.Error(exception.Code, exception.Message);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return ModuleJson.Error(
                "LOADER_VERSIONS_FAILED",
                "loader metadata request timed out");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is HttpRequestException or IOException or JsonException or
            System.Xml.XmlException or InvalidOperationException or FormatException or
            TimeoutException)
        {
            return ModuleJson.Error("LOADER_VERSIONS_FAILED", exception.Message);
        }
    }

    /// <summary>Loads and normalizes all builds for a loader/game pair.</summary>
    public async Task<IReadOnlyList<LoaderVersionInfo>> ListAsync(
        string kind,
        string gameVersion,
        CancellationToken cancellationToken = default)
    {
        var normalizedKind = kind.Trim().ToLowerInvariant();
        var game = gameVersion.Trim();
        return normalizedKind switch
        {
            "fabric" => await FabricOrQuiltAsync(
                _endpoints.Fabric,
                game,
                cancellationToken).ConfigureAwait(false),
            "quilt" => await FabricOrQuiltAsync(
                _endpoints.Quilt,
                game,
                cancellationToken).ConfigureAwait(false),
            "forge" => await ForgeAsync(game, cancellationToken).ConfigureAwait(false),
            "neoforge" => await NeoForgeAsync(game, cancellationToken).ConfigureAwait(false),
            "vanilla" => [],
            _ => throw new ArgumentException(
                $"unsupported loader kind '{kind}'",
                nameof(kind)),
        };
    }

    private async Task<IReadOnlyList<LoaderVersionInfo>> FabricOrQuiltAsync(
        Uri baseAddress,
        string gameVersion,
        CancellationToken cancellationToken)
    {
        var endpoint = new Uri(
            baseAddress,
            $"versions/loader/{Uri.EscapeDataString(gameVersion)}");
        using var document = await GetJsonAsync(endpoint, cancellationToken)
            .ConfigureAwait(false);
        if (document.RootElement.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException("loader metadata must be an array");
        }

        var versions = new List<(string Version, bool Stable)>();
        foreach (var item in document.RootElement.EnumerateArray())
        {
            if (!item.TryGetProperty("loader", out var loader) ||
                loader.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var version = ReadString(loader, "version");
            if (string.IsNullOrWhiteSpace(version)) continue;
            versions.Add((
                version,
                loader.TryGetProperty("stable", out var stable) &&
                stable.ValueKind == JsonValueKind.True));
        }

        return BuildItems(versions);
    }

    private async Task<IReadOnlyList<LoaderVersionInfo>> ForgeAsync(
        string gameVersion,
        CancellationToken cancellationToken)
    {
        var metadata = await GetStringAsync(
            _endpoints.ForgeMetadata,
            cancellationToken).ConfigureAwait(false);
        var document = XDocument.Parse(metadata);
        var prefix = gameVersion + "-";
        var versions = document
            .Descendants()
            .Where(element => element.Name.LocalName == "version")
            .Select(element => element.Value.Trim())
            .Where(value => value.StartsWith(prefix, StringComparison.Ordinal))
            .Select(value => value[prefix.Length..])
            .Where(value => value.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(value => (Version: value, Stable: !value.Contains('-')))
            .ToList();

        string? latest = null;
        string? recommended = null;
        try
        {
            using var promotions = await GetJsonAsync(
                _endpoints.ForgePromotions,
                cancellationToken).ConfigureAwait(false);
            if (promotions.RootElement.TryGetProperty("promos", out var promos) &&
                promos.ValueKind == JsonValueKind.Object)
            {
                latest = ReadString(promos, $"{gameVersion}-latest");
                recommended = ReadString(promos, $"{gameVersion}-recommended");
            }
        }
        catch (Exception exception) when (
            exception is HttpRequestException or IOException or JsonException)
        {
            // The full Maven list is still useful without promotions.
        }

        return BuildItems(versions, latest, recommended);
    }

    private async Task<IReadOnlyList<LoaderVersionInfo>> NeoForgeAsync(
        string gameVersion,
        CancellationToken cancellationToken)
    {
        var metadata = await GetStringAsync(
            _endpoints.NeoForgeMetadata,
            cancellationToken).ConfigureAwait(false);
        var document = XDocument.Parse(metadata);
        var prefix = NeoForgePrefix(gameVersion);
        var versions = document
            .Descendants()
            .Where(element => element.Name.LocalName == "version")
            .Select(element => element.Value.Trim())
            .Where(value => value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(value => (Version: value, Stable: !value.Contains('-')))
            .ToList();

        return BuildItems(versions);
    }

    private static string NeoForgePrefix(string gameVersion)
    {
        var normalized = gameVersion.Trim();
        if (normalized.StartsWith("1.", StringComparison.Ordinal))
        {
            normalized = normalized[2..];
        }

        var parts = normalized.Split('.');
        return parts.Length >= 2
            ? $"{parts[0]}.{parts[1]}."
            : normalized + ".";
    }

    private async Task<JsonDocument> GetJsonAsync(
        Uri endpoint,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
        request.Headers.Accept.Add(AcceptJson);
        using var timeout = CreateRequestTimeout(cancellationToken);
        using var response = await _http
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content
            .ReadAsStreamAsync(timeout.Token)
            .ConfigureAwait(false);
        return await JsonDocument
            .ParseAsync(stream, cancellationToken: timeout.Token)
            .ConfigureAwait(false);
    }

    private async Task<string> GetStringAsync(
        Uri endpoint,
        CancellationToken cancellationToken)
    {
        using var timeout = CreateRequestTimeout(cancellationToken);
        using var response = await _http
            .GetAsync(endpoint, HttpCompletionOption.ResponseHeadersRead, timeout.Token)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return await response.Content
            .ReadAsStringAsync(timeout.Token)
            .ConfigureAwait(false);
    }

    private static CancellationTokenSource CreateRequestTimeout(
        CancellationToken cancellationToken)
    {
        var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        return timeout;
    }

    private static IReadOnlyList<LoaderVersionInfo> BuildItems(
        IEnumerable<(string Version, bool Stable)> source,
        string? latest = null,
        string? recommended = null)
    {
        var unique = source
            .Where(item => !string.IsNullOrWhiteSpace(item.Version))
            .GroupBy(item => item.Version, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderByDescending(item => item.Version, LoaderVersionComparer.Instance)
            .ToList();
        if (unique.Count == 0) return [];

        var recommendedVersion = recommended;
        if (string.IsNullOrWhiteSpace(recommendedVersion))
        {
            recommendedVersion = unique.FirstOrDefault(item => item.Stable).Version;
        }

        var latestVersion = string.IsNullOrWhiteSpace(latest)
            ? unique[0].Version
            : latest;

        return unique
            .Select(item => new LoaderVersionInfo(
                item.Version,
                item.Stable,
                string.Equals(item.Version, latestVersion, StringComparison.OrdinalIgnoreCase),
                string.Equals(
                    item.Version,
                    recommendedVersion,
                    StringComparison.OrdinalIgnoreCase)))
            .ToArray();
    }

    private static string? ReadString(JsonElement parent, string name)
        => parent.TryGetProperty(name, out var element) &&
           element.ValueKind == JsonValueKind.String
            ? element.GetString()
            : null;

    private sealed class LoaderVersionComparer : IComparer<string>
    {
        public static LoaderVersionComparer Instance { get; } = new();

        public int Compare(string? x, string? y)
        {
            if (ReferenceEquals(x, y)) return 0;
            if (x is null) return -1;
            if (y is null) return 1;

            var left = Split(x);
            var right = Split(y);
            var length = Math.Min(left.Count, right.Count);
            for (var index = 0; index < length; index++)
            {
                var a = left[index];
                var b = right[index];
                var aNumeric = long.TryParse(
                    a,
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out var aNumber);
                var bNumeric = long.TryParse(
                    b,
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out var bNumber);
                if (aNumeric && bNumeric)
                {
                    if (aNumber != bNumber) return aNumber.CompareTo(bNumber);
                    continue;
                }

                if (aNumeric != bNumeric) return aNumeric ? 1 : -1;
                var comparison = string.Compare(a, b, StringComparison.OrdinalIgnoreCase);
                if (comparison != 0) return comparison;
            }

            if (left.Count == right.Count) return 0;
            // A release (0.20.0) sorts above its prerelease (0.20.0-beta.1).
            return left.Count < right.Count ? 1 : -1;
        }

        private static List<string> Split(string value)
        {
            var parts = new List<string>();
            var start = 0;
            for (var index = 0; index < value.Length; index++)
            {
                if (value[index] is '.' or '-' or '+')
                {
                    if (index > start) parts.Add(value[start..index]);
                    start = index + 1;
                }
            }

            if (start < value.Length) parts.Add(value[start..]);
            return parts;
        }
    }
}
