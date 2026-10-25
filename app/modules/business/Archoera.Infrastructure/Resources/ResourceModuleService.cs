using System.Text.Json;
using Archoera.Application;
using Archoera.Application.Downloads;
using Archoera.Application.Modules;
using Archoera.Application.Resources;
using Archoera.Domain.Downloads;
using Archoera.Domain.Resources;
using Archoera.Infrastructure.Downloads;
using Archoera.Infrastructure.Settings;
using Archoera.Infrastructure.Modules;
using Archoera.Infrastructure.Resources.Modrinth;

namespace Archoera.Infrastructure.Resources;

/// <summary>
/// Native-module facade for the resource UI. It keeps provider/HTTP details
/// behind stable JSON methods so Dart never talks to Modrinth directly.
///
/// The temporary <c>download.execute</c> path invokes the Rust CLI bridge until
/// the C++ DownloadManager/Rust module owns effects. It still consumes only the
/// validated DownloadPlan and never downloads bytes in C#.
/// </summary>
public sealed class ResourceModuleService : IModuleMethodHandler
{
    private const string SearchMethod = "resource.search";
    private const string ProjectMethod = "resource.project";
    private const string VersionsMethod = "resource.versions";
    private const string PlanMethod = "resource.plan";
    private const string ExecuteMethod = "download.execute";

    private static readonly string[] SupportedMethods =
        [SearchMethod, ProjectMethod, VersionsMethod, PlanMethod, ExecuteMethod];

    /// <summary>
    /// Methods that require the download policy gate before any work happens;
    /// the guard runs before request parsing, exactly as the guard did in the
    /// pre-dictionary switch shape.
    /// </summary>
    private static readonly HashSet<string> PolicyMethods =
        new(StringComparer.Ordinal)
        {
            SearchMethod, ProjectMethod, VersionsMethod, PlanMethod, ExecuteMethod,
        };

