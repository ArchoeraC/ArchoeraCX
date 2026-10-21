using System.Net;
using System.Text.Json;
using Archoera.Domain.Accounts;
using Archoera.Infrastructure.Accounts;
using Archoera.Infrastructure.Settings;

namespace Archoera.Tests;

public sealed class AccountAuthTests
{
    private const string Uuid = "11111111222233334444555555555555";

    [Fact]
    public async Task Device_code_flow_runs_the_full_chain_and_never_returns_tokens()
    {
        var directory = TempDirectory();
        try
        {
            var handler = new FakeHttpMessageHandler();
            handler.EnqueueJson(DeviceCodeJson);
            handler.EnqueueJson("""{"error":"authorization_pending"}""", HttpStatusCode.BadRequest);
            handler.EnqueueJson(TokenJson);
            handler.EnqueueJson(XboxJson("xbl-token"));
            handler.EnqueueJson(XboxJson("xsts-token"));
            handler.EnqueueJson("""{"access_token":"mc-access","expires_in":86400}""");
            handler.EnqueueJson(ProfileJson);
            handler.EnqueueJson("""{"keyPair":{"expiresAt":"2026-10-22T00:00:00Z"}}""");

            var store = new JsonAccountStore(directory);
            var auth = new MicrosoftAuthClient(new HttpClient(handler), TestOptions());
            var service = new AccountModuleService(store, store, auth);

            var start = await service.HandleAsync(
                "account.login.microsoft.start",
                "{}",
                CancellationToken.None);
            var startText = System.Text.Encoding.UTF8.GetString(start);
            Assert.DoesNotContain("device_code", startText, StringComparison.Ordinal);
            using (var startDocument = JsonDocument.Parse(start))
            {
                var data = startDocument.RootElement.GetProperty("data");
                Assert.Equal("ABCD-EFGH", data.GetProperty("userCode").GetString());
                Assert.Equal("https://microsoft.com/devicelogin", data.GetProperty("verificationUri").GetString());
                Assert.False(string.IsNullOrWhiteSpace(data.GetProperty("flowId").GetString()));
            }

            using var startJson = JsonDocument.Parse(start);
            var flowId = startJson.RootElement.GetProperty("data").GetProperty("flowId").GetString();
            Assert.NotNull(flowId);

            var poll = await service.HandleAsync(
                "account.login.microsoft.poll",
                $$"""{"flowId":"{{flowId}}"}""",
                CancellationToken.None);
            var pollText = System.Text.Encoding.UTF8.GetString(poll);
            Assert.DoesNotContain("mc-access", pollText, StringComparison.Ordinal);
            Assert.DoesNotContain("ms-refresh", pollText, StringComparison.Ordinal);
            Assert.DoesNotContain("xsts-token", pollText, StringComparison.Ordinal);

            using (var pollDocument = JsonDocument.Parse(poll))
            {
                var account = pollDocument.RootElement.GetProperty("data").GetProperty("account");
                Assert.Equal("Steve", account.GetProperty("displayName").GetString());
                Assert.Equal(Uuid, account.GetProperty("uuid").GetString());
                Assert.True(account.GetProperty("active").GetBoolean());
                Assert.Equal("microsoft", account.GetProperty("kind").GetString());
            }

            var listed = await store.ListAsync();
            var item = Assert.Single(listed);
            Assert.Equal(AccountKind.Microsoft, item.Kind);
            Assert.True(File.Exists(store.CredentialsPath));
            if (!OperatingSystem.IsWindows())
            {
                Assert.Equal(
                    UnixFileMode.UserRead | UnixFileMode.UserWrite,
                    File.GetUnixFileMode(store.CredentialsPath));
            }

            var credential = await store.GetCredentialAsync(item.Id);
            Assert.NotNull(credential);
            Assert.Equal("ms-refresh", credential!.MicrosoftRefreshToken);
            Assert.Equal("mc-access", credential.MinecraftAccessToken);
            Assert.NotNull(credential.CertificatesJson);

            var current = await service.HandleAsync(
                "account.current",
                "{}",
                CancellationToken.None);
            using var currentDocument = JsonDocument.Parse(current);
            Assert.Equal(
                item.Id,
                currentDocument.RootElement.GetProperty("data").GetProperty("account").GetProperty("id").GetString());

            var removed = await service.HandleAsync(
                "account.remove",
                $$"""{"id":"{{item.Id}}"}""",
                CancellationToken.None);
            using var removedDocument = JsonDocument.Parse(removed);
            Assert.True(removedDocument.RootElement.GetProperty("data").GetProperty("deleted").GetBoolean());
            Assert.Empty(await store.ListAsync());
            Assert.Null(await store.GetCredentialAsync(item.Id));
        }
        finally
        {
            Delete(directory);
        }
    }

