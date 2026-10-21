using System.Text.Json;
using Archoera.Domain.Settings;

namespace Archoera.Infrastructure.Settings;

/// <summary>
/// Small atomic JSON settings store. It is intentionally not SQLite yet:
/// instance roots are a bootstrap setting, while the instance database will
/// own per-instance metadata later.
/// </summary>
public sealed class JsonAppSettingsStore
{
    private readonly string _dataRoot;
    private readonly string _settingsPath;

    public JsonAppSettingsStore(string? dataRoot = null)
    {
        _dataRoot = AppDataPaths.Normalize(dataRoot ?? AppDataPaths.ResolveDataRoot());
        _settingsPath = AppDataPaths.SettingsPath(_dataRoot);
    }

    public string DataRoot => _dataRoot;
    public string SettingsPathValue => _settingsPath;

    public async Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        var fallback = Defaults();
        if (!File.Exists(_settingsPath))
        {
            return fallback;
        }

        try
        {
            await using var stream = new FileStream(
                _settingsPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 4096,
                useAsync: true);
            using var document = await JsonDocument
                .ParseAsync(stream, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return fallback;
            }

            var root = document.RootElement;
            var roots = ReadRoots(root);
            if (roots.Count == 0)
            {
                var legacy = ReadString(root, "instancesRoot");
                if (!string.IsNullOrWhiteSpace(legacy) && Path.IsPathFullyQualified(legacy))
                {
                    roots.Add(legacy);
                }
            }

            if (roots.Count == 0)
            {
                roots.Add(fallback.InstancesRoot);
            }

            IReadOnlyList<string> normalized;
            try
            {
                normalized = NormalizeRoots(roots);
            }
            catch (ArgumentException)
            {
                normalized = [fallback.InstancesRoot];
            }

            var javaPath = ReadString(root, "javaPath");
            if (!string.IsNullOrWhiteSpace(javaPath) &&
                !Path.IsPathFullyQualified(javaPath))
            {
                javaPath = null;
            }

            var javaAuto = !root.TryGetProperty("javaAuto", out var autoElement) ||
                           autoElement.ValueKind is not (JsonValueKind.True or JsonValueKind.False) ||
                           autoElement.GetBoolean();
            var memoryMin = ReadInt(root, "memoryMinMb") ?? fallback.MemoryMinMb;
            var memoryMax = ReadInt(root, "memoryMaxMb") ?? fallback.MemoryMaxMb;
            if (memoryMin < 128) memoryMin = fallback.MemoryMinMb;
            if (memoryMax < memoryMin) memoryMax = Math.Max(memoryMin, fallback.MemoryMaxMb);
            var jvmArgs = ReadString(root, "jvmArgs");

            return fallback with
            {
                InstancesRoot = normalized[0],
                InstancesRoots = normalized,
                JavaPath = string.IsNullOrWhiteSpace(javaPath) ? null : Path.GetFullPath(javaPath),
                JavaAuto = javaAuto,
                MemoryMinMb = memoryMin,
                MemoryMaxMb = memoryMax,
                JvmArgs = string.IsNullOrWhiteSpace(jvmArgs) ? null : jvmArgs,
            };
        }
        catch (Exception exception) when (
            exception is IOException or JsonException or UnauthorizedAccessException)
        {
            return fallback;
        }
    }

    public Task<AppSettings> SaveInstancesRootAsync(
        string instancesRoot,
        CancellationToken cancellationToken = default)
        => SaveInstancesRootsAsync([instancesRoot], cancellationToken);

    public async Task<AppSettings> SaveInstancesRootsAsync(
        IReadOnlyList<string> instancesRoots,
        CancellationToken cancellationToken = default)
    {
        if (instancesRoots is null || instancesRoots.Count == 0)
        {
            throw new ArgumentException("at least one instances root is required", nameof(instancesRoots));
        }

        foreach (var root in instancesRoots)
        {
            if (string.IsNullOrWhiteSpace(root) || !Path.IsPathFullyQualified(root))
            {
                throw new ArgumentException("instances roots must be absolute", nameof(instancesRoots));
            }
        }

        var normalized = NormalizeRoots(instancesRoots);
        var current = await LoadAsync(cancellationToken).ConfigureAwait(false);
        return await SaveAsync(
                current with
                {
                    InstancesRoot = normalized[0],
                    InstancesRoots = normalized,
                },
                cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<AppSettings> SaveJavaAsync(
        string? javaPath,
        bool javaAuto,
        CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrWhiteSpace(javaPath))
        {
            if (!Path.IsPathFullyQualified(javaPath))
            {
                throw new ArgumentException("javaPath must be absolute", nameof(javaPath));
            }

            javaPath = Path.GetFullPath(javaPath);
        }

        var current = await LoadAsync(cancellationToken).ConfigureAwait(false);
        return await SaveAsync(
                current with { JavaPath = javaPath, JavaAuto = javaAuto },
                cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<AppSettings> SaveAsync(
        AppSettings settings,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var normalized = NormalizeRoots(settings.Roots);
        foreach (var root in normalized)
        {
            try
            {
                Directory.CreateDirectory(root);
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException)
            {
                // Keep offline/removable roots configured; instance.sync reports it.
            }
        }

        Directory.CreateDirectory(_dataRoot);
        var temporary = _settingsPath + ".tmp";
        await using (var stream = new FileStream(
                         temporary,
                         FileMode.Create,
                         FileAccess.Write,
                         FileShare.None,
                         bufferSize: 4096,
                         useAsync: true))
        {
            await using var writer = new Utf8JsonWriter(stream, new JsonWriterOptions
            {
                Indented = true,
            });
            writer.WriteStartObject();
            writer.WriteNumber("schemaVersion", AppSettings.CurrentSchemaVersion);
            writer.WriteString("instancesRoot", normalized[0]);
            writer.WriteStartArray("instancesRoots");
            foreach (var root in normalized)
            {
                writer.WriteStringValue(root);
            }

            writer.WriteEndArray();
            if (!string.IsNullOrWhiteSpace(settings.JavaPath))
            {
                writer.WriteString("javaPath", settings.JavaPath);
            }
            writer.WriteBoolean("javaAuto", settings.JavaAuto);
            writer.WriteNumber("memoryMinMb", settings.MemoryMinMb);
            writer.WriteNumber("memoryMaxMb", settings.MemoryMaxMb);
            if (!string.IsNullOrWhiteSpace(settings.JvmArgs))
            {
                writer.WriteString("jvmArgs", settings.JvmArgs);
            }
            writer.WriteString("updatedAt", DateTimeOffset.UtcNow.ToString("O"));
            writer.WriteEndObject();
            await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
            await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        }

        File.Move(temporary, _settingsPath, overwrite: true);
        return settings with
        {
            InstancesRoot = normalized[0],
            InstancesRoots = normalized,
        };
    }

    private AppSettings Defaults()
    {
        var instancesRoot = AppDataPaths.DefaultInstancesRoot(_dataRoot);
        return new AppSettings(
            instancesRoot,
            _dataRoot,
            _settingsPath,
            [instancesRoot]);
    }

    private static List<string> ReadRoots(JsonElement root)
    {
        var result = new List<string>();
        if (!root.TryGetProperty("instancesRoots", out var element) ||
            element.ValueKind != JsonValueKind.Array)
        {
            return result;
        }

        foreach (var item in element.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String &&
                !string.IsNullOrWhiteSpace(item.GetString()))
            {
                result.Add(item.GetString()!);
            }
        }

        return result;
    }

    private static IReadOnlyList<string> NormalizeRoots(IEnumerable<string> roots)
    {
        var seen = new HashSet<string>(
            OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
                ? StringComparer.OrdinalIgnoreCase
                : StringComparer.Ordinal);
        var result = new List<string>();
        foreach (var root in roots)
        {
            if (string.IsNullOrWhiteSpace(root) || !Path.IsPathFullyQualified(root))
            {
                continue;
            }

            var normalized = AppDataPaths.Normalize(root);
            if (seen.Add(normalized))
            {
                result.Add(normalized);
            }
        }

        return result.Count > 0
            ? result
            : throw new ArgumentException("at least one valid instances root is required", nameof(roots));
    }

    private static int? ReadInt(JsonElement root, string name)
        => root.TryGetProperty(name, out var element) &&
           element.ValueKind == JsonValueKind.Number &&
           element.TryGetInt32(out var value)
            ? value
            : null;

    private static string? ReadString(JsonElement root, string name)
        => root.TryGetProperty(name, out var element) &&
           element.ValueKind == JsonValueKind.String
            ? element.GetString()
            : null;
}
