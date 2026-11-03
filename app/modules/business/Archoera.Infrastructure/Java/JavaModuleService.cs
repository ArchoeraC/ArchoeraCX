using System.Net;
using System.Text.Json;
using Archoera.Application;
using Archoera.Application.Downloads;
using Archoera.Application.Java;
using Archoera.Application.JavaRuntime;
using Archoera.Application.Json;
using Archoera.Application.Modules;
using Archoera.Domain.Downloads;
using Archoera.Domain.Java;
using Archoera.Domain.Mojang;
using Archoera.Infrastructure.Downloads;
using Archoera.Infrastructure.Java.Distributions;
using Archoera.Infrastructure.Modules;
using Archoera.Infrastructure.Mojang;
using Archoera.Infrastructure.Settings;

namespace Archoera.Infrastructure.Java;

/// <summary>Registered handler for <c>java.*</c> methods.</summary>
public sealed class JavaModuleService : IModuleMethodHandler
{
    private const string SupportedVersionsUrl =
        "https://github.com/BetaStudio2/ArchoeraCX";

    private const string ScanMethod = "java.scan";
    private const string SelectMethod = "java.select";
    private const string PlanRuntimeMethod = "java.runtime.plan";
    private const string DistributionsMethod = "java.distributions";
    private const string DistributionMajorsMethod = "java.distributions.majors";
    private const string DistributionVersionsMethod = "java.distributions.versions";
    private const string DistributionAdvisoriesMethod =
        "java.distributions.advisories";
    private const string DistributionInstallMethod = "java.distributions.install";

    private static readonly string[] SupportedMethods =
    [
        ScanMethod,
        SelectMethod,
        PlanRuntimeMethod,
        DistributionsMethod,
        DistributionMajorsMethod,
        DistributionVersionsMethod,
        DistributionAdvisoriesMethod,
        DistributionInstallMethod,
    ];

    private readonly IJavaScanner _scanner;
    private readonly JavaRuntimeSelectionService _selection;
    private readonly JavaRuntimePlanService _runtimePlans;
    private readonly JavaDistributionService _distributions;
    private readonly string _dataRoot;
    private readonly Dictionary<string, ModuleMethodDelegate> _handlers;

    public JavaModuleService(
        IJavaScanner? scanner = null,
        JsonAppSettingsStore? settings = null,
        string? dataRoot = null,
        JavaRuntimePlanService? runtimePlans = null)
        : this(scanner, settings, dataRoot, runtimePlans, distributions: null)
    {
    }

