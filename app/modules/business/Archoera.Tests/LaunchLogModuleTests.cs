using System.Text;
using System.Text.Json;
using Archoera.Application.Instances;
using Archoera.Application.Modules;
using Archoera.Domain.Instances;
using Archoera.Infrastructure.Launch;

namespace Archoera.Tests;

/// <summary>
/// Game log access for the log viewer. The UI cannot read files, so these
/// methods are the only path to a launch log.
/// </summary>
public sealed class LaunchLogModuleTests
{
    [Fact]
    public async Task Logs_lists_recent_launches_newest_first()
    {
        var root = TempDirectory();
        try
        {
            var logDirectory = Path.Combine(root, ".archoera", "logs");
            Directory.CreateDirectory(logDirectory);
            var older = Path.Combine(logDirectory, "launch-20260101-120000.log");
            var newer = Path.Combine(logDirectory, "launch-20260102-120000.log");
            File.WriteAllText(older, "old\n");
            File.WriteAllText(newer, "new\n");
            File.SetLastWriteTimeUtc(older, DateTime.UtcNow.AddHours(-2));
            File.SetLastWriteTimeUtc(newer, DateTime.UtcNow.AddHours(-1));
            // Unrelated files in the same directory must not be offered.
            File.WriteAllText(Path.Combine(logDirectory, "notes.txt"), "x");

            var service = CreateService(root);

            var data = await CallAsync(service, "launch.logs", """{"instanceId":"instance"}""");

            var logs = data.GetProperty("logs");
            Assert.Equal(2, logs.GetArrayLength());
            Assert.Equal("launch-20260102-120000.log", logs[0].GetProperty("fileName").GetString());
            Assert.Equal("launch-20260101-120000.log", logs[1].GetProperty("fileName").GetString());
            Assert.False(data.GetProperty("running").GetBoolean());
            Assert.Equal(logDirectory, data.GetProperty("logDirectory").GetString());
            Assert.Equal(
                new FileInfo(older).Length + new FileInfo(newer).Length,
                data.GetProperty("totalSize").GetInt64());
            Assert.Equal(10, data.GetProperty("retentionMaxFiles").GetInt32());
            Assert.Equal(
                32L * 1024 * 1024,
                data.GetProperty("retentionMaxBytes").GetInt64());
        }
        finally
        {
            Delete(root);
        }
    }

    [Fact]
    public async Task Logs_returns_an_empty_list_when_nothing_was_launched()
    {
        var root = TempDirectory();
        try
        {
            var service = CreateService(root);

            var data = await CallAsync(service, "launch.logs", """{"instanceId":"instance"}""");

            Assert.Equal(0, data.GetProperty("logs").GetArrayLength());
            Assert.False(data.GetProperty("running").GetBoolean());
        }
        finally
        {
            Delete(root);
        }
    }

    [Fact]
    public async Task Read_returns_the_requested_tail()
    {
        var root = TempDirectory();
        try
        {
            var path = WriteLog(root, "launch-20260101-120000.log", 100);
            var service = CreateService(root);

            var data = await CallAsync(
                service,
                "launch.log.read",
                """{"instanceId":"instance","lines":3}""");

            var lines = data.GetProperty("lines");
            Assert.Equal(3, lines.GetArrayLength());
            Assert.Equal("line 98", lines[0].GetString());
            Assert.Equal("line 99", lines[1].GetString());
            Assert.Equal("line 100", lines[2].GetString());
            Assert.Equal("launch-20260101-120000.log", data.GetProperty("fileName").GetString());
            Assert.Equal(new FileInfo(path).Length, data.GetProperty("size").GetInt64());
        }
        finally
        {
            Delete(root);
        }
    }

    [Fact]
    public async Task Read_flags_a_log_that_was_cut_to_the_tail_window()
    {
        var root = TempDirectory();
        try
        {
            // 1000 lines of 400 characters exceed the 256 KiB tail window.
            WriteLog(root, "launch-20260101-120000.log", 1000, padding: 400);
            var service = CreateService(root);

            var data = await CallAsync(
                service,
                "launch.log.read",
                """{"instanceId":"instance","lines":5}""");

            Assert.True(data.GetProperty("truncated").GetBoolean());
            var lines = data.GetProperty("lines");
            Assert.Equal(5, lines.GetArrayLength());
            Assert.StartsWith("line 996", lines[0].GetString());
        }
        finally
        {
            Delete(root);
        }
    }

