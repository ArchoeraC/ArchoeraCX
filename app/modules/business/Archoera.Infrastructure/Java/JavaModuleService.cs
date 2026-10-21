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
    private readonly JavaRuntimeSelectionService _selection;

    public JavaModuleService(
        IJavaScanner? scanner = null,
        JsonAppSettingsStore? settings = null)
    {
        _scanner = scanner ?? new JavaScanner();
        _selection = new JavaRuntimeSelectionService(_scanner, settings);
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

        try
        {
            var selected = _selection
                .SelectAsync(requiredMajor, gameVersion, cancellationToken)
                .GetAwaiter()
                .GetResult();
            return Task.FromResult(JavaScanJson.BuildSelection(selected));
        }
        catch (JavaSelectionException exception)
        {
            return Task.FromResult(ModuleJson.Error(exception.Code, exception.Message));
        }
    }

}
