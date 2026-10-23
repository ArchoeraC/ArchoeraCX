using System.Text.Json;

namespace Archoera.Infrastructure.Instances;

/// <summary>
/// The <c>instance.mods.*</c> handlers: list mod files of an instance, disable /
/// re-enable a mod by renaming, or delete one mod file.
/// </summary>
public sealed partial class InstanceModuleService
{
    private async Task<byte[]> ListModsAsync(
        string? paramsJson,
        CancellationToken cancellationToken)
    {
        using var document = Parse(paramsJson);
        var instanceId = ReadString(document.RootElement, "instanceId")
            ?? throw new ArgumentException("instanceId is required");
        var directory = await ResolveModsDirectoryAsync(instanceId, cancellationToken)
            .ConfigureAwait(false);
        var files = Directory.Exists(directory)
            ? Directory.EnumerateFiles(directory, "*", SearchOption.TopDirectoryOnly)
                .Where(IsModFileName)
                .OrderBy(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase)
                .ToArray()
            : [];

        return BuildEnvelope(writer =>
        {
            writer.WriteStartArray("items");
            foreach (var file in files) WriteMod(writer, file);
            writer.WriteEndArray();
            writer.WriteNumber("count", files.Length);
        });
    }

    private async Task<byte[]> SetModEnabledAsync(
        string? paramsJson,
        CancellationToken cancellationToken)
    {
        using var document = Parse(paramsJson);
        var root = document.RootElement;
        var instanceId = ReadString(root, "instanceId")
            ?? throw new ArgumentException("instanceId is required");
        var fileName = NormalizeModFileName(
            ReadString(root, "fileName")
            ?? throw new ArgumentException("fileName is required"));
        var enabled = root.TryGetProperty("enabled", out var enabledElement) &&
                      enabledElement.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? enabledElement.GetBoolean()
            : throw new ArgumentException("enabled must be a boolean");
        var directory = await ResolveModsDirectoryAsync(instanceId, cancellationToken)
            .ConfigureAwait(false);
        var source = Path.Combine(directory, fileName);
        if (!File.Exists(source))
        {
            throw new ArgumentException("mod file not found");
        }

        var baseName = fileName.EndsWith(".jar.disabled", StringComparison.OrdinalIgnoreCase)
            ? fileName[..^".disabled".Length]
            : fileName;
        var targetName = enabled ? baseName : baseName + ".disabled";
        var target = Path.Combine(directory, targetName);
        if (!string.Equals(source, target, StringComparison.Ordinal))
        {
            if (File.Exists(target))
            {
                throw new ArgumentException("target mod file already exists");
            }
            File.Move(source, target);
        }

        return BuildEnvelope(writer =>
        {
            writer.WritePropertyName("item");
            WriteMod(writer, target);
        });
    }

    private async Task<byte[]> RemoveModAsync(
        string? paramsJson,
        CancellationToken cancellationToken)
    {
        using var document = Parse(paramsJson);
        var root = document.RootElement;
        var instanceId = ReadString(root, "instanceId")
            ?? throw new ArgumentException("instanceId is required");
        var fileName = NormalizeModFileName(
            ReadString(root, "fileName")
            ?? throw new ArgumentException("fileName is required"));
        var directory = await ResolveModsDirectoryAsync(instanceId, cancellationToken)
            .ConfigureAwait(false);
        var path = Path.Combine(directory, fileName);
        if (!File.Exists(path))
        {
            throw new ArgumentException("mod file not found");
        }
        File.Delete(path);
        return BuildEnvelope(writer => writer.WriteBoolean("removed", true));
    }

    private async Task<string> ResolveModsDirectoryAsync(
        string instanceId,
        CancellationToken cancellationToken)
    {
        var items = await _repository.ListAsync(cancellationToken).ConfigureAwait(false);
        var record = items.FirstOrDefault(item => item.Id == instanceId)
            ?? throw new ArgumentException("instance not found");
        if (!Directory.Exists(record.RootPath))
        {
            throw new ArgumentException("instance directory is missing");
        }
        return Path.Combine(record.RootPath, "mods");
    }

    private static string NormalizeModFileName(string value)
    {
        var name = value.Trim();
        if (name.Length == 0 ||
            name.Contains('/') ||
            name.Contains('\\') ||
            name is "." or ".." ||
            !IsModFileName(name))
        {
            throw new ArgumentException("invalid mod file name");
        }
        return name;
    }

    private static bool IsModFileName(string path)
    {
        var name = Path.GetFileName(path);
        return name.EndsWith(".jar", StringComparison.OrdinalIgnoreCase) ||
               name.EndsWith(".jar.disabled", StringComparison.OrdinalIgnoreCase);
    }

    private static void WriteMod(Utf8JsonWriter writer, string path)
    {
        var fileName = Path.GetFileName(path);
        var disabled = fileName.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase);
        var name = disabled ? fileName[..^".disabled".Length] : fileName;
        var info = new FileInfo(path);
        writer.WriteStartObject();
        writer.WriteString("fileName", fileName);
        writer.WriteString("name", name);
        writer.WriteBoolean("enabled", !disabled);
        writer.WriteNumber("size", info.Exists ? info.Length : 0);
        writer.WriteString("modifiedAt", info.Exists ? info.LastWriteTimeUtc.ToString("O") : null);
        writer.WriteEndObject();
    }
}
