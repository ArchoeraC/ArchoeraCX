using System.Text.Json;
using Archoera.Application.Instances;
using Archoera.Application.Modules;
using Archoera.Domain.Instances;
using Archoera.Infrastructure.Instances;
using Archoera.Infrastructure.Modules;

namespace Archoera.Infrastructure.Diagnostics;

/// <summary>
/// `diagnostics.*` methods: analyze the newest launch of an instance and read
/// the persisted last result. The Flutter UI never opens logs or crash reports
/// itself; C# owns both the file access and the rule table.
/// </summary>
public sealed class DiagnosticsModuleService : IModuleMethodHandler
{
    private const string AnalyzeMethod = "diagnostics.analyze";
    private const string LatestMethod = "diagnostics.latest";
    private const int MaxStoredBytes = 2 * 1024 * 1024;

    private static readonly string[] SupportedMethods = [AnalyzeMethod, LatestMethod];

    private readonly IInstanceRepository _instances;
    private readonly Dictionary<string, ModuleMethodDelegate> _handlers;

    public DiagnosticsModuleService(IInstanceRepository? instances = null)
    {
        _instances = instances ?? new SqliteInstanceRepository();
        _handlers = new Dictionary<string, ModuleMethodDelegate>(StringComparer.Ordinal)
        {
            [AnalyzeMethod] = AnalyzeAsync,
            [LatestMethod] = LatestAsync,
        };
    }

    public IReadOnlyCollection<string> Methods => SupportedMethods;

    public async Task<byte[]> HandleAsync(
        string method,
        string? paramsJson,
        CancellationToken cancellationToken)
    {
        try
        {
            return _handlers.TryGetValue(method, out var handler)
                ? await handler(paramsJson, cancellationToken).ConfigureAwait(false)
                : ModuleJson.Error("E_UNSUPPORTED", $"unsupported diagnostics method '{method}'");
        }
        catch (ArgumentException exception)
        {
            return ModuleJson.Error("E_INVALID", exception.Message);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return ModuleJson.Error("DIAGNOSTICS_FAILED", exception.Message);
        }
    }

    private async Task<byte[]> AnalyzeAsync(
        string? paramsJson,
        CancellationToken cancellationToken)
    {
        var instance = await ResolveAsync(paramsJson, cancellationToken).ConfigureAwait(false);
        var logPath = ReadString(paramsJson, "logPath");
        var exitCode = ReadInt(paramsJson, "exitCode");
        var analysis = CrashAnalyzer.Analyze(
            instance.RootPath,
            logPath,
            exitCode);
        await StoreAsync(instance, analysis, cancellationToken).ConfigureAwait(false);
        return Envelope(analysis);
    }

    private async Task<byte[]> LatestAsync(
        string? paramsJson,
        CancellationToken cancellationToken)
    {
        var instance = await ResolveAsync(paramsJson, cancellationToken).ConfigureAwait(false);
        var path = StorePath(instance);
        if (!File.Exists(path))
        {
            return ModuleJson.Envelope(writer =>
            {
                writer.WriteString("instanceId", instance.Id);
                writer.WriteNull("analysis");
            });
        }

        using var document = JsonDocument.Parse(
            await ReadBoundedAsync(path, cancellationToken).ConfigureAwait(false));
        return ModuleJson.Envelope(writer =>
        {
            writer.WriteString("instanceId", instance.Id);
            writer.WritePropertyName("analysis");
            document.RootElement.WriteTo(writer);
        });
    }

    private static byte[] Envelope(CrashAnalysis analysis)
        => ModuleJson.Envelope(writer =>
        {
            writer.WritePropertyName("analysis");
            WriteAnalysis(writer, analysis);
        });

