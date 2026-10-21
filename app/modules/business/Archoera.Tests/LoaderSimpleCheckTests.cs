using System.Text.Json;
using Archoera.Infrastructure.Loaders;

namespace Archoera.Tests;

public sealed class LoaderSimpleCheckTests
{
    [Fact]
    public async Task Loader_check_reports_launch_files_for_vanilla()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "archoera-simple-check",
            Guid.NewGuid().ToString("N"));
        var root = Path.Combine(directory, "instance");
        Directory.CreateDirectory(Path.Combine(root, "libraries"));
        Directory.CreateDirectory(Path.Combine(root, "assets"));
        var versionDirectory = Path.Combine(root, "versions", "1.21.1");
        Directory.CreateDirectory(versionDirectory);
        File.WriteAllText(
            Path.Combine(versionDirectory, "1.21.1.json"),
            """
            {
              "id": "1.21.1",
              "type": "release",
              "mainClass": "net.minecraft.client.main.Main",
              "libraries": []
            }
            """);
        var clientJar = Path.Combine(versionDirectory, "1.21.1.jar");
        File.WriteAllText(clientJar, "client");

        try
        {
            var service = new LoaderModuleService();
            var parameters = $$"""
                               {
                                 "root": "{{root.Replace("\\", "\\\\")}}",
                                 "kind": "vanilla",
                                 "gameVersion": "1.21.1",
                                 "loaderVersion": "",
                                 "mode": "quick"
                               }
                               """;

            var result = await service.HandleAsync(
                "loader.check",
                parameters,
                CancellationToken.None);
            using (var document = JsonDocument.Parse(result))
            {
                Assert.True(document.RootElement.GetProperty("ok").GetBoolean());
                Assert.True(
                    document.RootElement
                        .GetProperty("data")
                        .GetProperty("launchFiles")
                        .GetProperty("ok")
                        .GetBoolean());
            }

            File.Delete(clientJar);
            var missing = await service.HandleAsync(
                "loader.check",
                parameters,
                CancellationToken.None);
            using var missingDocument = JsonDocument.Parse(missing);
            Assert.False(missingDocument.RootElement.GetProperty("ok").GetBoolean());
            Assert.Equal(
                "CLIENT_JAR_MISSING",
                missingDocument.RootElement
                    .GetProperty("data")
                    .GetProperty("launchFiles")
                    .GetProperty("code")
                    .GetString());
        }
        finally
        {
            try
            {
                Directory.Delete(directory, recursive: true);
            }
            catch
            {
                // best effort
            }
        }
    }
}
