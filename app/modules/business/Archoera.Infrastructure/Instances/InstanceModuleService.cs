using System.Text.Json;
using Archoera.Application.Instances;
using Archoera.Application.Modules;
using Archoera.Domain.Instances;

namespace Archoera.Infrastructure.Instances;

/// <summary>JSON facade for instance.list/create/update/delete.</summary>
public sealed class InstanceModuleService : IModuleMethodHandler
{
    private static readonly string[] SupportedMethods =
        ["instance.list", "instance.create", "instance.update", "instance.delete"];

    private readonly IInstanceRepository _repository;

    public InstanceModuleService(IInstanceRepository? repository = null)
    {
        _repository = repository ?? new SqliteInstanceRepository();
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
                "instance.list" => await ListAsync(cancellationToken).ConfigureAwait(false),
                "instance.create" => await CreateAsync(paramsJson, cancellationToken)
                    .ConfigureAwait(false),
                "instance.update" => await UpdateAsync(paramsJson, cancellationToken)
                    .ConfigureAwait(false),
                "instance.delete" => await DeleteAsync(paramsJson, cancellationToken)
                    .ConfigureAwait(false),
                _ => Error("E_UNSUPPORTED", $"unsupported instance method '{method}'"),
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

    private async Task<byte[]> ListAsync(CancellationToken cancellationToken)
    {
        var items = await _repository.ListAsync(cancellationToken).ConfigureAwait(false);
        return BuildEnvelope(writer =>
        {
            writer.WriteStartArray("items");
            foreach (var item in items) WriteRecord(writer, item);
            writer.WriteEndArray();
        });
    }

    private async Task<byte[]> CreateAsync(
        string? paramsJson,
        CancellationToken cancellationToken)
    {
        using var document = Parse(paramsJson);
        var root = document.RootElement;
        var record = await _repository.CreateAsync(
                ReadString(root, "name") ?? throw new ArgumentException("name is required"),
                ReadString(root, "gameVersion") ?? throw new ArgumentException("gameVersion is required"),
                ReadString(root, "loader") ?? "vanilla",
                ReadString(root, "loaderVersion") ?? string.Empty,
                ReadString(root, "rootPath"),
                cancellationToken)
            .ConfigureAwait(false);
        return BuildEnvelope(writer =>
        {
            writer.WritePropertyName("instance");
            WriteRecord(writer, record);
        });
    }

    private async Task<byte[]> UpdateAsync(
        string? paramsJson,
        CancellationToken cancellationToken)
    {
        using var document = Parse(paramsJson);
        var root = document.RootElement;
        var id = ReadString(root, "id") ?? throw new ArgumentException("id is required");
        var name = ReadString(root, "name");
        bool? favorite = root.TryGetProperty("favorite", out var favoriteElement) &&
                         favoriteElement.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? favoriteElement.GetBoolean()
            : null;
        DateTimeOffset? lastPlayed = null;
        if (ReadString(root, "lastPlayedAt") is { } lastPlayedText)
        {
            if (!DateTimeOffset.TryParse(lastPlayedText, out var parsed))
            {
                throw new ArgumentException("lastPlayedAt must be an ISO timestamp");
            }

            lastPlayed = parsed;
        }

        var record = await _repository
            .UpdateAsync(id, name, favorite, lastPlayed, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new ArgumentException("instance not found");
        return BuildEnvelope(writer =>
        {
            writer.WritePropertyName("instance");
            WriteRecord(writer, record);
        });
    }

    private async Task<byte[]> DeleteAsync(
        string? paramsJson,
        CancellationToken cancellationToken)
    {
        using var document = Parse(paramsJson);
        var id = ReadString(document.RootElement, "id") ?? throw new ArgumentException("id is required");
        var deleted = await _repository.DeleteAsync(id, cancellationToken).ConfigureAwait(false);
        return BuildEnvelope(writer => writer.WriteBoolean("deleted", deleted));
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

    private static string? ReadString(JsonElement root, string name)
        => root.TryGetProperty(name, out var element) && element.ValueKind == JsonValueKind.String
            ? element.GetString()
            : null;

    private static void WriteRecord(Utf8JsonWriter writer, GameInstanceRecord record)
    {
        writer.WriteStartObject();
        writer.WriteString("id", record.Id);
        writer.WriteString("name", record.Name);
        writer.WriteString("gameVersion", record.GameVersion);
        writer.WriteString("loader", record.Loader);
        writer.WriteString("loaderVersion", record.LoaderVersion);
        writer.WriteString("rootPath", record.RootPath);
        writer.WriteNumber("modCount", record.ModCount);
        writer.WriteBoolean("favorite", record.Favorite);
        writer.WriteString("createdAt", record.CreatedAt.ToString("O"));
        writer.WriteString("updatedAt", record.UpdatedAt.ToString("O"));
        writer.WriteString("lastPlayedAt", record.LastPlayedAt?.ToString("O"));
        writer.WriteEndObject();
    }

    private static byte[] BuildEnvelope(Action<Utf8JsonWriter> data)
    {
        using var memory = new MemoryStream();
        using (var writer = new Utf8JsonWriter(memory))
        {
            writer.WriteStartObject();
            writer.WriteBoolean("ok", true);
            writer.WriteStartObject("data");
            data(writer);
            writer.WriteEndObject();
            writer.WriteStartObject("meta");
            writer.WriteNumber("schemaVersion", 1);
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
