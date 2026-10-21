using System.Text.Json;
using Archoera.Application.Modules;
using Archoera.Application.Mojang;
using Archoera.Infrastructure.Modules;

namespace Archoera.Infrastructure.Mojang;

/// <summary>Registered handler for read-only Mojang version metadata.</summary>
public sealed class VersionModuleService : IModuleMethodHandler
{
    private static readonly string[] SupportedMethods = ["version.list"];

    private readonly IMojangMetadataClient _client;

    public VersionModuleService(IMojangMetadataClient? client = null)
    {
        _client = client ?? new MojangClient(
            new HttpClient(),
            new MojangClientOptions
            {
                UserAgent = MojangClientOptions.CreateUserAgent(
                    "0.1.0",
                    "https://github.com/BetaStudio2/ArchoeraCX"),
            });
    }

    public IReadOnlyCollection<string> Methods => SupportedMethods;

    public async Task<byte[]> HandleAsync(
        string method,
        string? paramsJson,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(method, "version.list", StringComparison.Ordinal))
        {
            return ModuleJson.Error("E_UNSUPPORTED", $"unsupported version method '{method}'");
        }

        try
        {
            var includeSnapshots = false;
            if (!string.IsNullOrWhiteSpace(paramsJson))
            {
                using var document = JsonDocument.Parse(paramsJson);
                if (document.RootElement.ValueKind == JsonValueKind.Object &&
                    document.RootElement.TryGetProperty("includeSnapshots", out var element) &&
                    element.ValueKind is JsonValueKind.True or JsonValueKind.False)
                {
                    includeSnapshots = element.GetBoolean();
                }
            }

            var manifest = await _client.GetManifestAsync(cancellationToken).ConfigureAwait(false);
            using var memory = new MemoryStream();
            using (var writer = new Utf8JsonWriter(memory))
            {
                writer.WriteStartObject();
                writer.WriteBoolean("ok", true);
                writer.WriteStartObject("data");
                writer.WriteString("latestRelease", manifest.LatestRelease);
                writer.WriteString("latestSnapshot", manifest.LatestSnapshot);
                writer.WriteStartArray("items");
                foreach (var item in manifest.Versions)
                {
                    if (!includeSnapshots &&
                        !string.Equals(item.Type, "release", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    writer.WriteStartObject();
                    writer.WriteString("id", item.Id);
                    writer.WriteString("type", item.Type);
                    writer.WriteString("url", item.Url);
                    writer.WriteString("sha1", item.Sha1);
                    if (item.ReleaseTime is not null)
                    {
                        writer.WriteString("releaseTime", item.ReleaseTime.Value.ToString("O"));
                    }
                    writer.WriteNumber("complianceLevel", item.ComplianceLevel);
                    writer.WriteEndObject();
                }
                writer.WriteEndArray();
                writer.WriteEndObject();
                writer.WriteStartObject("meta");
                writer.WriteNumber("schemaVersion", 1);
                writer.WriteEndObject();
                writer.WriteEndObject();
            }
            return memory.ToArray();
        }
        catch (Exception exception) when (
            exception is IOException or JsonException or MojangException or HttpRequestException)
        {
            return ModuleJson.Error("VERSION_LIST_FAILED", exception.Message);
        }
    }
}