    [Fact]
    public async Task Missing_client_id_returns_a_stable_configuration_error()
    {
        var directory = TempDirectory();
        try
        {
            var store = new JsonAccountStore(directory);
            var handler = new FakeHttpMessageHandler();
            var auth = new MicrosoftAuthClient(
                new HttpClient(handler),
                TestOptions() with { ClientId = "" });
            var service = new AccountModuleService(store, store, auth);

            var result = await service.HandleAsync(
                "account.login.microsoft.start",
                "{}",
                CancellationToken.None);
            using var document = JsonDocument.Parse(result);
            Assert.False(document.RootElement.GetProperty("ok").GetBoolean());
            Assert.Equal(
                "MICROSOFT_CLIENT_ID_MISSING",
                document.RootElement.GetProperty("error").GetProperty("code").GetString());

            var logPath = Path.Combine(directory, "logs", "account.log");
            Assert.True(File.Exists(logPath));
            var log = await File.ReadAllTextAsync(logPath);
            Assert.Contains("MICROSOFT_CLIENT_ID_MISSING", log, StringComparison.Ordinal);
            Assert.DoesNotContain("device_code", log, StringComparison.Ordinal);
            Assert.DoesNotContain("refresh_token", log, StringComparison.Ordinal);
        }
        finally
        {
            Delete(directory);
        }
    }

    [Fact]
    public async Task Offline_account_is_persisted_and_selected()
    {
        var directory = TempDirectory();
        var store = new JsonAccountStore(directory);
        var service = new AccountModuleService(store, store, auth: null, dataRoot: directory);

        var result = await service.HandleAsync(
            "account.login.offline",
            """{"name":"Steve"}""",
            CancellationToken.None);
        using var document = JsonDocument.Parse(result);
        var account = document.RootElement.GetProperty("data").GetProperty("account");
        Assert.Equal("Steve", account.GetProperty("displayName").GetString());
        Assert.Equal("offline", account.GetProperty("kind").GetString());
        Assert.True(account.GetProperty("active").GetBoolean());
        Assert.Equal(
            "5627dd98-e6be-3c21-b8a8-e92344183641",
            account.GetProperty("uuid").GetString());
        Assert.Single(await store.ListAsync());

        Delete(directory);
    }


    [Fact]
    public async Task Release_policy_requires_a_premium_account_before_offline_login()
    {
        var directory = TempDirectory();
        try
        {
            var store = new JsonAccountStore(directory);
            var service = new AccountModuleService(store, store, auth: null, dataRoot: directory);

            var blocked = await service.HandleAsync(
                "account.login.offline",
                """{"name":"Steve","requirePremium":true}""",
                CancellationToken.None);
            using (var document = JsonDocument.Parse(blocked))
            {
                Assert.False(document.RootElement.GetProperty("ok").GetBoolean());
                Assert.Equal(
                    "PREMIUM_ACCOUNT_REQUIRED",
                    document.RootElement.GetProperty("error").GetProperty("code").GetString());
            }

            await store.SaveAsync(new AccountRecord(
                "ms-premium",
                AccountKind.Microsoft,
                "Premium",
                Uuid,
                "xuid",
                null,
                null,
                false,
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow));

            var allowed = await service.HandleAsync(
                "account.login.offline",
                """{"name":"Steve","requirePremium":true}""",
                CancellationToken.None);
            using var allowedDocument = JsonDocument.Parse(allowed);
            Assert.True(allowedDocument.RootElement.GetProperty("ok").GetBoolean());
            Assert.Equal(
                "offline",
                allowedDocument.RootElement
                    .GetProperty("data")
                    .GetProperty("account")
                    .GetProperty("kind")
                    .GetString());
        }
        finally
        {
            Delete(directory);
        }
    }

    [Fact]
    public async Task Microsoft_only_region_rejects_offline_login()
    {
        var directory = TempDirectory();
        try
        {
            var store = new JsonAccountStore(directory);
            var service = new AccountModuleService(store, store, auth: null, dataRoot: directory);

            var result = await service.HandleAsync(
                "account.login.offline",
                """{"name":"Steve","requireMicrosoft":true}""",
                CancellationToken.None);
            using var document = JsonDocument.Parse(result);
            Assert.False(document.RootElement.GetProperty("ok").GetBoolean());
            Assert.Equal(
                "MICROSOFT_ACCOUNT_ONLY",
                document.RootElement.GetProperty("error").GetProperty("code").GetString());
            Assert.Empty(await store.ListAsync());
        }
        finally
        {
            Delete(directory);
        }
    }

