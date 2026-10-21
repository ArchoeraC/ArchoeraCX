using System.Text.Json;
using Archoera.Infrastructure.Settings;

namespace Archoera.Infrastructure.Modules;

/// <summary>Result of evaluating the local download-risk gate.</summary>
public sealed record DownloadPolicyState(
    bool Enabled,
    string Reason,
    string ConfigPath,
    int NoticeVersion,
    string? DownloadLevel,
    IReadOnlyList<string> MissingFields);

/// <summary>Raised when a download method is called before the local gate is open.</summary>
public sealed class DownloadPolicyException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

/// <summary>
/// Mainland-China builds keep download code available for local builds, but
/// require a user-authored compliance.json with explicit risk acceptance.
/// Global/dev builds are enabled by default and do not require the file.
/// </summary>
public sealed class DownloadPolicy
{
    public const int CurrentNoticeVersion = 1;
    public const string Acknowledgement = "ENABLE-DOWNLOADS 1";

    private readonly string _configPath;
    private readonly bool _chinaBuild;

    public DownloadPolicy(string? dataRoot = null, bool? chinaBuild = null)
    {
        var root = AppDataPaths.Normalize(dataRoot ?? AppDataPaths.ResolveDataRoot());
        _configPath = Path.Combine(root, "compliance.json");
        _chinaBuild = chinaBuild ?? IsChinaBuild();
    }

    public string ConfigPath => _configPath;

    public DownloadPolicyState Read()
    {
        if (!_chinaBuild)
        {
            return new DownloadPolicyState(
                true,
                "ENABLED",
                _configPath,
                CurrentNoticeVersion,
                "L2",
                []);
        }

        if (!File.Exists(_configPath))
        {
            return Disabled(
                "CONSENT_REQUIRED",
                ["developerMode", "acceptRisk", "downloadLevel", "acknowledgement"],
                null);
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(_configPath));
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return Disabled("CONSENT_INVALID", ["developerMode", "acceptRisk", "downloadLevel", "acknowledgement"], null);
            }

            var missing = new List<string>();
            var schema = root.TryGetProperty("schemaVersion", out var schemaElement) &&
                         schemaElement.TryGetInt32(out var schemaValue)
                ? schemaValue
                : 0;
            if (schema != 1) missing.Add("schemaVersion");

            var developerMode = ReadBool(root, "developerMode");
            if (!developerMode) missing.Add("developerMode");
            var acceptRisk = ReadBool(root, "acceptRisk");
            if (!acceptRisk) missing.Add("acceptRisk");

            var level = ReadString(root, "downloadLevel");
            if (!string.Equals(level, "L2", StringComparison.OrdinalIgnoreCase))
            {
                missing.Add("downloadLevel");
            }

            var acknowledgement = ReadString(root, "acknowledgement");
            if (!string.Equals(acknowledgement, Acknowledgement, StringComparison.Ordinal))
            {
                missing.Add("acknowledgement");
            }

            return missing.Count == 0
                ? new DownloadPolicyState(true, "ENABLED", _configPath, CurrentNoticeVersion, level, [])
                : Disabled("CONSENT_REQUIRED", missing, level);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return Disabled("CONSENT_INVALID", ["developerMode", "acceptRisk", "downloadLevel", "acknowledgement"], null);
        }
    }

    public void EnsureEnabled()
    {
        var state = Read();
        if (state.Enabled) return;
        throw new DownloadPolicyException(
            "DOWNLOAD_DISABLED",
            $"Downloads are disabled by local policy ({state.Reason}). " +
            $"Edit {state.ConfigPath} and restart/re-check. Missing/invalid: " +
            string.Join(", ", state.MissingFields));
    }

    private DownloadPolicyState Disabled(
        string reason,
        IReadOnlyList<string> missing,
        string? level)
        => new(false, reason, _configPath, CurrentNoticeVersion, level, missing);

    private static bool ReadBool(JsonElement root, string name)
        => root.TryGetProperty(name, out var value) &&
           value.ValueKind == JsonValueKind.True;

    private static string? ReadString(JsonElement root, string name)
        => root.TryGetProperty(name, out var value) &&
           value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static bool IsChinaBuild()
    {
#if ARCHOERACX_CHINA_BUILD
        return true;
#else
        return false;
#endif
    }
}
