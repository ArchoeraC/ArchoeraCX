using System.Text.Json;
using System.Text;
using Archoera.Application.Downloads;
using Archoera.Application.Modules;
using Archoera.Application.Mojang;
using Archoera.Domain.Downloads;
using Archoera.Domain.Mojang;
using Archoera.Infrastructure.Launch;
using Archoera.Infrastructure.Modules;

namespace Archoera.Infrastructure.Mojang;

/// <summary>Registered handler for read-only Mojang version metadata.</summary>
public sealed class VersionModuleService : IModuleMethodHandler
{
    private readonly string[] _supportedMethods;
    private readonly IMojangMetadataClient _client;
    private readonly MojangPlanService _planService;
    private readonly DownloadPolicy _policy;

    public VersionModuleService(
        IMojangMetadataClient? client = null,
        bool enableDownloads = true,
        DownloadPolicy? policy = null)
    {
        _policy = policy ?? new DownloadPolicy();
        _supportedMethods = enableDownloads
            ? ["version.list", "version.plan", "version.commit"]
            : ["version.list"];
        _client = client ?? new MojangClient(
            new HttpClient(),
            new MojangClientOptions
            {
                UserAgent = MojangClientOptions.CreateUserAgent(
                    "0.1.0",
                    "https://github.com/BetaStudio2/ArchoeraCX"),
            });
        _planService = new MojangPlanService(_client);
    }

    public IReadOnlyCollection<string> Methods => _supportedMethods;

    private async Task<byte[]> PlanAsync(
        string? paramsJson,
        CancellationToken cancellationToken)
    {
        try
        {
            using var document = JsonDocument.Parse(paramsJson ?? "{}");
            var root = ReadRequired(document.RootElement, "root");
            var versionId = ReadRequired(document.RootElement, "versionId");
            var installRoot = LaunchPaths.ResolveInstallRoot(root);
            var plan = await _planService
                .BuildForVersionAsync(
                    installRoot,
                    versionId,
                    MojangPlatform.Current(),
                    cancellationToken)
                .ConfigureAwait(false);
            var versionJson = await _client
                .GetVersionJsonAsync(versionId, cancellationToken)
                .ConfigureAwait(false);
            var planJson = DownloadPlanJson.Build(plan);
            using var planDocument = JsonDocument.Parse(planJson);
            return ModuleJson.Envelope(writer =>
            {
                writer.WriteString("versionId", versionId);
                writer.WriteString("installRoot", installRoot);
                writer.WriteString("versionJson", Encoding.UTF8.GetString(versionJson));
                writer.WritePropertyName("plan");
                planDocument.RootElement.WriteTo(writer);
            });
        }
        catch (Exception exception) when (
            exception is IOException or JsonException or MojangException or
            DownloadPlanException or HttpRequestException or ArgumentException)
        {
            return ModuleJson.Error("VERSION_PLAN_FAILED", exception.Message);
        }
    }

    private static byte[] Commit(string? paramsJson)
    {
        try
        {
            using var document = JsonDocument.Parse(paramsJson ?? "{}");
            var root = ReadRequired(document.RootElement, "root");
            var versionId = ReadRequired(document.RootElement, "versionId");
            var versionJson = ReadRequired(document.RootElement, "versionJson");
            if (!DownloadPlanPaths.IsPlainFileName(versionId))
            {
                return ModuleJson.Error("E_INVALID", "versionId is not a plain file name");
            }

            var installRoot = LaunchPaths.ResolveInstallRoot(root);
            var versionDirectory = Path.Combine(installRoot, "versions", versionId);
            Directory.CreateDirectory(versionDirectory);
            var path = Path.Combine(versionDirectory, $"{versionId}.json");
            var temporary = path + ".tmp";
            File.WriteAllText(
                temporary,
                versionJson,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            File.Move(temporary, path, overwrite: true);
            return ModuleJson.Envelope(writer =>
            {
                writer.WriteString("versionId", versionId);
                writer.WriteString("installRoot", installRoot);
                writer.WriteString("versionPath", path);
            });
        }
        catch (Exception exception) when (
            exception is IOException or JsonException or ArgumentException or
            UnauthorizedAccessException)
        {
            return ModuleJson.Error("VERSION_COMMIT_FAILED", exception.Message);
        }
    }

    private static string ReadRequired(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var element) ||
            element.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(element.GetString()))
        {
            throw new ArgumentException($"{name} is required");
        }

        return element.GetString()!.Trim();
    }

    public async Task<byte[]> HandleAsync(
        string method,
        string? paramsJson,
        CancellationToken cancellationToken)
    {
        if (string.Equals(method, "version.plan", StringComparison.Ordinal))
        {
            try
            {
                await _policy.EnsureEnabledAsync(cancellationToken).ConfigureAwait(false);
                return await PlanAsync(paramsJson, cancellationToken).ConfigureAwait(false);
            }
            catch (DownloadPolicyException exception)
            {
                return ModuleJson.Error(exception.Code, exception.Message);
            }
        }

        if (string.Equals(method, "version.commit", StringComparison.Ordinal))
        {
            try
            {
                await _policy.EnsureEnabledAsync(cancellationToken).ConfigureAwait(false);
                return Commit(paramsJson);
            }
            catch (DownloadPolicyException exception)
            {
                return ModuleJson.Error(exception.Code, exception.Message);
            }
        }

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
