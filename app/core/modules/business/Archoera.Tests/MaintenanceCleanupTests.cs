using System.Text.Json;
using Archoera.Infrastructure.Maintenance;
using Archoera.Infrastructure.Modules;
using Archoera.Infrastructure.Settings;

namespace Archoera.Tests;

/// <summary>
/// Stale download temp-file cleanup. The Rust executor writes
/// <c>&lt;dest&gt;.part</c> next to its target, with a <c>.part.meta</c>
/// resume sidecar and a <c>.part.lock</c> writer lock, so the sweep may only
/// drop groups that are provably abandoned.
/// </summary>
public sealed class MaintenanceCleanupTests
{
    private const string Method = "maintenance.cleanPartials";

    [Fact]
    public async Task Deletes_expired_partials_and_keeps_fresh_or_unrelated_files()
    {
        var directory = TempDirectory();
        try
        {
            var dataRoot = Path.Combine(directory, "data");
            var root = Path.Combine(directory, "instances");
            var mods = Path.Combine(root, "versions", "inst", "mods");
            var libraries = Path.Combine(root, "libraries", "vendor");
            var expired = DateTime.UtcNow.AddDays(-10);

            var part = Write(mods, "a.jar.part", 10, expired);
            var meta = Write(mods, "a.jar.part.meta", 20, expired);
            var lockFile = Write(mods, "a.jar.part.lock", 5, expired);
            var upperCase = Write(mods, "B.JAR.PART", 7, expired);
            var nested = Write(libraries, "lib.jar.part", 3, expired);
            var fresh = Write(mods, "fresh.jar.part", 30, DateTime.UtcNow);
            var untouched = Write(mods, "keep.jar", 40, expired);
            var notes = Write(mods, "readme.txt", 50, expired);

            var service = new MaintenanceModuleService(
                await StoreWithRootsAsync(dataRoot, root));
            var data = await CallAsync(service, """{"olderThanDays":3}""");

            Assert.Equal(5, data.GetProperty("deletedFiles").GetInt32());
            Assert.Equal(45L, data.GetProperty("freedBytes").GetInt64());
            Assert.Equal(8, data.GetProperty("scannedFiles").GetInt32());

            Assert.False(File.Exists(part));
            Assert.False(File.Exists(meta));
            Assert.False(File.Exists(lockFile));
            Assert.False(File.Exists(upperCase));
            Assert.False(File.Exists(nested));
            Assert.True(File.Exists(fresh));
            Assert.True(File.Exists(untouched));
            Assert.True(File.Exists(notes));
        }
        finally
        {
            Delete(directory);
        }
    }

    [Fact]
    public async Task Fresh_lock_protects_its_whole_group()
    {
        var directory = TempDirectory();
        try
        {
            var dataRoot = Path.Combine(directory, "data");
            var root = Path.Combine(directory, "instances");
            var mods = Path.Combine(root, "mods");
            var expired = DateTime.UtcNow.AddDays(-10);

            // A paused resume: the payload is old, but the writer refreshed the
            // lock two minutes ago, so the download may still continue.
            var part = Write(mods, "b.jar.part", 11, expired);
            var meta = Write(mods, "b.jar.part.meta", 12, expired);
            var lockFile = Write(mods, "b.jar.part.lock", 1, DateTime.UtcNow.AddMinutes(-2));
            var orphan = Write(mods, "c.jar.part", 13, expired);

            var service = new MaintenanceModuleService(
                await StoreWithRootsAsync(dataRoot, root));
            var data = await CallAsync(service, "{}");

            Assert.Equal(1, data.GetProperty("deletedFiles").GetInt32());
            Assert.Equal(13L, data.GetProperty("freedBytes").GetInt64());
            Assert.Equal(4, data.GetProperty("scannedFiles").GetInt32());

            Assert.True(File.Exists(part));
            Assert.True(File.Exists(meta));
            Assert.True(File.Exists(lockFile));
            Assert.False(File.Exists(orphan));
        }
        finally
        {
            Delete(directory);
        }
    }

