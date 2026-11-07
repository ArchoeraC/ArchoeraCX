using System.Text;
using Archoera.Application.Loaders;
using Archoera.Domain.Loaders;
using Archoera.Infrastructure.Loaders;

namespace Archoera.Tests;

/// <summary>
/// Forge 1.12.2 and older use the spec 0 installer: no processors, and the
/// universal jar is either embedded in the installer or listed as a local
/// library with an empty URL. Repair must extract it instead of waiting for a
/// processor that does not exist.
/// </summary>
public sealed class LegacyForgePlannerTests
{
    [Fact]
    public async Task Spec_0_plans_without_processors_and_extracts_the_universal_jar()
    {
        using var temp = new TempFixture("legacy-spec0");
        var root = temp.DirectoryPath;
        var libraries = Path.Combine(root, "libraries");
        System.IO.Directory.CreateDirectory(libraries);

        const string relative =
            "net/minecraftforge/forge/1.12.2-14.23.5.2860/" +
            "forge-1.12.2-14.23.5.2860.jar";
        var universal = Encoding.UTF8.GetBytes("synthetic universal jar");
        var sha1 = ForgeInstallerFixture.Sha1(universal);

        var installer = Path.Combine(root, "installer.jar");
        File.WriteAllBytes(
            installer,
            ForgeInstallerFixture.CreateZip(
                ("install_profile.json", """
                    {
                      "spec": 0,
                      "profile": "forge",
                      "version": "1.12.2-14.23.5.2860",
                      "minecraft": "1.12.2",
                      "json": "/version.json"
                    }
                    """),
                ("version.json", $$"""
                    {
                      "id": "1.12.2-forge-14.23.5.2860",
                      "inheritsFrom": "1.12.2",
                      "mainClass": "net.minecraft.launchwrapper.Launch",
                      "libraries": [
                        {
                          "name": "net.minecraftforge:forge:1.12.2-14.23.5.2860",
                          "downloads": {
                            "artifact": {
                              "path": "{{relative}}",
                              "url": "",
                              "sha1": "{{sha1}}",
                              "size": {{universal.Length}}
                            }
                          }
                        }
                      ]
                    }
                    """),
                ($"maven/{relative}", Encoding.UTF8.GetString(universal))));

        var manifest = await new ForgeArtifactPlanner().ParseAsync(
            installer,
            root,
            libraries,
            LoaderKind.Forge,
            CancellationToken.None);

        Assert.Empty(manifest.Processors);
        Assert.Equal(0, manifest.ProcessorCount);
        Assert.Equal("1.12.2-forge-14.23.5.2860", manifest.VersionId);

        var artifact = Assert.Single(
            manifest.Artifacts,
            candidate => candidate.RelativePath == relative);
        Assert.Equal(LoaderArtifactKind.Extracted, artifact.Kind);
        Assert.Equal($"installer:maven/{relative}", artifact.Key);

        var repair = new ForgeRepairService(
            new ThrowingRunner(),
            new LoaderArtifactVerifier());
        var result = await repair.RepairAsync(manifest, root, "/nonexistent/java");

        Assert.True(result.Ok);
        Assert.Equal(LoaderRepairStatus.Repaired, result.Status);
        Assert.Empty(result.ProcessorsRun);
        Assert.Equal(
            universal,
            File.ReadAllBytes(Path.Combine(libraries, relative)));

        var second = await repair.RepairAsync(manifest, root, "/nonexistent/java");
        Assert.Equal(LoaderRepairStatus.AlreadyValid, second.Status);
    }