    [Fact]
    public async Task Read_rejects_a_path_outside_the_log_directory()
    {
        var root = TempDirectory();
        try
        {
            WriteLog(root, "launch-20260101-120000.log", 5);
            File.WriteAllText(Path.Combine(root, "secret.log"), "secret");
            var service = CreateService(root);

            var result = await service.HandleAsync(
                "launch.log.read",
                """{"instanceId":"instance","fileName":"../secret.log"}""",
                CancellationToken.None);

            using var document = JsonDocument.Parse(result);
            Assert.False(document.RootElement.GetProperty("ok").GetBoolean());
            Assert.Equal(
                "E_INVALID",
                document.RootElement.GetProperty("error").GetProperty("code").GetString());
        }
        finally
        {
            Delete(root);
        }
    }

    [Fact]
    public async Task Read_reports_a_missing_named_log()
    {
        var root = TempDirectory();
        try
        {
            WriteLog(root, "launch-20260101-120000.log", 5);
            var service = CreateService(root);

            var result = await service.HandleAsync(
                "launch.log.read",
                """{"instanceId":"instance","fileName":"launch-19990101-000000.log"}""",
                CancellationToken.None);

            using var document = JsonDocument.Parse(result);
            Assert.Equal(
                "LOG_NOT_FOUND",
                document.RootElement.GetProperty("error").GetProperty("code").GetString());
        }
        finally
        {
            Delete(root);
        }
    }

    [Fact]
    public async Task Read_returns_no_lines_when_the_instance_has_no_log()
    {
        var root = TempDirectory();
        try
        {
            var service = CreateService(root);

            var data = await CallAsync(
                service,
                "launch.log.read",
                """{"instanceId":"instance"}""");

            Assert.Equal(0, data.GetProperty("lines").GetArrayLength());
            Assert.Equal(JsonValueKind.Null, data.GetProperty("fileName").ValueKind);
        }
        finally
        {
            Delete(root);
        }
    }