    public const string Modrinth = "modrinth";

    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromSeconds(60),
    };

    private readonly DownloadPlanRunner _runner;
    private readonly DownloadPolicy _policy;
    private readonly JsonAppSettingsStore _settings;
    private readonly RegionPolicy _regionPolicy;
    private readonly Dictionary<string, ModuleMethodDelegate> _handlers;

    public ResourceModuleService(
        string? downloaderPath = null,
        DownloadPolicy? policy = null,
        JsonAppSettingsStore? settings = null,
        RegionPolicy? regionPolicy = null)
    {
        _runner = new DownloadPlanRunner(downloaderPath);
        _policy = policy ?? new DownloadPolicy();
        _settings = settings ?? new JsonAppSettingsStore();
        _regionPolicy = regionPolicy ?? new RegionPolicy();
        _handlers = new Dictionary<string, ModuleMethodDelegate>(StringComparer.Ordinal)
        {
            [SearchMethod] = (paramsJson, ct) => RunAsync(paramsJson, SearchAsync, ct),
            [ProjectMethod] = (paramsJson, ct) => RunAsync(paramsJson, ProjectAsync, ct),
            [VersionsMethod] = (paramsJson, ct) => RunAsync(paramsJson, VersionsAsync, ct),
            [PlanMethod] = (paramsJson, ct) => RunAsync(paramsJson, PlanAsync, ct),
            [ExecuteMethod] = (paramsJson, ct) => RunAsync(paramsJson, ExecuteAsync, ct),
        };
    }

    /// <summary>
    /// Adapter between the string-parameter dispatch delegate and handlers
    /// that consume a validated <see cref="JsonElement"/> request root; the
    /// document lives only for the duration of the handler call.
    /// </summary>
    private static async Task<byte[]> RunAsync(
        string? paramsJson,
        Func<JsonElement, CancellationToken, Task<byte[]>> body,
        CancellationToken cancellationToken)
    {
        using var document = ParseRequest(paramsJson);
        return await body(document.RootElement, cancellationToken).ConfigureAwait(false);
    }

    private async Task<DownloadSourceMode> SourceModeAsync(CancellationToken cancellationToken)
    {
        try
        {
            var settings = await _settings.LoadAsync(cancellationToken).ConfigureAwait(false);
            return await DownloadSourceResolver
                .ResolveAsync(settings.DownloadSource, _regionPolicy, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            return DownloadSourceMode.Auto;
        }
    }

    public IReadOnlyCollection<string> Methods => SupportedMethods;

    public async Task<byte[]> HandleAsync(
        string method,
        string? paramsJson,
        CancellationToken cancellationToken)
    {
        try
        {
            if (PolicyMethods.Contains(method))
            {
                await _policy.EnsureEnabledAsync(cancellationToken).ConfigureAwait(false);
            }

            // The request document is validated (and disposed) here so an
            // unknown method with a malformed request answers exactly as it did
            // under the pre-dictionary switch: E_INVALID from ParseRequest.
            using var document = ParseRequest(paramsJson);

            return _handlers.TryGetValue(method, out var handler)
                ? await handler(paramsJson, cancellationToken).ConfigureAwait(false)
                : Error("E_UNSUPPORTED", $"unsupported resource method '{method}'");
        }
        catch (ResourceProviderException exception)
        {
            return Error(exception.Code, exception.Message);
        }
        catch (DownloadPolicyException exception)
        {
            return Error(exception.Code, exception.Message);
        }
        catch (DownloadPlanException exception)
        {
            return Error(exception.Code, exception.Message);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            return Error("E_INTERNAL", exception.Message);
        }
    }

    private async Task<byte[]> SearchAsync(
        JsonElement root,
        CancellationToken cancellationToken)
    {
        var request = ParseSearch(root);
        var client = CreateClient(root);
        var result = await client
            .SearchAsync(request, cancellationToken)
            .ConfigureAwait(false);

        return BuildSearch(result);
    }

    private async Task<byte[]> ProjectAsync(
        JsonElement root,
        CancellationToken cancellationToken)
    {
        var provider = ReadProvider(root);
        var project = RequireString(root, "projectId");
        var client = CreateClient(root);
        var result = await client
            .GetProjectAsync(project, cancellationToken)
            .ConfigureAwait(false);
        return BuildProject(result);
    }

    private async Task<byte[]> VersionsAsync(
        JsonElement root,
        CancellationToken cancellationToken)
    {
        var provider = ReadProvider(root);
        var project = RequireString(root, "projectId");
        var kind = ReadKind(root);
        var client = CreateClient(root);

        if (kind == ResourceKind.Modpack)
        {
            // Packs are self-contained: do not filter by the selected
            // instance. The chosen version carries its own game/loader deps.
            var allVersions = await client
                .GetAllVersionsAsync(project, cancellationToken)
                .ConfigureAwait(false);
            return BuildVersions(provider, allVersions);
        }

        var target = ReadTarget(root, kind);
        var versions = await client
            .GetVersionsAsync(project, kind, target, cancellationToken)
            .ConfigureAwait(false);

        return BuildVersions(provider, versions);
    }

    private async Task<byte[]> PlanAsync(
        JsonElement root,
        CancellationToken cancellationToken)
    {
        var provider = ReadProvider(root);
        if (!string.Equals(provider, Modrinth, StringComparison.OrdinalIgnoreCase))
        {
            // Reject before ReadKind/Directory.CreateDirectory: an unavailable
            // provider must not touch the filesystem or change error precedence.
            throw new ResourceProviderException(
                ResourceErrorCode.ProviderUnavailable,
                "CurseForge provider is unavailable until API access is configured; use Modrinth");
        }

        var kind = ReadKind(root);
        if (kind == ResourceKind.Modpack)
        {
            throw new ResourceProviderException(
                ResourceErrorCode.InvalidRequest,
                "modpacks are installed through modpack.install, not resource.plan");
        }

        var target = ReadTarget(root, kind);
        var project = RequireString(root, "projectId");
        var versionId = ReadOptionalString(root, "versionId");
        var rootPath = RequireString(root, "root");
        var includeDependencies = root.TryGetProperty(
                "includeDependencies",
                out var includeDependenciesElement) &&
            includeDependenciesElement.ValueKind is JsonValueKind.True or JsonValueKind.False &&
            includeDependenciesElement.GetBoolean();
        Directory.CreateDirectory(rootPath);
        var client = CreateClient(root);

        ResourceVersion version;
        if (string.IsNullOrWhiteSpace(versionId))
        {
            var versions = await client
                .GetVersionsAsync(project, kind, target, cancellationToken)
                .ConfigureAwait(false);
            version = versions.FirstOrDefault(
                          candidate => candidate.VersionType == ResourceVersionType.Release)
                      ?? versions.FirstOrDefault()
                      ?? throw new ResourceProviderException(
                          ResourceErrorCode.NoCompatibleBuild,
                          $"no compatible Modrinth version for {kind.ToWire()}");
        }
        else
        {
            version = await client
                .GetVersionAsync(versionId, cancellationToken)
                .ConfigureAwait(false);
        }

        var sourceMode = await SourceModeAsync(cancellationToken).ConfigureAwait(false);
        var plans = new List<DownloadPlan>
        {
            ResourceDownloadPlanBuilder.Build(rootPath, kind, target, version, sourceMode: sourceMode),
        };
        var warnings = new List<string>();
        if (includeDependencies && version.DependencyList.Count > 0)
        {
            var dependencies = await ResourceDependencyResolver
                .ResolveAsync(client, kind, target, version, cancellationToken)
                .ConfigureAwait(false);
            warnings.AddRange(dependencies.Warnings);
            foreach (var dependency in dependencies.Incompatible)
            {
                warnings.Add(
                    $"incompatible dependency ignored: {dependency.ProjectId ?? dependency.FileName ?? "unknown"}");
            }

            foreach (var dependency in dependencies.Required)
            {
                plans.Add(
                    ResourceDownloadPlanBuilder.Build(
                        rootPath,
                        dependency.Kind,
                        dependency.Target,
                        dependency.Version,
                        sourceMode: sourceMode));
            }
        }

        var plan = plans.Count == 1 ? plans[0] : DownloadPlanCombiner.Combine(plans);
        return BuildPlan(plan, warnings);
    }

    private async Task<byte[]> ExecuteAsync(
        JsonElement root,
        CancellationToken cancellationToken)
    {
        var planJson = root.TryGetProperty("plan", out var planElement)
            ? planElement.GetRawText()
            : throw new ResourceProviderException(
                ResourceErrorCode.InvalidRequest,
                "plan is required");

        if (!_runner.IsAvailable)
        {
            return Error(DownloaderMissing.Code, DownloaderMissing.Message);
        }

        if (!DownloadPlanJson.TryParse(planJson, out var plan, out var issue))
        {
            return Error(
                issue?.Code ?? DownloadPlanErrorCode.InvalidPlan,
                issue?.Message ?? "plan is not valid");
        }

        try
        {
            var result = await _runner
                .ExecuteAsync(plan!, cancellationToken, DownloadProfile.Resource)
                .ConfigureAwait(false);
            return BuildEnvelope(writer =>
            {
                writer.WriteStartObject("data");
                writer.WriteString("status", result.Status);
                writer.WriteBoolean("downloadOk", result.Ok);
                writer.WriteString("planPath", result.PlanPath);
                writer.WriteString("stdout", result.Stdout);
                writer.WriteString("stderr", result.Stderr);
                writer.WriteNumber("failedCount", result.FailedCount);
                writer.WriteEndObject();
                writer.WriteBoolean("ok", result.Ok);
            });
        }
        catch (DownloadRunnerException exception)
        {
            return Error(exception.Code, exception.Message);
        }
    }

    private static ResourceSearchRequest ParseSearch(JsonElement root)
    {
        var kind = ReadKind(root);
        var query = ReadOptionalString(root, "query") ?? string.Empty;
        var page = ReadInt(root, "page", 0);
        var pageSize = ReadInt(root, "pageSize", 20);
        var sortText = ReadOptionalString(root, "sort") ?? "relevance";
        if (!ResourceWire.TryParseSort(sortText, out var sort))
        {
            throw new ResourceProviderException(
                ResourceErrorCode.InvalidRequest,
                "sort must be relevance, downloads, follows, newest or updated");
        }

        var target = ReadSearchTarget(root, kind);

        return new ResourceSearchRequest
        {
            Query = query,
            Kind = kind,
            Target = target,
            Sort = sort,
            Page = page,
            PageSize = pageSize,
        };
    }

    private static ResourceTarget? ReadSearchTarget(
        JsonElement root,
        ResourceKind kind)
    {
        // A missing gameVersion means a global provider search. Installation
        // still requires ReadTarget because it writes into a concrete instance.
        if (string.IsNullOrWhiteSpace(ReadOptionalString(root, "gameVersion")))
        {
            return null;
        }

        return ReadTarget(root, kind);
    }

    private static ResourceTarget ReadTarget(JsonElement root, ResourceKind kind)
    {
        var game = RequireString(root, "gameVersion");
        var loader = ReadOptionalString(root, "loader");
        var shader = ReadOptionalString(root, "shaderLoader");

        return kind switch
        {
            ResourceKind.Mod => ResourceTarget.ForMod(game, loader ?? "fabric"),
            ResourceKind.Shader => new ResourceTarget(game, loader, shader ?? "iris"),
            ResourceKind.ResourcePack => new ResourceTarget(game, loader),
            ResourceKind.Modpack => new ResourceTarget(game, loader),
            _ => throw new ResourceProviderException(
                ResourceErrorCode.InvalidRequest,
                "unsupported resource kind"),
        };
    }

    private static ModrinthClient CreateClient(JsonElement root)
    {
        var provider = ReadProvider(root);
        if (!string.Equals(provider, Modrinth, StringComparison.OrdinalIgnoreCase))
        {
            throw new ResourceProviderException(
                ResourceErrorCode.ProviderUnavailable,
                "CurseForge provider is unavailable until API access is configured; use Modrinth");
        }

        var baseAddress = ResolveProviderBase(ReadOptionalString(root, "providerBase"));

        return new ModrinthClient(
            Http,
            new ModrinthClientOptions
            {
                BaseAddress = baseAddress,
                UserAgent = ModrinthClientOptions.CreateUserAgent(
                    ArchoeraBuildInfo.Version,
                    "dev@archoera.local"),
            });
    }

    /// <summary>
    /// Resolves the optional <c>providerBase</c> override. Production requests
    /// must target the official HTTPS endpoint; loopback stays available so
    /// tests and a local mock can run without weakening the host allow-list.
    /// Development builds may point anywhere over HTTP(S).
    /// </summary>
    internal static Uri ResolveProviderBase(string? baseText)
    {
        if (string.IsNullOrWhiteSpace(baseText))
        {
            return ModrinthClientOptions.DefaultBaseAddress;
        }

        if (!Uri.TryCreate(baseText.Trim(), UriKind.Absolute, out var uri))
        {
            throw new ResourceProviderException(
                ResourceErrorCode.InvalidRequest,
                "providerBase must be an absolute URI");
        }

        if (!IsProviderBaseAllowed(uri, AllowDevelopmentProviderBase))
        {
            throw new ResourceProviderException(
                ResourceErrorCode.InvalidRequest,
                "providerBase must be https://api.modrinth.com or a loopback address");
        }

        return uri;
    }

    private static bool AllowDevelopmentProviderBase
    {
        get
        {
#if DEBUG
            return true;
#else
            return false;
#endif
        }
    }

    /// <summary>
    /// Pure allow-list shared by production and tests. <paramref name="allowDevelopmentBase"/>
    /// mirrors the DEBUG-only escape so release behaviour is testable.
    /// </summary>
    internal static bool IsProviderBaseAllowed(Uri uri, bool allowDevelopmentBase)
    {
        ArgumentNullException.ThrowIfNull(uri);
        if (!uri.IsAbsoluteUri) return false;
        if (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp)
        {
            return false;
        }

        if (IsLoopbackHost(uri.Host))
        {
            return true;
        }

        if (allowDevelopmentBase) return true;

        return uri.Scheme == Uri.UriSchemeHttps &&
               uri.Port == 443 &&
               string.IsNullOrEmpty(uri.UserInfo) &&
               string.Equals(uri.Host, "api.modrinth.com", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsLoopbackHost(string host)
    {
        if (string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var bare = host.TrimStart('[').TrimEnd(']');
        return System.Net.IPAddress.TryParse(bare, out var address) &&
               System.Net.IPAddress.IsLoopback(address);
    }

    private static string ReadProvider(JsonElement root)
        => ReadOptionalString(root, "provider") ?? Modrinth;

    private static ResourceKind ReadKind(JsonElement root)
    {
        var value = ReadOptionalString(root, "kind") ?? "mod";
        return ResourceWire.TryParseKind(value, out var kind)
            ? kind
            : throw new ResourceProviderException(
                ResourceErrorCode.InvalidRequest,
                "kind must be mod, shader, resourcepack or modpack");
    }

    private static JsonDocument ParseRequest(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return JsonDocument.Parse("{}");
        }

        try
        {
            return JsonDocument.Parse(json);
        }
        catch (JsonException exception)
        {
            throw new ResourceProviderException(
                ResourceErrorCode.InvalidRequest,
                $"request is not valid JSON: {exception.Message}");
        }
    }

    private static byte[] BuildSearch(ResourceSearchResult result)
        => BuildEnvelope(writer =>
        {
            writer.WriteStartObject("data");
            writer.WriteString("provider", result.Provider);
            writer.WriteNumber("total", result.Total);
            writer.WriteNumber("page", result.Page);
            writer.WriteNumber("pageSize", result.PageSize);
            writer.WriteStartArray("items");
            foreach (var project in result.Hits)
            {
                WriteProject(writer, project);
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
            writer.WriteBoolean("ok", true);
        });

    private static byte[] BuildProject(ResourceProject project)
        => BuildEnvelope(writer =>
        {
            writer.WritePropertyName("data");
            WriteProject(writer, project);
            writer.WriteBoolean("ok", true);
        });

    private static byte[] BuildVersions(
        string provider,
        IReadOnlyList<ResourceVersion> versions)
        => BuildEnvelope(writer =>
        {
            writer.WriteStartObject("data");
            writer.WriteString("provider", provider);
            writer.WriteStartArray("items");
            foreach (var version in versions)
            {
                WriteVersion(writer, version);
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
            writer.WriteBoolean("ok", true);
        });

    private static byte[] BuildPlan(
        DownloadPlan plan,
        IReadOnlyList<string>? warnings = null)
    {
        var planBytes = DownloadPlanJson.Build(plan);
        using var planDocument = JsonDocument.Parse(planBytes);
        return BuildEnvelope(writer =>
        {
            writer.WriteStartObject("data");
            writer.WritePropertyName("plan");
            planDocument.RootElement.WriteTo(writer);
            if (warnings is { Count: > 0 })
            {
                writer.WriteStartArray("warnings");
                foreach (var warning in warnings)
                {
                    writer.WriteStringValue(warning);
                }

                writer.WriteEndArray();
            }

            writer.WriteEndObject();
            writer.WriteBoolean("ok", true);
        });
    }

    private static byte[] Error(string code, string message)
        => BuildEnvelope(writer =>
        {
            writer.WriteBoolean("ok", false);
            writer.WriteStartObject("error");
            writer.WriteString("code", code);
            writer.WriteString("message", message);
            writer.WriteEndObject();
        });

    private static byte[] BuildEnvelope(Action<Utf8JsonWriter> body)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            body(writer);
            writer.WriteStartObject("meta");
            writer.WriteNumber("schemaVersion", 1);
            writer.WriteEndObject();
            writer.WriteEndObject();
        }

        return buffer.ToArray();
    }

    private static void WriteProject(Utf8JsonWriter writer, ResourceProject project)
    {
        writer.WriteStartObject();
        writer.WriteString("id", project.Id);
        writer.WriteString("slug", project.Slug);
        writer.WriteString("name", project.Name);
        writer.WriteString("summary", project.Summary);
        writer.WriteString("kind", project.Kind.ToWire());
        writer.WriteNumber("downloads", project.Downloads);
        writer.WriteNumber("follows", project.Follows);
        writer.WriteString("updatedAt", project.UpdatedAt?.ToString("O"));
        writer.WriteString("author", project.Author);
        writer.WriteString("iconUrl", project.IconUrl);
        writer.WriteString("clientSide", project.ClientSide);
        writer.WriteString("license", project.License);
        writer.WriteString("sourceUrl", project.SourceUrl);
        writer.WriteString("issuesUrl", project.IssuesUrl);
        writer.WriteString("wikiUrl", project.WikiUrl);
        writer.WriteString("publishedAt", project.PublishedAt?.ToString("O"));
        writer.WriteString("body", project.Body);
        WriteStrings(writer, "categories", project.CategoryList);
        WriteStrings(writer, "loaders", project.LoaderList);
        WriteStrings(writer, "gameVersions", project.GameVersionList);
        writer.WriteStartArray("gallery");
        foreach (var image in project.GalleryList)
        {
            writer.WriteStartObject();
            writer.WriteString("url", image.Url);
            writer.WriteBoolean("featured", image.Featured);
            writer.WriteString("title", image.Title);
            writer.WriteString("description", image.Description);
            writer.WriteNumber("ordering", image.Ordering);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    private static void WriteVersion(Utf8JsonWriter writer, ResourceVersion version)
    {
        writer.WriteStartObject();
        writer.WriteString("id", version.Id);
        writer.WriteString("projectId", version.ProjectId);
        writer.WriteString("name", version.Name);
        writer.WriteString("versionNumber", version.VersionNumber);
        writer.WriteString("versionType", version.VersionType.ToWire());
        writer.WriteString("publishedAt", version.PublishedAt?.ToString("O"));
        writer.WriteNumber("downloads", version.Downloads);
        WriteStrings(writer, "gameVersions", version.GameVersions);
        WriteStrings(writer, "loaders", version.Loaders);
        writer.WriteStartArray("files");
        foreach (var file in version.FileList)
        {
            writer.WriteStartObject();
            writer.WriteString("fileName", file.FileName);
            writer.WriteString("url", file.Url);
            writer.WriteNumber("size", file.Size);
            writer.WriteString("sha1", file.Sha1);
            writer.WriteString("sha512", file.Sha512);
            writer.WriteBoolean("primary", file.Primary);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        writer.WriteStartArray("dependencies");
        foreach (var dependency in version.DependencyList)
        {
            writer.WriteStartObject();
            writer.WriteString("kind", dependency.Kind.ToWire());
            writer.WriteString("projectId", dependency.ProjectId);
            writer.WriteString("versionId", dependency.VersionId);
            writer.WriteString("fileName", dependency.FileName);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    private static void WriteStrings(
        Utf8JsonWriter writer,
        string name,
        IReadOnlyList<string> values)
    {
        writer.WriteStartArray(name);
        foreach (var value in values)
        {
            writer.WriteStringValue(value);
        }

        writer.WriteEndArray();
    }

    private static string RequireString(JsonElement root, string name)
        => ReadOptionalString(root, name)
           ?? throw new ResourceProviderException(
               ResourceErrorCode.InvalidRequest,
               $"{name} is required");

    private static string? ReadOptionalString(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var element) ||
            element.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (element.ValueKind != JsonValueKind.String)
        {
            throw new ResourceProviderException(
                ResourceErrorCode.InvalidRequest,
                $"{name} must be a string");
        }

        var text = element.GetString();
        return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
    }

    private static int ReadInt(JsonElement root, string name, int fallback)
    {
        if (!root.TryGetProperty(name, out var element) ||
            element.ValueKind == JsonValueKind.Null)
        {
            return fallback;
        }

        if (element.ValueKind != JsonValueKind.Number ||
            !element.TryGetInt32(out var value))
        {
            throw new ResourceProviderException(
                ResourceErrorCode.InvalidRequest,
                $"{name} must be an integer");
        }

        return value;
    }

}
