using System.Text.Json;
using Archoera.Application.Instances;
using Archoera.Domain.Accounts;
using Archoera.Domain.Instances;
using Archoera.Infrastructure.Accounts;
using Archoera.Infrastructure.Launch;

namespace Archoera.Tests;

/// <summary>
/// Online launch readiness and error mapping. These tests never perform a
/// network request; the refresh path is covered separately by auth tests.
/// </summary>
public sealed class LaunchModuleOnlineTests
{
    private const string Uuid = "11111111222233334444555555555555";

    [Fact]
    public async Task Prepare_reports_online_ready_when_a_valid_credential_exists()
    {
        var directory = TempDirectory();
        try
        {
            var (service, store, handler) = CreateService(directory);
            var account = await SaveMicrosoftAccountAsync(store);
            await store.SaveCredentialAsync(new AccountCredential(
                account.Id,
                "ms-refresh",
                "mc-access",
                DateTimeOffset.UtcNow.AddHours(1),
                null,
                DateTimeOffset.UtcNow));

            var result = await service.HandleAsync(
                "launch.prepare",
                """{"instanceId":"instance","mode":"online"}""",
                CancellationToken.None);

            using var document = JsonDocument.Parse(result);
            var data = document.RootElement.GetProperty("data");
            Assert.True(data.GetProperty("ready").GetBoolean());
            Assert.Equal("online", data.GetProperty("mode").GetString());
            Assert.Equal("microsoft", data.GetProperty("accountKind").GetString());
            Assert.Empty(handler.Requests);
        }
        finally
        {
            Delete(directory);
        }
    }

    [Fact]
    public async Task Prepare_reports_auth_required_when_credentials_are_missing()
    {
        var directory = TempDirectory();
        try
        {
            var (service, store, _) = CreateService(directory);
            await SaveMicrosoftAccountAsync(store);

            var result = await service.HandleAsync(
                "launch.prepare",
                """{"instanceId":"instance","mode":"online"}""",
                CancellationToken.None);

            using var document = JsonDocument.Parse(result);
            var data = document.RootElement.GetProperty("data");
            Assert.False(data.GetProperty("ready").GetBoolean());
            Assert.Equal("AUTH_REQUIRED", data.GetProperty("reason").GetString());
        }
        finally
        {
            Delete(directory);
        }
    }

    [Fact]
    public async Task Online_start_without_credentials_returns_auth_required()
    {
        var directory = TempDirectory();
        try
        {
            var (service, store, handler) = CreateService(directory);
            await SaveMicrosoftAccountAsync(store);

            var result = await service.HandleAsync(
                "launch.start",
                """{"instanceId":"instance","mode":"online"}""",
                CancellationToken.None);

            using var document = JsonDocument.Parse(result);
            Assert.False(document.RootElement.GetProperty("ok").GetBoolean());
            Assert.Equal(
                "AUTH_REQUIRED",
                document.RootElement.GetProperty("error").GetProperty("code").GetString());
            Assert.Empty(handler.Requests);
        }
        finally
        {
            Delete(directory);
        }
    }

    [Fact]
    public async Task Online_start_without_client_id_returns_configuration_error()
    {
        var directory = TempDirectory();
        try
        {
            var (service, store, _) = CreateService(directory, clientId: "");
            var account = await SaveMicrosoftAccountAsync(store);
            await store.SaveCredentialAsync(new AccountCredential(
                account.Id,
                "ms-refresh",
                "mc-access",
                DateTimeOffset.UtcNow.AddHours(1),
                null,
                DateTimeOffset.UtcNow));

            var result = await service.HandleAsync(
                "launch.start",
                """{"instanceId":"instance","mode":"online"}""",
                CancellationToken.None);

            using var document = JsonDocument.Parse(result);
            Assert.False(document.RootElement.GetProperty("ok").GetBoolean());
            Assert.Equal(
                "MICROSOFT_CLIENT_ID_MISSING",
                document.RootElement.GetProperty("error").GetProperty("code").GetString());
        }
        finally
        {
            Delete(directory);
        }
    }

    [Fact]
    public async Task Offline_start_with_a_microsoft_account_is_rejected()
    {
        var directory = TempDirectory();
        try
        {
            var (service, store, _) = CreateService(directory);
            await SaveMicrosoftAccountAsync(store);

            var result = await service.HandleAsync(
                "launch.start",
                """{"instanceId":"instance"}""",
                CancellationToken.None);

            using var document = JsonDocument.Parse(result);
            Assert.False(document.RootElement.GetProperty("ok").GetBoolean());
            Assert.Equal(
                "AUTH_REQUIRED",
                document.RootElement.GetProperty("error").GetProperty("code").GetString());
        }
        finally
        {
            Delete(directory);
        }
    }

    [Fact]
    public async Task Invalid_mode_returns_invalid()
    {
        var directory = TempDirectory();
        try
        {
            var (service, _, _) = CreateService(directory);

            var result = await service.HandleAsync(
                "launch.start",
                """{"instanceId":"instance","mode":"paper"}""",
                CancellationToken.None);

            using var document = JsonDocument.Parse(result);
            Assert.False(document.RootElement.GetProperty("ok").GetBoolean());
            Assert.Equal(
                "E_INVALID",
                document.RootElement.GetProperty("error").GetProperty("code").GetString());
        }
        finally
        {
            Delete(directory);
        }
    }

    private static (LaunchModuleService Service, JsonAccountStore Store, FakeHttpMessageHandler Handler)
        CreateService(string directory, string clientId = "test-client")
    {
        var store = new JsonAccountStore(directory);
        var handler = new FakeHttpMessageHandler();
        var auth = new MicrosoftAuthClient(
            new HttpClient(handler),
            new MicrosoftAuthOptions { ClientId = clientId });
        var instance = new GameInstanceRecord(
            "instance",
            "Instance",
            "1.21.1",
            "vanilla",
            string.Empty,
            Path.Combine(directory, "instance"),
            0,
            false,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            null);
        var service = new LaunchModuleService(
            instances: new StubInstances(instance),
            dataRoot: directory,
            accounts: store,
            credentials: store,
            auth: auth);
        return (service, store, handler);
    }

    private static async Task<AccountRecord> SaveMicrosoftAccountAsync(JsonAccountStore store)
    {
        var now = DateTimeOffset.UtcNow;
        var account = new AccountRecord(
            "ms-account",
            AccountKind.Microsoft,
            "Steve",
            Uuid,
            "xuid",
            null,
            null,
            true,
            now,
            now,
            null);
        return await store.SaveAsync(account);
    }

    private static string TempDirectory()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            "archoera-launch-online-" + Guid.NewGuid().ToString("N"));
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

    private sealed class StubInstances(GameInstanceRecord instance) : IInstanceRepository
    {
        public Task<IReadOnlyList<GameInstanceRecord>> ListAsync(
            CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<GameInstanceRecord>>([instance]);

        public Task<GameInstanceRecord> CreateAsync(
            string name,
            string gameVersion,
            string loader,
            string loaderVersion,
            string? rootPath,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<GameInstanceRecord?> UpdateAsync(
            string id,
            string? name,
            bool? favorite,
            DateTimeOffset? lastPlayedAt,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<bool> DeleteAsync(
            string id,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<InstanceSyncReport> SyncAsync(
            IReadOnlyList<string>? roots,
            bool dryRun,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }
}
