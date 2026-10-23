using System.Text.Json;
using System.Net.Http;
using System.Text;
using Archoera.Application.Mojang;
using Archoera.Application.Packs;
using Archoera.Application.Loaders;
using Archoera.Domain.Downloads;
using Archoera.Domain.Loaders;
using Archoera.Domain.Mojang;
using Archoera.Infrastructure.Downloads;
using Archoera.Infrastructure.Java;
using Archoera.Infrastructure.Instances;
using Archoera.Infrastructure.Packs;
using Archoera.Infrastructure.Settings;

namespace Archoera.Tests;

public sealed class PackGameInstallServiceTests
{
    [Fact]
    public async Task Fabric_profile_is_merged_with_vanilla()
    {
        var handler = new FakeHttpMessageHandler();
        handler.EnqueueJson("""
                            {
                              "id": "fabric-loader-0.19.3-1.20.1",
                              "inheritsFrom": "1.20.1",
                              "mainClass": "net.fabricmc.loader.impl.launch.knot.KnotClient",
                              "libraries": [
                                {
                                  "name": "net.fabricmc:fabric-loader:0.19.3",
                                  "url": "https://maven.fabricmc.net/",
                                  "downloads": {
                                    "artifact": {
                                      "path": "net/fabricmc/fabric-loader/0.19.3/fabric-loader-0.19.3.jar",
                                      "url": "https://maven.fabricmc.net/net/fabricmc/fabric-loader/0.19.3/fabric-loader-0.19.3.jar",
                                      "sha1": "cccccccccccccccccccccccccccccccccccccccc",
                                      "size": 100
                                    }
                                  }
                                }
                              ]
                            }
                            """);

        var directory = Path.Combine(
            Path.GetTempPath(),
            "archoera-pack-fabric",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var service = new PackGameInstallService(
                new FakeMojangMetadata(),
                new HttpClient(handler),
                new LoaderProfileEndpoints(
                    new Uri("https://meta.fabricmc.net/v2/"),
                    new Uri("https://meta.quiltmc.org/v3/")));

            var result = await service.BuildAsync(
                directory,
                "1.20.1",
                "fabric",
                "0.19.3",
                MojangPlatform.LinuxX64);

            Assert.Equal("fabric-loader-0.19.3-1.20.1", result.VersionId);
            Assert.Equal("fabric", result.LoaderKind);
            Assert.DoesNotContain("inheritsFrom", result.VersionJson);
            var destinations = result.Plan.Files.Select(file => file.Dest).ToHashSet();
            Assert.Contains(
                "versions/fabric-loader-0.19.3-1.20.1/" +
                "fabric-loader-0.19.3-1.20.1.jar",
                destinations);
            Assert.Contains(
                "libraries/net/fabricmc/fabric-loader/0.19.3/fabric-loader-0.19.3.jar",
                destinations);
            Assert.Contains(MojangTestData.AssetAPath, destinations);
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
    public async Task Fabric_profile_resolves_hashless_maven_libraries()
    {
        var handler = new FakeHttpMessageHandler();
        handler.EnqueueJson("""
                            {
                              "id": "fabric-loader-0.19.3-1.20.1",
                              "inheritsFrom": "1.20.1",
                              "mainClass": "net.fabricmc.loader.impl.launch.knot.KnotClient",
                              "libraries": [
                                {
                                  "name": "net.fabricmc:fabric-loader:0.19.3",
                                  "url": "https://maven.fabricmc.net/"
                                },
                                {
                                  "name": "org.ow2.asm:asm:9.10.1",
                                  "url": "https://maven.fabricmc.net/",
                                  "sha1": "dddddddddddddddddddddddddddddddddddddddd",
                                  "size": 100
                                }
                              ]
                            }
                            """);
        handler.EnqueueJson(
            "cccccccccccccccccccccccccccccccccccccccc");
        handler.Enqueue(() =>
        {
            var response = new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new ByteArrayContent([]),
            };
            response.Content.Headers.ContentLength = 1234;
            return response;
        });

        var directory = Path.Combine(
            Path.GetTempPath(),
            "archoera-pack-fabric-maven",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var service = new PackGameInstallService(
                new FakeMojangMetadata(),
                new HttpClient(handler),
                new LoaderProfileEndpoints(
                    new Uri("https://meta.fabricmc.net/v2/"),
                    new Uri("https://meta.quiltmc.org/v3/")));

            var result = await service.BuildAsync(
                directory,
                "1.20.1",
                "fabric",
                "fabric-loader-0.19.3-1.20.1",
                MojangPlatform.LinuxX64);

            var loader = result.Plan.Files.Single(file =>
                file.Dest ==
                "libraries/net/fabricmc/fabric-loader/0.19.3/fabric-loader-0.19.3.jar");
            Assert.Equal(
                "https://maven.fabricmc.net/net/fabricmc/fabric-loader/0.19.3/" +
                "fabric-loader-0.19.3.jar",
                loader.Url);
            Assert.Equal("cccccccccccccccccccccccccccccccccccccccc", loader.Hash.Hex);
            Assert.Equal(1234, loader.Size);
            Assert.Equal(
                "fabric-loader-0.19.3-1.20.1",
                result.VersionId);
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
    public async Task CheckForgeInstance_detects_deleted_generated_artifacts()
    {
        using var temp = new TempFixture("pack-forge-check");
        var fixture = temp.Loader;
        var manifest = await fixture.PlanAsync();
        fixture.WriteAllValid(manifest);

        var installerTarget = Path.Combine(
            fixture.Root,
            "downloads",
            "loaders",
            "forge",
            "1.20.1-47.2.0-installer.jar");
        Directory.CreateDirectory(Path.GetDirectoryName(installerTarget)!);
        File.Copy(fixture.InstallerPath, installerTarget, overwrite: true);

        var settings = new JsonAppSettingsStore(Path.Combine(temp.DirectoryPath, "data"));
        var service = new PackInstallService(
            repository: new SqliteInstanceRepository(
                Path.Combine(temp.DirectoryPath, "data"),
                settings),
            settings: settings,
            runner: new FakePlanRunner(),
            forgeRepair: new FakeForgeRepair());
        var now = DateTimeOffset.UtcNow;
        var instance = new Archoera.Domain.Instances.GameInstanceRecord(
            "check",
            "Check",
            "1.20.1",
            "forge",
            "47.2.0",
            fixture.Root,
            0,
            false,
            now,
            now,
            null);

        var valid = await service.CheckForgeInstanceAsync(instance);
        Assert.True(valid.Ok);

        File.Delete(fixture.Resolve(fixture.Find(manifest, "PATCHED")));
        var missing = await service.CheckForgeInstanceAsync(instance);
        Assert.False(missing.Ok);
        Assert.Equal("LOADER_FILES_MISSING", missing.Code);
    }

    [Fact]
    public async Task RepairForgeInstance_rebuilds_the_merged_profile()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "archoera-pack-repair",
            Guid.NewGuid().ToString("N"));
        var dataRoot = Path.Combine(directory, "data");
        var root = Path.Combine(directory, "instance");
        Directory.CreateDirectory(Path.Combine(root, "downloads", "loaders", "forge"));
        try
        {
            var installerPath = Path.Combine(
                root,
                "downloads/loaders/forge/1.20.1-47.2.0-installer.jar");
            await File.WriteAllBytesAsync(
                installerPath,
                ForgeInstallerFixture.CreateInstaller("[]", "[]"));

            var settings = new JsonAppSettingsStore(dataRoot);
            var repository = new SqliteInstanceRepository(dataRoot, settings);
            var now = DateTimeOffset.UtcNow;
            var instance = new Archoera.Domain.Instances.GameInstanceRecord(
                "inst-repair",
                "Repair",
                "1.20.1",
                "forge",
                // SJMCL/LauncherX imports keep the full profile id.
                "1.20.1-forge-47.2.0",
                root,
                0,
                false,
                now,
                now,
                null);
            var runner = new FakePlanRunner();
            var repairer = new FakeForgeRepair();
            var service = new PackInstallService(
                repository: repository,
                settings: settings,
                runner: runner,
                gameInstall: new PackGameInstallService(
                    new FakeMojangMetadata(),
                    new HttpClient(new FakeHttpMessageHandler()),
                    new LoaderProfileEndpoints(
                        new Uri("https://meta.fabricmc.net/v2/"),
                        new Uri("https://meta.quiltmc.org/v3/"))),
                forgeRepair: repairer);

            var outcome = await service.RepairForgeInstanceAsync(
                instance,
                "/tmp/fake-java");

            Assert.Equal("1.20.1-forge-47.2.0", outcome.VersionId);
            Assert.Equal(1, runner.Calls);
            Assert.Equal(1, repairer.Calls);
            var versionPath = Path.Combine(
                root,
                "versions",
                outcome.VersionId,
                $"{outcome.VersionId}.json");
            Assert.True(File.Exists(versionPath));
            Assert.DoesNotContain(
                "inheritsFrom",
                await File.ReadAllTextAsync(versionPath));
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
    public async Task RepairInstance_restores_a_vanilla_instance()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "archoera-simple-repair",
            Guid.NewGuid().ToString("N"));
        var dataRoot = Path.Combine(directory, "data");
        Directory.CreateDirectory(dataRoot);
        try
        {
            var settings = new JsonAppSettingsStore(dataRoot);
            var repository = new SqliteInstanceRepository(dataRoot, settings);
            var created = await repository.CreateAsync(
                "Vanilla",
                "1.20.1",
                "vanilla",
                string.Empty,
                rootPath: null);
            var runner = new FakePlanRunner();
            var service = new PackInstallService(
                repository: repository,
                settings: settings,
                runner: runner,
                gameInstall: new PackGameInstallService(
                    new FakeMojangMetadata(),
                    new HttpClient(new FakeHttpMessageHandler()),
                    new LoaderProfileEndpoints(
                        new Uri("https://meta.fabricmc.net/v2/"),
                        new Uri("https://meta.quiltmc.org/v3/"))),
                forgeRepair: new FakeForgeRepair());

            var result = await service.RepairInstanceAsync(created.Id);

            Assert.Equal("1.20.1", result.VersionId);
            Assert.Equal(1, runner.Calls);
            // New instances are isolated under versions/<name>, so the shared
            // versions/ directory lives at the install root.
            Assert.Contains(
                $"{Path.DirectorySeparatorChar}versions{Path.DirectorySeparatorChar}",
                created.RootPath);
            var installRoot = Archoera.Infrastructure.Launch.LaunchPaths
                .ResolveInstallRoot(created.RootPath);
            Assert.True(File.Exists(Path.Combine(
                installRoot,
                "versions",
                "1.20.1",
                "1.20.1.json")));
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
    public async Task Forge_packs_are_rejected_with_a_stable_code()
    {
        var service = new PackGameInstallService(new FakeMojangMetadata());
        var exception = await Assert.ThrowsAsync<PackException>(
            () => service.BuildAsync(
                Path.GetTempPath(),
                "1.20.1",
                "forge",
                "47.2.0",
                MojangPlatform.LinuxX64));
        Assert.Equal(PackErrorCode.LoaderUnsupported, exception.Code);
    }

    [Fact]
    public async Task Forge_installer_builds_a_combined_plan()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "archoera-pack-forge",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var installerPath = Path.Combine(directory, "installer.jar");
            await File.WriteAllBytesAsync(
                installerPath,
                ForgeInstallerFixture.CreateInstaller("[]", "[]"));

            var service = new PackGameInstallService(
                new FakeMojangMetadata(),
                new HttpClient(new FakeHttpMessageHandler()),
                new LoaderProfileEndpoints(
                    new Uri("https://meta.fabricmc.net/v2/"),
                    new Uri("https://meta.quiltmc.org/v3/")));

            var plan = await service.BuildForgeFromInstallerAsync(
                directory,
                LoaderKind.Forge,
                "1.20.1",
                "47.2.0",
                installerPath,
                MojangPlatform.LinuxX64,
                preferMavenMirror: false);

            Assert.Equal("1.20.1-forge-47.2.0", plan.VersionId);
            Assert.Contains(
                plan.Plan.Files,
                file => file.Dest.Contains("downloads/loaders/forge/", StringComparison.Ordinal));
            Assert.Contains(
                plan.Plan.Files,
                file => file.Dest.StartsWith(
                    "versions/1.20.1-forge-47.2.0/",
                    StringComparison.Ordinal));
            Assert.DoesNotContain("inheritsFrom", plan.VersionJson);
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

    private sealed class FakePlanRunner : IDownloadPlanRunner
    {
        public int Calls { get; private set; }
        public bool IsAvailable => true;

        public Task<PlanExecutionResult> ExecuteAsync(
            DownloadPlan plan,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(
                new PlanExecutionResult("completed", true, string.Empty, string.Empty, string.Empty, 0));
        }
    }

    private sealed class FakeForgeRepair : IForgeRepairService
    {
        public int Calls { get; private set; }

        public Task<LoaderRepairResult> RepairAsync(
            LoaderArtifactManifest manifest,
            string root,
            string javaPath,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(
                new LoaderRepairResult(
                    LoaderRepairStatus.AlreadyValid,
                    LoaderVerification.Valid,
                    [],
                    []));
        }
    }

    private sealed class FakeMojangMetadata : IMojangMetadataClient
    {
        private static readonly string VanillaJson = $$"""
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
        {
            using var document = JsonDocument.Parse(VanillaJson);
            return Task.FromResult(
                Archoera.Application.Mojang.MojangJson.ParseVersion(document.RootElement));
        }

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
