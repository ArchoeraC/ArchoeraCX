using System.Text.Json;
using Archoera.Domain.Loaders;

namespace Archoera.Application.Loaders;

/// <summary>Validated parameters for <c>loader.verify</c> / <c>loader.repair</c>.</summary>
public sealed record LoaderRequest(
    string InstallerPath,
    string Root,
    string LibrariesPath,
    LoaderKind Kind,
    string? JavaPath);

/// <summary>
/// AOT-safe request parser for the loader diagnostics methods. Paths must be
/// absolute so the module never depends on its own process working directory;
/// the planner still confines the libraries root inside the instance root.
/// </summary>
public static class LoaderRequestJson
{
    public static bool TryParse(
        string? json,
        out LoaderRequest? request,
        out string errorCode,
        out string errorMessage)
    {
        request = null;
        errorCode = "E_INVALID";
        errorMessage = string.Empty;

        if (string.IsNullOrWhiteSpace(json))
        {
            errorMessage = "loader request JSON is required";
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                errorMessage = "loader request must be a JSON object";
                return false;
            }

            if (!TryReadRequiredString(root, "installerPath", out var installerPath, out errorMessage) ||
                !TryReadRequiredString(root, "root", out var instanceRoot, out errorMessage) ||
                !TryReadRequiredString(root, "kind", out var kindText, out errorMessage))
            {
                return false;
            }

            if (!Path.IsPathFullyQualified(installerPath) ||
                !Path.IsPathFullyQualified(instanceRoot))
            {
                errorMessage = "installerPath and root must be absolute paths";
                return false;
            }

            var librariesPath = ReadOptionalString(root, "librariesPath") ?? "libraries";
            if (string.IsNullOrWhiteSpace(librariesPath) || Path.IsPathFullyQualified(librariesPath))
            {
                errorMessage = "librariesPath must be a non-empty relative path";
                return false;
            }

            var javaPath = ReadOptionalString(root, "javaPath");
            if (javaPath is not null && !Path.IsPathFullyQualified(javaPath))
            {
                errorMessage = "javaPath must be absolute when supplied";
                return false;
            }

            if (!TryParseKind(kindText, out var kind))
            {
                errorMessage = "kind must be 'forge' or 'neoforge'";
                return false;
            }

            request = new LoaderRequest(
                Path.GetFullPath(installerPath),
                Path.GetFullPath(instanceRoot),
                librariesPath.Replace('\\', '/'),
                kind,
                javaPath is null ? null : Path.GetFullPath(javaPath));
            errorCode = string.Empty;
            return true;
        }
        catch (JsonException exception)
        {
            errorMessage = $"loader request is not valid JSON: {exception.Message}";
            return false;
        }
    }

    private static bool TryReadRequiredString(
        JsonElement root,
        string name,
        out string value,
        out string error)
    {
        value = string.Empty;
        if (!root.TryGetProperty(name, out var element) ||
            element.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(element.GetString()))
        {
            error = $"{name} is required";
            return false;
        }

        value = element.GetString()!;
        error = string.Empty;
        return true;
    }

    private static string? ReadOptionalString(JsonElement root, string name)
        => root.TryGetProperty(name, out var element) &&
           element.ValueKind == JsonValueKind.String &&
           !string.IsNullOrWhiteSpace(element.GetString())
            ? element.GetString()
            : null;

    private static bool TryParseKind(string value, out LoaderKind kind)
    {
        if (string.Equals(value, "forge", StringComparison.OrdinalIgnoreCase))
        {
            kind = LoaderKind.Forge;
            return true;
        }

        if (string.Equals(value, "neoforge", StringComparison.OrdinalIgnoreCase))
        {
            kind = LoaderKind.NeoForge;
            return true;
        }

        kind = default;
        return false;
    }
}