    [Fact]
    public async Task Microsoft_only_region_does_not_switch_to_an_offline_account()
    {
        var directory = TempDirectory();
        try
        {
            var store = new JsonAccountStore(directory);
            var service = new AccountModuleService(store, store, auth: null, dataRoot: directory);
            var first = await service.HandleAsync(
                "account.login.offline",
                """{"name":"FirstPlayer"}""",
                CancellationToken.None);
            var second = await service.HandleAsync(
                "account.login.offline",
                """{"name":"SecondPlayer"}""",
                CancellationToken.None);
            using var firstDocument = JsonDocument.Parse(first);
            using var secondDocument = JsonDocument.Parse(second);
            var firstId = firstDocument.RootElement.GetProperty("data").GetProperty("account").GetProperty("id").GetString();
            var secondId = secondDocument.RootElement.GetProperty("data").GetProperty("account").GetProperty("id").GetString();
            Assert.NotNull(firstId);
            Assert.NotNull(secondId);
            await store.SetActiveAsync(firstId!);

            var result = await service.HandleAsync(
                "account.select",
                $$"""{"id":"{{secondId}}","requireMicrosoft":true}""",
                CancellationToken.None);
            using var document = JsonDocument.Parse(result);
            Assert.False(document.RootElement.GetProperty("ok").GetBoolean());
            Assert.Equal(
                "MICROSOFT_ACCOUNT_ONLY",
                document.RootElement.GetProperty("error").GetProperty("code").GetString());
            Assert.Equal(firstId, (await store.GetActiveAsync())!.Id);
        }
        finally
        {
            Delete(directory);
        }
    }

    [Fact]
    public async Task Removing_the_active_account_selects_another_account()
    {
        var directory = TempDirectory();
        try
        {
            var store = new JsonAccountStore(directory);
            var service = new AccountModuleService(store, store, auth: null, dataRoot: directory);
            var first = await service.HandleAsync(
                "account.login.offline",
                """{"name":"FirstPlayer"}""",
                CancellationToken.None);
            var second = await service.HandleAsync(
                "account.login.offline",
                """{"name":"SecondPlayer"}""",
                CancellationToken.None);
            using var firstDocument = JsonDocument.Parse(first);
            using var secondDocument = JsonDocument.Parse(second);
            var firstId = firstDocument.RootElement.GetProperty("data").GetProperty("account").GetProperty("id").GetString();
            var secondId = secondDocument.RootElement.GetProperty("data").GetProperty("account").GetProperty("id").GetString();
            Assert.NotNull(firstId);
            Assert.NotNull(secondId);

            await store.SetActiveAsync(firstId!);
            await service.HandleAsync(
                "account.remove",
                $$"""{"id":"{{firstId}}"}""",
                CancellationToken.None);

            var active = await store.GetActiveAsync();
            Assert.NotNull(active);
            Assert.Equal(secondId, active!.Id);
        }
        finally
        {
            Delete(directory);
        }
    }

    [Fact]
    public async Task Xsts_errors_are_mapped_to_a_user_actionable_code()
    {
        var directory = TempDirectory();
        try
        {
            var handler = new FakeHttpMessageHandler();
            handler.EnqueueJson(DeviceCodeJson);
            handler.EnqueueJson(TokenJson);
            handler.EnqueueJson(XboxJson("xbl-token"));
            handler.Enqueue(() =>
            {
                var response = new HttpResponseMessage(HttpStatusCode.Unauthorized)
                {
                    Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json"),
                };
                response.Headers.TryAddWithoutValidation("XErr", "2148916238");
                return response;
            });

            var store = new JsonAccountStore(directory);
            var auth = new MicrosoftAuthClient(new HttpClient(handler), TestOptions());
            var service = new AccountModuleService(store, store, auth);
            var start = await service.HandleAsync(
                "account.login.microsoft.start",
                "{}",
                CancellationToken.None);
            using var startDocument = JsonDocument.Parse(start);
            var flowId = startDocument.RootElement.GetProperty("data").GetProperty("flowId").GetString();

            var poll = await service.HandleAsync(
                "account.login.microsoft.poll",
                $$"""{"flowId":"{{flowId}}"}""",
                CancellationToken.None);
            using var pollDocument = JsonDocument.Parse(poll);
            Assert.False(pollDocument.RootElement.GetProperty("ok").GetBoolean());
            Assert.Equal(
                "XSTS_CHILD_ACCOUNT",
                pollDocument.RootElement.GetProperty("error").GetProperty("code").GetString());
            Assert.Empty(await store.ListAsync());
        }
        finally
        {
            Delete(directory);
        }
    }

