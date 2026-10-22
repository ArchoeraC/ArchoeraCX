using System.Net;
using System.Text;
using Archoera.Infrastructure.Loaders;

namespace Archoera.Tests;

public sealed class LoaderVersionServiceTests
{
    [Fact]
    public async Task Fabric_versions_are_sorted_and_marked()
    {
        var handler = new FakeHttpMessageHandler();
        handler.EnqueueJson("""
            [
              { "loader": { "version": "0.20.1", "stable": false } },
              { "loader": { "version": "0.16.10", "stable": true } },
              { "loader": { "version": "0.16.9", "stable": true } }
            ]
            """);
        var service = CreateService(handler);

        var versions = await service.ListAsync("fabric", "1.21.1");

        Assert.Equal(["0.20.1", "0.16.10", "0.16.9"], versions.Select(item => item.Version));
        Assert.True(versions[0].Latest);
        Assert.False(versions[0].Recommended);
        Assert.True(versions[1].Recommended);
        Assert.Equal("/v2/versions/loader/1.21.1", handler.Requests[0].Uri.AbsolutePath);
    }

    [Fact]
    public async Task Forge_versions_filter_by_game_and_use_promotions()
    {
        var handler = new FakeHttpMessageHandler();
        handler.Enqueue(() => TextResponse("""
            <metadata><versioning><versions>
              <version>1.21.1-52.0.1</version>
              <version>1.21.1-52.0.0</version>
              <version>1.20.1-47.2.0</version>
            </versions></versioning></metadata>
            """));
        handler.EnqueueJson("""
            { "promos": { "1.21.1-latest": "52.0.1", "1.21.1-recommended": "52.0.0" } }
            """);
        var service = CreateService(handler);

        var versions = await service.ListAsync("forge", "1.21.1");

        Assert.Equal(["52.0.1", "52.0.0"], versions.Select(item => item.Version));
        Assert.True(versions[0].Latest);
        Assert.False(versions[0].Recommended);
        Assert.True(versions[1].Recommended);
        Assert.All(versions, item => Assert.True(item.Stable));
    }

    [Fact]
    public async Task NeoForge_versions_use_the_minor_patch_prefix()
    {
        var handler = new FakeHttpMessageHandler();
        handler.Enqueue(() => TextResponse("""
            <metadata><versioning><versions>
              <version>21.1.209</version>
              <version>21.1.100</version>
              <version>21.1.99-beta</version>
              <version>20.1.80</version>
            </versions></versioning></metadata>
            """));
        var service = CreateService(handler);

        var versions = await service.ListAsync("neoforge", "1.21.1");

        Assert.Equal(["21.1.209", "21.1.100", "21.1.99-beta"], versions.Select(item => item.Version));
        Assert.True(versions[0].Latest);
        Assert.True(versions[0].Recommended);
        Assert.False(versions[2].Stable);
    }

    [Fact]
    public async Task NeoForge_falls_back_to_the_mirror_when_the_primary_fails()
    {
        var handler = new RoutingHandler()
            .Route(
                "mirror.local/neoforge",
                () => TextResponse("""
                    <metadata><versioning><versions>
                      <version>21.1.248</version>
                      <version>21.1.209</version>
                    </versions></versioning></metadata>
                    """));
        var service = CreateService(
            new HttpClient(handler),
            neoForgeMirror: new Uri("https://mirror.local/neoforge/maven-metadata.xml"));

        var versions = await service.ListAsync("neoforge", "1.21.1");

        Assert.Equal(["21.1.248", "21.1.209"], versions.Select(item => item.Version));
        Assert.Contains(handler.Requests, uri => uri.Host == "test.local");
        Assert.Contains(handler.Requests, uri => uri.Host == "mirror.local");
    }

    [Fact]
    public async Task Forge_metadata_falls_back_to_the_mirror()
    {
        var handler = new RoutingHandler()
            .Route(
                "mirror.local/forge",
                () => TextResponse("""
                    <metadata><versioning><versions>
                      <version>1.21.1-52.0.1</version>
                      <version>1.20.1-47.2.0</version>
                    </versions></versioning></metadata>
                    """));
        var service = CreateService(
            new HttpClient(handler),
            forgeMetadataMirror: new Uri("https://mirror.local/forge/maven-metadata.xml"));

        var versions = await service.ListAsync("forge", "1.21.1");

        Assert.Equal(["52.0.1"], versions.Select(item => item.Version));
        Assert.Contains(handler.Requests, uri => uri.Host == "mirror.local");
    }

    [Fact]
    public async Task Unknown_loader_is_rejected()
    {
        var service = CreateService(new FakeHttpMessageHandler());

        await Assert.ThrowsAsync<ArgumentException>(
            () => service.ListAsync("made-up", "1.21.1"));
    }

    private static LoaderVersionService CreateService(FakeHttpMessageHandler handler)
        => CreateService(new HttpClient(handler));

    private static LoaderVersionService CreateService(
        HttpClient client,
        Uri? forgeMetadataMirror = null,
        Uri? neoForgeMirror = null)
        => new(
            client,
            new LoaderVersionEndpoints(
                new Uri("https://test.local/v2/"),
                new Uri("https://test.local/v3/"),
                new Uri("https://test.local/forge/maven-metadata.xml"),
                new Uri("https://test.local/forge/promotions.json"),
                new Uri("https://test.local/neoforge/maven-metadata.xml"),
                ForgeMetadataMirror: forgeMetadataMirror,
                NeoForgeMetadataMirror: neoForgeMirror));

    /// <summary>
    /// Routes by URL fragment. Unknown hosts throw as if the connection was
    /// refused, which is how a blocked Maven manifests to HttpClient.
    /// </summary>
    private sealed class RoutingHandler : HttpMessageHandler
    {
        private readonly Dictionary<string, Func<HttpResponseMessage>> _routes = [];
        public List<Uri> Requests { get; } = [];

        public RoutingHandler Route(string fragment, Func<HttpResponseMessage> response)
        {
            _routes[fragment] = response;
            return this;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var uri = request.RequestUri ?? new Uri("about:blank");
            Requests.Add(uri);
            foreach (var (fragment, response) in _routes)
            {
                if (uri.AbsoluteUri.Contains(fragment, StringComparison.Ordinal))
                {
                    return Task.FromResult(response());
                }
            }

            throw new HttpRequestException($"connection refused: {uri.Host}");
        }
    }

    private static HttpResponseMessage TextResponse(string text)
        => new(HttpStatusCode.OK)
        {
            Content = new StringContent(text, Encoding.UTF8, "application/xml"),
        };
}
