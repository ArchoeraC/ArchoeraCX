using System.Net;
using System.Security.Cryptography;
using System.Text;
using Archoera.Application.Downloads;
using Archoera.Application.Mojang;
using Archoera.Domain.Downloads;
using Archoera.Domain.Mojang;
using Archoera.Infrastructure.Mojang;

namespace Archoera.Tests;

public sealed class MojangClientTests
{
    private const string ManifestUrl =
        "https://piston-meta.mojang.com/mc/game/version_manifest_v2.json";

    private const string VersionUrl =
        "https://piston-meta.mojang.com/v1/packages/version/1.20.1.json";

    private const string AssetIndexUrl =
        "https://piston-meta.mojang.com/v1/packages/index/5.json";

    private const string UserAgent = "ArchoeraCX/0.0.1+1 (tests@example.com)";

    [Fact]
    public async Task GetManifest_parses_latest_and_versions()
    {
        var versionJson = VersionJson();
        var handler = new FakeHttpMessageHandler();
        handler.EnqueueJson(ManifestJson(versionJson));

        var client = CreateClient(handler);
        var manifest = await client.GetManifestAsync();

        Assert.Equal("1.20.1", manifest.LatestRelease);
        Assert.Equal("1.20.1", manifest.LatestSnapshot);
        var summary = Assert.Single(manifest.Versions);
        Assert.Equal("1.20.1", summary.Id);
        Assert.Equal(VersionUrl, summary.Url);
        Assert.Equal(Sha1Of(versionJson), summary.Sha1);
        Assert.Equal(1, summary.ComplianceLevel);

        var request = Assert.Single(handler.Requests);
        Assert.Equal(new Uri(ManifestUrl), request.Uri);
        Assert.Equal(UserAgent, request.UserAgent);
        Assert.Equal("GET", request.Method);
    }

    [Fact]
    public async Task GetVersion_resolves_the_manifest_url_and_verifies_sha1()
    {
        var versionJson = VersionJson();
        var handler = new FakeHttpMessageHandler();
        handler.EnqueueJson(ManifestJson(versionJson));
        handler.EnqueueJson(versionJson);

        var version = await CreateClient(handler).GetVersionAsync("1.20.1");

        Assert.Equal("1.20.1", version.Id);
        Assert.Equal("release", version.Type);
        Assert.Equal("net.minecraft.client.main.Main", version.MainClass);
        Assert.Equal("5", version.AssetIndex?.Id);
        Assert.Equal(AssetIndexUrl, version.AssetIndex?.Url);
        Assert.Equal(1_000, version.Downloads.Client?.Size);
        Assert.Equal("client-1.12.xml", version.Logging?.Client?.Id);
        Assert.Equal("java-runtime-gamma", version.JavaVersion?.Component);
        Assert.Equal(17, version.JavaVersion?.MajorVersion);
        Assert.Empty(version.Libraries);

        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal(new Uri(ManifestUrl), handler.Requests[0].Uri);
        Assert.Equal(new Uri(VersionUrl), handler.Requests[1].Uri);
    }

