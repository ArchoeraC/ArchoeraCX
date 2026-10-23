using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Archoera.Infrastructure.Launch;

namespace Archoera.Tests;

/// <summary>
/// Covers the force-stop contract: the module may only kill a process it
/// launched itself, reports an already-finished game as "not stopped" and
/// rejects malformed requests before touching the registry.
/// </summary>
public sealed class LaunchStopTests
{
    [Fact]
    public async Task Stop_kills_the_registered_game_process()
    {
        var instanceId = "stop-" + Guid.NewGuid().ToString("N");
        using var sleeper = StartSleeper();
        var log = Path.GetTempFileName();
        LaunchProcessRegistry.Register(instanceId, sleeper.Id, log);
        try
        {
            var response = await StopAsync(instanceId);

            using var document = JsonDocument.Parse(response);
            Assert.True(document.RootElement.GetProperty("ok").GetBoolean());
            var data = document.RootElement.GetProperty("data");
            Assert.Equal(instanceId, data.GetProperty("instanceId").GetString());
            Assert.True(data.GetProperty("stopped").GetBoolean());
            Assert.Equal(sleeper.Id, data.GetProperty("pid").GetInt32());

            Assert.True(sleeper.WaitForExit(3000));
            Assert.Null(LaunchProcessRegistry.TryGet(instanceId));
        }
        finally
        {
            if (!sleeper.HasExited) sleeper.Kill(entireProcessTree: true);
            LaunchProcessRegistry.Unregister(instanceId, sleeper.Id);
            File.Delete(log);
        }
    }

    [Fact]
    public async Task Stop_reports_an_already_finished_game_without_an_error()
    {
        var instanceId = "stop-" + Guid.NewGuid().ToString("N");

        var response = await StopAsync(instanceId);

        using var document = JsonDocument.Parse(response);
        Assert.True(document.RootElement.GetProperty("ok").GetBoolean());
        var data = document.RootElement.GetProperty("data");
        Assert.Equal(instanceId, data.GetProperty("instanceId").GetString());
        Assert.False(data.GetProperty("stopped").GetBoolean());
        Assert.False(data.TryGetProperty("pid", out _));
    }

    [Fact]
    public async Task Stop_rejects_a_missing_instance_id()
    {
        var service = new LaunchStopModuleService();
        var bytes = await service.HandleAsync(
            "launch.stop",
            "{}",
            CancellationToken.None);
        using var document = JsonDocument.Parse(Encoding.UTF8.GetString(bytes));

        Assert.False(document.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal(
            "E_INVALID",
            document.RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task Unknown_method_on_the_stop_service_is_unsupported()
    {
        var service = new LaunchStopModuleService();
        var bytes = await service.HandleAsync(
            "launch.restart",
            "{}",
            CancellationToken.None);
        using var document = JsonDocument.Parse(Encoding.UTF8.GetString(bytes));

        Assert.Equal(
            "E_UNSUPPORTED",
            document.RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    private static async Task<string> StopAsync(string instanceId)
    {
        var service = new LaunchStopModuleService();
        var bytes = await service.HandleAsync(
            "launch.stop",
            JsonSerializer.Serialize(new { instanceId }),
            CancellationToken.None);
        return Encoding.UTF8.GetString(bytes);
    }

    private static Process StartSleeper()
    {
        ProcessStartInfo info = OperatingSystem.IsWindows()
            ? new ProcessStartInfo("cmd.exe", "/c ping -n 30 127.0.0.1 > nul")
            : new ProcessStartInfo("sleep", "30");
        info.UseShellExecute = false;
        info.CreateNoWindow = true;
        return Process.Start(info) ??
               throw new InvalidOperationException("sleeper process did not start");
    }
}
