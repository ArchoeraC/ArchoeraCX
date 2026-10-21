using Archoera.Domain.Accounts;
using Archoera.Infrastructure.Accounts;

namespace Archoera.Tests;

public sealed class VaultCredentialStoreTests
{
    [Fact]
    public async Task Vault_round_trips_credentials_and_removes_legacy_file()
    {
        var directory = TempDirectory();
        try
        {
            var dataRoot = Path.Combine(directory, "data");
            Directory.CreateDirectory(dataRoot);
            var legacy = Path.Combine(dataRoot, "accounts.credentials.json");
            await File.WriteAllTextAsync(
                legacy,
                """
                {"schemaVersion":1,"items":[{"accountId":"legacy","microsoftRefreshToken":"old"}]}
                """);

            var store = new VaultCredentialStore(
                dataRoot,
                VaultBinary());
            await store.SaveCredentialAsync(
                new AccountCredential(
                    "ms-1",
                    "refresh-token",
                    "access-token",
                    DateTimeOffset.UtcNow.AddHours(1),
                    "{\"cert\":\"value\"}",
                    DateTimeOffset.UtcNow));
            var loaded = await store.GetCredentialAsync("ms-1");

            Assert.NotNull(loaded);
            Assert.Equal("refresh-token", loaded!.MicrosoftRefreshToken);
            Assert.Equal("access-token", loaded.MinecraftAccessToken);
            Assert.Equal("{\"cert\":\"value\"}", loaded.CertificatesJson);
            Assert.True(File.Exists(Path.Combine(dataRoot, "credentials.vault")));
            Assert.False(File.Exists(legacy));

            var migrated = await store.GetCredentialAsync("legacy");
            Assert.NotNull(migrated);
            Assert.Equal("old", migrated!.MicrosoftRefreshToken);

            await store.DeleteCredentialAsync("ms-1");
            Assert.Null(await store.GetCredentialAsync("ms-1"));
        }
        finally
        {
            Delete(directory);
        }
    }

    private static string VaultBinary()
    {
        var configured = Environment.GetEnvironmentVariable("ARCHOERACX_VAULT_BIN");
        if (!string.IsNullOrWhiteSpace(configured)) return configured;
        var directory = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (directory is not null)
        {
            var candidate = Path.Combine(
                directory.FullName,
                "build",
                "native",
                "archoera-vault");
            if (File.Exists(candidate)) return candidate;
            directory = directory.Parent;
        }
        return Path.GetFullPath(
            Path.Combine(Directory.GetCurrentDirectory(), "build", "native", "archoera-vault"));
    }

    private static string TempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "archoera-vault-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void Delete(string directory)
    {
        try
        {
            Directory.Delete(directory, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}
