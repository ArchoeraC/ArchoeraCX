using System.Text.Json;
using Archoera.Application.Packs;
using Archoera.Application.Modules;
using Archoera.Infrastructure.Downloads;
using Archoera.Infrastructure.Modules;
using Archoera.Infrastructure.Packs.Platform;

namespace Archoera.Infrastructure.Packs;

/// <summary>
/// `pack.platform.*` facade for pack sources with a public catalogue and
/// direct file manifests. Today this is FTB; Technic and ATLauncher slots are
/// reserved behind the same request/response shape so the UI does not need a
/// new panel per provider.
/// </summary>
public sealed class PlatformPackModuleService : IModuleMethodHandler
{
    private const string ProvidersMethod = "pack.platform.providers";
    private const string SearchMethod = "pack.platform.search";
    private const string VersionsMethod = "pack.platform.versions";
    private const string InstallMethod = "pack.platform.install";

    private static readonly string[] SupportedMethods =
        [ProvidersMethod, SearchMethod, VersionsMethod, InstallMethod];

    private readonly PlatformPackService _packs;
    private readonly DownloadPolicy _policy;

    public PlatformPackModuleService(
        PlatformPackService? packs = null,
        DownloadPolicy? policy = null)
    {
        _packs = packs ?? new PlatformPackService();
        _policy = policy ?? new DownloadPolicy();
    }

    public IReadOnlyCollection<string> Methods => SupportedMethods;

    public async Task<byte[]> HandleAsync(
        string method,
        string? paramsJson,
        CancellationToken cancellationToken)
    {
        try
        {
            using var document = Parse(paramsJson);
            var root = document.RootElement;
            if (method == ProvidersMethod)
            {
                return Providers();
            }

            await _policy.EnsureEnabledAsync(cancellationToken).ConfigureAwait(false);
            return method switch
            {
                SearchMethod => await SearchAsync(root, cancellationToken).ConfigureAwait(false),
                VersionsMethod => await VersionsAsync(root, cancellationToken).ConfigureAwait(false),
                InstallMethod => await InstallAsync(root, cancellationToken).ConfigureAwait(false),
                _ => ModuleJson.Error(
                    "E_UNSUPPORTED",
                    $"unsupported platform pack method '{method}'"),
            };
        }
        catch (PlatformPackException exception)
        {
            return ModuleJson.Error(exception.Code, exception.Message);
        }
        catch (PackException exception)
        {
            return ModuleJson.Error(exception.Code, exception.Message);
        }
        catch (DownloadRunnerException exception)
        {
            return ModuleJson.Error(exception.Code, exception.Message);
        }
        catch (DownloadPolicyException exception)
        {
            return ModuleJson.Error(exception.Code, exception.Message);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is ArgumentException or IOException or JsonException or
            UnauthorizedAccessException or HttpRequestException or
            InvalidOperationException or NotSupportedException)
        {
            return ModuleJson.Error("PACK_INSTALL_FAILED", exception.Message);
        }
    }

    private byte[] Providers()
        => ModuleJson.Envelope(writer =>
        {
            writer.WriteStartArray("items");
            foreach (var provider in _packs.Providers)
            {
                writer.WriteStartObject();
                writer.WriteString("id", provider.Id);
                writer.WriteString("name", provider.DisplayName);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
        });

    private async Task<byte[]> SearchAsync(
        JsonElement root,
        CancellationToken cancellationToken)
    {
        var provider = RequireString(root, "provider");
        var query = ReadOptionalString(root, "query") ?? string.Empty;
        var page = ReadInt(root, "page", 0);
        var pageSize = ReadInt(root, "pageSize", 20);
        var (items, total) = await _packs
            .SearchAsync(provider, query, page, pageSize, cancellationToken)
            .ConfigureAwait(false);
        return ModuleJson.Envelope(writer =>
        {
            writer.WriteString("provider", provider);
            writer.WriteNumber("total", total);
            writer.WriteNumber("page", page);
            writer.WriteNumber("pageSize", pageSize);
            writer.WriteStartArray("items");
            foreach (var item in items)
            {
                writer.WriteStartObject();
                writer.WriteString("provider", item.Provider);
                writer.WriteString("id", item.Id);
                writer.WriteString("name", item.Name);
                writer.WriteString("summary", item.Summary);
                writer.WriteString("iconUrl", item.IconUrl);
                writer.WriteString("author", item.Author);
                writer.WriteNumber("downloads", item.Downloads);
                writer.WriteString("updatedAt", item.UpdatedAt?.ToString("O"));
                writer.WriteString("websiteUrl", item.WebsiteUrl);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
        });
    }

    private async Task<byte[]> VersionsAsync(
        JsonElement root,
        CancellationToken cancellationToken)
    {
        var provider = RequireString(root, "provider");
        var packId = RequireString(root, "packId");
        var versions = await _packs
            .VersionsAsync(provider, packId, cancellationToken)
            .ConfigureAwait(false);
        return ModuleJson.Envelope(writer =>
        {
            writer.WriteString("provider", provider);
            writer.WriteString("packId", packId);
            writer.WriteStartArray("items");
            foreach (var version in versions)
            {
                writer.WriteStartObject();
                writer.WriteString("provider", version.Provider);
                writer.WriteString("packId", version.PackId);
                writer.WriteString("id", version.VersionId);
                writer.WriteString("name", version.Name);
                writer.WriteString("gameVersion", version.GameVersion);
                writer.WriteString("loader", version.Loader);
                writer.WriteString("loaderVersion", version.LoaderVersion);
                writer.WriteString("versionType", version.VersionType);
                writer.WriteString("publishedAt", version.PublishedAt?.ToString("O"));
                writer.WriteBoolean("directArchive", version.DirectArchive);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
        });
    }

    private async Task<byte[]> InstallAsync(
        JsonElement root,
        CancellationToken cancellationToken)
    {
        var provider = RequireString(root, "provider");
        var packId = RequireString(root, "packId");
        var versionId = RequireString(root, "versionId");
        var name = ReadOptionalString(root, "name");
        var rootPath = ReadOptionalString(root, "rootPath");
        var result = await _packs
            .InstallAsync(provider, packId, versionId, name, rootPath, cancellationToken)
            .ConfigureAwait(false);
        return PackModuleService.BuildResult(result);
    }

    private static JsonDocument Parse(string? paramsJson)
    {
        if (string.IsNullOrWhiteSpace(paramsJson)) return JsonDocument.Parse("{}");
        try
        {
            return JsonDocument.Parse(paramsJson);
        }
        catch (JsonException exception)
        {
            throw new ArgumentException($"request is not valid JSON: {exception.Message}");
        }
    }

    private static string RequireString(JsonElement root, string name)
        => ReadOptionalString(root, name)
           ?? throw new ArgumentException($"{name} is required");

    private static string? ReadOptionalString(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var element) ||
            element.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (element.ValueKind != JsonValueKind.String)
        {
            throw new ArgumentException($"{name} must be a string");
        }

        var text = element.GetString();
        return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
    }

    private static int ReadInt(JsonElement root, string name, int fallback)
        => root.TryGetProperty(name, out var element) &&
           element.ValueKind == JsonValueKind.Number &&
           element.TryGetInt32(out var value)
            ? value
            : fallback;
}
