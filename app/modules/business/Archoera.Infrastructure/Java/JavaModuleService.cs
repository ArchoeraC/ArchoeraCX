using System.Text.Json;
using Archoera.Application.Downloads;
using Archoera.Application.Java;
using Archoera.Application.JavaRuntime;
using Archoera.Application.Modules;
using Archoera.Domain.Java;
using Archoera.Domain.Mojang;
using Archoera.Infrastructure.Modules;
using Archoera.Infrastructure.Mojang;
using Archoera.Infrastructure.Settings;

namespace Archoera.Infrastructure.Java;

/// <summary>Registered handler for <c>java.*</c> methods.</summary>
public sealed class JavaModuleService : IModuleMethodHandler
{
    private const string SupportedVersionsUrl =
        "https://github.com/BetaStudio2/ArchoeraCX";

    private static readonly string[] SupportedMethods =
        ["java.scan", "java.select", "java.runtime.plan"];

    private readonly IJavaScanner _scanner;
    private readonly JavaRuntimeSelectionService _selection;
    private readonly JavaRuntimePlanService _runtimePlans;
    private readonly string _dataRoot;

    public JavaModuleService(
        IJavaScanner? scanner = null,
        JsonAppSettingsStore? settings = null,
        string? dataRoot = null,
        JavaRuntimePlanService? runtimePlans = null)
    {
        _scanner = scanner ?? new JavaScanner();
        var store = settings ?? new JsonAppSettingsStore(dataRoot);
        _dataRoot = store.DataRoot;
        _selection = new JavaRuntimeSelectionService(_scanner, store);
        _runtimePlans = runtimePlans ?? new JavaRuntimePlanService(
            new JavaRuntimeClient(
                new HttpClient(),
                new MojangClientOptions
                {
                    UserAgent = MojangClientOptions.CreateUserAgent(
                        "0.0.1+1",
                        SupportedVersionsUrl),
                }));
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

        if (string.Equals(method, "java.runtime.plan", StringComparison.Ordinal))
        {
            return PlanRuntimeAsync(paramsJson, cancellationToken);
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

        // Runtimes installed by the launcher live under the data root; always
        // include them so a fresh install shows up on the next scan.
        var managedRoot = ManagedJavaRuntimes.Root(_dataRoot);
        if (!request.Roots.Contains(managedRoot, StringComparer.Ordinal))
        {
            request = request with
            {
                Roots = [.. request.Roots, managedRoot],
            };
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


    /// <summary>
    /// Builds the Mojang runtime download plan for one Java feature release.
    /// The caller executes it through <c>download.execute</c>; nothing is
    /// downloaded here.
    /// </summary>
    private async Task<byte[]> PlanRuntimeAsync(
        string? paramsJson,
        CancellationToken cancellationToken)
    {
        string? root = null;
        int? major = null;
        try
        {
            if (!string.IsNullOrWhiteSpace(paramsJson))
            {
                using var document = JsonDocument.Parse(paramsJson);
                var element = document.RootElement;
                if (element.ValueKind != JsonValueKind.Object)
                {
                    return ModuleJson.Error("E_INVALID", "params must be an object");
                }

                if (element.TryGetProperty("root", out var rootElement) &&
                    rootElement.ValueKind == JsonValueKind.String)
                {
                    root = rootElement.GetString();
                }

                if (element.TryGetProperty("major", out var majorElement) &&
                    majorElement.ValueKind == JsonValueKind.Number &&
                    majorElement.TryGetInt32(out var value))
                {
                    major = value;
                }
            }
        }
        catch (JsonException exception)
        {
            return ModuleJson.Error("E_INVALID", exception.Message);
        }

        if (major is null || major is < 8 or > 99)
        {
            return ModuleJson.Error(
                "E_INVALID",
                "java.runtime.plan requires a Java major between 8 and 99");
        }

        string targetRoot;
        try
        {
            // The plan prefixes destinations with runtime/java-<major>, so the
            // plan root is the data root (or the caller's shared root).
            targetRoot = string.IsNullOrWhiteSpace(root)
                ? _dataRoot
                : Path.GetFullPath(root.Trim());
        }
        catch (Exception exception) when (
            exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return ModuleJson.Error("E_INVALID", "root must be an absolute path");
        }

        try
        {
            var result = await _runtimePlans
                .BuildAsync(targetRoot, major.Value, MojangPlatform.Current(), cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            var planJson = DownloadPlanJson.Build(result.Plan);
            using var planDocument = JsonDocument.Parse(planJson);
            return ModuleJson.Envelope(writer =>
            {
                writer.WriteString("root", targetRoot);
                writer.WriteNumber("major", major.Value);
                writer.WriteString("component", result.ComponentName);
                writer.WriteString("version", result.VersionName);
                writer.WriteString("platformKey", result.PlatformKey);
                writer.WriteNumber("fileCount", result.FileCount);
                writer.WriteNumber("totalBytes", result.TotalBytes);
                writer.WritePropertyName("plan");
                planDocument.RootElement.WriteTo(writer);
            });
        }
        catch (JavaRuntimeException exception)
        {
            return ModuleJson.Error(exception.Code, exception.Message);
        }
    }
}
