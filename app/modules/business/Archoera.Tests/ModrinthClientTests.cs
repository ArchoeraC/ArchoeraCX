using System.Net;
using Archoera.Application.Resources;
using Archoera.Domain.Resources;
using Archoera.Infrastructure.Resources.Modrinth;

namespace Archoera.Tests;

public sealed class ModrinthClientTests
{
    private const string UserAgent = "ArchoeraCX/0.0.1+1 (test@example.com)";
    private static readonly string Sha1 = new('1', 40);
    private static readonly string Sha512 = new('2', 128);

    [Fact]
    public async Task SearchAsync_maps_hits_and_sends_an_identifiable_request()
    {
        var handler = new FakeHttpMessageHandler();
        handler.EnqueueJson(SearchJson);
        var client = CreateClient(handler);

        var result = await client.SearchAsync(new ResourceSearchRequest
        {
            Query = "sodium",
            Kind = ResourceKind.Mod,
            Target = ResourceTarget.ForMod("1.21.1", "fabric"),
            PageSize = 20,
        });

        Assert.Equal("modrinth", result.Provider);
        Assert.Equal(1, result.Total);
        Assert.Single(result.Hits);

        var hit = result.Hits[0];
        Assert.Equal("AANobbMI", hit.Id);
        Assert.Equal("sodium", hit.Slug);
        Assert.Equal("Sodium", hit.Name);
        Assert.Equal("jellysquid3", hit.Author);
        Assert.Equal(123456, hit.Downloads);
        Assert.Equal(45, hit.Follows);
        Assert.Equal(ResourceKind.Mod, hit.Kind);
        Assert.Contains("optimization", hit.CategoryList);
        Assert.Contains("fabric", hit.LoaderList);

        var request = Assert.Single(handler.Requests);
        Assert.Equal(UserAgent, request.UserAgent);
        Assert.Equal("GET", request.Method);
        Assert.Equal("/v2/search", request.Uri.AbsolutePath);

        var query = Uri.UnescapeDataString(request.Uri.Query);
        Assert.Contains("query=sodium", query, StringComparison.Ordinal);
        Assert.Contains("index=relevance", query, StringComparison.Ordinal);
        Assert.Contains("limit=20", query, StringComparison.Ordinal);
        Assert.Contains("offset=0", query, StringComparison.Ordinal);
        Assert.Contains("project_type:mod", query, StringComparison.Ordinal);
        Assert.Contains("categories:fabric", query, StringComparison.Ordinal);
        Assert.Contains("versions:1.21.1", query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SearchAsync_without_target_is_global()
    {
        var handler = new FakeHttpMessageHandler();
        handler.EnqueueJson(SearchJson);
        var client = CreateClient(handler);

        await client.SearchAsync(new ResourceSearchRequest
        {
            Query = "sodium",
            Kind = ResourceKind.Mod,
            PageSize = 20,
        });

        var request = Assert.Single(handler.Requests);
        var query = Uri.UnescapeDataString(request.Uri.Query);
        Assert.Contains("project_type:mod", query, StringComparison.Ordinal);
        Assert.DoesNotContain("versions:", query, StringComparison.Ordinal);
        Assert.DoesNotContain("categories:fabric", query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetProjectAsync_maps_project_metadata()
    {
        var handler = new FakeHttpMessageHandler();
        handler.EnqueueJson(ProjectJson);
        var client = CreateClient(handler);

        var project = await client.GetProjectAsync("AANobbMI");

        Assert.Equal("AANobbMI", project.Id);
        Assert.Equal("sodium", project.Slug);
        Assert.Equal(ResourceKind.Mod, project.Kind);
        Assert.Equal(45, project.Follows);
        Assert.Equal("lgpl-3.0-only", project.License);
        Assert.Equal("required", project.ClientSide);
        Assert.Equal("https://github.com/CaffeineMC/sodium", project.SourceUrl);
        Assert.NotNull(project.Body);
        var gallery = Assert.Single(project.GalleryList);
        Assert.True(gallery.Featured);
        Assert.Equal("https://cdn.modrinth.com/data/AANobbMI/gallery/one.png", gallery.Url);
        Assert.Equal("/v2/project/AANobbMI", handler.Requests[0].Uri.AbsolutePath);
    }

    [Fact]
    public async Task GetVersionsAsync_filters_on_version_level_fields()
    {
        var handler = new FakeHttpMessageHandler();
        handler.EnqueueJson(VersionsJson);
        var client = CreateClient(handler);

        var versions = await client.GetVersionsAsync(
            "AANobbMI",
            ResourceKind.Mod,
            ResourceTarget.ForMod("1.21.1", "fabric"));

        var version = Assert.Single(versions);
        Assert.Equal("fabric-1211", version.Id);
        Assert.Equal(ResourceVersionType.Release, version.VersionType);
        Assert.Equal(1234, version.PrimaryFile!.Size);
        Assert.Equal("sodium-0.6.0.jar", version.PrimaryFile.FileName);
        Assert.Equal(Sha512, version.PrimaryFile.Sha512);
        Assert.Equal("sodium.jar", version.PrimaryFile.Url.Split('/')[^1]);

        var dependency = Assert.Single(version.DependencyList);
        Assert.Equal(ResourceDependencyKind.Required, dependency.Kind);
        Assert.Equal("P7dR8mSH", dependency.ProjectId);

        var request = Assert.Single(handler.Requests);
        Assert.Equal("/v2/project/AANobbMI/version", request.Uri.AbsolutePath);
        var query = Uri.UnescapeDataString(request.Uri.Query);
        Assert.Contains("game_versions=[\"1.21.1\"]", query, StringComparison.Ordinal);
        Assert.Contains("loaders=[\"fabric\"]", query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetVersionsAsync_never_accepts_a_loader_mismatch_or_snapshot()
    {
        var handler = new FakeHttpMessageHandler();
        handler.EnqueueJson(VersionsJson);
        var client = CreateClient(handler);

        var noForge = await client.GetVersionsAsync(
            "AANobbMI",
            ResourceKind.Mod,
            ResourceTarget.ForMod("1.21.1", "quilt"));
        Assert.Empty(noForge);

        handler.EnqueueJson(VersionsJson);
        var noSnapshot = await client.GetVersionsAsync(
            "AANobbMI",
            ResourceKind.Mod,
            ResourceTarget.ForMod("1.21.1-pre1", "fabric"));
        Assert.Empty(noSnapshot);
    }

    [Theory]
    [InlineData(ResourceKind.Shader, "iris")]
    [InlineData(ResourceKind.ResourcePack, "minecraft")]
    public void Compatibility_uses_shader_loader_and_minecraft_for_packs(
        ResourceKind kind,
        string loader)
    {
        var target = kind == ResourceKind.Shader
            ? ResourceTarget.ForShader("1.21.1", loader)
            : ResourceTarget.ForResourcePack("1.21.1");

        var version = new ResourceVersion(
            "v1",
            "p1",
            "name",
            "1.0",
            ["1.21.1"],
            [loader],
            Files: []);

        Assert.True(ResourceCompatibility.IsCompatible(version, kind, target));
        Assert.False(ResourceCompatibility.IsCompatible(
            version,
            kind,
            new ResourceTarget("1.22", "fabric", "iris")));
    }

    [Theory]
    [InlineData("24w14a", true)]
    [InlineData("1.21.5-pre1", true)]
    [InlineData("1.21.5-rc1", true)]
    [InlineData("b1.7.3", true)]
    [InlineData("1.21.1", false)]
    [InlineData("1.21", false)]
    public void Snapshot_detection(string gameVersion, bool expected)
    {
        Assert.Equal(expected, ResourceCompatibility.IsSnapshot(gameVersion));
    }

    [Fact]
    public async Task Rate_limit_429_is_retried_with_RetryAfter()
    {
        var handler = new FakeHttpMessageHandler();
        handler.EnqueueStatus(HttpStatusCode.TooManyRequests, retryAfterSeconds: 0, rateLimitRemaining: 0);
        handler.EnqueueJson(SearchJson);
        var client = CreateClient(handler, rateLimitRetries: 1);

        var result = await client.SearchAsync(new ResourceSearchRequest { Query = "sodium" });

        Assert.Equal(1, result.Total);
        Assert.Equal(2, handler.Requests.Count);
        Assert.NotNull(client.LastRateLimit);
        Assert.Equal(300, client.LastRateLimit!.Limit);
        Assert.Equal(0, client.LastRateLimit.Remaining);
        Assert.Equal(60, client.LastRateLimit.ResetSeconds);
    }

    [Fact]
    public async Task Rate_limit_429_exposes_RetryAfter_when_the_budget_is_spent()
    {
        var handler = new FakeHttpMessageHandler();
        handler.EnqueueStatus(HttpStatusCode.TooManyRequests, retryAfterSeconds: 2);
        var client = CreateClient(handler, rateLimitRetries: 0);

        var exception = await Assert.ThrowsAsync<ResourceProviderException>(
            () => client.SearchAsync(new ResourceSearchRequest { Query = "sodium" }));

        Assert.Equal("RATE_LIMITED", exception.Code);
        Assert.Equal(429, exception.StatusCode);
        Assert.Equal(TimeSpan.FromSeconds(2), exception.RetryAfter);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task Forbidden_maps_to_distribution_denied()
    {
        var handler = new FakeHttpMessageHandler();
        handler.EnqueueStatus(HttpStatusCode.Forbidden);
        var client = CreateClient(handler);

        var exception = await Assert.ThrowsAsync<ResourceProviderException>(
            () => client.GetVersionAsync("abc123"));

        Assert.Equal("DISTRIBUTION_DENIED", exception.Code);
        Assert.Equal(403, exception.StatusCode);
    }

    [Fact]
    public async Task NotFound_maps_to_not_found()
    {
        var handler = new FakeHttpMessageHandler();
        handler.EnqueueStatus(HttpStatusCode.NotFound);
        var client = CreateClient(handler);

        var exception = await Assert.ThrowsAsync<ResourceProviderException>(
            () => client.GetProjectAsync("missing"));

        Assert.Equal("NOT_FOUND", exception.Code);
        Assert.Equal(404, exception.StatusCode);
    }

    [Fact]
    public async Task Malformed_json_maps_to_invalid_response()
    {
        var handler = new FakeHttpMessageHandler();
        handler.EnqueueJson("{\"hits\":");
        var client = CreateClient(handler);

        var exception = await Assert.ThrowsAsync<ResourceProviderException>(
            () => client.SearchAsync(new ResourceSearchRequest { Query = "sodium" }));

        Assert.Equal("INVALID_RESPONSE", exception.Code);
    }

    [Theory]
    [InlineData("")]
    [InlineData("okhttp/4.12.0")]
    [InlineData("curl/8.0")]
    public void Identifiable_user_agent_is_required(string userAgent)
    {
        var handler = new FakeHttpMessageHandler();
        var http = new HttpClient(handler);

        Assert.Throws<ArgumentException>(() => new ModrinthClient(
            http,
            new ModrinthClientOptions { UserAgent = userAgent }));
    }

    [Fact]
    public async Task Invalid_page_size_is_rejected_before_any_request()
    {
        var handler = new FakeHttpMessageHandler();
        var client = CreateClient(handler);

        var exception = await Assert.ThrowsAsync<ResourceProviderException>(
            () => client.SearchAsync(new ResourceSearchRequest { PageSize = 101 }));

        Assert.Equal("INVALID_REQUEST", exception.Code);
        Assert.Empty(handler.Requests);
    }

    private static ModrinthClient CreateClient(
        FakeHttpMessageHandler handler,
        int rateLimitRetries = 1)
        => new(
            new HttpClient(handler),
            new ModrinthClientOptions
            {
                UserAgent = UserAgent,
                RateLimitRetries = rateLimitRetries,
            });

    private const string SearchJson = $$"""
        {
          "hits": [
            {
              "project_id": "AANobbMI",
              "slug": "sodium",
              "title": "Sodium",
              "description": "Modern rendering engine",
              "author": "jellysquid3",
              "icon_url": "https://cdn.modrinth.com/data/AANobbMI/icon.png",
              "downloads": 123456,
              "follows": 45,
              "date_modified": "2026-09-01T12:00:00Z",
              "categories": ["optimization", "fabric"],
              "display_categories": ["optimization"],
              "versions": ["1.21.1", "1.21"],
              "project_type": "mod",
              "client_side": "required",
              "license": "lgpl-3.0-only"
            }
          ],
          "offset": 0,
          "limit": 20,
          "total_hits": 1
        }
        """;

    private const string ProjectJson = $$"""
        {
          "id": "AANobbMI",
          "slug": "sodium",
          "title": "Sodium",
          "description": "Modern rendering engine",
          "categories": ["optimization"],
          "client_side": "required",
          "server_side": "unsupported",
          "project_type": "mod",
          "downloads": 123456,
          "followers": 45,
          "icon_url": "https://cdn.modrinth.com/data/AANobbMI/icon.png",
          "updated": "2026-09-01T12:00:00Z",
          "game_versions": ["1.21.1"],
          "loaders": ["fabric"],
          "license": { "id": "lgpl-3.0-only", "name": "LGPL-3.0-only" },
          "source_url": "https://github.com/CaffeineMC/sodium",
          "body": "A modern rendering engine.",
          "published": "2020-12-01T00:00:00Z",
          "gallery": [
            {
              "url": "https://cdn.modrinth.com/data/AANobbMI/gallery/one.png",
              "featured": true,
              "title": "In-game",
              "description": "Sodium running in a world.",
              "ordering": 0
            }
          ]
        }
        """;

    private static readonly string VersionsJson = $$"""
        [
          {
            "id": "fabric-1211",
            "project_id": "AANobbMI",
            "name": "Sodium 0.6.0",
            "version_number": "0.6.0",
            "game_versions": ["1.21.1"],
            "loaders": ["fabric"],
            "date_published": "2026-09-01T12:00:00Z",
            "downloads": 42,
            "version_type": "release",
            "files": [
              {
                "filename": "sodium-0.6.0.jar",
                "url": "https://cdn.modrinth.com/data/AANobbMI/versions/fabric-1211/sodium.jar",
                "hashes": { "sha1": "{{Sha1}}", "sha512": "{{Sha512}}" },
                "size": 1234,
                "primary": true
              }
            ],
            "dependencies": [
              {
                "version_id": null,
                "project_id": "P7dR8mSH",
                "file_name": null,
                "dependency_type": "required"
              }
            ]
          },
          {
            "id": "forge-1211",
            "project_id": "AANobbMI",
            "name": "Sodium Forge",
            "version_number": "0.6.0",
            "game_versions": ["1.21.1"],
            "loaders": ["forge"],
            "version_type": "release",
            "files": []
          },
          {
            "id": "snapshot",
            "project_id": "AANobbMI",
            "name": "Sodium snapshot",
            "version_number": "0.6.1",
            "game_versions": ["1.21.1-pre1"],
            "loaders": ["fabric"],
            "version_type": "beta",
            "files": []
          },
          {
            "id": "wrong-game",
            "project_id": "AANobbMI",
            "name": "Sodium 1.21",
            "version_number": "0.5.0",
            "game_versions": ["1.21"],
            "loaders": ["fabric"],
            "version_type": "release",
            "files": []
          }
        ]
        """;
}
