using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Archoera.Application.Downloads;
using Archoera.Application.Modules;
using Archoera.Application.Resources;
using Archoera.Domain.Downloads;
using Archoera.Domain.Resources;
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
    private static readonly string[] SupportedMethods =
        ["resource.search", "resource.project", "resource.versions", "resource.plan", "download.execute"];

    public const string Modrinth = "modrinth";

    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromSeconds(60),
    };

    private readonly string _downloaderPath;
    private readonly DownloadPolicy _policy;

    public ResourceModuleService(
        string? downloaderPath = null,
        DownloadPolicy? policy = null)
    {
        _downloaderPath = downloaderPath ?? ResolveDownloaderPath();
        _policy = policy ?? new DownloadPolicy();
    }

    public IReadOnlyCollection<string> Methods => SupportedMethods;

    public async Task<byte[]> HandleAsync(
        string method,
        string? paramsJson,
        CancellationToken cancellationToken)
    {
        try
        {
            if (method is "resource.search" or "resource.project" or "resource.versions" or
                "resource.plan" or "download.execute")
            {
                await _policy.EnsureEnabledAsync(cancellationToken).ConfigureAwait(false);
            }

            using var document = ParseRequest(paramsJson);
            var root = document.RootElement;

            return method switch
            {
                "resource.search" => await SearchAsync(root, cancellationToken)
                    .ConfigureAwait(false),
                "resource.project" => await ProjectAsync(root, cancellationToken)
                    .ConfigureAwait(false),
                "resource.versions" => await VersionsAsync(root, cancellationToken)
                    .ConfigureAwait(false),
                "resource.plan" => await PlanAsync(root, cancellationToken)
                    .ConfigureAwait(false),
                "download.execute" => await ExecuteAsync(root, cancellationToken)
                    .ConfigureAwait(false),
                _ => Error("E_UNSUPPORTED", $"unsupported resource method '{method}'"),
            };
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
        var target = ReadTarget(root, kind);
        var client = CreateClient(root);

        if (!string.Equals(provider, Modrinth, StringComparison.OrdinalIgnoreCase))
        {
            throw new ResourceProviderException(
                ResourceErrorCode.ProviderAuth,
                "CurseForge requires an API key and is not wired yet");
        }

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
            throw new ResourceProviderException(
                ResourceErrorCode.ProviderAuth,
                "CurseForge requires an API key and is not wired yet");
        }

        var kind = ReadKind(root);
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

        var plans = new List<DownloadPlan>
        {
            ResourceDownloadPlanBuilder.Build(rootPath, kind, target, version),
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
                        dependency.Version));
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

        if (string.IsNullOrWhiteSpace(_downloaderPath) || !File.Exists(_downloaderPath))
        {
            return Error(
                "DOWNLOADER_MISSING",
                "archoera-dl was not found; build app/modules/downloader and set ARCHOERACX_DL_BIN");
        }

        string rootPath;
        try
        {
            using var planDocument = JsonDocument.Parse(planJson);
            rootPath = RequireString(planDocument.RootElement, "root");
        }
        catch (JsonException exception)
        {
            return Error(ResourceErrorCode.InvalidRequest, exception.Message);
        }

        var planDirectory = Path.Combine(rootPath, ".archoera", "plans");
        Directory.CreateDirectory(planDirectory);
        var planPath = Path.Combine(
            planDirectory,
            $"plan-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}.json");
        await File.WriteAllTextAsync(
                planPath,
                planJson,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                cancellationToken)
            .ConfigureAwait(false);

        var startInfo = new ProcessStartInfo
        {
            FileName = _downloaderPath,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = Path.GetDirectoryName(_downloaderPath) ?? AppContext.BaseDirectory,
        };
        startInfo.ArgumentList.Add(planPath);

        using var process = new Process { StartInfo = startInfo };
        if (!process.Start())
        {
            return Error("DOWNLOADER_FAILED", "archoera-dl did not start");
        }

        var startedAt = Stopwatch.GetTimestamp();
        var lastProgressAt = startedAt;
        long lastDone = 0;
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();

        var stdoutTask = PumpLinesAsync(
            process.StandardOutput,
            line =>
            {
                if (stdout.Length < 256 * 1024) stdout.AppendLine(line);
            });
        var stderrTask = PumpLinesAsync(
            process.StandardError,
            line =>
            {
                if (stderr.Length < 64 * 1024) stderr.AppendLine(line);
                if (!TryParseProgress(line, out var done, out var total)) return;
                var now = Stopwatch.GetTimestamp();
                var elapsed = Stopwatch.GetElapsedTime(lastProgressAt, now).TotalSeconds;
                var rate = elapsed > 0
                    ? Math.Max(0, (long)((done - lastDone) / elapsed))
                    : 0;
                lastDone = Math.Max(lastDone, done);
                lastProgressAt = now;
                ModuleProgress.Report(done, total, rate, line);
            });

        using var waitSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        waitSource.CancelAfter(TimeSpan.FromMinutes(30));
        try
        {
            await process.WaitForExitAsync(waitSource.Token).ConfigureAwait(false);
            await Task.WhenAll(stdoutTask, stderrTask).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            try
            {
                if (!process.HasExited) process.Kill(entireProcessTree: true);
            }
            catch
            {
                // best effort
            }

            cancellationToken.ThrowIfCancellationRequested();
            return Error("DOWNLOAD_TIMEOUT", "download timed out");
        }

        var stdoutText = stdout.ToString();
        var stderrText = stderr.ToString();
        var resultText = LastJsonLine(stdoutText);
        if (resultText is null)
        {
            return Error(
                "DOWNLOADER_FAILED",
                $"archoera-dl returned no result: {Tail(stderrText, 500)}");
        }

        using var resultDocument = JsonDocument.Parse(resultText);
        var result = resultDocument.RootElement;
        var status = result.TryGetProperty("status", out var statusElement)
            ? statusElement.GetString()
            : null;
        var ok = string.Equals(status, "completed", StringComparison.OrdinalIgnoreCase);

        return BuildEnvelope(writer =>
        {
            writer.WriteStartObject("data");
            writer.WriteString("status", status ?? "failed");
            writer.WriteBoolean("downloadOk", ok);
            writer.WriteString("planPath", planPath);
            writer.WriteString("stdout", Tail(stdoutText, 4000));
            writer.WriteString("stderr", Tail(stderrText, 4000));
            writer.WriteEndObject();
            writer.WriteBoolean("ok", ok);
        });
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
                ResourceErrorCode.ProviderAuth,
                "only Modrinth is available before the CurseForge key is configured");
        }

        var baseAddress = ReadOptionalString(root, "providerBase") is { } baseText
            ? new Uri(baseText, UriKind.Absolute)
            : ModrinthClientOptions.DefaultBaseAddress;

        return new ModrinthClient(
            Http,
            new ModrinthClientOptions
            {
                BaseAddress = baseAddress,
                UserAgent = ModrinthClientOptions.CreateUserAgent(
                    "0.1.0",
                    "dev@archoera.local"),
            });
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
                "kind must be mod, shader or resourcepack");
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

    private static async Task PumpLinesAsync(
        StreamReader reader,
        Action<string> onLine)
    {
        while (await reader.ReadLineAsync().ConfigureAwait(false) is { } line)
        {
            onLine(line);
        }
    }

    private static bool TryParseProgress(
        string line,
        out long done,
        out long total)
    {
        done = 0;
        total = 0;
        if (line.Length == 0 || line[0] != '{') return false;
        try
        {
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            if (!root.TryGetProperty("event", out var eventElement) ||
                eventElement.ValueKind != JsonValueKind.String ||
                !string.Equals(
                    eventElement.GetString(),
                    "progress",
                    StringComparison.Ordinal))
            {
                return false;
            }

            if (!root.TryGetProperty("doneBytes", out var doneElement) ||
                !doneElement.TryGetInt64(out done) ||
                !root.TryGetProperty("totalBytes", out var totalElement) ||
                !totalElement.TryGetInt64(out total))
            {
                return false;
            }

            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static string? LastJsonLine(string text)
    {
        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        for (var index = lines.Length - 1; index >= 0; index--)
        {
            var line = lines[index].Trim();
            if (line.StartsWith('{') && line.EndsWith('}'))
            {
                return line;
            }
        }

        return null;
    }

    private static string Tail(string text, int max)
        => text.Length <= max ? text : text[^max..];

    private static string ResolveDownloaderPath()
    {
        var configured = Environment.GetEnvironmentVariable("ARCHOERACX_DL_BIN");
        if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured))
        {
            return configured;
        }

        var names = OperatingSystem.IsWindows()
            ? new[] { "archoera-dl.exe", "archoera-dl" }
            : new[] { "archoera-dl" };
        var bases = new List<string>
        {
            AppContext.BaseDirectory,
            Path.Combine(AppContext.BaseDirectory, "lib"),
            Path.Combine(AppContext.BaseDirectory, ".."),
            Path.Combine(AppContext.BaseDirectory, "..", "lib"),
        };

        var processDirectory = Environment.ProcessPath is { } processPath
            ? Path.GetDirectoryName(processPath)
            : null;
        if (!string.IsNullOrWhiteSpace(processDirectory))
        {
            bases.Add(processDirectory);
            bases.Add(Path.Combine(processDirectory, "lib"));
        }

        // Development fallbacks, but never fail if the process CWD is invalid.
        string? cwd = null;
        try
        {
            cwd = Environment.CurrentDirectory;
        }
        catch
        {
            // Deleted/replaced working directory; packaged paths above still work.
        }

        if (!string.IsNullOrWhiteSpace(cwd))
        {
            bases.Add(Path.Combine(cwd, "build", "native"));
            bases.Add(Path.Combine(cwd, "app", "modules", "downloader", "target", "release"));
            bases.Add(Path.Combine(cwd, "modules", "downloader", "target", "release"));
        }

        foreach (var baseDirectory in bases)
        {
            foreach (var name in names)
            {
                var candidate = Path.Combine(baseDirectory, name);
                if (File.Exists(candidate)) return candidate;
            }
        }

        return string.Empty;
    }
}
