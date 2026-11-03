using System.Text;
using Archoera.Application.Downloads;
using Archoera.Application.Loaders;
using Archoera.Application.Mojang;
using Archoera.Domain.Downloads;
using Archoera.Domain.Loaders;
using Archoera.Domain.Mojang;
using Archoera.Infrastructure.Loaders;

namespace Archoera.Tests;

public sealed class ForgeInstallPlanServiceTests
{
    [Fact]
    public async Task BuildAsync_combines_vanilla_and_forge_dependencies_without_generated_artifacts()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "archoera-forge-install",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var processor = ForgeInstallerFixture.Library(
                "com.example:processor:1.0",
                "com/example/processor/1.0/processor-1.0.jar",
                "https://maven.example.invalid/processor-1.0.jar",
                ForgeInstallerFixture.Sha('c'),
                10);
            var installerBytes = ForgeInstallerFixture.CreateInstaller(
                $"[{processor}]",
                "[]");
            var installerPath = Path.Combine(directory, "installer.jar");
            await File.WriteAllBytesAsync(installerPath, installerBytes);

            var root = Path.Combine(directory, "instance");
            Directory.CreateDirectory(root);

            var service = new ForgeInstallPlanService(new FakeMojangMetadata());
            var result = await service.BuildAsync(
                root,
                LoaderKind.Forge,
                "1.20.1",
                "47.2.0",
                installerPath,
                MojangPlatform.LinuxX64);

