using System.Security.Cryptography;
using System.Text;
using Archoera.Application.Accounts;
using Archoera.Infrastructure.Accounts;

namespace Archoera.Tests;

public sealed class AuthlibInjectorTests
{
    [Fact]
    public async Task Client_resolves_metadata_and_authenticates()
    {
        var handler = new FakeHttpMessageHandler();
        handler.EnqueueJson(
            """
            {"meta":{"serverName":"Test Skin","links":{"homepage":"https://example.com"}},"skinDomains":["example.com"]}
            """);
        handler.EnqueueJson(
            """
            {"accessToken":"access-1","clientToken":"client-1","selectedProfile":{"id":"0123456789abcdef0123456789abcdef","name":"Player"}}
            """);
        handler.EnqueueJson(
            """
            {"properties":[{"name":"textures","value":"eyJ0ZXh0dXJlcyI6eyJTS0lOIjp7InVybCI6Imh0dHBzOi8vZXhhbXBsZS5jb20vc2tpbi5wbmcifX19"}]}
            """);
        using var http = new HttpClient(handler);
        var client = new AuthlibInjectorClient(http);

        var metadata = await client.ResolveAsync(
            "https://example.com/api/yggdrasil",
            CancellationToken.None);
        Assert.Equal("Test Skin", metadata.ServerName);
        Assert.Equal("https://example.com/api/yggdrasil", metadata.ApiRoot);

        var session = await client.AuthenticateAsync(
            metadata,
            "player@example.com",
            "secret",
            CancellationToken.None);
        Assert.Equal("access-1", session.AccessToken);
        Assert.Equal("Player", session.Name);
        Assert.Equal("01234567-89ab-cdef-0123-456789abcdef", session.Uuid);
        Assert.Equal("https://example.com/skin.png", session.SkinUrl);
        Assert.Equal(
            "https://example.com/api/yggdrasil/authserver/authenticate",
            handler.Requests[1].Uri.AbsoluteUri);
    }

    [Fact]
    public async Task Client_rejects_non_https_servers_except_loopback()
    {
        var client = new AuthlibInjectorClient(new HttpClient(new FakeHttpMessageHandler()));
        var error = await Assert.ThrowsAsync<AccountAuthException>(
            () => client.ResolveAsync("http://example.com/api/yggdrasil", CancellationToken.None));
        Assert.Equal("AUTHLIB_SERVER_INVALID", error.Code);

        // Loopback http is accepted for local test servers; the request itself
        // fails because the fake handler has no response queued.
        var loopback = new AuthlibInjectorClient(new HttpClient(new FakeHttpMessageHandler()));
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => loopback.ResolveAsync("http://127.0.0.1:25566/api/yggdrasil", CancellationToken.None));
    }

    [Fact]
    public async Task Manager_downloads_verifies_and_caches_the_jar()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "archoera-authlib",
            Guid.NewGuid().ToString("N"));
        try
        {
            var jar = Encoding.UTF8.GetBytes("fake authlib-injector jar");
            var sha = Convert.ToHexString(SHA256.HashData(jar)).ToLowerInvariant();
            var handler = new FakeHttpMessageHandler();
            handler.EnqueueJson(
                "{\"version\":\"1.2.8\"," +
                "\"download_url\":\"https://authlib-injector.yushi.moe/artifact/56/authlib-injector-1.2.8.jar\"," +
                "\"checksums\":{\"sha256\":\"" + sha + "\"}}");
            handler.Enqueue(() => new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(jar),
            });
            using var http = new HttpClient(handler);
            var manager = new AuthlibInjectorManager(
                directory,
                http,
                [new Uri("https://test.local/artifact/latest.json")]);

            var path = await manager.EnsureAsync(CancellationToken.None);
            Assert.True(File.Exists(path));
            Assert.Equal(sha, Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant());
            Assert.Contains("authlib-injector-1.2.8.jar", path, StringComparison.Ordinal);

            // The cached jar is reused; only the metadata request is repeated.
            handler.EnqueueJson(
                "{\"version\":\"1.2.8\"," +
                "\"download_url\":\"https://authlib-injector.yushi.moe/artifact/56/authlib-injector-1.2.8.jar\"," +
                "\"checksums\":{\"sha256\":\"" + sha + "\"}}");
            var again = await manager.EnsureAsync(CancellationToken.None);
            Assert.Equal(path, again);
            Assert.Equal(3, handler.Requests.Count);
        }
        finally
        {
            try
            {
                if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
            }
            catch
            {
                // best effort
            }
        }
    }
}
