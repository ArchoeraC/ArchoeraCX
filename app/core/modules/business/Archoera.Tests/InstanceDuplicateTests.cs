using System.Text.Json;
using Archoera.Application.Instances;
using Archoera.Domain.Instances;
using Archoera.Infrastructure.Instances;
using Archoera.Infrastructure.Settings;

namespace Archoera.Tests;

public sealed class InstanceDuplicateTests
{
    [Fact]
    public async Task Duplicate_copies_the_game_directory_and_keeps_overrides()
    {
        var directory = TempDirectory();
        try
        {
            var dataRoot = Path.Combine(directory, "data");
            var settings = new JsonAppSettingsStore(dataRoot);
            var repository = new SqliteInstanceRepository(dataRoot, settings);
            var sourceRoot = Path.Combine(directory, "game", "source");
            var source = await repository.CreateAsync(
                "Source Pack",
                "1.21.4",
                "fabric",
                "0.16.10",
                sourceRoot);

            Write(sourceRoot, "options.txt", "fov:80");
            Write(sourceRoot, Path.Combine("mods", "example.jar"), "jar");
            Write(sourceRoot, Path.Combine("mods", "deep", "nested.jar"), "nested");
            Write(sourceRoot, Path.Combine("config", "modpack", "settings.toml"), "enabled = true");
            Write(sourceRoot, Path.Combine(".archoera", "logs", "2026-10-23.log"), "log line");
            Write(sourceRoot, Path.Combine(".archoera", "notes.json"), "{}");

            var overrides = new InstanceOverrides(
                MemoryMaxMb: 4096,
                GcMode: "zgc",
                WindowWidth: 1600);
            await repository.UpdateAsync(
                source.Id,
                name: null,
                favorite: null,
                lastPlayedAt: null,
                overrides: overrides,
                setOverrides: true);

            var service = new InstanceModuleService(repository, settings);
            using var document = JsonDocument.Parse(
                await service.HandleAsync(
                    "instance.duplicate",
                    $$"""{"id":"{{source.Id}}","name":"Source Pack Copy"}""",
                    CancellationToken.None));

            Assert.True(document.RootElement.GetProperty("ok").GetBoolean());
            var copy = document.RootElement.GetProperty("data").GetProperty("instance");
            var copyId = copy.GetProperty("id").GetString();
            Assert.NotNull(copyId);
            Assert.NotEqual(source.Id, copyId);
            Assert.Equal("Source Pack Copy", copy.GetProperty("name").GetString());
            Assert.Equal("1.21.4", copy.GetProperty("gameVersion").GetString());
            Assert.Equal("fabric", copy.GetProperty("loader").GetString());
            Assert.Equal("0.16.10", copy.GetProperty("loaderVersion").GetString());

            var copyOverrides = copy.GetProperty("overrides");
            Assert.Equal(4096, copyOverrides.GetProperty("memoryMaxMb").GetInt32());
            Assert.Equal("zgc", copyOverrides.GetProperty("gcMode").GetString());
            Assert.Equal(1600, copyOverrides.GetProperty("windowWidth").GetInt32());

            var copyRoot = copy.GetProperty("rootPath").GetString()!;
            Assert.True(Directory.Exists(copyRoot));
            Assert.NotEqual(Path.GetFullPath(sourceRoot), Path.GetFullPath(copyRoot));

            // Game files, mods and nested folders come along.
            Assert.Equal(
                "fov:80",
                await File.ReadAllTextAsync(Path.Combine(copyRoot, "options.txt")));
            Assert.Equal(
                "jar",
                await File.ReadAllTextAsync(Path.Combine(copyRoot, "mods", "example.jar")));
            Assert.Equal(
                "nested",
                await File.ReadAllTextAsync(
                    Path.Combine(copyRoot, "mods", "deep", "nested.jar")));
            Assert.Equal(
                "enabled = true",
                await File.ReadAllTextAsync(
                    Path.Combine(copyRoot, "config", "modpack", "settings.toml")));
            Assert.True(File.Exists(Path.Combine(copyRoot, ".archoera", "notes.json")));

            // Log history and the portable metadata are not inherited.
            Assert.False(Directory.Exists(Path.Combine(copyRoot, ".archoera", "logs")));
            using (var metadata = JsonDocument.Parse(
                       await File.ReadAllTextAsync(
                           Path.Combine(copyRoot, ".archoera", "instance.json"))))
            {
                Assert.Equal(copyId, metadata.RootElement.GetProperty("id").GetString());
                Assert.Equal(
                    "Source Pack Copy",
                    metadata.RootElement.GetProperty("name").GetString());
                Assert.Equal(
                    4096,
                    metadata.RootElement.GetProperty("overrides")
                        .GetProperty("memoryMaxMb")
                        .GetInt32());
            }

            // The source keeps its own metadata and log files untouched.
            using (var metadata = JsonDocument.Parse(
                       await File.ReadAllTextAsync(
                           Path.Combine(sourceRoot, ".archoera", "instance.json"))))
            {
                Assert.Equal(source.Id, metadata.RootElement.GetProperty("id").GetString());
            }
            Assert.True(File.Exists(
                Path.Combine(sourceRoot, ".archoera", "logs", "2026-10-23.log")));

            var items = await repository.ListAsync();
            Assert.Equal(2, items.Count);
            Assert.Contains(items, item => item.Id == copyId && item.Name == "Source Pack Copy");
        }
        finally
        {
            Delete(directory);
        }
    }

