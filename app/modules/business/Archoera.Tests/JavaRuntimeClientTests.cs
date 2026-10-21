using System.Net;
using System.Text;
using Archoera.Domain.Java;
using Archoera.Infrastructure.Mojang;

namespace Archoera.Tests;

public sealed class JavaRuntimeClientTests
{
    [Fact]
    public async Task GetCatalog_parses_all_json_with_the_default_source_and_user_agent()
    {
        var manifestJson = JavaRuntimeTestData.HappyManifestJson();
        var handler = new FakeHttpMessageHandler();
        handler.EnqueueJson(JavaRuntimeTestData.CatalogJson(manifestJson));

        var catalog = await CreateClient(handler).GetCatalogAsync();

        Assert.True(catalog.HasPlatform(JavaRuntimePlatform.Linux));
        Assert.Equal("21.0.7", catalog.Select("linux", "java-runtime-delta")?.Version.Name);

        var request = Assert.Single(handler.Requests);
        Assert.Equal(new Uri(JavaRuntimeTestData.CatalogUrl), request.Uri);
        Assert.Equal(JavaRuntimeTestData.UserAgent, request.UserAgent);
        Assert.Equal("GET", request.Method);
    }

    [Fact]
    public async Task GetCatalog_uses_a_configured_base_and_path()
    {
        var handler = new FakeHttpMessageHandler();
        handler.EnqueueJson(JavaRuntimeTestData.CatalogJson(JavaRuntimeTestData.HappyManifestJson()));

        var source = new JavaRuntimeCatalogSource
        {
            BaseAddress = new Uri("https://mirror.example/"),
            CatalogPath = "runtime/all.json",
        };

        await CreateClient(handler, source: source).GetCatalogAsync();

        Assert.Equal(
            new Uri("https://mirror.example/runtime/all.json"),
            Assert.Single(handler.Requests).Uri);
    }

    [Fact]
    public async Task GetRuntimeManifest_verifies_sha1_and_size_then_parses_files()
    {
        var manifestJson = JavaRuntimeTestData.HappyManifestJson();
        var handler = new FakeHttpMessageHandler();
        handler.EnqueueJson(manifestJson);

        var manifest = await CreateClient(handler).GetRuntimeManifestAsync(
            JavaRuntimeTestData.ComponentFor(manifestJson));

        Assert.Equal(4, manifest.Files.Count);
        var java = manifest.Files["bin/java"];
        Assert.True(java.IsFile);
        Assert.True(java.Executable);
        Assert.Equal("file", java.Type);
        Assert.Equal(JavaRuntimeTestData.JavaUrl, java.Raw?.Url);
        Assert.Equal(JavaRuntimeTestData.Sha1('a'), java.Raw?.Sha1);
        Assert.Equal(123456, java.Raw?.Size);
        Assert.NotNull(java.Lzma);

        var directory = manifest.Files["lib/modules"];
        Assert.True(directory.IsDirectory);
        Assert.Null(directory.Raw);

        var request = Assert.Single(handler.Requests);
        Assert.Equal(new Uri(JavaRuntimeTestData.ManifestUrl), request.Uri);
        Assert.Equal(JavaRuntimeTestData.UserAgent, request.UserAgent);
    }

    [Fact]
    public async Task GetRuntimeManifest_rejects_a_sha1_mismatch()
    {
        var manifestJson = JavaRuntimeTestData.HappyManifestJson();
        var handler = new FakeHttpMessageHandler();
        handler.EnqueueJson(manifestJson);

        var component = JavaRuntimeTestData.ComponentFor(
            manifestJson,
            sha1: new string('0', 40));

        var exception = await Assert.ThrowsAsync<JavaRuntimeException>(
            () => CreateClient(handler).GetRuntimeManifestAsync(component));

        Assert.Equal(JavaRuntimeErrorCode.HashMismatch, exception.Code);
    }

    [Fact]
    public async Task GetRuntimeManifest_rejects_a_size_mismatch()
    {
        var manifestJson = JavaRuntimeTestData.HappyManifestJson();
        var handler = new FakeHttpMessageHandler();
        handler.EnqueueJson(manifestJson);

        var component = JavaRuntimeTestData.Component(
            JavaRuntimeTestData.Sha1Of(manifestJson),
            Encoding.UTF8.GetByteCount(manifestJson) + 1);

        var exception = await Assert.ThrowsAsync<JavaRuntimeException>(
            () => CreateClient(handler).GetRuntimeManifestAsync(component));

        Assert.Equal(JavaRuntimeErrorCode.HashMismatch, exception.Code);
    }

    [Fact]
    public async Task GetRuntimeManifest_requires_hash_and_size_without_network_access()
    {
        var handler = new FakeHttpMessageHandler();

        var noHash = await Assert.ThrowsAsync<JavaRuntimeException>(
            () => CreateClient(handler).GetRuntimeManifestAsync(
                JavaRuntimeTestData.Component(sha1: null, size: 10)));
        Assert.Equal(JavaRuntimeErrorCode.MissingHash, noHash.Code);

        var noSize = await Assert.ThrowsAsync<JavaRuntimeException>(
            () => CreateClient(handler).GetRuntimeManifestAsync(
                JavaRuntimeTestData.Component(JavaRuntimeTestData.Sha1('a'), size: null)));
        Assert.Equal(JavaRuntimeErrorCode.MissingSize, noSize.Code);

        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Transient_5xx_is_retried()
    {
        var handler = new FakeHttpMessageHandler();
        handler.EnqueueStatus(HttpStatusCode.InternalServerError);
        handler.EnqueueJson(
            JavaRuntimeTestData.CatalogJson(JavaRuntimeTestData.HappyManifestJson()));

        var catalog = await CreateClient(handler, FastRetryOptions()).GetCatalogAsync();

        Assert.True(catalog.HasPlatform(JavaRuntimePlatform.Linux));
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task Malformed_catalog_json_fails_with_invalid_response()
    {
        var handler = new FakeHttpMessageHandler();
        handler.EnqueueJson("{ not json");

        var exception = await Assert.ThrowsAsync<JavaRuntimeException>(
            () => CreateClient(handler).GetCatalogAsync());

        Assert.Equal(JavaRuntimeErrorCode.InvalidResponse, exception.Code);
    }

    private static JavaRuntimeClient CreateClient(
        FakeHttpMessageHandler handler,
        MojangClientOptions? options = null,
        JavaRuntimeCatalogSource? source = null)
        => new(
            new HttpClient(handler),
            options ?? new MojangClientOptions
            {
                UserAgent = JavaRuntimeTestData.UserAgent,
            },
            source);

    private static MojangClientOptions FastRetryOptions()
        => new()
        {
            UserAgent = JavaRuntimeTestData.UserAgent,
            RetryDelay = TimeSpan.FromMilliseconds(1),
            MaxRetryDelay = TimeSpan.FromMilliseconds(5),
        };
}
