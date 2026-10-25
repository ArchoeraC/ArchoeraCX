using System.Net;
using Archoera.Infrastructure.Modules;

namespace Archoera.Tests;

public sealed class RegionPolicyTests
{
    [Fact]
    public async Task China_override_disables_microsoft_only()
    {
        var policy = new RegionPolicy(regionOverride: "CN");
        var state = await policy.ReadAsync();
        Assert.Equal("CN", state.Region);
        Assert.False(state.MicrosoftOnly);
        Assert.Equal("override", state.Source);
    }

    [Fact]
    public async Task Non_china_override_requires_microsoft_only()
    {
        var policy = new RegionPolicy(regionOverride: "GLOBAL");
        var state = await policy.ReadAsync();
        Assert.Equal("GLOBAL", state.Region);
        Assert.True(state.MicrosoftOnly);
    }

    [Fact]
    public async Task Offline_locale_fallback_always_resolves()
    {
        using var http = new HttpClient(new FailingHandler());
        var policy = new RegionPolicy(http);

        var state = await policy.ReadAsync();

        Assert.Contains(
            state.Region,
            new[] { RegionPolicy.China, RegionPolicy.Global });
        Assert.Equal("locale", state.Source);
    }

    private sealed class FailingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
            => Task.FromException<HttpResponseMessage>(
                new HttpRequestException("offline"));
    }
}
