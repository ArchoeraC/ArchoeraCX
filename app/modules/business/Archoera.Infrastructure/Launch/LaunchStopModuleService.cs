using System.Text.Json;
using Archoera.Application.Modules;
using Archoera.Infrastructure.Modules;

namespace Archoera.Infrastructure.Launch;

/// <summary>
/// Force-terminates the game JVM started by this launcher session.
///
/// The pid always comes from <see cref="LaunchProcessRegistry"/>, never from
/// the caller: the UI can only stop a game this process launched, not an
/// arbitrary process. The Minecraft client has no graceful shutdown channel,
/// so this is a hard kill of the process tree. The log viewer warns twice
/// before calling it because unsaved world progress can be lost.
/// </summary>
public sealed class LaunchStopModuleService : IModuleMethodHandler
{
    private const string StopMethod = "launch.stop";

    private static readonly string[] SupportedMethods = [StopMethod];

    private readonly Dictionary<string, ModuleMethodDelegate> _handlers;

    public LaunchStopModuleService()
    {
        _handlers = new Dictionary<string, ModuleMethodDelegate>(StringComparer.Ordinal)
        {
            [StopMethod] = StopAsync,
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
                : ModuleJson.Error(
                    "E_UNSUPPORTED",
                    $"unsupported launch method '{method}'");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (ArgumentException exception)
        {
            return ModuleJson.Error("E_INVALID", exception.Message);
        }
        catch (Exception exception)
        {
            return ModuleJson.Error("E_INTERNAL", exception.Message);
        }
    }

    private Task<byte[]> StopAsync(
        string? paramsJson,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var instanceId = ReadInstanceId(paramsJson);
        var stopped = LaunchProcessRegistry.TryStop(instanceId, out var pid);

        return Task.FromResult(
            ModuleJson.Envelope(writer =>
            {
                writer.WriteString("instanceId", instanceId);
                writer.WriteBoolean("stopped", stopped);
                if (stopped) writer.WriteNumber("pid", pid);
            }));
    }

    private static string ReadInstanceId(string? paramsJson)
    {
        if (string.IsNullOrWhiteSpace(paramsJson))
        {
            throw new ArgumentException("instanceId is required");
        }

        using var document = JsonDocument.Parse(paramsJson);
        if (document.RootElement.ValueKind != JsonValueKind.Object ||
            !document.RootElement.TryGetProperty("instanceId", out var element) ||
            element.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(element.GetString()))
        {
            throw new ArgumentException("instanceId is required");
        }

        return element.GetString()!;
    }
}
