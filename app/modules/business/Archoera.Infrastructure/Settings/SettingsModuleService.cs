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
        int? memoryMin = null;
        int? memoryMax = null;
        string? jvmArgs = null;
        var jvmArgsPresent = false;
        string? gcMode = null;
        string? gameArgs = null;
        var gameArgsPresent = false;
        string? downloadSource = null;
        int? downloadConcurrency = null;
        int? downloadPerHost = null;
        string? downloadProxy = null;
        var downloadProxyPresent = false;
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

            if (root.TryGetProperty("memoryMinMb", out var minElement))
            {
                memoryMin = minElement.ValueKind == JsonValueKind.Number &&
                            minElement.TryGetInt32(out var min)
                    ? min
                    : throw new ArgumentException("memoryMinMb must be an integer");
            }

            if (root.TryGetProperty("memoryMaxMb", out var maxElement))
            {
                memoryMax = maxElement.ValueKind == JsonValueKind.Number &&
                            maxElement.TryGetInt32(out var max)
                    ? max
                    : throw new ArgumentException("memoryMaxMb must be an integer");
            }

            if (root.TryGetProperty("jvmArgs", out var jvmElement))
            {
                jvmArgsPresent = true;
                jvmArgs = jvmElement.ValueKind == JsonValueKind.Null
                    ? null
                    : jvmElement.ValueKind == JsonValueKind.String
                        ? jvmElement.GetString()
                        : throw new ArgumentException("jvmArgs must be a string or null");
            }

            if (root.TryGetProperty("gcMode", out var gcElement) &&
                gcElement.ValueKind == JsonValueKind.String)
            {
                gcMode = gcElement.GetString();
                if (gcMode is not ("auto" or "g1gc" or "zgc" or "parallel" or "serial" or "shenandoah" or "epsilon"))
                {
                    return Error("E_INVALID", "unsupported gcMode");
                }
            }

            if (root.TryGetProperty("gameArgs", out var gameElement))
            {
                gameArgsPresent = true;
                gameArgs = gameElement.ValueKind == JsonValueKind.Null
                    ? null
                    : gameElement.ValueKind == JsonValueKind.String
                        ? gameElement.GetString()
                        : throw new ArgumentException("gameArgs must be a string or null");
            }

            if (root.TryGetProperty("downloadSource", out var sourceElement))
            {
                downloadSource = sourceElement.ValueKind == JsonValueKind.String
                    ? sourceElement.GetString()
                    : throw new ArgumentException("downloadSource must be a string");
                if (downloadSource is not ("auto" or "official" or "mirror"))
                {
                    return Error("E_INVALID", "unsupported downloadSource");
                }
            }

            if (root.TryGetProperty("downloadConcurrency", out var concurrencyElement))
            {
                downloadConcurrency = concurrencyElement.ValueKind == JsonValueKind.Number &&
                                      concurrencyElement.TryGetInt32(out var concurrency)
                    ? concurrency
                    : throw new ArgumentException("downloadConcurrency must be an integer");
                if (downloadConcurrency is < 1 or > 64)
                {
                    return Error("E_INVALID", "downloadConcurrency must be within 1..64");
                }
            }

            if (root.TryGetProperty("downloadProxy", out var proxyElement))
            {
                downloadProxyPresent = true;
                downloadProxy = proxyElement.ValueKind == JsonValueKind.Null
                    ? null
                    : proxyElement.ValueKind == JsonValueKind.String
                        ? proxyElement.GetString()?.Trim()
                        : throw new ArgumentException("downloadProxy must be a string or null");
                if (!string.IsNullOrEmpty(downloadProxy) &&
                    (!Uri.TryCreate(downloadProxy, UriKind.Absolute, out var proxyUri) ||
                     proxyUri.Scheme is not ("http" or "https" or "socks4" or "socks4a"
                         or "socks5" or "socks5h") ||
                     string.IsNullOrEmpty(proxyUri.Host)))
                {
                    return Error(
                        "E_INVALID",
                        "downloadProxy must be an http(s) or socks URL with a host");
                }
            }

            if (root.TryGetProperty("downloadPerHost", out var perHostElement))
            {
                downloadPerHost = perHostElement.ValueKind == JsonValueKind.Number &&
                                  perHostElement.TryGetInt32(out var perHost)
                    ? perHost
                    : throw new ArgumentException("downloadPerHost must be an integer");
                if (downloadPerHost is < 0 or > 64)
                {
                    return Error("E_INVALID", "downloadPerHost must be within 0..64");
                }
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

        if (memoryMin is not null || memoryMax is not null)
        {
            var minimum = memoryMin ?? current.MemoryMinMb;
            var maximum = memoryMax ?? current.MemoryMaxMb;
            if (minimum is < 128 or > 65536 || maximum is < 128 or > 65536 ||
                minimum > maximum)
            {
                return Error("E_INVALID", "memory range must be within 128–65536 MB and min <= max");
            }

            current = current with { MemoryMinMb = minimum, MemoryMaxMb = maximum };
        }

        if (jvmArgsPresent)
        {
            current = current with
            {
                JvmArgs = string.IsNullOrWhiteSpace(jvmArgs) ? null : jvmArgs,
            };
        }

        if (gcMode is not null)
        {
            current = current with { GcMode = gcMode };
        }

        if (gameArgsPresent)
        {
            current = current with
            {
                GameArgs = string.IsNullOrWhiteSpace(gameArgs) ? null : gameArgs,
            };
        }

        if (downloadSource is not null ||
            downloadConcurrency is not null ||
            downloadPerHost is not null)
        {
            current = current with
            {
                DownloadSource = downloadSource ?? current.DownloadSource,
                DownloadConcurrency = downloadConcurrency ?? current.DownloadConcurrency,
                DownloadPerHost = downloadPerHost ?? current.DownloadPerHost,
            };
        }

        if (downloadProxyPresent)
        {
            current = current with
            {
                DownloadProxy = string.IsNullOrEmpty(downloadProxy)
                    ? null
                    : downloadProxy,
            };
        }

        if (instancesRoots is not { Count: > 0 } &&
            string.IsNullOrWhiteSpace(legacyRoot) &&
            !javaPathPresent &&
            javaAuto is null &&
            memoryMin is null &&
            memoryMax is null &&
            !jvmArgsPresent &&
            gcMode is null &&
            !gameArgsPresent &&
            downloadSource is null &&
            downloadConcurrency is null &&
            downloadPerHost is null &&
            !downloadProxyPresent)
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
            writer.WriteNumber("memoryMinMb", settings.MemoryMinMb);
            writer.WriteNumber("memoryMaxMb", settings.MemoryMaxMb);
            if (string.IsNullOrWhiteSpace(settings.JvmArgs))
            {
                writer.WriteNull("jvmArgs");
            }
            else
            {
                writer.WriteString("jvmArgs", settings.JvmArgs);
            }
            writer.WriteString("gcMode", settings.GcMode);
            if (string.IsNullOrWhiteSpace(settings.GameArgs))
            {
                writer.WriteNull("gameArgs");
            }
            else
            {
                writer.WriteString("gameArgs", settings.GameArgs);
            }
            writer.WriteString("downloadSource", settings.DownloadSource);
            writer.WriteNumber("downloadConcurrency", settings.DownloadConcurrency);
            writer.WriteNumber("downloadPerHost", settings.DownloadPerHost);
            if (string.IsNullOrWhiteSpace(settings.DownloadProxy))
            {
                writer.WriteNull("downloadProxy");
            }
            else
            {
                writer.WriteString("downloadProxy", settings.DownloadProxy);
            }
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