    [Fact]
    public async Task Requested_age_selects_which_files_expire()
    {
        var directory = TempDirectory();
        try
        {
            var dataRoot = Path.Combine(directory, "data");
            var root = Path.Combine(directory, "instances");
            var mods = Path.Combine(root, "mods");

            var old = Write(mods, "old.jar.part", 4, DateTime.UtcNow.AddDays(-2));
            var recent = Write(mods, "recent.jar.part", 6, DateTime.UtcNow.AddHours(-12));

            var service = new MaintenanceModuleService(
                await StoreWithRootsAsync(dataRoot, root));
            var data = await CallAsync(service, """{"olderThanDays":1}""");

            Assert.Equal(1, data.GetProperty("deletedFiles").GetInt32());
            Assert.Equal(4L, data.GetProperty("freedBytes").GetInt64());
            Assert.False(File.Exists(old));
            Assert.True(File.Exists(recent));
        }
        finally
        {
            Delete(directory);
        }
    }

    [Fact]
    public async Task Default_age_is_three_days()
    {
        var directory = TempDirectory();
        try
        {
            var dataRoot = Path.Combine(directory, "data");
            var root = Path.Combine(directory, "instances");
            var mods = Path.Combine(root, "mods");

            var expired = Write(mods, "default.jar.part", 8, DateTime.UtcNow.AddDays(-4));
            var recent = Write(mods, "default-recent.jar.part", 9, DateTime.UtcNow.AddDays(-2));

            var service = new MaintenanceModuleService(
                await StoreWithRootsAsync(dataRoot, root));
            var data = await CallAsync(service, paramsJson: null);

            Assert.Equal(1, data.GetProperty("deletedFiles").GetInt32());
            Assert.Equal(8L, data.GetProperty("freedBytes").GetInt64());
            Assert.False(File.Exists(expired));
            Assert.True(File.Exists(recent));
        }
        finally
        {
            Delete(directory);
        }
    }

    [Theory]
    [InlineData("""{"olderThanDays":0}""")]
    [InlineData("""{"olderThanDays":366}""")]
    [InlineData("""{"olderThanDays":-1}""")]
    [InlineData("""{"olderThanDays":3.5}""")]
    [InlineData("""{"olderThanDays":"3"}""")]
    [InlineData("[]")]
    [InlineData("not json")]
    public async Task Invalid_older_than_days_returns_E_INVALID(string payload)
    {
        var directory = TempDirectory();
        try
        {
            var dataRoot = Path.Combine(directory, "data");
            var root = Path.Combine(directory, "instances");
            Directory.CreateDirectory(root);

            var service = new MaintenanceModuleService(
                await StoreWithRootsAsync(dataRoot, root));
            var bytes = await service.HandleAsync(Method, payload, CancellationToken.None);

            using var document = JsonDocument.Parse(bytes);
            var response = document.RootElement;
            Assert.False(response.GetProperty("ok").GetBoolean());
            Assert.Equal(
                "E_INVALID",
                response.GetProperty("error").GetProperty("code").GetString());
        }
        finally
        {
            Delete(directory);
        }
    }

