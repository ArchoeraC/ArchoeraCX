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
            var instancesRoot = ReadString(root, "instancesRoot");
            if (string.IsNullOrWhiteSpace(instancesRoot) ||
                !Path.IsPathFullyQualified(instancesRoot))
            {
                instancesRoot = fallback.InstancesRoot;
            }

            return fallback with { InstancesRoot = AppDataPaths.Normalize(instancesRoot) };
        }
        catch (Exception exception) when (
            exception is IOException or JsonException or UnauthorizedAccessException)
        {
            return fallback;
        }
    }

    public async Task<AppSettings> SaveInstancesRootAsync(
        string instancesRoot,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(instancesRoot) ||
            !Path.IsPathFullyQualified(instancesRoot))
        {
            throw new ArgumentException("instancesRoot must be absolute", nameof(instancesRoot));
        }

        var normalized = AppDataPaths.Normalize(instancesRoot);
        Directory.CreateDirectory(normalized);
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
            writer.WriteString("instancesRoot", normalized);
            writer.WriteString("updatedAt", DateTimeOffset.UtcNow.ToString("O"));
            writer.WriteEndObject();
            await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
            await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        }

        File.Move(temporary, _settingsPath, overwrite: true);
        return new AppSettings(normalized, _dataRoot, _settingsPath);
    }

    private AppSettings Defaults()
        => new(
            AppDataPaths.DefaultInstancesRoot(_dataRoot),
            _dataRoot,
            _settingsPath);

    private static string? ReadString(JsonElement root, string name)
        => root.TryGetProperty(name, out var element) &&
           element.ValueKind == JsonValueKind.String
            ? element.GetString()
            : null;
}
