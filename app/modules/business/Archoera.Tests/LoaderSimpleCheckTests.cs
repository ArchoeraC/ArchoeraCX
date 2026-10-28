using System.Text.Json;
using Archoera.Infrastructure.Loaders;

namespace Archoera.Tests;

public sealed class LoaderSimpleCheckTests
{
    [Fact]
    public async Task Commit_writes_an_instance_profile_and_rejects_escapes()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "archoera-loader-commit",
            Guid.NewGuid().ToString("N"));
        var root = Path.Combine(directory, "minecraft");
        var instanceRoot = Path.Combine(root, "versions", "AutumnTown");
        Directory.CreateDirectory(instanceRoot);
        try
        {
            var service = new LoaderModuleService();
            var profilePath = Path.Combine(instanceRoot, "AutumnTown.json");
            var parameters = $$"""
                               {
                                 "root": "{{root.Replace("\\", "\\\\")}}",
                                 "versionId": "neoforge-21.1.176",
                                 "versionJson": "{\"id\":\"neoforge-21.1.176\"}",
                                 "profilePath": "{{profilePath.Replace("\\", "\\\\")}}"
                               }
                               """;

            var result = await service.HandleAsync(
                "loader.commit",
                parameters,
                CancellationToken.None);
            using (var document = JsonDocument.Parse(result))
            {
                Assert.True(document.RootElement.GetProperty("ok").GetBoolean());
                Assert.Equal(
                    Path.GetFullPath(profilePath),
                    document.RootElement
                        .GetProperty("data")
                        .GetProperty("versionPath")
                        .GetString());
            }
            Assert.True(File.Exists(profilePath));
            Assert.False(Directory.Exists(Path.Combine(
                root,
                "versions",
                "neoforge-21.1.176")));

            var escape = $$"""
                           {
                             "root": "{{root.Replace("\\", "\\\\")}}",
                             "versionId": "neoforge-21.1.176",
                             "versionJson": "{\"id\":\"neoforge-21.1.176\"}",
                             "profilePath": "{{Path.Combine(instanceRoot, "Other.json").Replace("\\", "\\\\")}}"
                           }
                           """;
            var rejected = await service.HandleAsync(
                "loader.commit",
                escape,
                CancellationToken.None);
            using var rejectedDocument = JsonDocument.Parse(rejected);
            Assert.False(rejectedDocument.RootElement.GetProperty("ok").GetBoolean());
            Assert.Equal(
                "E_INVALID",
                rejectedDocument.RootElement
                    .GetProperty("error")
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
