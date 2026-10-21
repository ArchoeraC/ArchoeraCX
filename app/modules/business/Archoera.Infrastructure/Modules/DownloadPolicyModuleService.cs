using System.Text.Json;
using Archoera.Application.Modules;

namespace Archoera.Infrastructure.Modules;

/// <summary>Read-only policy state for the download UI.</summary>
public sealed class DownloadPolicyModuleService : IModuleMethodHandler
{
    private static readonly string[] SupportedMethods = ["download.policy"];
    private readonly DownloadPolicy _policy;

    public DownloadPolicyModuleService(DownloadPolicy policy)
    {
        _policy = policy;
    }

    public IReadOnlyCollection<string> Methods => SupportedMethods;

    public async Task<byte[]> HandleAsync(
        string method,
        string? paramsJson,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(method, "download.policy", StringComparison.Ordinal))
        {
            return ModuleJson.Error(
                "E_UNSUPPORTED",
                $"unsupported policy method '{method}'");
        }

        var state = await _policy.ReadAsync(cancellationToken).ConfigureAwait(false);
        return ModuleJson.Envelope(writer =>
        {
            writer.WriteBoolean("enabled", state.Enabled);
            writer.WriteString("reason", state.Reason);
            writer.WriteString("configPath", state.ConfigPath);
            writer.WriteNumber("noticeVersion", state.NoticeVersion);
            writer.WriteString("downloadLevel", state.DownloadLevel);
            writer.WriteStartArray("missingFields");
            foreach (var field in state.MissingFields)
            {
                writer.WriteStringValue(field);
            }
            writer.WriteEndArray();
        });
    }
}