            var destinations = result.Plan.Files.Select(file => file.Dest).ToHashSet();
            Assert.Contains(
                "versions/1.20.1-forge-47.2.0/1.20.1-forge-47.2.0.jar",
                destinations);
            Assert.Contains(
                "libraries/com/example/processor/1.0/processor-1.0.jar",
                destinations);
            Assert.Contains(
                "downloads/loaders/forge/1.20.1-47.2.0-installer.jar",
                destinations);
            // loader.plan reports the installer location to the caller; it has
            // to be the same path the plan writes.
            Assert.Contains(
                ForgeMavenUrls.DefaultInstallerRelativePath(
                    LoaderKind.Forge,
                    "1.20.1",
                    "47.2.0"),
                destinations);
            Assert.Contains(MojangTestData.AssetIndexPath, destinations);
            Assert.Contains(MojangTestData.AssetAPath, destinations);
            Assert.DoesNotContain(
                destinations,
                destination => destination.Contains("client.jar", StringComparison.Ordinal) &&
                               destination.Contains("forge", StringComparison.Ordinal));
            Assert.Equal(["1.20.1", "1.20.1-forge-47.2.0"], result.Version.Chain);
            Assert.Null(result.Version.Version.InheritsFrom);
        }
        finally
        {
            try
            {
                Directory.Delete(directory, recursive: true);
            }
            catch
            {
                // best effort on platforms with transient file handles
            }
        }
    }

    [Fact]
    public async Task BuildAsync_uses_the_resolved_source_for_vanilla_artifacts()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "archoera-forge-install",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var installerBytes = ForgeInstallerFixture.CreateInstaller("[]", "[]");
            var installerPath = Path.Combine(directory, "installer.jar");
            await File.WriteAllBytesAsync(installerPath, installerBytes);
            var root = Path.Combine(directory, "instance");
            Directory.CreateDirectory(root);

            var result = await new ForgeInstallPlanService(new FakeMojangMetadata())
                .BuildAsync(
                    root,
                    LoaderKind.Forge,
                    "1.20.1",
                    "47.2.0",
                    installerPath,
                    MojangPlatform.LinuxX64,
                    sourceMode: DownloadSourceMode.Mirror);

            var client = result.Plan.Files.Single(
                file => file.Dest.EndsWith(
                    "1.20.1-forge-47.2.0.jar",
                    StringComparison.Ordinal));
            // The merged client jar is the first large file in a Forge
            // install; it used to stay on official hosts even in mirror mode.
            Assert.StartsWith(
                "https://bmclapi2.bangbang93.com/version/",
                client.Url,
                StringComparison.Ordinal);
        }
        finally
        {
            try
            {
                Directory.Delete(directory, recursive: true);
            }
            catch
            {
                // best effort on platforms with transient file handles
            }
        }
    }

    [Fact]
    public async Task BuildAsync_plans_pre_1_7_libraries_without_downloading_the_universal()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "archoera-forge-install",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            const string universalRelative =
                "net/minecraftforge/forge/1.7.10-10.13.4.1614-1.7.10/" +
                "forge-1.7.10-10.13.4.1614-1.7.10.jar";
            const string launchwrapper =
                "libraries/net/minecraft/launchwrapper/1.12/launchwrapper-1.12.jar";
            const string asm =
                "libraries/org/ow2/asm/asm-all/5.0.3/asm-all-5.0.3.jar";

            var installerBytes = ForgeInstallerFixture.CreateZip(
                ("install_profile.json", """
                    {
                      "install": {
                        "path": "net.minecraftforge:forge:1.7.10-10.13.4.1614-1.7.10",
                        "filePath": "forge-1.7.10-10.13.4.1614-1.7.10-universal.jar"
                      },
                      "versionInfo": {
                        "id": "1.7.10-Forge10.13.4.1614-1.7.10",
                        "inheritsFrom": "1.7.10",
                        "mainClass": "net.minecraft.launchwrapper.Launch",
                        "libraries": [
                          { "name": "net.minecraft:launchwrapper:1.12" },
                          { "name": "org.ow2.asm:asm-all:5.0.3" },
                          { "name": "com.example:serveronly:1.0", "clientreq": false },
                          {
                            "name": "net.minecraftforge:forge:1.7.10-10.13.4.1614-1.7.10",
                            "url": "https://maven.minecraftforge.net/"
                          }
                        ]
                      }
                    }
                    """),
                ("forge-1.7.10-10.13.4.1614-1.7.10-universal.jar", "universal"));

            var installerPath = Path.Combine(directory, "installer.jar");
            await File.WriteAllBytesAsync(installerPath, installerBytes);
            var root = Path.Combine(directory, "instance");
            Directory.CreateDirectory(root);

            var resolver = new RecordingLegacyResolver();
            var service = new ForgeInstallPlanService(
                new FakeLegacyMojangMetadata(),
                legacyResolver: resolver);

            var result = await service.BuildAsync(
                root,
                LoaderKind.Forge,
                "1.7.10",
                "10.13.4.1614-1.7.10",
                installerPath,
                MojangPlatform.LinuxX64);

            var destinations = result.Plan.Files.Select(file => file.Dest).ToList();
            Assert.Single(destinations, dest => dest == launchwrapper);
            Assert.Contains(asm, destinations);
            Assert.DoesNotContain(universalRelative, destinations);
            Assert.DoesNotContain(
                destinations,
                dest => dest.Contains("serveronly", StringComparison.Ordinal));
            Assert.Equal(["org.ow2.asm:asm-all:5.0.3"], resolver.Calls);

            var libraries = result.Version.Version.Libraries
                .Select(library => library.Name)
                .ToList();
            Assert.Single(libraries, name => name == "net.minecraft:launchwrapper:1.12");
            Assert.Contains("org.ow2.asm:asm-all:5.0.3", libraries);
            Assert.Contains(
                "net.minecraftforge:forge:1.7.10-10.13.4.1614-1.7.10",
                libraries);
        }
        finally
        {
            try
            {
                Directory.Delete(directory, recursive: true);
            }
            catch
            {
                // best effort on platforms with transient file handles
            }
        }
    }

    private sealed class RecordingLegacyResolver : ILegacyForgeLibraryResolver
    {
        public List<string> Calls { get; } = [];

        public Task<LegacyForgeLibrary> ResolveAsync(
            string name,
            string? repository,
            IReadOnlyList<string> checksums,
            DownloadSourceMode sourceMode,
            CancellationToken cancellationToken)
        {
            Calls.Add(name);
            return Task.FromResult(new LegacyForgeLibrary(
                $"https://maven.minecraftforge.net/{name.Replace(':', '/')}.jar",
                [],
                DownloadHash.Create(DownloadHashAlgorithm.Sha1, ForgeInstallerFixture.Sha('f')),
                123));
        }
    }

    private sealed class FakeLegacyMojangMetadata : IMojangMetadataClient
    {
        private static readonly string VanillaJson =
            $$"""
              {
                "id": "1.7.10",
                "mainClass": "net.minecraft.client.main.Main",
                "assets": "1.7.10",
                "downloads": {
                  "client": {
                    "url": "https://piston-data.mojang.com/v1/objects/client/1.7.10.jar",
                    "sha1": "{{MojangTestData.Sha1('1')}}",
                    "size": 1000
                  }
                },
                "libraries": [
                  {
                    "name": "net.minecraft:launchwrapper:1.12",
                    "downloads": {
                      "artifact": {
                        "path": "net/minecraft/launchwrapper/1.12/launchwrapper-1.12.jar",
                        "url": "https://libraries.minecraft.net/net/minecraft/launchwrapper/1.12/launchwrapper-1.12.jar",
                        "sha1": "{{MojangTestData.Sha1('2')}}",
                        "size": 100
                      }
                    }
                  }
                ]
              }
              """;

        public Task<MojangVersionManifest> GetManifestAsync(
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<MojangVersion> GetVersionAsync(
            string versionId,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<byte[]> GetVersionJsonAsync(
            string versionId,
            CancellationToken cancellationToken = default)
            => Task.FromResult(Encoding.UTF8.GetBytes(VanillaJson));

        public Task<MojangAssetIndex> GetAssetIndexAsync(
            MojangAssetIndexRef index,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    private sealed class FakeMojangMetadata : IMojangMetadataClient
    {
        private static readonly string VanillaJson =
            $$"""
              {
                "id": "1.20.1",
                "mainClass": "net.minecraft.client.main.Main",
                "assets": "5",
                "assetIndex": {
                  "id": "5",
                  "url": "https://piston-meta.mojang.com/v1/packages/index/5.json",
                  "sha1": "{{MojangTestData.Sha1('8')}}",
                  "size": 500,
                  "totalSize": 2000
                },
                "downloads": {
                  "client": {
                    "url": "https://piston-data.mojang.com/v1/objects/client/client.jar",
                    "sha1": "{{MojangTestData.Sha1('1')}}",
                    "size": 1000
                  }
                },
                "libraries": []
              }
              """;

        public Task<MojangVersionManifest> GetManifestAsync(
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<MojangVersion> GetVersionAsync(
            string versionId,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<byte[]> GetVersionJsonAsync(
            string versionId,
            CancellationToken cancellationToken = default)
            => Task.FromResult(Encoding.UTF8.GetBytes(VanillaJson));

        public Task<MojangAssetIndex> GetAssetIndexAsync(
            MojangAssetIndexRef index,
            CancellationToken cancellationToken = default)
            => Task.FromResult(MojangTestData.AssetIndex());
    }
}
