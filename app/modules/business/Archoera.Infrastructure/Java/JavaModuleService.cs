using System.Text.Json;
using Archoera.Application.Java;
using Archoera.Application.Modules;
using Archoera.Infrastructure.Modules;

namespace Archoera.Infrastructure.Java;

/// <summary>Registered handler for <c>java.*</c> methods.</summary>
public sealed class JavaModuleService : IModuleMethodHandler
{
    private static readonly string[] SupportedMethods = ["java.scan"];

    private readonly IJavaScanner _scanner;

    public JavaModuleService(IJavaScanner? scanner = null)
    {
        _scanner = scanner ?? new JavaScanner();
    }

    public IReadOnlyCollection<string> Methods => SupportedMethods;

    public Task<byte[]> HandleAsync(
        string method,
        string? paramsJson,
        CancellationToken cancellationToken)
    {
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
}