    [Fact]
    public async Task GetVersion_unknown_id_fails_with_not_found()
    {
        var handler = new FakeHttpMessageHandler();
        handler.EnqueueJson(ManifestJson(VersionJson()));

        var exception = await Assert.ThrowsAsync<MojangException>(
            () => CreateClient(handler).GetVersionAsync("9.99.9"));

        Assert.Equal(MojangErrorCode.NotFound, exception.Code);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task GetVersion_rejects_a_hash_mismatch()
    {
        var versionJson = VersionJson();
        var handler = new FakeHttpMessageHandler();
        handler.EnqueueJson(ManifestJson(versionJson, sha1: new string('0', 40)));
        handler.EnqueueJson(versionJson);

        var exception = await Assert.ThrowsAsync<MojangException>(
            () => CreateClient(handler).GetVersionAsync("1.20.1"));

        Assert.Equal(MojangErrorCode.HashMismatch, exception.Code);
    }

    [Fact]
    public async Task GetAssetIndex_verifies_sha1_and_parses_objects()
    {
        var assetJson = AssetIndexJson();
        var refValue = new MojangAssetIndexRef(
            "5",
            AssetIndexUrl,
            Sha1Of(assetJson),
            Encoding.UTF8.GetByteCount(assetJson),
            TotalSize: 12);

        var handler = new FakeHttpMessageHandler();
        handler.EnqueueJson(assetJson);

        var index = await CreateClient(handler).GetAssetIndexAsync(refValue);

        Assert.Equal("5", index.Id);
        Assert.False(index.Virtual);
        Assert.False(index.MapToResources);
        Assert.Equal(3, index.Objects.Count);
        Assert.Equal(5, index.Objects["icons/a.png"].Size);
        Assert.Equal(new string('a', 40), index.Objects["icons/a.png"].Hash);
        Assert.Equal(new Uri(AssetIndexUrl), Assert.Single(handler.Requests).Uri);
    }

    [Fact]
    public async Task GetAssetIndex_rejects_a_hash_mismatch()
    {
        var assetJson = AssetIndexJson();
        var refValue = new MojangAssetIndexRef(
            "5",
            AssetIndexUrl,
            new string('0', 40),
            Encoding.UTF8.GetByteCount(assetJson));

        var handler = new FakeHttpMessageHandler();
        handler.EnqueueJson(assetJson);

        var exception = await Assert.ThrowsAsync<MojangException>(
            () => CreateClient(handler).GetAssetIndexAsync(refValue));

        Assert.Equal(MojangErrorCode.HashMismatch, exception.Code);
    }

    [Fact]
    public async Task GetAssetIndex_requires_a_sha1_without_touching_the_network()
    {
        var refValue = new MojangAssetIndexRef("5", AssetIndexUrl, Sha1: null, Size: 10);
        var handler = new FakeHttpMessageHandler();

        var exception = await Assert.ThrowsAsync<MojangException>(
            () => CreateClient(handler).GetAssetIndexAsync(refValue));

        Assert.Equal(MojangErrorCode.MissingHash, exception.Code);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Transient_5xx_is_retried()
    {
        var handler = new FakeHttpMessageHandler();
        handler.EnqueueStatus(HttpStatusCode.InternalServerError);
        handler.EnqueueJson(ManifestJson(VersionJson()));

        var client = CreateClient(handler, new MojangClientOptions
        {
            UserAgent = UserAgent,
            RetryDelay = TimeSpan.FromMilliseconds(1),
            MaxRetryDelay = TimeSpan.FromMilliseconds(5),
        });

        var manifest = await client.GetManifestAsync();

        Assert.Single(manifest.Versions);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task PlanService_builds_a_full_plan_from_metadata()
    {
        var assetJson = AssetIndexJson();
        var versionJson = VersionJson(
            assetIndexSha1: Sha1Of(assetJson),
            assetIndexSize: Encoding.UTF8.GetByteCount(assetJson));

        var handler = new FakeHttpMessageHandler();
        handler.EnqueueJson(ManifestJson(versionJson));
        handler.EnqueueJson(versionJson);
        handler.EnqueueJson(assetJson);

        var service = new MojangPlanService(CreateClient(handler));
        var plan = await service.BuildForVersionAsync(
            MojangTestData.Root,
            "1.20.1",
            MojangPlatform.LinuxX64);

        var destinations = plan.Files.Select(file => file.Dest).ToArray();

        Assert.Contains(MojangTestData.ClientPath, destinations);
        Assert.Contains(MojangTestData.AssetIndexPath, destinations);
        Assert.Contains(MojangTestData.AssetAPath, destinations);
        Assert.Contains(MojangTestData.AssetBPath, destinations);
        Assert.Contains(MojangTestData.LoggingPath, destinations);
        Assert.Equal(5, plan.Files.Count);

        Assert.Equal(3, handler.Requests.Count);
        Assert.Equal(new Uri(ManifestUrl), handler.Requests[0].Uri);
        Assert.Equal(new Uri(VersionUrl), handler.Requests[1].Uri);
        Assert.Equal(new Uri(AssetIndexUrl), handler.Requests[2].Uri);

        Assert.True(
            DownloadPlanJson.TryParse(DownloadPlanJson.Build(plan), out _, out var error),
            error?.ToString());
    }

    [Fact]
    public async Task PlanService_rejects_inherited_versions_before_fetching_assets()
    {
        var versionJson = VersionJson(inheritsFrom: "1.20.1");
        var handler = new FakeHttpMessageHandler();
        handler.EnqueueJson(ManifestJson(versionJson));
        handler.EnqueueJson(versionJson);

        var service = new MojangPlanService(CreateClient(handler));

        var exception = await Assert.ThrowsAsync<MojangException>(
            () => service.BuildForVersionAsync(
                MojangTestData.Root,
                "1.20.1",
                MojangPlatform.LinuxX64));

        Assert.Equal(MojangErrorCode.UnsupportedInherits, exception.Code);
        Assert.Equal(2, handler.Requests.Count);
    }

    private static MojangClient CreateClient(
        FakeHttpMessageHandler handler,
        MojangClientOptions? options = null)
    {
        var httpClient = new HttpClient(handler);
        return new MojangClient(httpClient, options ?? new MojangClientOptions
        {
            UserAgent = UserAgent,
        });
    }

    private static string ManifestJson(
        string versionJson,
        string? versionUrl = null,
        string? sha1 = null)
    {
        var versionSha1 = sha1 ?? Sha1Of(versionJson);
        return $$"""
            {
              "latest": { "release": "1.20.1", "snapshot": "1.20.1" },
              "versions": [
                {
                  "id": "1.20.1",
                  "type": "release",
                  "url": "{{versionUrl ?? VersionUrl}}",
                  "sha1": "{{versionSha1}}",
                  "releaseTime": "2024-08-08T12:00:00+00:00",
                  "complianceLevel": 1
                }
              ]
            }
            """;
    }

    private static string VersionJson(
        string? inheritsFrom = null,
        string? assetIndexSha1 = null,
        long assetIndexSize = 400)
    {
        var inherits = inheritsFrom is null
            ? string.Empty
            : $",\n  \"inheritsFrom\": \"{inheritsFrom}\"";
        var clientSha1 = new string('1', 40);
        var loggingSha1 = new string('7', 40);
        var indexSha1 = assetIndexSha1 ?? new string('8', 40);

        return $$"""
            {
              "id": "1.20.1",
              "type": "release",
              "mainClass": "net.minecraft.client.main.Main"{{inherits}},
              "assets": "5",
              "assetIndex": {
                "id": "5",
                "url": "{{AssetIndexUrl}}",
                "sha1": "{{indexSha1}}",
                "size": {{assetIndexSize}},
                "totalSize": 12
              },
              "downloads": {
                "client": {
                  "url": "https://piston-data.mojang.com/v1/objects/client/client.jar",
                  "sha1": "{{clientSha1}}",
                  "size": 1000
                }
              },
              "javaVersion": { "component": "java-runtime-gamma", "majorVersion": 17 },
              "logging": {
                "client": {
                  "argument": "-Dlog4j.configurationFile=${path}",
                  "file": {
                    "id": "client-1.12.xml",
                    "url": "https://piston-data.mojang.com/v1/objects/log/client-1.12.xml",
                    "sha1": "{{loggingSha1}}",
                    "size": 42,
                    "type": "log4j2-xml"
                  }
                }
              },
              "libraries": []
            }
            """;
    }

    private static string AssetIndexJson()
    {
        var hashA = new string('a', 40);
        var hashB = new string('b', 40);

        return $$"""
            {
              "objects": {
                "icons/a.png": { "hash": "{{hashA}}", "size": 5 },
                "icons/b.png": { "hash": "{{hashA}}", "size": 5 },
                "sounds/c.ogg": { "hash": "{{hashB}}", "size": 7 }
              },
              "virtual": false,
              "map_to_resources": false
            }
            """;
    }

    private static string Sha1Of(string text)
        => Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
}
