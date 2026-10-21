using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Archoera.Application.Instances;
using Archoera.Application.Java;
using Archoera.Application.Modules;
using Archoera.Application.Mojang;
using Archoera.Domain.Accounts;
using Archoera.Domain.Instances;
using Archoera.Domain.Java;
using Archoera.Domain.Mojang;
using Archoera.Domain.Settings;
using Archoera.Infrastructure.Accounts;
using Archoera.Infrastructure.Instances;
using Archoera.Infrastructure.Java;
using Archoera.Infrastructure.Modules;
using Archoera.Infrastructure.Settings;

namespace Archoera.Infrastructure.Launch;

/// <summary>Basic offline launch through the C# business module.</summary>
public sealed class LaunchModuleService : IModuleMethodHandler
{
    private static readonly string[] SupportedMethods = ["launch.start"];

    private readonly IInstanceRepository _instances;
    private readonly JsonAppSettingsStore _settings;
    private readonly JsonAccountStore _accounts;
    private readonly JavaScanner _java = new();

    public LaunchModuleService(
        IInstanceRepository? instances = null,
        JsonAppSettingsStore? settings = null,
        string? dataRoot = null)
    {
        _settings = settings ?? new JsonAppSettingsStore(dataRoot);
        _instances = instances ?? new SqliteInstanceRepository(dataRoot, _settings);
        _accounts = new JsonAccountStore(dataRoot);
    }

    public IReadOnlyCollection<string> Methods => SupportedMethods;

    public async Task<byte[]> HandleAsync(
        string method,
        string? paramsJson,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(method, "launch.start", StringComparison.Ordinal))
        {
            return ModuleJson.Error("E_UNSUPPORTED", $"unsupported launch method '{method}'");
        }

