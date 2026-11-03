using System.Text.Json;
using Archoera.Application.Modules;
using Archoera.Domain.Mojang;
using Archoera.Infrastructure.Maintenance;
using Archoera.Infrastructure.Modules;
using Archoera.Infrastructure.Settings;

namespace Archoera.Tests;

/// <summary>
/// Unused-dependency cleanup: the scan proves usage from every version JSON
/// under a root plus every discovered instance profile, and only deletes what
/// no profile references. Any unreadable profile fails the root closed.
/// </summary>
public sealed class UnusedDependencyTests
{
    private const string PlanMethod = "maintenance.planUnusedDependencies";
    private const string DeleteMethod = "maintenance.deleteUnusedDependencies";

    private const string UsedHash = "aaaa1111aaaa1111aaaa1111aaaa1111aaaa1111";
    private const string StaleHash = "bbbb2222bbbb2222bbbb2222bbbb2222bbbb2222";

    [Fact]
    public async Task Flags_unused_libraries_and_keeps_referenced_ones()
    {
        var directory = TempDirectory();
        try
        {
            var root = BuildRoot(directory);
            var stale = WriteLib(root, "com/example/stale/1.0/stale-1.0.jar", 7);

            var report = UnusedDependencyScanner.Plan(
                [root], MojangPlatform.Current(), CancellationToken.None);
            var scanned = Assert.Single(report.Roots);
            Assert.Equal(UnusedDependencyScanner.StatusScanned, scanned.Status);
            Assert.Null(scanned.Reason);
            // Discovery also counts the "legacy-loader-1.0" folder as an
            // instance: only real installer ids are filtered out.
            Assert.Equal(2, scanned.InstanceCount);

            var libraries = scanned.Libraries!;
            Assert.Equal(2, libraries.FileCount);
            Assert.Equal(15L, libraries.TotalBytes);
            Assert.Contains("libraries/com/example/stale/1.0/stale-1.0.jar", libraries.Samples);
            Assert.True(File.Exists(stale));

            // Two unused libraries plus the stale asset object.
            Assert.Equal(3, report.TotalFiles);
            Assert.Equal(24L, report.TotalBytes);
            Assert.False(report.LimitReached);
        }
        finally
        {
            Delete(directory);
        }
    }

    [Fact]
    public async Task Native_classifiers_count_as_used_for_the_running_platform()
    {
        var directory = TempDirectory();
        try
        {
            var root = BuildRoot(directory);
            WriteLib(root, "com/example/used-natives/1.0/used-natives-1.0-natives-linux.jar", 5);

            var report = UnusedDependencyScanner.Plan(
                [root], MojangPlatform.Current(), CancellationToken.None);
            var scanned = Assert.Single(report.Roots);
            // natives-linux matches the host platform and is used; the
            // windows classifier file has no consumer on this machine.
            Assert.NotNull(scanned.Libraries);
            Assert.Equal(1, scanned.Libraries!.FileCount);
            Assert.Contains(
                "libraries/com/example/used-natives/1.0/used-natives-1.0-natives-windows.jar",
                scanned.Libraries.Samples);
        }
        finally
        {
            Delete(directory);
        }
    }

    [Fact]
    public async Task Deletes_candidates_and_keeps_referenced_files()
    {
        var directory = TempDirectory();
        try
        {
            var root = BuildRoot(directory);
            var stale = WriteLib(root, "com/example/stale/1.0/stale-1.0.jar", 7);
            var used = WriteLib(root, "com/example/used-a/1.0/used-a-1.0.jar", 11);

            var report = UnusedDependencyScanner.Delete(
                [root], MojangPlatform.Current(), CancellationToken.None);
            var scanned = Assert.Single(report.Roots);
            Assert.Equal(2, scanned.Libraries!.FileCount);
            Assert.Equal(15L, scanned.Libraries.TotalBytes);
            Assert.Equal(1, scanned.Assets!.FileCount);
            Assert.Equal(9L, scanned.Assets.TotalBytes);
            Assert.Equal(3, report.TotalFiles);
            Assert.Equal(24L, report.TotalBytes);

            Assert.False(File.Exists(stale));
            Assert.True(File.Exists(used));
            // Empty maven parents are pruned up to the libraries root.
            Assert.False(Directory.Exists(Path.Combine(root, "libraries", "com", "example", "stale")));
            Assert.True(Directory.Exists(Path.Combine(root, "libraries", "com")));
        }
        finally
        {
            Delete(directory);
        }
    }

    [Fact]
    public async Task Asset_objects_follow_the_local_index_and_never_touch_other_shapes()
    {
        var directory = TempDirectory();
        try
        {
            var root = BuildRoot(directory);
            var stale = WriteObject(root, StaleHash, 9);

            var report = UnusedDependencyScanner.Plan(
                [root], MojangPlatform.Current(), CancellationToken.None);
            var scanned = Assert.Single(report.Roots);
            var assets = scanned.Assets!;
            Assert.Equal(1, assets.FileCount);
            Assert.Equal(9L, assets.TotalBytes);
            Assert.Contains($"assets/objects/{StaleHash[..2]}/{StaleHash}", assets.Samples);
            Assert.True(File.Exists(stale));
        }
        finally
        {
            Delete(directory);
        }
    }

