using System.Text.Json;
using Archoera.Application.Java;
using Archoera.Application.Modules;
using Archoera.Domain.Java;
using Archoera.Infrastructure.Modules;
using Archoera.Infrastructure.Settings;

namespace Archoera.Infrastructure.Java;

/// <summary>Registered handler for <c>java.*</c> methods.</summary>
public sealed class JavaModuleService : IModuleMethodHandler
{
    private static readonly string[] SupportedMethods = ["java.scan", "java.select"];

    private readonly IJavaScanner _scanner;
    private readonly JsonAppSettingsStore _settings;

    public JavaModuleService(
        IJavaScanner? scanner = null,
        JsonAppSettingsStore? settings = null)
    {
        _scanner = scanner ?? new JavaScanner();
        _settings = settings ?? new JsonAppSettingsStore();
    }

    public IReadOnlyCollection<string> Methods => SupportedMethods;

    public Task<byte[]> HandleAsync(
        string method,
        string? paramsJson,
        CancellationToken cancellationToken)
    {
        if (string.Equals(method, "java.select", StringComparison.Ordinal))
        {
            return SelectAsync(paramsJson, cancellationToken);
        }

        if (!string.Equals(method, "java.scan", StringComparison.Ordinal))
        {
            return Task.FromResult(ModuleJson.Error(
                "E_UNSUPPORTED",
                $"unsupported java method '{method}'"));
        }

        if (!JavaScanRequestJson.TryParse(paramsJson, out var request))
        {
            return Task.FromResult(ModuleJson.Error(
                "E_INVALID",
                "java.scan params are not a valid scan request"));
        }

        var items = _scanner.Scan(request, cancellationToken);
        return Task.FromResult(JavaScanJson.BuildEnvelope(items));
    }

    private Task<byte[]> SelectAsync(
        string? paramsJson,
        CancellationToken cancellationToken)
    {
        int? requiredMajor = null;
        string? gameVersion = null;
        try
        {
            if (!string.IsNullOrWhiteSpace(paramsJson))
            {
                using var document = JsonDocument.Parse(paramsJson);
                var root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object)
                {
                    return Task.FromResult(ModuleJson.Error("E_INVALID", "params must be an object"));
                }
                if (root.TryGetProperty("requiredMajor", out var majorElement) &&
                    majorElement.ValueKind == JsonValueKind.Number)
                {
                    requiredMajor = majorElement.GetInt32();
                }
                if (root.TryGetProperty("gameVersion", out var versionElement) &&
                    versionElement.ValueKind == JsonValueKind.String)
                {
                    gameVersion = versionElement.GetString();
                }
            }
        }
        catch (JsonException exception)
        {
            return Task.FromResult(ModuleJson.Error("E_INVALID", exception.Message));
        }

        requiredMajor ??= InferMajor(gameVersion);
        var settings = _settings.LoadAsync(cancellationToken).GetAwaiter().GetResult();
        IReadOnlyList<JavaRuntimeInfo> items;
        if (!settings.JavaAuto && !string.IsNullOrWhiteSpace(settings.JavaPath))
        {
            items = _scanner.Scan(
                new JavaScanRequest
                {
                    IncludeDefaults = false,
                    Executables = [settings.JavaPath],
                },
                cancellationToken);
            if (items.Count == 0)
            {
                return Task.FromResult(ModuleJson.Error(
                    "JAVA_MISSING",
                    $"configured Java is not usable: {settings.JavaPath}"));
            }
        }
        else
        {
            items = _scanner.Scan(JavaScanRequest.Default, cancellationToken);
        }

        var selected = JavaSelector.Select(items, requiredMajor);
        if (selected is null)
        {
            return Task.FromResult(ModuleJson.Error(
                requiredMajor is null ? "JAVA_MISSING" : "JAVA_VERSION_MISMATCH",
                requiredMajor is null
                    ? "no Java runtime found"
                    : $"no Java {requiredMajor}+ runtime found"));
        }

        return Task.FromResult(JavaScanJson.BuildSelection(selected));
    }

    private static int? InferMajor(string? id)
    {
        if (string.IsNullOrWhiteSpace(id)) return null;
        if (id.StartsWith("26.", StringComparison.Ordinal)) return 25;
        if (id.StartsWith("1.20.5", StringComparison.Ordinal) ||
            id.StartsWith("1.21", StringComparison.Ordinal)) return 21;
        if (id.StartsWith("1.18", StringComparison.Ordinal) ||
            id.StartsWith("1.19", StringComparison.Ordinal)) return 17;
        if (id.StartsWith("1.17", StringComparison.Ordinal)) return 16;
        return null;
    }
}