    [Fact]
    public async Task Duplicate_without_overrides_keeps_the_defaults()
    {
        var directory = TempDirectory();
        try
        {
            var dataRoot = Path.Combine(directory, "data");
            var settings = new JsonAppSettingsStore(dataRoot);
            var repository = new SqliteInstanceRepository(dataRoot, settings);
            var source = await repository.CreateAsync(
                "Plain",
                "1.21.4",
                "vanilla",
                string.Empty,
                Path.Combine(directory, "plain"));

            var service = new InstanceModuleService(repository, settings);
            using var document = JsonDocument.Parse(
                await service.HandleAsync(
                    "instance.duplicate",
                    $$"""{"id":"{{source.Id}}","name":"Plain Copy"}""",
                    CancellationToken.None));

            Assert.True(document.RootElement.GetProperty("ok").GetBoolean());
            var overrides = document.RootElement
                .GetProperty("data")
                .GetProperty("instance")
                .GetProperty("overrides");
            Assert.Empty(overrides.EnumerateObject());

            using var metadata = JsonDocument.Parse(
                await File.ReadAllTextAsync(
                    Path.Combine(
                        document.RootElement
                            .GetProperty("data")
                            .GetProperty("instance")
                            .GetProperty("rootPath")
                            .GetString()!,
                        ".archoera",
                        "instance.json")));
            Assert.False(metadata.RootElement.TryGetProperty("overrides", out _));
        }
        finally
        {
            Delete(directory);
        }
    }

    [Fact]
    public async Task Duplicate_rejects_an_unknown_instance_and_a_missing_directory()
    {
        var directory = TempDirectory();
        try
        {
            var dataRoot = Path.Combine(directory, "data");
            var settings = new JsonAppSettingsStore(dataRoot);
            var repository = new SqliteInstanceRepository(dataRoot, settings);
            var service = new InstanceModuleService(repository, settings);

            using (var unknown = JsonDocument.Parse(
                       await service.HandleAsync(
                           "instance.duplicate",
                           """{"id":"inst-missing","name":"Copy"}""",
                           CancellationToken.None)))
            {
                AssertError(unknown, "instance not found");
            }

            var sourceRoot = Path.Combine(directory, "gone");
            var source = await repository.CreateAsync(
                "Gone",
                "1.21.4",
                "vanilla",
                string.Empty,
                sourceRoot);
            Directory.Delete(sourceRoot, recursive: true);

            using var missing = JsonDocument.Parse(
                await service.HandleAsync(
                    "instance.duplicate",
                    $$"""{"id":"{{source.Id}}","name":"Gone Copy"}""",
                    CancellationToken.None));
            AssertError(missing, "instance directory is missing");
        }
        finally
        {
            Delete(directory);
        }
    }

    [Fact]
    public async Task Duplicate_rejects_an_empty_name()
    {
        var directory = TempDirectory();
        try
        {
            var dataRoot = Path.Combine(directory, "data");
            var settings = new JsonAppSettingsStore(dataRoot);
            var repository = new SqliteInstanceRepository(dataRoot, settings);
            var source = await repository.CreateAsync(
                "Named",
                "1.21.4",
                "vanilla",
                string.Empty,
                Path.Combine(directory, "named"));

            var service = new InstanceModuleService(repository, settings);
            using var document = JsonDocument.Parse(
                await service.HandleAsync(
                    "instance.duplicate",
                    $$"""{"id":"{{source.Id}}","name":"  "}""",
                    CancellationToken.None));

            Assert.False(document.RootElement.GetProperty("ok").GetBoolean());
            Assert.Equal(
                "E_INVALID",
                document.RootElement
                    .GetProperty("error")
                    .GetProperty("code")
                    .GetString());
            Assert.Single(await repository.ListAsync());
        }
        finally
        {
            Delete(directory);
        }
    }

