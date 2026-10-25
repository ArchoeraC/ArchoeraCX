using System.Text.Json;
using Archoera.Application.Modules;
using Archoera.Application.Packs;
using Archoera.Application.Resources;
using Archoera.Domain.Downloads;
using Archoera.Domain.Resources;
using Archoera.Infrastructure.Downloads;
using Archoera.Infrastructure.Java;
using Archoera.Infrastructure.Modules;
using Archoera.Infrastructure.Resources.Modrinth;

namespace Archoera.Infrastructure.Packs;

/// <summary>
/// Module facade for Modrinth <c>.mrpack</c> installs. Dart never parses an
/// archive; it only sends a local path or a Modrinth project/version pair.
/// </summary>
public sealed class PackModuleService : IModuleMethodHandler
{
    private static readonly string[] SupportedMethods =
        ["modpack.inspect", "modpack.install", "modpack.installLocal", "loader.autoRepair"];
    private const string ModrinthProvider = "modrinth";

    private readonly PackInstallService _installer;
    private readonly DownloadPolicy _policy;

    public PackModuleService(
        PackInstallService? installer = null,
        DownloadPolicy? policy = null)
    {
        _installer = installer ?? new PackInstallService();
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

            if (string.Equals(method, "modpack.inspect", StringComparison.Ordinal))
            {
                return Inspect(root);
            }

            if (method is "modpack.install" or "modpack.installLocal" or
                "loader.autoRepair")
            {
                await _policy.EnsureEnabledAsync(cancellationToken).ConfigureAwait(false);
            }

            return method switch
            {
                "modpack.installLocal" => await InstallLocalAsync(root, cancellationToken)
                    .ConfigureAwait(false),
                "modpack.install" => await InstallModrinthAsync(root, cancellationToken)
                    .ConfigureAwait(false),
                "loader.autoRepair" => await RepairInstanceAsync(root, cancellationToken)
                    .ConfigureAwait(false),
                _ => ModuleJson.Error(
                    "E_UNSUPPORTED",
                    $"unsupported pack method '{method}'"),
            };
        }
        catch (PackException exception)
        {
            return ModuleJson.Error(exception.Code, exception.Message);
        }
        catch (DownloadRunnerException exception)
        {
            return ModuleJson.Error(exception.Code, exception.Message);
        }
        catch (JavaSelectionException exception)
        {
            return ModuleJson.Error(exception.Code, exception.Message);
        }
        catch (DownloadPolicyException exception)
        {
            return ModuleJson.Error(exception.Code, exception.Message);
        }
        catch (DownloadPlanException exception)
        {
            return ModuleJson.Error(exception.Code, exception.Message);
        }
        catch (ResourceProviderException exception)
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

    private static byte[] Inspect(JsonElement root)
    {
        var path = RequireString(root, "path");
        var contents = PackArchive.Read(path, clientSide: true);
        var index = contents.Index;
        return ModuleJson.Envelope(writer =>
        {
            writer.WriteString("name", index.Name);
            writer.WriteString("versionId", index.VersionId);
            writer.WriteString("summary", index.Summary);
            writer.WriteString("gameVersion", index.MinecraftVersion);
            writer.WriteString("loader", index.Loader.Kind);
            writer.WriteString("loaderVersion", index.Loader.Version);
            writer.WriteNumber("fileCount", index.Files.Count);
            writer.WriteNumber("totalBytes", index.TotalBytes);
            writer.WriteNumber("overrideFiles", contents.Overrides.Count);
            writer.WriteNumber("overrideBytes", contents.OverrideBytes);
            writer.WriteNumber("ignoredServerFiles", contents.IgnoredServerFiles);
        });
    }

    private async Task<byte[]> InstallLocalAsync(
        JsonElement root,
        CancellationToken cancellationToken)
    {
        var path = RequireString(root, "path");
        var name = ReadOptionalString(root, "name");
        var rootPath = ReadOptionalString(root, "rootPath");
        var result = await _installer
            .InstallLocalAsync(path, name, rootPath, cancellationToken)
            .ConfigureAwait(false);
        return BuildResult(result);
    }

    private async Task<byte[]> InstallModrinthAsync(
        JsonElement root,
        CancellationToken cancellationToken)
    {
        var provider = ReadOptionalString(root, "provider") ?? "modrinth";
        if (!string.Equals(provider, ModrinthProvider, StringComparison.OrdinalIgnoreCase))
        {
            throw new ResourceProviderException(
                ResourceErrorCode.ProviderUnavailable,
                "CurseForge is not supported");
        }

        var projectId = RequireString(root, "projectId");
        var versionId = RequireString(root, "versionId");
        var name = ReadOptionalString(root, "name");
        var rootPath = ReadOptionalString(root, "rootPath");
        var result = await _installer
            .InstallModrinthAsync(projectId, versionId, name, rootPath, cancellationToken)
            .ConfigureAwait(false);
        return BuildResult(result);
    }

    private async Task<byte[]> RepairInstanceAsync(
        JsonElement root,
        CancellationToken cancellationToken)
    {
        var instanceId = RequireString(root, "instanceId");
        var result = await _installer
            .RepairInstanceAsync(instanceId, cancellationToken)
            .ConfigureAwait(false);
        return ModuleJson.Envelope(writer =>
        {
            writer.WriteString("instanceId", result.Instance.Id);
            writer.WriteString("versionId", result.VersionId);
            writer.WriteString("loader", result.Instance.Loader);
            writer.WriteString("gameVersion", result.Instance.GameVersion);
        });
    }

    private static byte[] BuildResult(PackInstallResult result)
    {
        var instance = result.Instance;
        return ModuleJson.Envelope(writer =>
        {
            writer.WriteStartObject("instance");
            writer.WriteString("id", instance.Id);
            writer.WriteString("name", instance.Name);
            writer.WriteString("gameVersion", instance.GameVersion);
            writer.WriteString("loader", instance.Loader);
            writer.WriteString("loaderVersion", instance.LoaderVersion);
            writer.WriteString("rootPath", instance.RootPath);
            writer.WriteNumber("modCount", instance.ModCount);
            writer.WriteBoolean("favorite", instance.Favorite);
            writer.WriteString("createdAt", instance.CreatedAt.ToString("O"));
            writer.WriteString("updatedAt", instance.UpdatedAt.ToString("O"));
            writer.WriteEndObject();
            writer.WriteString("packVersionId", result.PackVersionId);
            writer.WriteString("source", result.Source);
            writer.WriteNumber("downloadedFiles", result.DownloadedFiles);
            writer.WriteNumber("skippedFiles", result.SkippedFiles);
            writer.WriteNumber("extractedFiles", result.ExtractedFiles);
            writer.WriteNumber("downloadedBytes", result.DownloadedBytes);
        });
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
}
