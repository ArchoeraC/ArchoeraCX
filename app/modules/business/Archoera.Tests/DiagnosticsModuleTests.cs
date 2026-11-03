using System.Text.Json;
using Archoera.Infrastructure.Diagnostics;
using Archoera.Infrastructure.Instances;
using Archoera.Infrastructure.Launch;
using Archoera.Infrastructure.Settings;

namespace Archoera.Tests;

public sealed class DiagnosticsModuleTests
{
    [Fact]
    public async Task Analyze_persists_and_latest_returns_the_same_findings()
    {
        var directory = NewDirectory();
        try
        {
            var dataRoot = Path.Combine(directory, "data");
            var settings = new JsonAppSettingsStore(dataRoot);
            var repository = new SqliteInstanceRepository(dataRoot, settings);
            var instanceRoot = Path.Combine(directory, "instance");
            var instance = await repository.CreateAsync(
                "Crash Test",
                "1.21.4",
                "fabric",
                "0.16.10",
                instanceRoot);

            var logs = Path.Combine(instanceRoot, ".archoera", "logs");
            Directory.CreateDirectory(logs);
            var logPath = Path.Combine(logs, "launch-20261030-120000.log");
            await File.WriteAllTextAsync(
                logPath,
                """
                java.lang.OutOfMemoryError: Java heap space
                [exited with code 1]
                """);

            var service = new DiagnosticsModuleService(repository);
            using var analyzed = JsonDocument.Parse(
                await service.HandleAsync(
                    "diagnostics.analyze",
                    $$"""{"instanceId":"{{instance.Id}}","logPath":"{{logPath.Replace("\\", "\\\\")}}"}""",
                    CancellationToken.None));
            Assert.True(analyzed.RootElement.GetProperty("ok").GetBoolean());
            var analysis = analyzed.RootElement
                .GetProperty("data")
                .GetProperty("analysis");
            Assert.Equal("crashed", analysis.GetProperty("outcome").GetString());
            Assert.Equal(
                "jvm.heap",
                analysis.GetProperty("findings")[0].GetProperty("ruleId").GetString());

            using var latest = JsonDocument.Parse(
                await service.HandleAsync(
                    "diagnostics.latest",
                    $$"""{"instanceId":"{{instance.Id}}"}""",
                    CancellationToken.None));
            Assert.Equal(
                "crashed",
                latest.RootElement
                    .GetProperty("data")
                    .GetProperty("analysis")
                    .GetProperty("outcome")
                    .GetString());
        }
        finally
        {
            Delete(directory);
        }
    }

    [Fact]
    public async Task Launch_wait_returns_the_exit_code_and_stops_waiting()
    {
        var directory = NewDirectory();
        try
        {
            var logPath = Path.Combine(directory, "launch.log");
            await File.WriteAllTextAsync(logPath, "[exited with code 7]");
            LaunchProcessRegistry.Register("wait-instance", 4242, logPath);

            var service = new LaunchModuleService();
            var wait = service.HandleAsync(
                "launch.wait",
                """{"instanceId":"wait-instance","logPath":"ignored","timeoutSec":5}""",
                CancellationToken.None);

            LaunchProcessRegistry.CompleteExit("wait-instance", 4242, 7);
            using var document = JsonDocument.Parse(await wait);
            var data = document.RootElement.GetProperty("data");
            Assert.False(data.GetProperty("running").GetBoolean());
            Assert.Equal(7, data.GetProperty("exitCode").GetInt32());
        }
        finally
        {
            Delete(directory);
        }
    }

    private static string NewDirectory()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            "archoera-diagnostics",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void Delete(string path)
    {
        try
        {
            if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
        }
        catch
        {
            // best effort
        }
    }
}
