using System.Net;
using Archoera.Application.Downloads;
using Archoera.Application.Loaders;
using Archoera.Domain.Downloads;
using Archoera.Infrastructure.Loaders;

namespace Archoera.Tests;

/// <summary>
/// Pre-1.7 Forge metadata has no <c>downloads</c> block: it lists a Maven
/// coordinate, an optional repository and up to two checksums. The resolver
/// must use those checksums, fall back to the <c>.sha1</c> sidecar only when
/// they are absent, and take the size from <c>Content-Length</c>.
/// </summary>
public sealed class LegacyForgeLibraryResolverTests
{
    private const string Coordinate =
        "org.scala-lang:scala-parser-combinators_2.11:1.0.1";
    private const string Sha1 = "f05d7345bf5a58924f2837c6c1f4d73a938e1ff0";
    private const string ForgeRepository = "https://maven.minecraftforge.net/";

    [Fact]
    public async Task Uses_installer_checksum_and_head_size_without_a_sidecar()
    {
        var handler = new StubHandler(request =>
            request.Method == HttpMethod.Head
                ? Head(419_701)
                : new HttpResponseMessage(HttpStatusCode.NotFound));

        var resolver = new LegacyForgeLibraryResolver(new HttpClient(handler));
        var result = await resolver.ResolveAsync(
            Coordinate,
            ForgeRepository,
            [Sha1, "a1cbbcbde1dcc614f4253ed1aa0b320bc78d8f1d"],
            DownloadSourceMode.Official,
            CancellationToken.None);

        Assert.Equal(
            "https://maven.minecraftforge.net/org/scala-lang/" +
            "scala-parser-combinators_2.11/1.0.1/" +
            "scala-parser-combinators_2.11-1.0.1.jar",
            result.Url);
        Assert.Equal(Sha1, result.Hash.Hex);
        Assert.Equal(DownloadHashAlgorithm.Sha1, result.Hash.Algorithm);
        Assert.Equal(419_701, result.Size);
        Assert.Empty(result.Mirrors);
        Assert.DoesNotContain(
            handler.Requests,
            uri => uri.EndsWith(".sha1", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Falls_back_to_the_sidecar_when_checksums_are_absent()
    {
        var handler = new StubHandler(request =>
        {
            if (request.Method == HttpMethod.Head)
            {
                return Head(32_999);
            }

            return request.RequestUri!.AbsoluteUri.EndsWith(".sha1", StringComparison.Ordinal)
                ? Text(HttpStatusCode.OK, Sha1 + "\n")
                : new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        var resolver = new LegacyForgeLibraryResolver(new HttpClient(handler));
        var result = await resolver.ResolveAsync(
            "net.minecraft:launchwrapper:1.12",
            repository: null,
            checksums: [],
            DownloadSourceMode.Official,
            CancellationToken.None);

        Assert.Equal(
            "https://libraries.minecraft.net/net/minecraft/launchwrapper/1.12/launchwrapper-1.12.jar",
            result.Url);
        Assert.Equal(Sha1, result.Hash.Hex);
        Assert.Equal(32_999, result.Size);
        Assert.Contains(
            handler.Requests,
            uri => uri.EndsWith(".sha1", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Mirror_mode_prefers_bmclapi_and_keeps_the_official_fallback()
    {
        var handler = new StubHandler(request =>
            request.Method == HttpMethod.Head
                ? Head(1_234)
                : new HttpResponseMessage(HttpStatusCode.NotFound));

        var resolver = new LegacyForgeLibraryResolver(new HttpClient(handler));
        var result = await resolver.ResolveAsync(
            Coordinate,
            ForgeRepository,
            [Sha1],
            DownloadSourceMode.Mirror,
            CancellationToken.None);

        Assert.StartsWith(
            ForgeMavenMirror.Bmclapi.AbsoluteUri,
            result.Url,
            StringComparison.Ordinal);
        var fallback = Assert.Single(result.Mirrors);
        Assert.StartsWith(ForgeRepository, fallback, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Rejects_a_non_https_repository()
    {
        var resolver = new LegacyForgeLibraryResolver(
            new HttpClient(new StubHandler(_ => Head(1))));

        var error = await Assert.ThrowsAsync<ForgeDownloadException>(
            () => resolver.ResolveAsync(
                Coordinate,
                "http://maven.example.invalid/",
                [Sha1],
                DownloadSourceMode.Official,
                CancellationToken.None));

        Assert.Equal(ForgeDownloadErrorCode.InvalidMetadata, error.Code);
    }

    private static HttpResponseMessage Head(long size)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent([]),
        };
        response.Content.Headers.ContentLength = size;
        return response;
    }

    private static HttpResponseMessage Text(HttpStatusCode status, string body)
        => new(status)
        {
            Content = new StringContent(body),
        };

    private sealed class StubHandler(
        Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<string> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Add(request.RequestUri!.AbsoluteUri);
            return Task.FromResult(respond(request));
        }
    }
}