    /// <summary>Test seam: inject the JDK catalogue instead of the registry.</summary>
    internal JavaModuleService(
        IJavaScanner? scanner,
        JsonAppSettingsStore? settings,
        string? dataRoot,
        JavaRuntimePlanService? runtimePlans,
        JavaDistributionService? distributions)
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
                        ArchoeraBuildInfo.Version,
                        SupportedVersionsUrl),
                }));
        _distributions = distributions ?? CreateDistributionService(_dataRoot);
        _handlers = new Dictionary<string, ModuleMethodDelegate>(StringComparer.Ordinal)
        {
            [ScanMethod] = ScanAsync,
            [SelectMethod] = SelectAsync,
            [PlanRuntimeMethod] = PlanRuntimeAsync,
            [DistributionsMethod] = DistributionsAsync,
            [DistributionMajorsMethod] = DistributionMajorsAsync,
            [DistributionVersionsMethod] = DistributionVersionsAsync,
            [DistributionAdvisoriesMethod] = DistributionAdvisoriesAsync,
            [DistributionInstallMethod] = DistributionInstallAsync,
        };
    }

    /// <summary>
    /// Shared HTTP client and the registration order shown by the install
    /// page. Adding a vendor is one line here plus its provider class.
    /// </summary>
    private static JavaDistributionService CreateDistributionService(string dataRoot)
    {
        var http = new HttpClient(
            new HttpClientHandler
            {
                AutomaticDecompression = DecompressionMethods.All,
            })
        {
            Timeout = TimeSpan.FromSeconds(120),
        };
        http.DefaultRequestHeaders.UserAgent.ParseAdd(JavaDistributionHttp.UserAgent);
        http.DefaultRequestHeaders.Accept.ParseAdd("application/json");
        var registry = new JavaDistributionRegistry(
        [
            new AdoptiumJavaDistributionProvider(http),
            new ZuluJavaDistributionProvider(http),
            new MicrosoftJavaDistributionProvider(http),
            new GraalVmJavaDistributionProvider(http),
            new CorrettoJavaDistributionProvider(http),
            new LibericaJavaDistributionProvider(http),
            new SapMachineJavaDistributionProvider(http),
            new DragonwellJavaDistributionProvider(http),
        ]);
        var cve = new JavaCveCatalog(http, dataRoot);
        return new JavaDistributionService(
            registry,
            dataRoot,
            cve: cve,
            http: http);
    }

    public IReadOnlyCollection<string> Methods => SupportedMethods;

    public Task<byte[]> HandleAsync(
        string method,
        string? paramsJson,
        CancellationToken cancellationToken)
    {
        return _handlers.TryGetValue(method, out var handler)
            ? handler(paramsJson, cancellationToken)
            : Task.FromResult(ModuleJson.Error(
                "E_UNSUPPORTED",
                $"unsupported java method '{method}'"));
    }

    private Task<byte[]> ScanAsync(
        string? paramsJson,
        CancellationToken cancellationToken)
    {
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
    /// Static catalogue: registered vendors, host capability and the default
    /// install root. No vendor network call, so the page can paint instantly.
    /// </summary>
    private Task<byte[]> DistributionsAsync(
        string? paramsJson,
        CancellationToken cancellationToken)
    {
        JavaDistributionPlatform platform;
        try
        {
            platform = _distributions.Platform;
        }
        catch (JavaDistributionException exception)
        {
            return Task.FromResult(ModuleJson.Error(exception.Code, exception.Message));
        }

        return Task.FromResult(ModuleJson.Envelope(writer =>
        {
            writer.WriteString("defaultRoot", _distributions.DefaultRoot);
            writer.WriteNumber(
                "recommendedMajor",
                JavaDistributionService.MinecraftRecommendedMajor);
            writer.WriteStartObject("platform");
            writer.WriteString("os", platform.Os);
            writer.WriteString("arch", platform.Arch);
            writer.WriteString("archive", platform.ArchiveType);
            writer.WriteBoolean("current", true);
            writer.WriteEndObject();
            writer.WriteStartArray("platforms");
            foreach (var target in _distributions.Platforms)
            {
                writer.WriteStartObject();
                writer.WriteString("os", target.Os);
                writer.WriteString("arch", target.Arch);
                writer.WriteString("archive", target.ArchiveType);
                writer.WriteBoolean("current", target.IsCurrentHost);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteStartArray("providers");
            foreach (var provider in _distributions.Providers)
            {
                writer.WriteStartObject();
                writer.WriteString("id", provider.Id);
                writer.WriteString("name", provider.Name);
                writer.WriteString("shortName", provider.ShortName);
                writer.WriteString("vendor", provider.Vendor);
                writer.WriteString("homepage", provider.Homepage);
                writer.WriteBoolean("listsAllVersions", provider.ListsAllVersions);
                writer.WriteNumber("recommendedMajor", provider.RecommendedMajor);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
        }));
    }

    private async Task<byte[]> DistributionMajorsAsync(
        string? paramsJson,
        CancellationToken cancellationToken)
    {
        try
        {
            using var reader = JsonReader.Parse(paramsJson);
            var provider = reader.RequireString("provider");
            var platform = ReadPlatform(reader);
            var majors = await _distributions
                .GetMajorsAsync(provider, cancellationToken, platform)
                .ConfigureAwait(false);
            var recommended = _distributions.RecommendedMajor(provider);
            return ModuleJson.Envelope(writer =>
            {
                writer.WriteString("provider", provider);
                writer.WriteNumber("recommendedMajor", recommended);
                writer.WriteStartArray("majors");
                foreach (var major in majors) writer.WriteNumberValue(major);
                writer.WriteEndArray();
            });
        }
        catch (JsonReaderException exception)
        {
            return ModuleJson.Error(exception.Code, exception.Message);
        }
        catch (JavaDistributionException exception)
        {
            return ModuleJson.Error(exception.Code, exception.Message);
        }
    }

    private async Task<byte[]> DistributionVersionsAsync(
        string? paramsJson,
        CancellationToken cancellationToken)
    {
        try
        {
            using var reader = JsonReader.Parse(paramsJson);
            var provider = reader.RequireString("provider");
            var major = reader.RequireInt32("major");
            var platform = ReadPlatform(reader);
            var releases = await _distributions
                .GetReleasesAsync(provider, major, cancellationToken, platform)
                .ConfigureAwait(false);
            var recommended = _distributions.RecommendedMajor(provider);
            return ModuleJson.Envelope(writer =>
            {
                writer.WriteString("provider", provider);
                writer.WriteNumber("major", major);
                writer.WriteNumber("recommendedMajor", recommended);
                writer.WriteStartArray("versions");
                foreach (var release in releases)
                {
                    writer.WriteStartObject();
                    writer.WriteString("id", release.VersionId);
                    writer.WriteNumber("major", release.Major);
                    writer.WriteString("version", release.Version);
                    writer.WriteString("archive", release.ArchiveType);
                    writer.WriteNumber("size", release.Size);
                    if (release.Sha256 is null)
                    {
                        writer.WriteNull("sha256");
                    }
                    else
                    {
                        writer.WriteString("sha256", release.Sha256);
                    }

                    writer.WriteBoolean("lts", release.Lts);
                    writer.WriteBoolean("recommended", release.Recommended);
                    writer.WriteBoolean("latestPatch", release.LatestPatch);
                    if (release.ReleasedAt is null)
                    {
                        writer.WriteNull("releasedAt");
                    }
                    else
                    {
                        writer.WriteString("releasedAt", release.ReleasedAt);
                    }

                    writer.WriteEndObject();
                }

                writer.WriteEndArray();
            });
        }
        catch (JsonReaderException exception)
        {
            return ModuleJson.Error(exception.Code, exception.Message);
        }
        catch (JavaDistributionException exception)
        {
            return ModuleJson.Error(exception.Code, exception.Message);
        }
    }

    /// <summary>
    /// Optional target triple. Omitting every platform key keeps the current
    /// host; providing only one of os/arch is rejected instead of guessed.
    /// </summary>
    private static JavaDistributionPlatform? ReadPlatform(JsonReader reader)
    {
        var os = reader.OptionalString("os");
        var arch = reader.OptionalString("arch");
        var archive = reader.OptionalString("archive");
        if (string.IsNullOrWhiteSpace(os) &&
            string.IsNullOrWhiteSpace(arch) &&
            string.IsNullOrWhiteSpace(archive))
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(os) || string.IsNullOrWhiteSpace(arch))
        {
            throw new JavaDistributionException(
                "JAVA_PLATFORM_UNSUPPORTED",
                "os and arch must be provided together");
        }

        return JavaDistributionPlatform.Parse(os, arch, archive);
    }

    /// <summary>
    /// Returns CVE ids that affect one resolved build. The lookup is
    /// best-effort: an unavailable feed is reported in the payload instead of
    /// failing the whole catalogue request.
    /// </summary>
    private async Task<byte[]> DistributionAdvisoriesAsync(
        string? paramsJson,
        CancellationToken cancellationToken)
    {
        try
        {
            using var reader = JsonReader.Parse(paramsJson);
            var provider = reader.RequireString("provider");
            var major = reader.RequireInt32("major");
            var versionId = reader.RequireString("versionId");
            var platform = ReadPlatform(reader);
            var report = await _distributions
                .GetAdvisoriesAsync(
                    provider,
                    major,
                    versionId,
                    cancellationToken,
                    platform)
                .ConfigureAwait(false);
            return ModuleJson.Envelope(writer =>
            {
                writer.WriteString("provider", report.Provider);
                writer.WriteString("version", report.Version);
                writer.WriteString("status", report.Status);
                if (report.Reason is null)
                {
                    writer.WriteNull("reason");
                }
                else
                {
                    writer.WriteString("reason", report.Reason);
                }

                writer.WriteString("checkedAt", report.CheckedAt);
                writer.WriteStartArray("sources");
                foreach (var source in report.Sources)
                {
                    writer.WriteStringValue(source);
                }

                writer.WriteEndArray();
                writer.WriteStartArray("advisories");
                foreach (var advisory in report.Advisories)
                {
                    writer.WriteStartObject();
                    writer.WriteString("id", advisory.Id);
                    writer.WriteString("severity", advisory.Severity);
                    if (advisory.Score is { } score)
                    {
                        writer.WriteNumber("score", score);
                    }
                    else
                    {
                        writer.WriteNull("score");
                    }

                    writer.WriteString("summary", advisory.Summary);
                    if (advisory.Url is null)
                    {
                        writer.WriteNull("url");
                    }
                    else
                    {
                        writer.WriteString("url", advisory.Url);
                    }

                    if (advisory.PublishedAt is null)
                    {
                        writer.WriteNull("publishedAt");
                    }
                    else
                    {
                        writer.WriteString("publishedAt", advisory.PublishedAt);
                    }

                    writer.WriteStartArray("aliases");
                    foreach (var alias in advisory.Aliases)
                    {
                        writer.WriteStringValue(alias);
                    }

                    writer.WriteEndArray();
                    writer.WriteEndObject();
                }

                writer.WriteEndArray();
            });
        }
        catch (JsonReaderException exception)
        {
            return ModuleJson.Error(exception.Code, exception.Message);
        }
        catch (JavaDistributionException exception)
        {
            return ModuleJson.Error(exception.Code, exception.Message);
        }
    }

    /// <summary>
    /// Resolves a provider-native build id, downloads the binary archive
    /// through the Rust executor, verifies/extracts it and returns the JDK
    /// home. The UI never supplies a URL or hash.
    /// </summary>
    private async Task<byte[]> DistributionInstallAsync(
        string? paramsJson,
        CancellationToken cancellationToken)
    {
        try
        {
            using var reader = JsonReader.Parse(paramsJson);
            var provider = reader.RequireString("provider");
            var versionId = reader.RequireString("versionId");
            var directory = reader.OptionalString("directory");
            var autoExtract = reader.OptionalBool("autoExtract") ?? true;
            var autoDelete = reader.OptionalBool("autoDelete") ?? true;
            var allowOutdated = reader.OptionalBool("allowOutdated") ?? false;
            var acknowledgeCves = reader.OptionalBool("acknowledgeCves") ?? false;
            var allowIncompatible =
                reader.OptionalBool("allowIncompatible") ?? false;
            var platform = ReadPlatform(reader);
            var result = await _distributions
                .InstallAsync(
                    provider,
                    versionId,
                    directory,
                    autoExtract,
                    autoDelete,
                    allowOutdated,
                    acknowledgeCves,
                    cancellationToken,
                    platform,
                    allowIncompatible)
                .ConfigureAwait(false);
            return ModuleJson.Envelope(writer =>
            {
                writer.WriteString("provider", result.Provider);
                writer.WriteString("version", result.Version);
                writer.WriteNumber("major", result.Major);
                writer.WriteString("home", result.Home);
                writer.WriteString("executable", result.Executable);
                writer.WriteNumber("bytes", result.Bytes);
                writer.WriteBoolean("extracted", result.Extracted);
                if (result.Archive is null)
                {
                    writer.WriteNull("archive");
                }
                else
                {
                    writer.WriteString("archive", result.Archive);
                }
            });
        }
        catch (JsonReaderException exception)
        {
            return ModuleJson.Error(exception.Code, exception.Message);
        }
        catch (JavaDistributionException exception)
        {
            return ModuleJson.Error(exception.Code, exception.Message);
        }
        catch (DownloadRunnerException exception)
        {
            return ModuleJson.Error(exception.Code, exception.Message);
        }
        catch (DownloadPlanException exception)
        {
            return ModuleJson.Error("JAVA_DOWNLOAD_FAILED", exception.Message);
        }
        catch (Exception exception) when (
            exception is ArgumentException or NotSupportedException or
            PathTooLongException or IOException or UnauthorizedAccessException)
        {
            return ModuleJson.Error("JAVA_INSTALL_FAILED", exception.Message);
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