    [Fact]
    public async Task Symbolic_links_are_not_followed_or_deleted()
    {
        // Creating symlinks on Windows needs elevation; the guard is only
        // meaningful on POSIX where the launcher normally runs.
        if (OperatingSystem.IsWindows()) return;

        var directory = TempDirectory();
        try
        {
            var dataRoot = Path.Combine(directory, "data");
            var root = Path.Combine(directory, "instances");
            var outside = Path.Combine(directory, "outside");
            var expired = DateTime.UtcNow.AddDays(-10);

            var hidden = Write(outside, "hidden.jar.part", 21, expired);
            Directory.CreateDirectory(root);
            Directory.CreateSymbolicLink(Path.Combine(root, "linked"), outside);
            File.CreateSymbolicLink(Path.Combine(root, "linked.jar.part"), hidden);
            var normal = Write(root, "normal.jar.part", 4, expired);

            var service = new MaintenanceModuleService(
                await StoreWithRootsAsync(dataRoot, root));
            var data = await CallAsync(service, "{}");

            Assert.Equal(1, data.GetProperty("deletedFiles").GetInt32());
            Assert.Equal(4L, data.GetProperty("freedBytes").GetInt64());
            Assert.Equal(2, data.GetProperty("scannedFiles").GetInt32());
            Assert.True(File.Exists(hidden));
            Assert.True(File.Exists(Path.Combine(root, "linked.jar.part")));
            Assert.False(File.Exists(normal));
        }
        finally
        {
            Delete(directory);
        }
    }

    [Fact]
    public async Task Registry_dispatches_clean_partials()
    {
        var directory = TempDirectory();
        try
        {
            var dataRoot = Path.Combine(directory, "data");
            var root = Path.Combine(directory, "instances");
            var store = new JsonAppSettingsStore(dataRoot);
            await store.SaveInstancesRootsAsync([root]);
            var expired = Write(
                Path.Combine(root, "mods"),
                "registered.jar.part",
                5,
                DateTime.UtcNow.AddDays(-10));

            var registry = BusinessModuleRegistry.CreateDefault(dataRoot);
            using var document = JsonDocument.Parse(
                await registry.HandleAsync(Method, "{}", CancellationToken.None));
            var response = document.RootElement;

            Assert.True(response.GetProperty("ok").GetBoolean());
            Assert.Equal(
                1,
                response.GetProperty("data").GetProperty("deletedFiles").GetInt32());
            Assert.False(File.Exists(expired));
        }
        finally
        {
            Delete(directory);
        }
    }

    [Fact]
    public void Scan_stops_at_the_entry_cap_without_failing()
    {
        var directory = TempDirectory();
        try
        {
            var root = Path.Combine(directory, "instances");
            Directory.CreateDirectory(root);
            var expired = DateTime.UtcNow.AddDays(-10);
            for (var index = 0; index < 5; index++)
            {
                Write(root, $"capped{index}.jar.part", 4, expired);
            }

            var report = MaintenanceModuleService.ScanRoots(
                [root],
                DateTime.UtcNow,
                olderThanDays: 3,
                maxEntries: 3,
                CancellationToken.None);

            Assert.True(report.LimitReached);
            Assert.Equal(3, report.ScannedFiles);
            Assert.Equal(3, report.DeletedFiles);
            Assert.Equal(12L, report.FreedBytes);
            Assert.Equal(2, Directory.GetFiles(root, "*.part").Length);
        }
        finally
        {
            Delete(directory);
        }
    }

    private static async Task<JsonElement> CallAsync(
        MaintenanceModuleService service,
        string? paramsJson)
    {
        var bytes = await service.HandleAsync(Method, paramsJson, CancellationToken.None);
        using var document = JsonDocument.Parse(bytes);
        var response = document.RootElement.Clone();
        Assert.True(response.GetProperty("ok").GetBoolean());
        return response.GetProperty("data");
    }

    private static async Task<JsonAppSettingsStore> StoreWithRootsAsync(
        string dataRoot,
        params string[] roots)
    {
        var store = new JsonAppSettingsStore(dataRoot);
        await store.SaveInstancesRootsAsync(roots);
        return store;
    }

    private static string Write(
        string directory,
        string name,
        int bytes,
        DateTime lastWriteUtc)
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, name);
        File.WriteAllBytes(path, new byte[bytes]);
        File.SetLastWriteTimeUtc(path, lastWriteUtc);
        return path;
    }

    private static string TempDirectory()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            "archoera-maintenance-" + Guid.NewGuid().ToString("N"));
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
