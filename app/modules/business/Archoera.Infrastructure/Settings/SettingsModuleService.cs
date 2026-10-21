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
        catch (ArgumentException exception)
        {
            return Error("E_INVALID", exception.Message);
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
        string? javaPath = null;
        var javaPathPresent = false;
        bool? javaAuto = null;
        try
        {
            using var document = JsonDocument.Parse(paramsJson);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return Error("E_INVALID", "settings.set requires a JSON object");
            }

            if (root.TryGetProperty("instancesRoots", out var rootsElement) &&
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

            if (root.TryGetProperty("instancesRoot", out var rootElement) &&
                rootElement.ValueKind == JsonValueKind.String)
            {
                legacyRoot = rootElement.GetString();
            }

            if (root.TryGetProperty("javaPath", out var javaElement))
            {
                javaPathPresent = true;
                javaPath = javaElement.ValueKind == JsonValueKind.Null
                    ? null
                    : javaElement.ValueKind == JsonValueKind.String
                        ? javaElement.GetString()
                        : throw new ArgumentException("javaPath must be a string or null");
            }

            if (root.TryGetProperty("javaAuto", out var autoElement))
            {
                javaAuto = autoElement.ValueKind is JsonValueKind.True or JsonValueKind.False
                    ? autoElement.GetBoolean()
                    : throw new ArgumentException("javaAuto must be a boolean");
            }
        }
        catch (JsonException exception)
        {
            return Error("E_INVALID", exception.Message);
        }

        var current = await _store.LoadAsync(cancellationToken).ConfigureAwait(false);
        if (instancesRoots is { Count: > 0 })
        {
            current = current with
            {
                InstancesRoot = instancesRoots[0],
                InstancesRoots = instancesRoots,
            };
        }
        else if (!string.IsNullOrWhiteSpace(legacyRoot))
        {
            if (!Path.IsPathFullyQualified(legacyRoot))
            {
                return Error("E_INVALID", "instancesRoot must be an absolute directory");
            }

            current = current with
            {
                InstancesRoot = legacyRoot,
                InstancesRoots = [legacyRoot],
            };
        }

        if (javaPathPresent || javaAuto is not null)
        {
            current = current with
            {
                JavaPath = javaPathPresent
                    ? (string.IsNullOrWhiteSpace(javaPath) ? null : javaPath)
                    : current.JavaPath,
                JavaAuto = javaAuto ?? current.JavaAuto,
            };
        }

        if (instancesRoots is not { Count: > 0 } &&
            string.IsNullOrWhiteSpace(legacyRoot) &&
            !javaPathPresent &&
            javaAuto is null)
        {
            return Error("E_INVALID", "settings.set requires at least one field");
        }

        // Instances roots must be absolute and non-empty; SaveAsync validates.
        if (current.Roots.Count == 0)
        {
            return Error("E_INVALID", "at least one instances root is required");
        }

        var saved = await _store.SaveAsync(current, cancellationToken).ConfigureAwait(false);
        return Build(saved);
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
            if (string.IsNullOrWhiteSpace(settings.JavaPath))
            {
                writer.WriteNull("javaPath");
            }
            else
            {
                writer.WriteString("javaPath", settings.JavaPath);
            }
            writer.WriteBoolean("javaAuto", settings.JavaAuto);
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
