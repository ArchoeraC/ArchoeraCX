using Archoera.Application.Downloads;

namespace Archoera.Tests;

public sealed class DownloadProxyResolverTests
{
    [Fact]
    public void Reads_the_proxy_and_bypass_list_from_the_environment()
    {
        var oldProxy = Environment.GetEnvironmentVariable("HTTPS_PROXY");
        var oldNoProxy = Environment.GetEnvironmentVariable("NO_PROXY");
        try
        {
            Environment.SetEnvironmentVariable("HTTPS_PROXY", "127.0.0.1:7890");
            Environment.SetEnvironmentVariable("NO_PROXY", "localhost, .lan,10.0.0.0/8");

            var proxy = DownloadProxyResolver.Resolve();

            Assert.NotNull(proxy);
            Assert.Equal("http://127.0.0.1:7890/", proxy!.Url);
            Assert.Contains("localhost", proxy.NoProxy);
            Assert.Contains(".lan", proxy.NoProxy);
            Assert.Contains("10.0.0.0/8", proxy.NoProxy);
        }
        finally
        {
            Environment.SetEnvironmentVariable("HTTPS_PROXY", oldProxy);
            Environment.SetEnvironmentVariable("NO_PROXY", oldNoProxy);
        }
    }

    [Fact]
    public void Manual_proxy_from_settings_is_normalized()
    {
        var proxy = DownloadProxyResolver.FromSettings("127.0.0.1:7890");
        Assert.NotNull(proxy);
        Assert.Equal("http://127.0.0.1:7890/", proxy!.Url);

        var socks = DownloadProxyResolver.FromSettings("socks5h://127.0.0.1:7890");
        Assert.NotNull(socks);
        Assert.Equal("socks5h://127.0.0.1:7890/", socks!.Url);

        Assert.Null(DownloadProxyResolver.FromSettings("ftp://127.0.0.1:1080"));
        Assert.Null(DownloadProxyResolver.FromSettings("   "));
    }

    [Theory]
    [InlineData(
        "function FindProxyForURL(url, host) { return \"PROXY 127.0.0.1:7890; SOCKS5 127.0.0.1:7890; DIRECT;\"; }",
        "http://127.0.0.1:7890/")]
    [InlineData(
        "function FindProxyForURL(url, host) { return \"SOCKS5 127.0.0.1:1080; DIRECT;\"; }",
        "socks5h://127.0.0.1:1080/")]
    [InlineData("function FindProxyForURL(url, host) { return \"DIRECT\"; }", null)]
    public void Pac_scripts_yield_the_first_usable_endpoint(string script, string? expected)
    {
        Assert.Equal(expected, DownloadProxyResolver.ParsePac(script));
    }

    [Fact]
    public void Pac_endpoint_respects_socks5_ordering()
    {
        // DIRECT first: the endpoint after it is still a proxy, not a bypass.
        var parsed = DownloadProxyResolver.ParsePac(
            "return \"DIRECT; PROXY 10.0.0.2:3128\";");
        Assert.Equal("http://10.0.0.2:3128/", parsed);
    }
}