    [Fact]
    public async Task A_missing_asset_index_fails_assets_closed_but_libraries_still_scan()
    {
        var directory = TempDirectory();
        try
        {
            var root = BuildRoot(directory, withAssetIndex: false);
            WriteLib(root, "com/example/stale/1.0/stale-1.0.jar", 7);
            WriteObject(root, StaleHash, 9);

            var report = UnusedDependencyScanner.Plan(
                [root], MojangPlatform.Current(), CancellationToken.None);
            var scanned = Assert.Single(report.Roots);
            Assert.NotNull(scanned.Libraries);
            Assert.Equal(2, scanned.Libraries!.FileCount);
            Assert.Null(scanned.Assets);
        }
        finally
        {
            Delete(directory);
        }
    }

    [Fact]
    public async Task Legacy_inherits_chains_keep_their_base_libraries_alive()
    {
        var directory = TempDirectory();
        try
        {
            var root = TempRoot(directory);
            WriteProfile(
                Path.Combine(root, "versions", "base"),
                "base",
                """{"name":"com.example:base-lib:1.0"}""",
                assetIndexId: null);
            WriteProfile(
                Path.Combine(root, "versions", "child"),
                "child",
                """{"name":"com.example:child-lib:1.0"}""",
                assetIndexId: null,
                inheritsFrom: "base");
            WriteLib(root, "com/example/base-lib/1.0/base-lib-1.0.jar", 3);
            WriteLib(root, "com/example/child-lib/1.0/child-lib-1.0.jar", 4);
            var orphan = WriteLib(root, "com/example/orphan/1.0/orphan-1.0.jar", 5);

            var report = UnusedDependencyScanner.Plan(
                [root], MojangPlatform.Current(), CancellationToken.None);
            var scanned = Assert.Single(report.Roots);
            var libraries = scanned.Libraries!;
            Assert.Equal(1, libraries.FileCount);
            Assert.Contains("libraries/com/example/orphan/1.0/orphan-1.0.jar", libraries.Samples);
            Assert.True(File.Exists(orphan));
        }
        finally
        {
            Delete(directory);
        }
    }

    [Fact]
    public async Task A_broken_profile_fails_the_whole_root_closed()
    {
        var directory = TempDirectory();
        try
        {
            var root = TempRoot(directory);
            WriteProfile(
                Path.Combine(root, "versions", "inst"),
                "inst",
                "not json at all",
                assetIndexId: null);
            Directory.CreateDirectory(Path.Combine(root, "versions", "inst", "mods"));
            WriteLib(root, "com/example/orphan/1.0/orphan-1.0.jar", 5);

            var report = UnusedDependencyScanner.Plan(
                [root], MojangPlatform.Current(), CancellationToken.None);
            var scanned = Assert.Single(report.Roots);
            Assert.Equal(UnusedDependencyScanner.StatusSkipped, scanned.Status);
            Assert.Equal(UnusedDependencyScanner.ReasonUnreadableProfile, scanned.Reason);
            Assert.Null(scanned.Libraries);
            Assert.Null(scanned.Assets);
        }
        finally
        {
            Delete(directory);
        }
    }

    [Fact]
    public async Task A_root_without_instances_is_skipped()
    {
        var directory = TempDirectory();
        try
        {
            var root = TempRoot(directory);
            Directory.CreateDirectory(Path.Combine(root, "libraries"));

            var report = UnusedDependencyScanner.Plan(
                [root], MojangPlatform.Current(), CancellationToken.None);
            var scanned = Assert.Single(report.Roots);
            Assert.Equal(UnusedDependencyScanner.StatusSkipped, scanned.Status);
            Assert.Equal(UnusedDependencyScanner.ReasonNoInstances, scanned.Reason);
        }
        finally
        {
            Delete(directory);
        }
    }

    [Fact]
    public async Task Registry_dispatches_both_methods()
    {
        var directory = TempDirectory();
        try
        {
            var dataRoot = Path.Combine(directory, "data");
            var root = BuildRoot(directory);
            var stale = WriteLib(root, "com/example/stale/1.0/stale-1.0.jar", 7);
            var store = new JsonAppSettingsStore(dataRoot);
            await store.SaveInstancesRootsAsync([root]);

            var registry = BusinessModuleRegistry.CreateDefault(dataRoot);
            var plan = await CallDataAsync(registry, PlanMethod);
            Assert.Equal(3, plan.GetProperty("totalFiles").GetInt32());
            var libraries = plan.GetProperty("roots")[0].GetProperty("libraries");
            Assert.Equal(2, libraries.GetProperty("files").GetInt32());

            var deleted = await CallDataAsync(registry, DeleteMethod);
            Assert.Equal(3, deleted.GetProperty("totalFiles").GetInt32());
            Assert.Equal(24L, deleted.GetProperty("totalBytes").GetInt64());
            Assert.False(File.Exists(stale));
        }
        finally
        {
            Delete(directory);
        }
    }

