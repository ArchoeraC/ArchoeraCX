using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Archoera.Infrastructure.Accounts;

namespace Archoera.Tests;

public sealed class AuthlibAccountLoginTests
{
    [Fact]
    public async Task Login_requires_terms_before_any_request()
    {
        var directory = NewDirectory();
        try
        {
            var store = new JsonAccountStore(Path.Combine(directory, "data"));
            var handler = new FakeHttpMessageHandler();
            using var http = new HttpClient(handler);
            var service = new AccountModuleService(
                accounts: store,
                auth: null,
                credentials: store,
                dataRoot: Path.Combine(directory, "data"),
                authlib: new AuthlibInjectorClient(http),
                injector: new AuthlibInjectorManager(
                    Path.Combine(directory, "data"),
                    http,
                    [new Uri("https://test.local/latest.json")]));

            using var document = JsonDocument.Parse(
                await service.HandleAsync(
                    "account.login.authlib",
                    """
                    {"server":"https://example.com/api/yggdrasil","username":"user","password":"pw","termsUrl":"https://example.com/terms","acceptedTerms":false}
                    """,
                    CancellationToken.None));

            Assert.False(document.RootElement.GetProperty("ok").GetBoolean());
            Assert.Equal(
                "AUTHLIB_TERMS_REQUIRED",
                document.RootElement.GetProperty("error").GetProperty("code").GetString());
            Assert.Empty(handler.Requests);
            Assert.Empty(await store.ListAsync());
        }
        finally
        {
            Delete(directory);
        }
    }

    [Fact]
    public async Task Login_downloads_the_jar_and_stores_the_yggdrasil_session()
    {
        var directory = NewDirectory();
        try
        {
            var dataRoot = Path.Combine(directory, "data");
            var store = new JsonAccountStore(dataRoot);
            var jar = Encoding.UTF8.GetBytes("authlib jar");
            var sha = Convert.ToHexString(SHA256.HashData(jar)).ToLowerInvariant();
            var handler = new FakeHttpMessageHandler();
            handler.EnqueueJson(
                """
                {"meta":{"serverName":"Test Skin","links":{"homepage":"https://example.com"}}}
                """);
            handler.EnqueueJson(
                "{\"version\":\"1.2.8\"," +
                "\"download_url\":\"https://authlib-injector.yushi.moe/artifact/56/authlib-injector-1.2.8.jar\"," +
                "\"checksums\":{\"sha256\":\"" + sha + "\"}}");
            handler.Enqueue(() => new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(jar),
            });
            handler.EnqueueJson(
                """
                {"accessToken":"access-1","clientToken":"client-1","selectedProfile":{"id":"0123456789abcdef0123456789abcdef","name":"Player"}}
                """);
            handler.EnqueueStatus(System.Net.HttpStatusCode.NotFound);
            using var http = new HttpClient(handler);
            var service = new AccountModuleService(
                accounts: store,
                auth: null,
                credentials: store,
                dataRoot: dataRoot,
                authlib: new AuthlibInjectorClient(http),
                injector: new AuthlibInjectorManager(
                    dataRoot,
                    http,
                    [new Uri("https://test.local/latest.json")]));

            using var document = JsonDocument.Parse(
                await service.HandleAsync(
                    "account.login.authlib",
                    """
                    {"server":"https://example.com/api/yggdrasil","username":"user","password":"pw","termsUrl":"https://example.com/terms","acceptedTerms":true}
                    """,
                    CancellationToken.None));

            Assert.True(document.RootElement.GetProperty("ok").GetBoolean());
            var account = document.RootElement.GetProperty("data").GetProperty("account");
            Assert.Equal("authlib", account.GetProperty("kind").GetString());
            Assert.Equal("Player", account.GetProperty("displayName").GetString());
            Assert.Equal(
                "01234567-89ab-cdef-0123-456789abcdef",
                account.GetProperty("uuid").GetString());
            Assert.Equal(
                "https://example.com/api/yggdrasil",
                account.GetProperty("authlibServer").GetString());

            var credential = await store.GetCredentialAsync(
                account.GetProperty("id").GetString()!);
            Assert.NotNull(credential);
            Assert.Equal("access-1", credential!.AuthlibAccessToken);
            Assert.Equal("client-1", credential.AuthlibClientToken);
            Assert.NotNull(credential.AuthlibTermsAcceptedAt);
            Assert.Equal("https://example.com/terms", credential.AuthlibTermsUrl);
            Assert.True(
                File.Exists(
                    Path.Combine(
                        dataRoot,
                        "authlib-injector",
                        "authlib-injector-1.2.8.jar")));
        }
        finally
        {
            Delete(directory);
        }
    }

    private static string NewDirectory()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            "archoera-authlib-login",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void Delete(string path)
    {
        try
        {
            if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
        }
        catch
        {
            // best effort
        }
    }
}