    [Fact]
    public async Task Version_info_only_installer_extracts_the_legacy_universal_jar()
    {
        using var temp = new TempFixture("legacy-versioninfo");
        var root = temp.DirectoryPath;
        var libraries = Path.Combine(root, "libraries");
        System.IO.Directory.CreateDirectory(libraries);

        const string coordinate =
            "net.minecraftforge:forge:1.7.10-10.13.4.1614-1.7.10";
        const string relative =
            "net/minecraftforge/forge/1.7.10-10.13.4.1614-1.7.10/" +
            "forge-1.7.10-10.13.4.1614-1.7.10.jar";
        const string filePath =
            "forge-1.7.10-10.13.4.1614-1.7.10-universal.jar";
        var universal = Encoding.UTF8.GetBytes("legacy universal jar");

        var installer = Path.Combine(root, "installer.jar");
        File.WriteAllBytes(
            installer,
            ForgeInstallerFixture.CreateZip(
                ("install_profile.json", $$"""
                    {
                      "install": {
                        "path": "{{coordinate}}",
                        "filePath": "{{filePath}}"
                      },
                      "versionInfo": {
                        "id": "1.7.10-Forge10.13.4.1614-1.7.10",
                        "inheritsFrom": "1.7.10",
                        "mainClass": "net.minecraft.launchwrapper.Launch",
                        "libraries": [
                          {
                            "name": "{{coordinate}}",
                            "url": "https://maven.minecraftforge.net/"
                          },
                          { "name": "net.minecraft:launchwrapper:1.12" }
                        ]
                      }
                    }
                    """),
                (filePath, Encoding.UTF8.GetString(universal))));

        var manifest = await new ForgeArtifactPlanner().ParseAsync(
            installer,
            root,
            libraries,
            LoaderKind.Forge,
            CancellationToken.None);

        Assert.Empty(manifest.Processors);
        Assert.Equal(0, manifest.ProcessorCount);
        Assert.Equal("1.7.10-Forge10.13.4.1614-1.7.10", manifest.VersionId);
        Assert.Equal("1.7.10", manifest.LoaderId.GameVersion);

        var artifact = Assert.Single(
            manifest.Artifacts,
            candidate => candidate.RelativePath == relative);
        Assert.Equal(LoaderArtifactKind.Extracted, artifact.Kind);
        Assert.Equal($"installer:{filePath}", artifact.Key);
        Assert.True(artifact.RequiredForLaunch);

        var repair = new ForgeRepairService(
            new ThrowingRunner(),
            new LoaderArtifactVerifier());
        var result = await repair.RepairAsync(manifest, root, "/nonexistent/java");

        Assert.True(result.Ok);
        Assert.Equal(LoaderRepairStatus.Repaired, result.Status);
        Assert.Empty(result.ProcessorsRun);
        Assert.Equal(
            universal,
            File.ReadAllBytes(Path.Combine(libraries, relative)));

        var second = await repair.RepairAsync(manifest, root, "/nonexistent/java");
        Assert.Equal(LoaderRepairStatus.AlreadyValid, second.Status);
    }

    [Fact]
    public async Task Version_info_without_inherits_derives_the_game_version_from_the_id()
    {
        using var temp = new TempFixture("legacy-164");
        var root = temp.DirectoryPath;
        var libraries = Path.Combine(root, "libraries");
        System.IO.Directory.CreateDirectory(libraries);

        var installer = Path.Combine(root, "installer.jar");
        File.WriteAllBytes(
            installer,
            ForgeInstallerFixture.CreateZip(
                ("install_profile.json", """
                    {
                      "install": {
                        "path": "net.minecraftforge:minecraftforge:9.11.1.1345",
                        "filePath": "minecraftforge-universal-1.6.4-9.11.1.1345.jar"
                      },
                      "versionInfo": {
                        "id": "1.6.4-Forge9.11.1.1345",
                        "mainClass": "net.minecraft.launchwrapper.Launch",
                        "libraries": []
                      }
                    }
                    """),
                ("minecraftforge-universal-1.6.4-9.11.1.1345.jar", "universal")));

        var manifest = await new ForgeArtifactPlanner().ParseAsync(
            installer,
            root,
            libraries,
            LoaderKind.Forge,
            CancellationToken.None);

        Assert.Equal("1.6.4", manifest.LoaderId.GameVersion);
        Assert.Equal("9.11.1.1345", manifest.LoaderId.LoaderVersion);
        Assert.Equal("1.6.4-Forge9.11.1.1345", manifest.VersionId);
        Assert.Empty(manifest.Processors);
    }

    private sealed class ThrowingRunner : IProcessorRunner
    {
        public bool CanRunWithoutJava(ProcessorStep step) => false;

        public Task<ProcessorRunResult> RunAsync(
            ProcessorStep step,
            string javaPath,
            string workingDir,
            TimeSpan timeout,
            CancellationToken cancellationToken = default)
            => throw new InvalidOperationException(
                "spec 0 installers have no processors to run");
    }
}
