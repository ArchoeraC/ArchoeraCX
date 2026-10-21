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
            var version = LoadVersion(paths);
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

    private static MojangVersion LoadVersion(LaunchPaths paths)
    {
        var directoryName = Path.GetFileName(paths.InstanceRoot);
        var candidates = new[]
        {
            Path.Combine(paths.InstanceRoot, $"{directoryName}.json"),
            Path.Combine(paths.SharedRoot, "versions", directoryName, $"{directoryName}.json"),
        };

        foreach (var path in candidates)
        {
            if (!File.Exists(path)) continue;
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            var version = MojangJson.ParseVersion(document.RootElement);
            if (!string.IsNullOrWhiteSpace(version.InheritsFrom))
            {
                throw new LaunchException(
                    "VERSION_NOT_MERGED",
                    $"version '{version.Id}' still inherits from '{version.InheritsFrom}'; install/merge it first");
            }
            return version;
        }

        throw new LaunchException(
            "VERSION_JSON_MISSING",
            $"no version JSON found in {paths.InstanceRoot}");
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
        var exact = runtimes.FirstOrDefault(item => item.Major == required);
        if (exact is not null) return exact;
        var newer = runtimes
            .Where(item => item.Major > required)
            .OrderBy(item => item.Major)
            .FirstOrDefault();
        if (newer is not null) return newer;
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