    [Fact]
    public async Task Duplicate_of_an_existing_name_uses_a_distinct_directory()
    {
        var directory = TempDirectory();
        try
        {
            var dataRoot = Path.Combine(directory, "data");
            var settings = new JsonAppSettingsStore(dataRoot);
            var repository = new SqliteInstanceRepository(dataRoot, settings);
            // No explicit root: the repository owns the versions/<name> layout.
            var source = await repository.CreateAsync(
                "Same Name",
                "1.21.4",
                "vanilla",
                string.Empty,
                rootPath: null);
            Write(source.RootPath, "options.txt", "fov:80");

            var service = new InstanceModuleService(repository, settings);
            using var document = JsonDocument.Parse(
                await service.HandleAsync(
                    "instance.duplicate",
                    $$"""{"id":"{{source.Id}}","name":"Same Name"}""",
                    CancellationToken.None));

            Assert.True(document.RootElement.GetProperty("ok").GetBoolean());
            var copyRoot = document.RootElement
                .GetProperty("data")
                .GetProperty("instance")
                .GetProperty("rootPath")
                .GetString()!;
            Assert.True(Directory.Exists(copyRoot));
            Assert.NotEqual(Path.GetFullPath(source.RootPath), Path.GetFullPath(copyRoot));
            Assert.Equal("Same Name (2)", Path.GetFileName(copyRoot));
            Assert.True(Directory.Exists(source.RootPath));
            Assert.Equal(
                "fov:80",
                await File.ReadAllTextAsync(Path.Combine(copyRoot, "options.txt")));
            Assert.Equal(2, (await repository.ListAsync()).Count);
        }
        finally
        {
            Delete(directory);
        }
    }

    [Fact]
    public async Task Duplicate_rejects_a_target_that_resolves_onto_the_source()
    {
        var directory = TempDirectory();
        try
        {
            var dataRoot = Path.Combine(directory, "data");
            var settings = new JsonAppSettingsStore(dataRoot);
            var storage = new JsonInstanceRepository(dataRoot, settings);
            var instancesRoot = (await settings.LoadAsync()).Roots[0];
            var sourceRoot = Path.Combine(instancesRoot, "versions", "self");
            var source = await storage.CreateAsync(
                "Self",
                "1.21.4",
                "vanilla",
                string.Empty,
                sourceRoot);
            Write(sourceRoot, "options.txt", "fov:80");

            // A custom/legacy repository can still resolve a new name onto an
            // existing root; the duplicate handler must reject that overlap.
            var repository = new PinnedRootInstances(source, sourceRoot);
            var service = new InstanceModuleService(repository, settings);
            using var document = JsonDocument.Parse(
                await service.HandleAsync(
                    "instance.duplicate",
                    $$"""{"id":"{{source.Id}}","name":"self"}""",
                    CancellationToken.None));

            Assert.False(document.RootElement.GetProperty("ok").GetBoolean());
            var error = document.RootElement.GetProperty("error");
            Assert.Equal("E_INVALID", error.GetProperty("code").GetString());
            Assert.Contains(
                "outside the source",
                error.GetProperty("message").GetString(),
                StringComparison.Ordinal);

            // The rejection leaves the source instance and its metadata intact
            // and does not register a second instance.
            var items = await repository.ListAsync();
            var single = Assert.Single(items);
            Assert.Equal(source.Id, single.Id);
            Assert.Equal("Self", single.Name);
            Assert.Equal("fov:80", await File.ReadAllTextAsync(
                Path.Combine(sourceRoot, "options.txt")));
            using var metadata = JsonDocument.Parse(
                await File.ReadAllTextAsync(
                    Path.Combine(sourceRoot, ".archoera", "instance.json")));
            Assert.Equal(source.Id, metadata.RootElement.GetProperty("id").GetString());
        }
        finally
        {
            Delete(directory);
        }
    }