    private static void WriteAnalysis(Utf8JsonWriter writer, CrashAnalysis analysis)
    {
        writer.WriteStartObject();
        writer.WriteString("outcome", analysis.Outcome);
        if (analysis.ExitCode is { } code)
        {
            writer.WriteNumber("exitCode", code);
        }
        else
        {
            writer.WriteNull("exitCode");
        }

        WriteNullable(writer, "logPath", analysis.LogPath);
        WriteNullable(writer, "crashReportPath", analysis.CrashReportPath);
        WriteNullable(writer, "hsErrPath", analysis.HsErrPath);
        writer.WriteString("analyzedAt", analysis.AnalyzedAt.ToString("O"));
        writer.WriteStartArray("findings");
        foreach (var finding in analysis.Findings)
        {
            writer.WriteStartObject();
            writer.WriteString("ruleId", finding.RuleId);
            writer.WriteString("severity", finding.Severity);
            writer.WriteNumber("confidence", finding.Confidence);
            writer.WriteStartObject("args");
            foreach (var pair in finding.Args)
            {
                writer.WriteString(pair.Key, pair.Value);
            }

            writer.WriteEndObject();
            writer.WriteStartArray("evidence");
            foreach (var line in finding.Evidence)
            {
                writer.WriteStringValue(line);
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    private async Task StoreAsync(
        GameInstanceRecord instance,
        CrashAnalysis analysis,
        CancellationToken cancellationToken)
    {
        var path = StorePath(instance);
        var directory = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(directory);
        var temporary = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            await using (var stream = new FileStream(
                             temporary,
                             FileMode.Create,
                             FileAccess.Write,
                             FileShare.None))
            using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions
                   {
                       Indented = true,
                   }))
            {
                writer.WriteStartObject();
                writer.WriteNumber("schemaVersion", 1);
                writer.WriteString("instanceId", instance.Id);
                writer.WriteString("outcome", analysis.Outcome);
                if (analysis.ExitCode is { } code)
                {
                    writer.WriteNumber("exitCode", code);
                }
                else
                {
                    writer.WriteNull("exitCode");
                }

                WriteNullable(writer, "logPath", analysis.LogPath);
                WriteNullable(writer, "crashReportPath", analysis.CrashReportPath);
                WriteNullable(writer, "hsErrPath", analysis.HsErrPath);
                writer.WriteString("analyzedAt", analysis.AnalyzedAt.ToString("O"));
                writer.WriteStartArray("findings");
                foreach (var finding in analysis.Findings)
                {
                    writer.WriteStartObject();
                    writer.WriteString("ruleId", finding.RuleId);
                    writer.WriteString("severity", finding.Severity);
                    writer.WriteNumber("confidence", finding.Confidence);
                    writer.WriteStartObject("args");
                    foreach (var pair in finding.Args)
                    {
                        writer.WriteString(pair.Key, pair.Value);
                    }

                    writer.WriteEndObject();
                    writer.WriteStartArray("evidence");
                    foreach (var line in finding.Evidence)
                    {
                        writer.WriteStringValue(line);
                    }

                    writer.WriteEndArray();
                    writer.WriteEndObject();
                }

                writer.WriteEndArray();
                writer.WriteEndObject();
            }

            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            try
            {
                if (File.Exists(temporary)) File.Delete(temporary);
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException)
            {
                // Best effort; a leftover temp file is harmless.
            }
        }
    }

    private static async Task<byte[]> ReadBoundedAsync(
        string path,
        CancellationToken cancellationToken)
    {
        var info = new FileInfo(path);
        if (info.Length > MaxStoredBytes)
        {
            throw new IOException("stored diagnostic result is too large");
        }

        return await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
    }

    private async Task<GameInstanceRecord> ResolveAsync(
        string? paramsJson,
        CancellationToken cancellationToken)
    {
        var instanceId = ReadString(paramsJson, "instanceId")
            ?? throw new ArgumentException("instanceId is required");
        var instance = (await _instances.ListAsync(cancellationToken).ConfigureAwait(false))
            .FirstOrDefault(item => string.Equals(item.Id, instanceId, StringComparison.Ordinal))
            ?? throw new ArgumentException($"instance not found: {instanceId}");
        if (!Directory.Exists(instance.RootPath))
        {
            throw new ArgumentException($"instance directory is missing: {instance.RootPath}");
        }

        return instance;
    }

    private static string StorePath(GameInstanceRecord instance)
        => Path.Combine(instance.RootPath, ".archoera", "diagnostics", "latest.json");

    private static string? ReadString(string? paramsJson, string name)
    {
        if (string.IsNullOrWhiteSpace(paramsJson)) return null;
        using var document = JsonDocument.Parse(paramsJson);
        return document.RootElement.TryGetProperty(name, out var element) &&
               element.ValueKind == JsonValueKind.String
            ? element.GetString()
            : null;
    }

    private static int? ReadInt(string? paramsJson, string name)
    {
        if (string.IsNullOrWhiteSpace(paramsJson)) return null;
        using var document = JsonDocument.Parse(paramsJson);
        return document.RootElement.TryGetProperty(name, out var element) &&
               element.ValueKind == JsonValueKind.Number &&
               element.TryGetInt32(out var value)
            ? value
            : null;
    }

    private static void WriteNullable(Utf8JsonWriter writer, string name, string? value)
    {
        if (value is null)
        {
            writer.WriteNull(name);
        }
        else
        {
            writer.WriteString(name, value);
        }
    }
}
