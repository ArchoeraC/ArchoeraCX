using System.Text.Json;
using Archoera.Application.Modules;
using Archoera.Domain.Settings;

namespace Archoera.Infrastructure.Settings;

/// <summary>JSON facade for settings.get / settings.set.</summary>
public sealed class SettingsModuleService : IModuleMethodHandler
{
    private static readonly string[] SupportedMethods = ["settings.get", "settings.set"];

    private readonly JsonAppSettingsStore _store;

    public SettingsModuleService(JsonAppSettingsStore? store = null)
    {
        _store = store ?? new JsonAppSettingsStore();
    }

    public IReadOnlyCollection<string> Methods => SupportedMethods;

    public async Task<byte[]> HandleAsync(
        string method,
        string? paramsJson,
        CancellationToken cancellationToken)
    {
        try
        {
            return method switch
            {
                "settings.get" => await GetAsync(cancellationToken).ConfigureAwait(false),
                "settings.set" => await SetAsync(paramsJson, cancellationToken)
                    .ConfigureAwait(false),
                _ => Error("E_UNSUPPORTED", $"unsupported settings method '{method}'"),
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            return Error("E_INTERNAL", exception.Message);
        }
    }

    private async Task<byte[]> GetAsync(CancellationToken cancellationToken)
    {
        var settings = await _store.LoadAsync(cancellationToken).ConfigureAwait(false);
        return Build(settings);
    }

    private async Task<byte[]> SetAsync(string? paramsJson, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(paramsJson))
        {
            return Error("E_INVALID", "settings.set requires a JSON object");
        }

        IReadOnlyList<string>? instancesRoots = null;
        string? legacyRoot = null;
        try
        {
            using var document = JsonDocument.Parse(paramsJson);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return Error("E_INVALID", "settings.set requires a JSON object");
            }

            if (document.RootElement.TryGetProperty("instancesRoots", out var rootsElement) &&
                rootsElement.ValueKind == JsonValueKind.Array)
            {
                var roots = new List<string>();
                foreach (var item in rootsElement.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.String &&
                        !string.IsNullOrWhiteSpace(item.GetString()))
                    {
                        roots.Add(item.GetString()!);
                    }
                }

                instancesRoots = roots;
            }

            if (document.RootElement.TryGetProperty("instancesRoot", out var rootElement) &&
                rootElement.ValueKind == JsonValueKind.String)
            {
                legacyRoot = rootElement.GetString();
            }
        }
        catch (JsonException exception)
        {
            return Error("E_INVALID", exception.Message);
        }

        if (instancesRoots is { Count: > 0 })
        {
            var settings = await _store
                .SaveInstancesRootsAsync(instancesRoots, cancellationToken)
                .ConfigureAwait(false);
            return Build(settings);
        }

        if (string.IsNullOrWhiteSpace(legacyRoot) ||
            !Path.IsPathFullyQualified(legacyRoot))
        {
            return Error("E_INVALID", "instancesRoots must contain absolute directories");
        }

        return Build(await _store
            .SaveInstancesRootAsync(legacyRoot, cancellationToken)
            .ConfigureAwait(false));
    }

    private static byte[] Build(AppSettings settings)
    {
        using var memory = new MemoryStream();
        using (var writer = new Utf8JsonWriter(memory))
        {
            writer.WriteStartObject();
            writer.WriteBoolean("ok", true);
            writer.WriteStartObject("data");
            writer.WriteStartObject("settings");
            writer.WriteString("instancesRoot", settings.InstancesRoot);
            writer.WriteStartArray("instancesRoots");
            foreach (var root in settings.Roots)
            {
                writer.WriteStringValue(root);
            }
            writer.WriteEndArray();
            writer.WriteString("dataRoot", settings.DataRoot);
            writer.WriteString("settingsPath", settings.SettingsPath);
            writer.WriteEndObject();
            writer.WriteEndObject();
            writer.WriteStartObject("meta");
            writer.WriteNumber("schemaVersion", AppSettings.CurrentSchemaVersion);
            writer.WriteEndObject();
            writer.WriteEndObject();
        }

        return memory.ToArray();
    }

    private static byte[] Error(string code, string message)
    {
        using var memory = new MemoryStream();
        using (var writer = new Utf8JsonWriter(memory))
        {
            writer.WriteStartObject();
            writer.WriteBoolean("ok", false);
            writer.WriteStartObject("error");
            writer.WriteString("code", code);
            writer.WriteString("message", message);
            writer.WriteEndObject();
            writer.WriteEndObject();
        }

        return memory.ToArray();
    }
}