    [Fact]
    public async Task Json_repository_gives_duplicate_names_distinct_directories()
    {
        var directory = TempDirectory();
        try
        {
            var dataRoot = Path.Combine(directory, "data");
            var settings = new JsonAppSettingsStore(dataRoot);
            var repository = new JsonInstanceRepository(dataRoot, settings);

            var first = await repository.CreateAsync(
                "Same Name",
                "1.21.4",
                "vanilla",
                string.Empty,
                rootPath: null);
            var second = await repository.CreateAsync(
                "Same Name",
                "1.21.4",
                "vanilla",
                string.Empty,
                rootPath: null);

            Assert.NotEqual(first.RootPath, second.RootPath);
            Assert.Equal("Same Name (2)", Path.GetFileName(second.RootPath));
            Assert.True(Directory.Exists(second.RootPath));
            Assert.Equal(2, (await repository.ListAsync()).Count);
        }
        finally
        {
            Delete(directory);
        }
    }

    [Fact]
    public async Task Duplicate_renames_the_instance_owned_launch_profile()
    {
        var directory = TempDirectory();
        try
        {
            var dataRoot = Path.Combine(directory, "data");
            var settings = new JsonAppSettingsStore(dataRoot);
            var repository = new SqliteInstanceRepository(dataRoot, settings);
            var sourceRoot = Path.Combine(directory, "game", "source");
            var source = await repository.CreateAsync(
                "Profiled",
                "1.21.1",
                "forge",
                "47.2.0",
                sourceRoot);
            Write(
                sourceRoot,
                "source.json",
                """{"id":"1.21.1-forge-47.2.0","mainClass":"cpw.mods.bootstraplauncher.BootstrapLauncher"}""");

            var service = new InstanceModuleService(repository, settings);
            using var document = JsonDocument.Parse(
                await service.HandleAsync(
                    "instance.duplicate",
                    $$"""{"id":"{{source.Id}}","name":"Profiled Copy"}""",
                    CancellationToken.None));

            Assert.True(document.RootElement.GetProperty("ok").GetBoolean());
            var copyRoot = document.RootElement
                .GetProperty("data")
                .GetProperty("instance")
                .GetProperty("rootPath")
                .GetString()!;
            var targetName = Path.GetFileName(
                Path.TrimEndingDirectorySeparator(copyRoot));
            Assert.True(File.Exists(Path.Combine(copyRoot, $"{targetName}.json")));
            Assert.False(File.Exists(Path.Combine(copyRoot, "source.json")));
        }
        finally
        {
            Delete(directory);
        }
    }

    private sealed class PinnedRootInstances(
        GameInstanceRecord source,
        string pinnedRoot) : IInstanceRepository
    {
        private readonly List<GameInstanceRecord> _items = [source];
        private int _created;

        public Task<IReadOnlyList<GameInstanceRecord>> ListAsync(
            CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<GameInstanceRecord>>(_items.ToArray());

        public Task<GameInstanceRecord> CreateAsync(
            string name,
            string gameVersion,
            string loader,
            string loaderVersion,
            string? rootPath,
            CancellationToken cancellationToken = default)
        {
            var now = DateTimeOffset.UtcNow;
            var record = source with
            {
                Id = $"inst-pinned-{_created++}",
                Name = name,
                GameVersion = gameVersion,
                Loader = loader,
                LoaderVersion = loaderVersion,
                RootPath = pinnedRoot,
                CreatedAt = now,
                UpdatedAt = now,
            };
            _items.Add(record);
            return Task.FromResult(record);
        }

        public Task<GameInstanceRecord?> UpdateAsync(
            string id,
            string? name,
            bool? favorite,
            DateTimeOffset? lastPlayedAt,
            InstanceOverrides? overrides = null,
            bool setOverrides = false,
            string? loader = null,
            string? loaderVersion = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult<GameInstanceRecord?>(
                _items.FirstOrDefault(item => item.Id == id));

        public Task<bool> DeleteAsync(
            string id,
            CancellationToken cancellationToken = default)
            => Task.FromResult(
                _items.RemoveAll(item => string.Equals(item.Id, id, StringComparison.Ordinal)) > 0);

        public Task<InstanceSyncReport> SyncAsync(
            IReadOnlyList<string>? roots,
            bool dryRun,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new InstanceSyncReport(
                _items.ToArray(),
                [],
                [],
                [],
                []));
    }

    private static void AssertError(JsonDocument document, string messagePart)
    {
        Assert.False(document.RootElement.GetProperty("ok").GetBoolean());
        var error = document.RootElement.GetProperty("error");
        Assert.Equal("E_INVALID", error.GetProperty("code").GetString());
        Assert.Contains(
            messagePart,
            error.GetProperty("message").GetString(),
            StringComparison.Ordinal);
    }

    private static void Write(string root, string relativePath, string content)
    {
        var path = Path.Combine(root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    private static string TempDirectory()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            "archoera-instance-duplicate",
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
