using System.Text;
using Archoera.Application.Mojang;
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
