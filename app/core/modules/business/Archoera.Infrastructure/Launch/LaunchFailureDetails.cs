using System.Text.Json;
using Archoera.Infrastructure.Modules;

namespace Archoera.Infrastructure.Launch;

/// <summary>
/// Adds structured launch diagnostics to the normal error envelope. The UI
/// shows the failing stage and can copy the values verbatim into a report;
/// secrets (tokens, account credentials) are never included.
/// </summary>
internal static class LaunchFailureDetails
{
    public static byte[] Error(
        string code,
        string message,
        string stage,
        string instanceId,
        string? javaPath,
        string? javaVersion,
        string? logPath,
        int? requiredJavaMajor = null)
        => ModuleJson.Error(
            code,
            message,
            details =>
            {
                if (!string.IsNullOrEmpty(instanceId))
                {
                    details.WriteString("instanceId", instanceId);
                }

                details.WriteString("stage", stage);
                details.WriteBoolean("retryable", IsRetryable(code));
                if (!string.IsNullOrEmpty(javaPath))
                {
                    details.WriteString("javaPath", javaPath);
                }

                if (!string.IsNullOrEmpty(javaVersion))
                {
                    details.WriteString("javaVersion", javaVersion);
                }

                if (!string.IsNullOrEmpty(logPath))
                {
                    details.WriteString("logPath", logPath);
                }

                if (requiredJavaMajor is > 0)
                {
                    details.WriteNumber("javaRequired", requiredJavaMajor.Value);
                }
            });

    /// <summary>
    /// True when retrying the same launch can plausibly succeed after the
    /// missing files are repaired or Java is installed; false when the user
    /// must change an account, policy or configuration decision first.
    /// </summary>
    public static bool IsRetryable(string code) => code switch
    {
        "LIBRARIES_MISSING" => true,
        "CLIENT_JAR_MISSING" => true,
        "ASSET_INDEX_MISSING" => true,
        "ASSET_INDEX_INVALID" => true,
        "ASSET_MISSING" => true,
        "LOADER_VERSION_MISSING" => true,
        "LOADER_FILES_MISSING" => true,
        "LOADER_NOT_INSTALLED" => true,
        "LAUNCH_FILES_MISSING" => true,
        "VERSION_JSON_MISSING" => true,
        "VERSION_NOT_MERGED" => true,
        "VERSION_INVALID" => true,
        "JAVA_MISSING" => true,
        "JAVA_VERSION_MISMATCH" => true,
        "AUTO_REPAIR_FAILED" => true,
        "LAUNCH_FAILED" => true,
        _ => false,
    };
}