        try
        {
            var instanceId = ReadRequired(paramsJson, "instanceId");
            if (ReadBool(paramsJson, "requirePremium"))
            {
                var accounts = await _accounts
                    .ListAsync(cancellationToken)
                    .ConfigureAwait(false);
                if (!accounts.Any(account =>
                        account.Kind == AccountKind.Microsoft &&
                        !string.IsNullOrWhiteSpace(account.Uuid)))
                {
                    throw new LaunchException(
                        "PREMIUM_ACCOUNT_REQUIRED",
                        "Sign in with a Microsoft account that owns Minecraft before launching.");
                }
            }

            var instance = (await _instances.ListAsync(cancellationToken).ConfigureAwait(false))
                .FirstOrDefault(item => string.Equals(item.Id, instanceId, StringComparison.Ordinal))
                ?? throw new LaunchException("INSTANCE_NOT_FOUND", $"instance not found: {instanceId}");
            var settings = await _settings.LoadAsync(cancellationToken).ConfigureAwait(false);
            var account = await _accounts.GetActiveAsync(cancellationToken).ConfigureAwait(false)
                ?? throw new LaunchException("ACCOUNT_REQUIRED", "add an offline account before launching");
            if (account.Kind != AccountKind.Offline)
            {
                throw new LaunchException(
                    "AUTH_REQUIRED",
                    "basic launch currently supports offline accounts only");
            }

            var paths = LaunchPaths.Resolve(instance.RootPath);
            var version = LoadVersion(instance, paths);
            var java = await SelectJavaAsync(settings, version, cancellationToken).ConfigureAwait(false);
            var plan = LaunchPlanBuilder.Build(instance, version, account, java, settings, paths);
            var (process, logPath) = Start(plan);
            return ModuleJson.Envelope(writer =>
            {
                writer.WriteNumber("pid", process.Id);
                writer.WriteString("instanceId", instance.Id);
                writer.WriteString("javaPath", java.Path);
                writer.WriteString("javaVersion", java.Version);
                writer.WriteString("accountName", account.DisplayName);
                writer.WriteString("logPath", logPath);
            });
        }
        catch (LaunchException exception)
        {
            return ModuleJson.Error(exception.Code, exception.Message);
        }
        catch (ArgumentException exception)
        {
            return ModuleJson.Error("E_INVALID", exception.Message);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            return ModuleJson.Error("LAUNCH_FAILED", exception.Message);
        }
    }

    internal static MojangVersion LoadVersion(
        GameInstanceRecord instance,
        LaunchPaths paths)
    {
        var directoryName = Path.GetFileName(paths.InstanceRoot);
        var candidates = new List<string>
        {
            Path.Combine(paths.InstanceRoot, $"{directoryName}.json"),
            Path.Combine(paths.SharedRoot, "versions", directoryName, $"{directoryName}.json"),
            Path.Combine(paths.InstanceRoot, $"{instance.GameVersion}.json"),
            Path.Combine(
                paths.SharedRoot,
                "versions",
                instance.GameVersion,
                $"{instance.GameVersion}.json"),
        };

        // Version-isolated instances created under a versions/ directory may
        // have a custom folder name. Also accept the version id recorded by a
        // loader install (Forge/NeoForge merged profiles).
        foreach (var versionsRoot in new[]
                 {
                     Path.Combine(paths.SharedRoot, "versions"),
                     Path.Combine(paths.InstanceRoot, "versions"),
                 })
        {
            if (!Directory.Exists(versionsRoot)) continue;
            foreach (var directory in Directory.EnumerateDirectories(versionsRoot))
            {
                foreach (var jsonPath in Directory.EnumerateFiles(
                             directory,
                             "*.json",
                             SearchOption.TopDirectoryOnly))
                {
                    candidates.Add(jsonPath);
                }
            }
        }

        var matches = new List<(MojangVersion Version, int Score, DateTime Modified)>();
        foreach (var path in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!File.Exists(path)) continue;
            MojangVersion version;
            try
            {
                using var document = JsonDocument.Parse(File.ReadAllText(path));
                version = MojangJson.ParseVersion(document.RootElement);
            }
            catch (Exception exception) when (
                exception is IOException or JsonException or ArgumentException)
            {
                continue;
            }

            var isLoaderInstance = !string.Equals(
                instance.Loader,
                "vanilla",
                StringComparison.OrdinalIgnoreCase);
            var score = 0;
            if (string.Equals(version.Id, directoryName, StringComparison.OrdinalIgnoreCase) &&
                (!isLoaderInstance ||
                 version.Id.Contains(instance.Loader, StringComparison.OrdinalIgnoreCase)))
            {
                score += 100;
            }

            if (string.Equals(version.Id, instance.GameVersion, StringComparison.OrdinalIgnoreCase))
            {
                score += isLoaderInstance ? 10 : 80;
            }

            if (isLoaderInstance &&
                version.Id.Contains(instance.GameVersion, StringComparison.OrdinalIgnoreCase))
            {
                score += 20;
                if (version.Id.Contains(instance.Loader, StringComparison.OrdinalIgnoreCase))
                {
                    score += 20;
                }

                if (!string.IsNullOrWhiteSpace(instance.LoaderVersion) &&
                    version.Id.Contains(
                        instance.LoaderVersion,
                        StringComparison.OrdinalIgnoreCase))
                {
                    score += 10;
                }
            }

            if (score == 0) continue;
            matches.Add((version, score, File.GetLastWriteTimeUtc(path)));
        }

        if (matches.Count == 0)
        {
            throw new LaunchException(
                "VERSION_JSON_MISSING",
                $"no version JSON found for {instance.GameVersion} in {paths.SharedRoot}");
        }

        var selected = matches
            .OrderByDescending(item => item.Score)
            .ThenByDescending(item => item.Modified)
            .First()
            .Version;
        if (!string.IsNullOrWhiteSpace(selected.InheritsFrom))
        {
            throw new LaunchException(
                "VERSION_NOT_MERGED",
                $"version '{selected.Id}' still inherits from '{selected.InheritsFrom}'; install/merge it first");
        }

        return selected;
    }

    private async Task<JavaRuntimeInfo> SelectJavaAsync(
        AppSettings settings,
        MojangVersion version,
        CancellationToken cancellationToken)
    {
        var required = version.JavaVersion?.MajorVersion ?? InferMajor(version.Id);
        if (!settings.JavaAuto && !string.IsNullOrWhiteSpace(settings.JavaPath))
        {
            var manual = _java.Scan(
                new JavaScanRequest
                {
                    IncludeDefaults = false,
                    Executables = [settings.JavaPath],
                },
                cancellationToken);
            var match = manual.FirstOrDefault();
            if (match is null)
            {
                throw new LaunchException(
                    "JAVA_MISSING",
                    $"configured Java is not usable: {settings.JavaPath}");
            }
            if (required is not null && match.Major < required)
            {
                throw new LaunchException(
                    "JAVA_VERSION_MISMATCH",
                    $"Java {match.Major} is too old; this instance needs Java {required}");
            }
            return match;
        }

        var runtimes = _java.Scan(JavaScanRequest.Default, cancellationToken);
        if (runtimes.Count == 0)
        {
            throw new LaunchException("JAVA_MISSING", "no Java runtime found");
        }

        if (required is null) return runtimes[0];
        var selected = JavaSelector.Select(runtimes, required);
        if (selected is not null) return selected;
        throw new LaunchException(
            "JAVA_VERSION_MISMATCH",
            $"no Java {required}+ runtime found");
    }

    private static int? InferMajor(string id)
    {
        if (id.StartsWith("26.", StringComparison.Ordinal)) return 25;
        if (id.StartsWith("1.20.5", StringComparison.Ordinal) ||
            id.StartsWith("1.21", StringComparison.Ordinal)) return 21;
        if (id.StartsWith("1.18", StringComparison.Ordinal) ||
            id.StartsWith("1.19", StringComparison.Ordinal)) return 17;
        if (id.StartsWith("1.17", StringComparison.Ordinal)) return 16;
        return null;
    }

    private static (Process Process, string LogPath) Start(LaunchPlan plan)
    {
        Directory.CreateDirectory(plan.LogDirectory);
        var startInfo = new ProcessStartInfo
        {
            FileName = plan.JavaPath,
            WorkingDirectory = plan.GameDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var argument in plan.Arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        foreach (var name in new[]
                 {
                     "JAVA_TOOL_OPTIONS", "_JAVA_OPTIONS", "JDK_JAVA_OPTIONS", "CLASSPATH",
                 })
        {
            startInfo.Environment.Remove(name);
        }

        var logPath = Path.Combine(
            plan.LogDirectory,
            $"launch-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}.log");
        var process = new Process { StartInfo = startInfo };
        if (!process.Start())
        {
            throw new LaunchException("LAUNCH_FAILED", "JVM did not start");
        }

        var stream = new FileStream(logPath, FileMode.Create, FileAccess.Write, FileShare.Read);
        var log = new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true };
        process.OutputDataReceived += (_, args) =>
        {
            if (args.Data is not null) lock (log) log.WriteLine(args.Data);
        };
        process.ErrorDataReceived += (_, args) =>
        {
            if (args.Data is not null) lock (log) log.WriteLine(args.Data);
        };
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        process.EnableRaisingEvents = true;
        process.Exited += (_, _) =>
        {
            lock (log)
            {
                log.WriteLine($"[exited with code {process.ExitCode}]");
                log.Dispose();
            }
        };
        return (process, logPath);
    }

    private static bool ReadBool(string? json, string name)
    {
        if (string.IsNullOrWhiteSpace(json)) return false;
        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.TryGetProperty(name, out var element) &&
                   element.ValueKind is JsonValueKind.True or JsonValueKind.False &&
                   element.GetBoolean();
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static string ReadRequired(string? json, string name)
    {
        if (string.IsNullOrWhiteSpace(json)) throw new ArgumentException("request JSON is required");
        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty(name, out var element) ||
            element.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(element.GetString()))
        {
            throw new ArgumentException($"{name} is required");
        }
        return element.GetString()!;
    }
}