    [Fact]
    public async Task Delete_refuses_to_run_when_the_scan_hits_its_budget()
    {
        var directory = TempDirectory();
        try
        {
            var root = BuildRoot(directory);
            var stale = WriteLib(root, "com/example/stale/1.0/stale-1.0.jar", 7);

            // A two-entry budget runs out inside the versions/ profile walk,
            // before any candidate can be proven unused.
            var report = UnusedDependencyScanner.Delete(
                [root],
                MojangPlatform.Current(),
                CancellationToken.None,
                maxEntries: 2);

            Assert.True(report.LimitReached);
            Assert.Equal(0, report.TotalFiles);
            Assert.True(File.Exists(stale));
        }
        finally
        {
            Delete(directory);
        }
    }

    // ------------------------------------------------------------------
    // fixtures

    /// <summary>A root with one instance, one legacy profile, libraries and assets.</summary>
    private static string BuildRoot(string directory, bool withAssetIndex = true)
    {
        var root = TempRoot(directory);
        var assetIndexId = withAssetIndex ? "test" : null;

        WriteProfile(
            Path.Combine(root, "versions", "inst"),
            "inst",
            """
            {"name":"com.example:used-a:1.0"},
            {"name":"com.example:used-natives:1.0",
             "natives":{"linux":"natives-linux","windows":"natives-windows"}}
            """,
            assetIndexId);
        Directory.CreateDirectory(Path.Combine(root, "versions", "inst", "mods"));

        WriteProfile(
            Path.Combine(root, "versions", "legacy-loader-1.0"),
            "legacy-loader-1.0",
            """{"name":"net.legacy:old:1.0"}""",
            assetIndexId);

        WriteLib(root, "com/example/used-a/1.0/used-a-1.0.jar", 11);
        WriteLib(root, "com/example/used-natives/1.0/used-natives-1.0.jar", 6);
        WriteLib(root, "com/example/used-natives/1.0/used-natives-1.0-natives-windows.jar", 8);
        WriteLib(root, "net/legacy/old/1.0/old-1.0.jar", 4);
        WriteLib(root, "com/example/partial/1.0/partial-1.0.jar.part", 13);

        if (withAssetIndex)
        {
            var indexes = Path.Combine(root, "assets", "indexes");
            Directory.CreateDirectory(indexes);
            File.WriteAllText(
                Path.Combine(indexes, "test.json"),
                "{\"objects\":{\"a.png\":{\"hash\":\"" + UsedHash + "\",\"size\":3}}}");
            WriteObject(root, UsedHash, 3);
            WriteObject(root, StaleHash, 9);
        }

        return root;
    }

    private static string TempRoot(string directory)
    {
        var root = Path.Combine(directory, "minecraft");
        Directory.CreateDirectory(root);
        return root;
    }

    private static void WriteProfile(
        string instanceDirectory,
        string id,
        string librariesBody,
        string? assetIndexId,
        string inheritsFrom = "")
    {
        Directory.CreateDirectory(instanceDirectory);
        var assetBlock = assetIndexId is null
            ? string.Empty
            : ",\"assetIndex\":{\"id\":\"" + assetIndexId
                + "\",\"url\":\"https://resources.example.invalid\",\"sha1\":\""
                + UsedHash + "\",\"size\":1,\"totalSize\":2}";
        var inheritsBlock = string.IsNullOrWhiteSpace(inheritsFrom)
            ? string.Empty
            : ",\"inheritsFrom\":\"" + inheritsFrom + "\"";
        var json = "{\"id\":\"" + id
            + "\",\"type\":\"release\",\"mainClass\":\"net.minecraft.client.main.Main\""
            + inheritsBlock + assetBlock + ",\"libraries\":[" + librariesBody + "]}";
        File.WriteAllText(Path.Combine(instanceDirectory, $"{id}.json"), json);
    }

    private static string WriteLib(string root, string relative, int bytes)
    {
        var path = Path.Combine(root, "libraries", relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, new byte[bytes]);
        return path;
    }

    private static string WriteObject(string root, string hash, int bytes)
    {
        var path = Path.Combine(root, "assets", "objects", hash[..2], hash);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, new byte[bytes]);
        return path;
    }

    private static async Task<JsonElement> CallDataAsync(
        ModuleMethodRegistry registry,
        string method)
    {
        var bytes = await registry.HandleAsync(method, null, CancellationToken.None);
        using var document = JsonDocument.Parse(bytes);
        var response = document.RootElement.Clone();
        Assert.True(
            response.GetProperty("ok").GetBoolean(),
            response.GetRawText());
        return response.GetProperty("data");
    }

    private static string TempDirectory()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            "archoera-unused-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void Delete(string path)
    {
        try
        {
            if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
        }
        catch (IOException)
        {
            // Best-effort cleanup; the OS temp dir handles the rest.
        }
    }
}
