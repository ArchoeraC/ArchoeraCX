using System.Net;
using System.Net.Http.Headers;
using Archoera.Application.Loaders;
using Archoera.Infrastructure.Loaders;

namespace Archoera.Tests;

public sealed class MavenSidecarClientTests
{
    private static readonly Uri InstallerUri =
        new(ForgeInstallerFixture.ForgeInstallerUrl);

    [Fact]
    public async Task GetMetadataAsync_reads_the_sha1_sidecar_and_head_size()
    {
        var handler = new FakeHttpMessageHandler();
        handler.Enqueue(() => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                $"{ForgeInstallerFixture.Sha('a').ToUpperInvariant()}\r\n"),
        });
        handler.Enqueue(() => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(new byte[123]),
        });

        var metadata = await CreateClient(handler).GetMetadataAsync(InstallerUri);

        Assert.Equal(ForgeInstallerFixture.Sha('a'), metadata.Sha1);
        Assert.Equal(123, metadata.Size);

        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal("GET", handler.Requests[0].Method);
        Assert.EndsWith(".jar.sha1", handler.Requests[0].Uri.AbsolutePath, StringComparison.Ordinal);
        Assert.Equal("HEAD", handler.Requests[1].Method);
        Assert.Equal(InstallerUri.AbsolutePath, handler.Requests[1].Uri.AbsolutePath);
    }

    [Fact]
    public async Task Missing_sidecar_maps_to_not_found()
    {
        var handler = new FakeHttpMessageHandler();
        handler.EnqueueStatus(HttpStatusCode.NotFound);

        var exception = await Assert.ThrowsAsync<ForgeDownloadException>(
            () => CreateClient(handler).GetSha1Async(InstallerUri));

        Assert.Equal(ForgeDownloadErrorCode.NotFound, exception.Code);
        Assert.Equal(404, exception.StatusCode);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task Malformed_sidecar_maps_to_invalid_response()
    {
        var handler = new FakeHttpMessageHandler();
        handler.EnqueueJson("not-a-sha1");

        var exception = await Assert.ThrowsAsync<ForgeDownloadException>(
            () => CreateClient(handler).GetSha1Async(InstallerUri));

        Assert.Equal(ForgeDownloadErrorCode.InvalidResponse, exception.Code);
    }

    [Fact]
    public async Task Head_without_content_length_falls_back_to_a_range_get()
    {
        var handler = new FakeHttpMessageHandler();
        handler.Enqueue(() => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(ForgeInstallerFixture.Sha('a')),
        });
        handler.Enqueue(() =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("ignored"),
            };
            response.Content.Headers.ContentLength = null;
            return response;
        });
        handler.Enqueue(() =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.PartialContent)
            {
                Content = new ByteArrayContent([0]),
            };
            response.Content.Headers.ContentRange = new ContentRangeHeaderValue(0, 0, 777);
            return response;
        });

        var metadata = await CreateClient(handler).GetMetadataAsync(InstallerUri);

        Assert.Equal(777, metadata.Size);
        Assert.Equal("HEAD", handler.Requests[1].Method);
        Assert.Equal("GET", handler.Requests[2].Method);
    }

    [Fact]
    public async Task Head_method_not_allowed_falls_back_to_a_range_get()
    {
        var handler = new FakeHttpMessageHandler();
        handler.Enqueue(() => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(ForgeInstallerFixture.Sha('b')),
        });
        handler.Enqueue(() => new HttpResponseMessage(HttpStatusCode.MethodNotAllowed));
        handler.Enqueue(() =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.PartialContent)
            {
                Content = new ByteArrayContent([0]),
            };
            response.Content.Headers.ContentRange = new ContentRangeHeaderValue(0, 0, 42);
            return response;
        });

        var metadata = await CreateClient(handler).GetMetadataAsync(InstallerUri);

        Assert.Equal(42, metadata.Size);
    }

    [Fact]
    public async Task Missing_size_everywhere_maps_to_missing_size()
    {
        var handler = new FakeHttpMessageHandler();
        handler.Enqueue(() => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(ForgeInstallerFixture.Sha('c')),
        });
        handler.Enqueue(() =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("ignored"),
            };
            response.Content.Headers.ContentLength = null;
            return response;
        });
        handler.Enqueue(() =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("ignored"),
            };
            response.Content.Headers.ContentLength = null;
            return response;
        });

        var exception = await Assert.ThrowsAsync<ForgeDownloadException>(
            () => CreateClient(handler).GetMetadataAsync(InstallerUri));

        Assert.Equal(ForgeDownloadErrorCode.MissingSize, exception.Code);
    }

    [Fact]
    public async Task Unexpected_http_status_maps_to_invalid_response()
    {
        var handler = new FakeHttpMessageHandler();
        handler.EnqueueStatus(HttpStatusCode.InternalServerError);

        var exception = await Assert.ThrowsAsync<ForgeDownloadException>(
            () => CreateClient(handler).GetSha1Async(InstallerUri));

        Assert.Equal(ForgeDownloadErrorCode.InvalidResponse, exception.Code);
        Assert.Equal(500, exception.StatusCode);
    }

    [Fact]
    public async Task Transport_failure_maps_to_network_error()
    {
        var handler = new FakeHttpMessageHandler();
        handler.Enqueue(() => throw new HttpRequestException("boom"));

        var exception = await Assert.ThrowsAsync<ForgeDownloadException>(
            () => CreateClient(handler).GetSha1Async(InstallerUri));

        Assert.Equal(ForgeDownloadErrorCode.NetworkError, exception.Code);
    }

    private static MavenSidecarClient CreateClient(FakeHttpMessageHandler handler)
        => new(new HttpClient(handler));
}
