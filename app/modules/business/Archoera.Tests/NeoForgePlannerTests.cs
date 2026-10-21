using System.IO.Compression;
using System.Text;
using Archoera.Application.Loaders;
using Archoera.Domain.Loaders;
using Archoera.Infrastructure.Loaders;

namespace Archoera.Tests;

public sealed class NeoForgePlannerTests
{
    [Theory]
    [InlineData("1.20.1", "1.20.1-47.1.106", "47.1.106",
        "net.neoforged", "forge", "net/neoforged/forge/1.20.1-47.1.106/forge-1.20.1-47.1.106-clientdata.lzma")]
    [InlineData("1.21.1", "21.1.209", "21.1.209",
        "net.neoforged", "neoforge", "net/neoforged/neoforge/21.1.209/neoforge-21.1.209-clientdata.lzma")]
    public async Task PlanAsync_resolves_neoforge_binpatch_coordinates(
        string gameVersion,
        string profileVersion,
        string expectedLoaderVersion,
        string group,
        string artifact,
        string expectedBinpatchRelative)
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "archoera-tests",
            $"neoforge-{Guid.NewGuid():N}");
        var root = Path.Combine(directory, "instance");
        var libraries = Path.Combine(root, "libraries");
        Directory.CreateDirectory(libraries);

        var installerPath = Path.Combine(root, "installer.jar");
        var versionId = $"neoforge-{profileVersion}";
        var profile = $$"""
            {
              "spec": 1,
              "profile": "neoforge",
              "version": "{{profileVersion}}",
              "minecraft": "{{gameVersion}}",
              "json": "/version.json",
              "data": {
                "BINPATCH": { "client": "/data/client.lzma" },
                "PATCHED": { "client": "[{{group}}:{{artifact}}:{{profileVersion}}:client]" }
              },
              "libraries": [],
              "processors": []
            }
            """;
        var version = $$"""
            {
              "id": "{{versionId}}",
              "inheritsFrom": "{{gameVersion}}",
              "libraries": []
            }
            """;

        try
        {
            using (var file = File.Create(installerPath))
            using (var archive = new ZipArchive(file, ZipArchiveMode.Create))
            {
                AddEntry(archive, "install_profile.json", profile);
                AddEntry(archive, "version.json", version);
                AddEntry(archive, "data/client.lzma", "synthetic");
            }

            var manifest = await new ForgeArtifactPlanner().ParseAsync(
                installerPath,
                root,
                libraries,
                LoaderKind.NeoForge);

            Assert.Equal(LoaderKind.NeoForge, manifest.LoaderId.Kind);
            Assert.Equal(gameVersion, manifest.LoaderId.GameVersion);
            Assert.Equal(expectedLoaderVersion, manifest.LoaderId.LoaderVersion);
            Assert.Equal(0, manifest.ProcessorCount);
            Assert.Contains(
                manifest.Artifacts,
                candidate => candidate.RelativePath == expectedBinpatchRelative);
            Assert.DoesNotContain(
                manifest.Artifacts,
                candidate => Path.IsPathRooted(candidate.RelativePath));
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

    private static void AddEntry(ZipArchive archive, string name, string text)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.Fastest);
        using var stream = entry.Open();
        using var writer = new StreamWriter(stream, new UTF8Encoding(false));
        writer.Write(text);
    }
}
