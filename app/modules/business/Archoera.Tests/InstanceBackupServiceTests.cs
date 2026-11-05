using Archoera.Domain.Instances;
using Archoera.Infrastructure.Instances;
using Archoera.Infrastructure.Settings;

namespace Archoera.Tests;

public sealed class InstanceBackupServiceTests
{
    [Fact]
    public async Task CreateCopiesInstanceAndDiscardRemovesSnapshot()
    {
        var dataRoot = TempDirectory();
        try
        {
            var settings = new JsonAppSettingsStore(dataRoot);
            var repository = new JsonInstanceRepository(settings: settings);
            var record = await repository.CreateAsync(
                "Backup test", "1.20.1", "fabric", "0.15.0", rootPath: null);
            var mods = Path.Combine(record.RootPath, "mods");
            Directory.CreateDirectory(mods);
            await File.WriteAllTextAsync(
                Path.Combine(mods, "a.jar"),
                "mod-bytes");
            var service = new InstanceBackupService(repository, settings);

            var snapshot = await service.CreateAsync(record.Id, CancellationToken.None);

            Assert.True(Directory.Exists(snapshot.BackupPath));
            Assert.True(File.Exists(Path.Combine(snapshot.BackupPath, "manifest.json")));
            Assert.True(snapshot.FileCount >= 2);
            Assert.Equal(
                "mod-bytes",
                await File.ReadAllTextAsync(
                    Path.Combine(snapshot.BackupPath, "mods", "a.jar")));

            Assert.True(await service.DiscardAsync(
                record.Id, snapshot.BackupId, CancellationToken.None));
            Assert.False(Directory.Exists(snapshot.BackupPath));
        }
        finally
        {
            Delete(dataRoot);
        }
    }

    [Fact]
    public async Task RestoreRollsBackFilesAndMetadataOnlyAfterVerification()
    {
        var dataRoot = TempDirectory();
        try
        {
            var settings = new JsonAppSettingsStore(dataRoot);
            var repository = new JsonInstanceRepository(settings: settings);
            var record = await repository.CreateAsync(
                "Backup test", "1.20.1", "fabric", "0.15.0", rootPath: null);
            var config = Path.Combine(record.RootPath, "config");
            Directory.CreateDirectory(config);
            var keep = Path.Combine(config, "keep.txt");
            await File.WriteAllTextAsync(keep, "original");
            var service = new InstanceBackupService(repository, settings);
            var snapshot = await service.CreateAsync(record.Id, CancellationToken.None);

            // Simulate a partially applied update: one file removed, one file
            // added and the loader metadata changed.
            File.Delete(keep);
            var mods = Path.Combine(record.RootPath, "mods");
            Directory.CreateDirectory(mods);
            await File.WriteAllTextAsync(
                Path.Combine(mods, "update.jar"), "new");
            await repository.UpdateAsync(
                record.Id, name: null, favorite: null, lastPlayedAt: null,
                loader: "quilt", loaderVersion: "0.20.0");

            var outcome = await service.RestoreAsync(
                record.Id, snapshot.BackupId, CancellationToken.None);

            Assert.True(outcome.Restored, outcome.Error);
            Assert.True(outcome.BackupDiscarded);
            Assert.False(Directory.Exists(snapshot.BackupPath));
            Assert.Equal("original", await File.ReadAllTextAsync(keep));
            Assert.False(File.Exists(Path.Combine(record.RootPath, "mods", "update.jar")));
            var restored = (await repository.ListAsync())
                .Single(item => item.Id == record.Id);
            Assert.Equal("fabric", restored.Loader);
            Assert.Equal("0.15.0", restored.LoaderVersion);
        }
        finally
        {
            Delete(dataRoot);
        }
    }

    [Fact]
    public async Task RestoreRefusesDamagedSnapshotAndKeepsCurrentFiles()
    {
        var dataRoot = TempDirectory();
        try
        {
            var settings = new JsonAppSettingsStore(dataRoot);
            var repository = new JsonInstanceRepository(settings: settings);
            var record = await repository.CreateAsync(
                "Backup test", "1.20.1", "fabric", "0.15.0", rootPath: null);
            var config = Path.Combine(record.RootPath, "config");
            Directory.CreateDirectory(config);
            var live = Path.Combine(config, "live.txt");
            await File.WriteAllTextAsync(live, "before");
            var service = new InstanceBackupService(repository, settings);
            var snapshot = await service.CreateAsync(record.Id, CancellationToken.None);
            await File.WriteAllTextAsync(
                Path.Combine(snapshot.BackupPath, "config", "live.txt"), "damaged");
            await File.WriteAllTextAsync(live, "after");

            var outcome = await service.RestoreAsync(
                record.Id, snapshot.BackupId, CancellationToken.None);

            Assert.False(outcome.Restored);
            Assert.False(outcome.BackupDiscarded);
            Assert.True(Directory.Exists(snapshot.BackupPath));
            Assert.Equal("after", await File.ReadAllTextAsync(live));
        }
        finally
        {
            Delete(dataRoot);
        }
    }

    [Fact]
    public async Task DiscardRejectsBackupIdThatEscapesTheBackupsDirectory()
    {
        var dataRoot = TempDirectory();
        try
        {
            var settings = new JsonAppSettingsStore(dataRoot);
            var repository = new JsonInstanceRepository(settings: settings);
            var record = await repository.CreateAsync(
                "Backup test", "1.20.1", "fabric", "0.15.0", rootPath: null);
            var service = new InstanceBackupService(repository, settings);

            await Assert.ThrowsAsync<ArgumentException>(() => service.DiscardAsync(
                record.Id, "../escape", CancellationToken.None));
        }
        finally
        {
            Delete(dataRoot);
        }
    }

    [Fact]
    public async Task TrashPlanResolvesManagedRootAndRejectsForeignRoot()
    {
        var dataRoot = TempDirectory();
        try
        {
            var settings = new JsonAppSettingsStore(dataRoot);
            var repository = new JsonInstanceRepository(settings: settings);
            var record = await repository.CreateAsync(
                "Trash test", "1.20.1", "vanilla", string.Empty, rootPath: null);
            var settingsDocument = await settings.LoadAsync(CancellationToken.None);

            var resolved = InstanceDirectoryCleaner.TryResolveForTrash(
                record.RootPath,
                settingsDocument.Roots,
                [],
                out var path,
                out var reason);
            Assert.True(resolved);
            Assert.Equal(
                Path.TrimEndingDirectorySeparator(Path.GetFullPath(record.RootPath)),
                path);
            Assert.Null(reason);

            var foreign = TempDirectory();
            // A marker alone reuses the instance shape; without a configured
            // root the cleaner must still refuse to hand it to the trash.
            Directory.CreateDirectory(Path.Combine(foreign, ".archoera"));
            await File.WriteAllTextAsync(
                Path.Combine(foreign, ".archoera", "instance.json"), "{}");
            var rejected = InstanceDirectoryCleaner.TryResolveForTrash(
                foreign,
                settingsDocument.Roots,
                [],
                out _,
                out var rejectedReason);
            Assert.False(rejected);
            Assert.Equal(InstanceDeleteSkipReason.OutsideRoot, rejectedReason);
        }
        finally
        {
            Delete(dataRoot);
        }
    }

    private static string TempDirectory()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            "archoera-backups",
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
