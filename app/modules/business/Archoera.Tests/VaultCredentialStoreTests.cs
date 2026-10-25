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

    [Fact]
    public void Binary_candidates_skip_the_working_directory_in_release()
    {
        var cwd = Path.Combine(Path.GetTempPath(), "archoera-cwd-probe");
        var baseDirectory = Path.Combine(Path.GetTempPath(), "archoera-app");

        var candidates = VaultCredentialStore.BuildBinaryCandidates(
            baseDirectory,
            cwd,
            includeDevelopmentProbes: false);

        Assert.Equal(
            Path.Combine(baseDirectory, OperatingSystem.IsWindows()
                ? "archoera-vault.exe"
                : "archoera-vault"),
            candidates[0]);
        Assert.DoesNotContain(
            candidates,
            candidate => candidate.StartsWith(cwd, StringComparison.Ordinal));
        var executable = OperatingSystem.IsWindows()
            ? "archoera-vault.exe"
            : "archoera-vault";
        Assert.Contains(
            Path.GetFullPath(Path.Combine(baseDirectory, "..", "Frameworks", executable)),
            candidates);
    }

    [Fact]
    public void SelectBinary_prefers_an_existing_override_and_ignores_non_files()
    {
        var directory = TempDirectory();
        try
        {
            var appDirectory = Path.Combine(directory, "app");
            var overrideDirectory = Path.Combine(directory, "override");
            Directory.CreateDirectory(appDirectory);
            Directory.CreateDirectory(overrideDirectory);
            var name = OperatingSystem.IsWindows()
                ? "archoera-vault.exe"
                : "archoera-vault";
            var bundled = Path.Combine(appDirectory, name);
            var overridden = Path.Combine(overrideDirectory, name);
            File.WriteAllText(bundled, "vault");
            File.WriteAllText(overridden, "vault");

            var candidates = VaultCredentialStore.BuildBinaryCandidates(
                appDirectory,
                currentDirectory: null,
                includeDevelopmentProbes: false);

            Assert.Equal(
                Path.GetFullPath(overridden),
                VaultCredentialStore.SelectBinary(overridden, candidates));
            Assert.Equal(
                Path.GetFullPath(bundled),
                VaultCredentialStore.SelectBinary(null, candidates));
            Assert.Equal(
                Path.GetFullPath(bundled),
                VaultCredentialStore.SelectBinary(appDirectory, candidates));
            Assert.Null(VaultCredentialStore.SelectBinary(
                Path.Combine(directory, "missing"),
                []));
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
        var executable = OperatingSystem.IsWindows()
            ? "archoera-vault.exe"
            : "archoera-vault";
        var directory = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (directory is not null)
        {
            foreach (var relative in new[]
                     {
                         Path.Combine("build", "native", executable),
                         Path.Combine("app", "build", "native", executable),
                     })
            {
                var candidate = Path.Combine(directory.FullName, relative);
                if (File.Exists(candidate)) return candidate;
            }
            directory = directory.Parent;
        }
        return Path.GetFullPath(
            Path.Combine(Directory.GetCurrentDirectory(), "build", "native", executable));
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