    [Fact]
    public async Task Refresh_replaces_tokens_and_keeps_the_account_id()
    {
        var directory = TempDirectory();
        try
        {
            var handler = new FakeHttpMessageHandler();
            handler.EnqueueJson(DeviceCodeJson);
            handler.EnqueueJson(TokenJson);
            handler.EnqueueJson(XboxJson("xbl-token"));
            handler.EnqueueJson(XboxJson("xsts-token"));
            handler.EnqueueJson("""{"access_token":"mc-access","expires_in":86400}""");
            handler.EnqueueJson(ProfileJson);
            handler.EnqueueJson("""{"keyPair":{"expiresAt":"2026-10-22T00:00:00Z"}}""");

            var store = new JsonAccountStore(directory);
            var auth = new MicrosoftAuthClient(new HttpClient(handler), TestOptions());
            var service = new AccountModuleService(store, store, auth);
            var start = await service.HandleAsync(
                "account.login.microsoft.start",
                "{}",
                CancellationToken.None);
            using var startDocument = JsonDocument.Parse(start);
            var flowId = startDocument.RootElement.GetProperty("data").GetProperty("flowId").GetString();
            await service.HandleAsync(
                "account.login.microsoft.poll",
                $$"""{"flowId":"{{flowId}}"}""",
                CancellationToken.None);
            var account = Assert.Single(await store.ListAsync());

            handler.EnqueueJson(
                """{"access_token":"ms-access-2","refresh_token":"ms-refresh-2","expires_in":3600,"scope":"XboxLive.signin offline_access","token_type":"Bearer"}""");
            handler.EnqueueJson(XboxJson("xbl-token-2"));
            handler.EnqueueJson(XboxJson("xsts-token-2"));
            handler.EnqueueJson("""{"access_token":"mc-access-2","expires_in":86400}""");
            handler.EnqueueJson(ProfileJson);
            handler.EnqueueJson("""{"keyPair":{"expiresAt":"2026-10-23T00:00:00Z"}}""");

            var refreshed = await service.HandleAsync(
                "account.refresh",
                $$"""{"id":"{{account.Id}}"}""",
                CancellationToken.None);
            using var refreshedDocument = JsonDocument.Parse(refreshed);
            Assert.True(refreshedDocument.RootElement.GetProperty("ok").GetBoolean());
            Assert.Equal(
                account.Id,
                refreshedDocument.RootElement.GetProperty("data").GetProperty("account").GetProperty("id").GetString());

            var credential = await store.GetCredentialAsync(account.Id);
            Assert.Equal("ms-refresh-2", credential!.MicrosoftRefreshToken);
            Assert.Equal("mc-access-2", credential.MinecraftAccessToken);
        }
        finally
        {
            Delete(directory);
        }
    }

    private static MicrosoftAuthOptions TestOptions()
        => new()
        {
            ClientId = "test-client",
            LoginBase = new Uri("https://login.test/tenant/oauth2/v2.0/"),
            XboxLiveEndpoint = new Uri("https://xbl.test/user/authenticate"),
            XstsEndpoint = new Uri("https://xsts.test/xsts/authorize"),
            MinecraftLoginEndpoint = new Uri("https://mc.test/authentication/login_with_xbox"),
            MinecraftProfileEndpoint = new Uri("https://mc.test/minecraft/profile"),
            MinecraftCertificatesEndpoint = new Uri("https://mc.test/player/certificates"),
        };

    private static string XboxJson(string token)
        => "{\"Token\":\"" + token + "\",\"DisplayClaims\":{\"xui\":[{\"uhs\":\"user-hash\"}]}}";

    private const string DeviceCodeJson =
        """{"device_code":"device-secret","user_code":"ABCD-EFGH","verification_uri":"https://microsoft.com/devicelogin","expires_in":900,"interval":1}""";

    private const string TokenJson =
        """{"access_token":"ms-access","refresh_token":"ms-refresh","expires_in":3600,"scope":"XboxLive.signin offline_access","token_type":"Bearer"}""";

    private const string ProfileJson =
        """{"id":"11111111222233334444555555555555","name":"Steve","skins":[{"url":"https://textures.test/skin","state":"ACTIVE"}],"capes":[{"url":"https://textures.test/cape","state":"ACTIVE"}]}""";

    private static string TempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "archoera-account-" + Guid.NewGuid().ToString("N"));
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
