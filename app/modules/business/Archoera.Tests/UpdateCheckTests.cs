using System.Net;
using System.Text.Json;
using Archoera.Infrastructure.Update;

namespace Archoera.Tests;

/// <summary>
/// Offline coverage for <c>update.check</c>: the GitHub response is replayed
/// by <see cref="FakeHttpMessageHandler"/>, tests never touch the network.
/// </summary>
public sealed class UpdateCheckTests
{
    private const string LatestReleaseUrl =
        "https://api.github.com/repos/BetaStudio2/ArchoeraCX/releases/latest";

    [Fact]
    public async Task Newer_release_is_reported_with_release_metadata()
    {
        var handler = new FakeHttpMessageHandler();
        handler.EnqueueJson(ReleaseJson(
            tag: "0.0.2",
            name: "ArchoeraCX 0.0.2",
            body: new string('x', 5000)));

        using var document = JsonDocument.Parse(
            await CreateService(handler).HandleAsync("update.check", "{}", CancellationToken.None));

        Assert.True(document.RootElement.GetProperty("ok").GetBoolean());
        var data = document.RootElement.GetProperty("data");
        Assert.Equal("0.0.1+1", data.GetProperty("current").GetString());
        Assert.Equal("0.0.2", data.GetProperty("latest").GetString());
        Assert.True(data.GetProperty("hasUpdate").GetBoolean());
        Assert.Equal(
            "https://github.com/BetaStudio2/ArchoeraCX/releases/tag/0.0.2",
            data.GetProperty("url").GetString());
        Assert.Equal("ArchoeraCX 0.0.2", data.GetProperty("name").GetString());
        Assert.Equal("2026-10-20T12:00:00Z", data.GetProperty("publishedAt").GetString());
        // Release bodies are truncated to the contract limit.
        Assert.Equal(4000, data.GetProperty("notes").GetString()!.Length);

        var request = Assert.Single(handler.Requests);
        Assert.Equal(new Uri(LatestReleaseUrl), request.Uri);
        Assert.Equal("GET", request.Method);
        Assert.Equal(
            "ArchoeraCX/0.0.1+1 (https://github.com/BetaStudio2/ArchoeraCX)",
            request.UserAgent);
    }

    [Fact]
    public async Task Same_version_is_not_an_update()
    {
        var handler = new FakeHttpMessageHandler();
        handler.EnqueueJson(ReleaseJson(tag: "v0.0.1"));

        using var document = JsonDocument.Parse(
            await CreateService(handler).HandleAsync("update.check", "{}", CancellationToken.None));

        Assert.True(document.RootElement.GetProperty("ok").GetBoolean());
        var data = document.RootElement.GetProperty("data");
        Assert.Equal("0.0.1", data.GetProperty("latest").GetString());
        Assert.False(data.GetProperty("hasUpdate").GetBoolean());
    }

    [Fact]
    public async Task Leading_v_prefix_is_stripped_before_comparison()
    {
        var handler = new FakeHttpMessageHandler();
        handler.EnqueueJson(ReleaseJson(tag: "v0.1.0"));

        using var document = JsonDocument.Parse(
            await CreateService(handler).HandleAsync("update.check", "{\"ignored\":true}", CancellationToken.None));

        var data = document.RootElement.GetProperty("data");
        Assert.Equal("0.1.0", data.GetProperty("latest").GetString());
        Assert.True(data.GetProperty("hasUpdate").GetBoolean());
        Assert.DoesNotContain("v0.1.0", data.GetProperty("latest").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Http_500_maps_to_network_error()
    {
        var handler = new FakeHttpMessageHandler();
        handler.EnqueueStatus(HttpStatusCode.InternalServerError);

        using var document = JsonDocument.Parse(
            await CreateService(handler).HandleAsync("update.check", "{}", CancellationToken.None));

        Assert.False(document.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal(
            "E_NETWORK",
            document.RootElement.GetProperty("error").GetProperty("code").GetString());
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task Malformed_json_maps_to_invalid_error()
    {
        var handler = new FakeHttpMessageHandler();
        handler.EnqueueJson("{ not json");

        using var document = JsonDocument.Parse(
            await CreateService(handler).HandleAsync("update.check", "{}", CancellationToken.None));

        Assert.False(document.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal(
            "E_INVALID",
            document.RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    private static UpdateModuleService CreateService(FakeHttpMessageHandler handler)
        => new(new HttpClient(handler));

    [Fact]
    public async Task Missing_latest_release_falls_back_to_the_release_list()
    {
        var handler = new FakeHttpMessageHandler();
        handler.EnqueueStatus(HttpStatusCode.NotFound);
        handler.EnqueueJson("[" + ReleaseJson(tag: "v0.0.2") + "]");

        using var document = JsonDocument.Parse(
            await CreateService(handler).HandleAsync("update.check", "{}", CancellationToken.None));

        Assert.True(document.RootElement.GetProperty("ok").GetBoolean());
        var data = document.RootElement.GetProperty("data");
        Assert.Equal("0.0.2", data.GetProperty("latest").GetString());
        Assert.True(data.GetProperty("hasUpdate").GetBoolean());
        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal(
            "https://api.github.com/repos/BetaStudio2/ArchoeraCX/releases?per_page=1",
            handler.Requests[1].Uri!.ToString());
    }

    [Fact]
    public async Task Empty_release_list_reads_as_up_to_date()
    {
        var handler = new FakeHttpMessageHandler();
        handler.EnqueueStatus(HttpStatusCode.NotFound);
        handler.EnqueueJson("[]");

        using var document = JsonDocument.Parse(
            await CreateService(handler).HandleAsync("update.check", "{}", CancellationToken.None));

        Assert.True(document.RootElement.GetProperty("ok").GetBoolean());
        var data = document.RootElement.GetProperty("data");
        Assert.Equal("0.0.1+1", data.GetProperty("latest").GetString());
        Assert.False(data.GetProperty("hasUpdate").GetBoolean());
        Assert.Equal(
            "https://github.com/BetaStudio2/ArchoeraCX/releases",
            data.GetProperty("url").GetString());
    }

    private static string ReleaseJson(
        string tag,
        string? name = "release",
        string? body = "notes")
        => JsonSerializer.Serialize(new
        {
            tag_name = tag,
            name,
            body,
            html_url = $"https://github.com/BetaStudio2/ArchoeraCX/releases/tag/{tag}",
            published_at = "2026-10-20T12:00:00Z",
        });
}
