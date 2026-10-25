using System.Text;
using System.Text.Json;
using Archoera.Application.Mojang;
using Archoera.Domain.Mojang;
using Archoera.Infrastructure.Mojang;
using Archoera.Infrastructure.Modules;
using Archoera.Infrastructure.Settings;

namespace Archoera.Tests;

public sealed class VersionModuleServiceTests
{
    [Fact]
    public async Task Plan_fetches_the_version_document_once()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "archoera-version-plan",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var region = new RegionPolicy(regionOverride: RegionPolicy.Global);
            var client = new CountingMojangClient();
            var service = new VersionModuleService(
                client,
                enableDownloads: true,
                new DownloadPolicy(root, region),
                new JsonAppSettingsStore(root),
                region);

            var result = await service.HandleAsync(
                "version.plan",
                JsonSerializer.Serialize(new { root, versionId = MojangTestData.VersionId }),
                CancellationToken.None);

            using var document = JsonDocument.Parse(result);
            Assert.True(
                document.RootElement.GetProperty("ok").GetBoolean(),
                Encoding.UTF8.GetString(result));

            // Regression: the version document used to be fetched once by the
            // plan service and once more for the response. Each fetch also
            // re-read the manifest, turning one plan into five metadata
            // requests against the official host.
            Assert.Equal(1, client.VersionJsonCalls);
            Assert.Equal(0, client.ManifestCalls);
            Assert.Equal(0, client.VersionCalls);
            Assert.Equal(1, client.AssetIndexCalls);
        }
        finally
        {
            try
            {
                Directory.Delete(root, recursive: true);
            }
            catch (IOException)
            {
                // Best effort on platforms with transient file handles.
            }
        }
    }

    [Fact]
    public async Task Auto_source_uses_the_mirror_in_mainland_china()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "archoera-version-plan",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            // The policy gate is allowed (GLOBAL), while the download source
            // uses the same CN detection the settings/region UI reports.
            var policyRegion = new RegionPolicy(regionOverride: RegionPolicy.Global);
            var sourceRegion = new RegionPolicy(regionOverride: RegionPolicy.China);
            var service = new VersionModuleService(
                new CountingMojangClient(),
                enableDownloads: true,
                new DownloadPolicy(root, policyRegion),
                new JsonAppSettingsStore(root),
                sourceRegion);

            var result = await service.HandleAsync(
                "version.plan",
                JsonSerializer.Serialize(new { root, versionId = MojangTestData.VersionId }),
                CancellationToken.None);

            using var document = JsonDocument.Parse(result);
            Assert.True(
                document.RootElement.GetProperty("ok").GetBoolean(),
                Encoding.UTF8.GetString(result));
            var files = document.RootElement
                .GetProperty("data")
                .GetProperty("plan")
                .GetProperty("files");
            var client = files
                .EnumerateArray()
                .Single(file =>
                    file.GetProperty("dest").GetString() == MojangTestData.ClientPath);
            Assert.StartsWith(
                "https://bmclapi2.bangbang93.com/version/",
                client.GetProperty("url").GetString(),
                StringComparison.Ordinal);
        }
        finally
        {
            try
            {
                Directory.Delete(root, recursive: true);
            }
            catch (IOException)
            {
                // Best effort on platforms with transient file handles.
            }
        }
    }

    private sealed class CountingMojangClient : IMojangMetadataClient
    {
        private static readonly string VersionJson = $$"""
            {
              "id": "{{MojangTestData.VersionId}}",
              "mainClass": "net.minecraft.client.main.Main",
              "assets": "{{MojangTestData.AssetIndexId}}",
              "assetIndex": {
                "id": "{{MojangTestData.AssetIndexId}}",
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

        public int ManifestCalls { get; private set; }
        public int VersionCalls { get; private set; }
        public int VersionJsonCalls { get; private set; }
        public int AssetIndexCalls { get; private set; }

        public Task<MojangVersionManifest> GetManifestAsync(
            CancellationToken cancellationToken = default)
        {
            ManifestCalls++;
            throw new InvalidOperationException(
                "version.plan must use the already fetched version document");
        }

        public Task<MojangVersion> GetVersionAsync(
            string versionId,
            CancellationToken cancellationToken = default)
        {
            VersionCalls++;
            throw new InvalidOperationException(
                "version.plan must use the already fetched version document");
        }

        public Task<byte[]> GetVersionJsonAsync(
            string versionId,
            CancellationToken cancellationToken = default)
        {
            VersionJsonCalls++;
            return Task.FromResult(Encoding.UTF8.GetBytes(VersionJson));
        }

        public Task<MojangAssetIndex> GetAssetIndexAsync(
            MojangAssetIndexRef index,
            CancellationToken cancellationToken = default)
        {
            AssetIndexCalls++;
            return Task.FromResult(MojangTestData.AssetIndex());
        }
    }
}