    [Fact]
    public async Task Tail_streams_lines_appended_while_the_game_runs()
    {
        var root = TempDirectory();
        var pid = Environment.ProcessId;
        try
        {
            var path = WriteLog(root, "launch-20260101-120000.log", 2);
            var service = CreateService(root);
            LaunchProcessRegistry.Register("instance", pid, path);

            var received = new List<string>();
            using var scope = ModuleProgress.Begin((_, _, _, message) =>
            {
                if (message is not null) lock (received) received.Add(message);
            });
            using var cancellation = new CancellationTokenSource();
            var tail = service.HandleAsync(
                "launch.log.tail",
                """{"instanceId":"instance","follow":true}""",
                cancellation.Token);

            await WaitForAsync(
                () => Snapshot(received).Any(chunk => chunk.Contains("line 2", StringComparison.Ordinal)));
            File.AppendAllText(path, "late line\n");
            await WaitForAsync(
                () => Snapshot(received).Any(chunk => chunk.Contains("late line", StringComparison.Ordinal)));

            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => tail);
        }
        finally
        {
            LaunchProcessRegistry.Unregister("instance", pid);
            Delete(root);
        }
    }

    [Fact]
    public async Task Tail_without_follow_returns_the_existing_lines()
    {
        var root = TempDirectory();
        try
        {
            WriteLog(root, "launch-20260101-120000.log", 4);
            var service = CreateService(root);

            var received = new List<string>();
            using var scope = ModuleProgress.Begin((_, _, _, message) =>
            {
                if (message is not null) lock (received) received.Add(message);
            });

            var data = await CallAsync(
                service,
                "launch.log.tail",
                """{"instanceId":"instance"}""");

            Assert.Equal(4, data.GetProperty("lines").GetInt32());
            Assert.False(data.GetProperty("followed").GetBoolean());
            var text = string.Join('\n', Snapshot(received));
            Assert.Contains("line 1", text, StringComparison.Ordinal);
            Assert.Contains("line 4", text, StringComparison.Ordinal);
        }
        finally
        {
            Delete(root);
        }
    }

    [Fact]
    public async Task Clear_deletes_archived_logs_and_keeps_unrelated_files()
    {
        var root = TempDirectory();
        try
        {
            var logDirectory = Path.Combine(root, ".archoera", "logs");
            Directory.CreateDirectory(logDirectory);
            var first = Path.Combine(logDirectory, "launch-20260101-120000.log");
            var second = Path.Combine(logDirectory, "launch-20260102-120000.log");
            File.WriteAllText(first, "old\n");
            File.WriteAllText(second, "new\n");
            var notes = Path.Combine(logDirectory, "notes.txt");
            File.WriteAllText(notes, "keep me");

            var service = CreateService(root);
            var data = await CallAsync(
                service,
                "launch.logs.clear",
                """{"instanceId":"instance"}""");

            Assert.Equal(2, data.GetProperty("deletedFiles").GetInt32());
            Assert.True(data.GetProperty("freedBytes").GetInt64() > 0);
            Assert.Equal(1, data.GetProperty("instances").GetInt32());
            Assert.False(File.Exists(first));
            Assert.False(File.Exists(second));
            Assert.True(File.Exists(notes));

            // Nothing left to delete on the second pass.
            var again = await CallAsync(
                service,
                "launch.logs.clear",
                """{"instanceId":"instance"}""");
            Assert.Equal(0, again.GetProperty("deletedFiles").GetInt32());
            Assert.Equal(0, again.GetProperty("instances").GetInt32());
        }
        finally
        {
            Delete(root);
        }
    }

    [Fact]
    public async Task Clear_all_sweeps_the_known_instances()
    {
        var root = TempDirectory();
        try
        {
            var logDirectory = Path.Combine(root, ".archoera", "logs");
            Directory.CreateDirectory(logDirectory);
            var log = Path.Combine(logDirectory, "launch-20260103-120000.log");
            File.WriteAllText(log, "line\n");

            var service = CreateService(root);
            var data = await CallAsync(service, "launch.logs.clear", """{"all":true}""");

            Assert.Equal(1, data.GetProperty("deletedFiles").GetInt32());
            Assert.Equal(1, data.GetProperty("instances").GetInt32());
            Assert.False(File.Exists(log));
        }
        finally
        {
            Delete(root);
        }
    }

    private static LaunchLogModuleService CreateService(string root)
        => new(new StubInstances(Instance(root)));

    private static GameInstanceRecord Instance(string root)
    {
        var now = DateTimeOffset.UtcNow;
        return new GameInstanceRecord(
            "instance",
            "AutumnTown",
            "1.21.1",
            "neoforge",
            "21.1.235",
            root,
            0,
            false,
            now,
            now,
            null);
    }

    private static string WriteLog(
        string root,
        string name,
        int lines,
        int padding = 0)
    {
        var directory = Path.Combine(root, ".archoera", "logs");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, name);
        var builder = new StringBuilder();
        for (var index = 1; index <= lines; index++)
        {
            builder.Append("line ").Append(index);
            if (padding > 0) builder.Append(' ').Append('x', padding);
            builder.Append('\n');
        }

        File.WriteAllText(path, builder.ToString());
        return path;
    }

    private static async Task<JsonElement> CallAsync(
        LaunchLogModuleService service,
        string method,
        string paramsJson)
    {
        var result = await service.HandleAsync(method, paramsJson, CancellationToken.None);
        var document = JsonDocument.Parse(result);
        Assert.True(
            document.RootElement.GetProperty("ok").GetBoolean(),
            document.RootElement.ToString());
        return document.RootElement.GetProperty("data").Clone();
    }

    private static async Task WaitForAsync(Func<bool> condition)
    {
        for (var attempt = 0; attempt < 60; attempt++)
        {
            if (condition()) return;
            await Task.Delay(100);
        }

        Assert.Fail("condition was not reached in time");
    }

    private static List<string> Snapshot(List<string> received)
    {
        lock (received)
        {
            return received.ToList();
        }
    }

    private static string TempDirectory()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            "archoera-launch-log-" + Guid.NewGuid().ToString("N"));
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
            Archoera.Domain.Instances.InstanceOverrides? overrides = null,
            bool setOverrides = false,
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
