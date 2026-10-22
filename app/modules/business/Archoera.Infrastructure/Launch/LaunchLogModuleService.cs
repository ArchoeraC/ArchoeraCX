using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Archoera.Application.Instances;
using Archoera.Application.Modules;
using Archoera.Domain.Instances;
using Archoera.Infrastructure.Instances;
using Archoera.Infrastructure.Modules;

namespace Archoera.Infrastructure.Launch;

/// <summary>
/// Game log access for the launch log viewer.
///
/// The Flutter UI may not touch files, so every read goes through these
/// methods. Logs are plain text written by <see cref="LaunchModuleService"/> to
/// <c>&lt;instance&gt;/.archoera/logs/launch-*.log</c>. Retention is bounded
/// (<see cref="LaunchLogFiles"/>): only the newest launches are kept, and the
/// oldest files are pruned on the next start.
///
/// <c>launch.log.tail</c> streams appended lines through the ABI progress
/// callback (one message per batch) instead of being polled from Dart, so the
/// viewer stays wake-driven: C# pushes, C++ wakes, Dart drains.
/// </summary>
public sealed class LaunchLogModuleService : IModuleMethodHandler
{
    private static readonly string[] SupportedMethods =
        ["launch.logs", "launch.log.read", "launch.log.tail"];

    /// <summary>How many past launches are offered in the file picker.</summary>
    private const int MaxLogFiles = 50;

    private const int DefaultLines = 400;
    private const int MaxLines = 5000;

    /// <summary>Longest single log line forwarded to the UI.</summary>
    private const int MaxLineLength = 4000;

    /// <summary>Bytes of an existing log read before following starts.</summary>
    private const int InitialTailBytes = 256 * 1024;

    /// <summary>Maximum characters forwarded in one progress event.</summary>
    private const int MaxChunkLength = 16 * 1024;

    /// <summary>Follow interval; also the upper bound of log latency.</summary>
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(250);

    /// <summary>Hard cap so a forgotten viewer cannot pin a task forever.</summary>
    private static readonly TimeSpan MaxFollowDuration = TimeSpan.FromHours(6);

    private readonly IInstanceRepository _instances;

    public LaunchLogModuleService(IInstanceRepository? instances = null)
    {
        _instances = instances ?? new SqliteInstanceRepository();
    }

    public IReadOnlyCollection<string> Methods => SupportedMethods;

    public async Task<byte[]> HandleAsync(
        string method,
        string? paramsJson,
        CancellationToken cancellationToken)
    {
        try
        {
            return method switch
            {
                "launch.logs" => await ListAsync(paramsJson, cancellationToken)
                    .ConfigureAwait(false),
                "launch.log.read" => await ReadAsync(paramsJson, cancellationToken)
                    .ConfigureAwait(false),
                "launch.log.tail" => await TailAsync(paramsJson, cancellationToken)
                    .ConfigureAwait(false),
                _ => ModuleJson.Error(
                    "E_UNSUPPORTED",
                    $"unsupported log method '{method}'"),
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (LaunchException exception)
        {
            return ModuleJson.Error(exception.Code, exception.Message);
        }
        catch (ArgumentException exception)
        {
            return ModuleJson.Error("E_INVALID", exception.Message);
        }
        catch (IOException exception)
        {
            return ModuleJson.Error("LOG_UNREADABLE", exception.Message);
        }
        catch (UnauthorizedAccessException exception)
        {
            return ModuleJson.Error("LOG_UNREADABLE", exception.Message);
        }
        catch (JsonException exception)
        {
            return ModuleJson.Error("E_INVALID", exception.Message);
        }
    }

    private async Task<byte[]> ListAsync(
        string? paramsJson,
        CancellationToken cancellationToken)
    {
        var target = await ResolveAsync(paramsJson, cancellationToken)
            .ConfigureAwait(false);
        var running = LaunchProcessRegistry.TryGet(target.Instance.Id);
        var all = EnumerateLogs(target.Directory).ToList();
        var files = all.Take(MaxLogFiles).ToList();
        var totalSize = all.Sum(SafeLength);

        return ModuleJson.Envelope(writer =>
        {
            writer.WriteString("instanceId", target.Instance.Id);
            writer.WriteString("instanceName", target.Instance.Name);
            writer.WriteString("logDirectory", target.Directory);
            writer.WriteBoolean("running", running is not null);
            writer.WriteNumber("totalSize", totalSize);
            writer.WriteNumber("retentionMaxFiles", LaunchLogFiles.MaxFiles);
            writer.WriteNumber("retentionMaxBytes", LaunchLogFiles.MaxTotalBytes);
            if (running is not null)
            {
                writer.WriteNumber("pid", running.Pid);
                writer.WriteString("activeLog", Path.GetFileName(running.LogPath));
            }

            writer.WriteStartArray("logs");
            foreach (var file in files)
            {
                writer.WriteStartObject();
                writer.WriteString("fileName", file.Name);
                writer.WriteNumber("size", file.Length);
                writer.WriteString("modifiedAt", file.LastWriteTimeUtc.ToString("O"));
                writer.WriteBoolean(
                    "active",
                    running is not null &&
                    string.Equals(
                        Path.GetFileName(running.LogPath),
                        file.Name,
                        StringComparison.Ordinal));
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
        });
    }

    private async Task<byte[]> ReadAsync(
        string? paramsJson,
        CancellationToken cancellationToken)
    {
        var target = await ResolveAsync(paramsJson, cancellationToken)
            .ConfigureAwait(false);
        var lines = Math.Clamp(ReadInt(paramsJson, "lines") ?? DefaultLines, 1, MaxLines);
        var path = ResolveLogPath(paramsJson, target);

        var snapshot = path is null
            ? new LogSnapshot(null, [], false, 0)
            : ReadTail(path, lines);
        var running = LaunchProcessRegistry.TryGet(target.Instance.Id);

        return ModuleJson.Envelope(writer =>
        {
            writer.WriteString("instanceId", target.Instance.Id);
            writer.WriteString("instanceName", target.Instance.Name);
            writer.WriteString("logDirectory", target.Directory);
            if (snapshot.FileName is null)
            {
                writer.WriteNull("fileName");
            }
            else
            {
                writer.WriteString("fileName", snapshot.FileName);
            }

            writer.WriteNumber("size", snapshot.Size);
            writer.WriteBoolean("truncated", snapshot.Truncated);
            writer.WriteBoolean("running", running is not null);
            writer.WriteStartArray("lines");
            foreach (var line in snapshot.Lines)
            {
                writer.WriteStringValue(line);
            }

            writer.WriteEndArray();
        });
    }

    private async Task<byte[]> TailAsync(
        string? paramsJson,
        CancellationToken cancellationToken)
    {
        var target = await ResolveAsync(paramsJson, cancellationToken)
            .ConfigureAwait(false);
        var lines = Math.Clamp(ReadInt(paramsJson, "lines") ?? DefaultLines, 1, MaxLines);
        var follow = ReadBool(paramsJson, "follow");
        var path = ResolveLogPath(paramsJson, target);

        if (path is null)
        {
            return ModuleJson.Envelope(writer =>
            {
                writer.WriteString("instanceId", target.Instance.Id);
                writer.WriteNull("fileName");
                writer.WriteNumber("lines", 0);
                writer.WriteBoolean("running", false);
                writer.WriteBoolean("followed", false);
            });
        }

        var fileName = Path.GetFileName(path);
        var forwarded = 0;
        using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);

        // Start with the tail so a viewer opened mid-session shows context, then
        // switch to appending bytes only.
        stream.Seek(Math.Max(0, stream.Length - InitialTailBytes), SeekOrigin.Begin);
        var buffer = new byte[8 * 1024];
        var carry = new List<byte>(16 * 1024);

        void Emit(string chunk)
        {
            if (chunk.Length == 0) return;
            forwarded += CountLines(chunk);
            ModuleProgress.Report(stream.Length, stream.Length, 0, chunk);
        }

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var read = stream.Read(buffer, 0, buffer.Length);
            if (read <= 0) break;
            AppendCompleteLines(buffer.AsSpan(0, read), carry, Emit);
        }

        // A log that does not end with a newline still has a visible last line.
        FlushCarry(carry, Emit);

        var running = LaunchProcessRegistry.TryGet(target.Instance.Id);
        if (!follow || running is null)
        {
            return Summary(target, fileName, forwarded, running is not null, false);
        }

        var deadline = DateTimeOffset.UtcNow + MaxFollowDuration;
        var followed = true;
        while (DateTimeOffset.UtcNow < deadline)
        {
            await Task.Delay(PollInterval, cancellationToken).ConfigureAwait(false);
            var appended = false;
            while (true)
            {
                var read = stream.Read(buffer, 0, buffer.Length);
                if (read <= 0) break;
                appended = true;
                AppendCompleteLines(buffer.AsSpan(0, read), carry, Emit);
            }

            FlushCarry(carry, Emit);
            if (LaunchProcessRegistry.TryGet(target.Instance.Id) is not null) continue;

            // The process ended: drain once more, then stop following.
            if (appended) continue;
            followed = false;
            break;
        }

        return Summary(
            target,
            fileName,
            forwarded,
            LaunchProcessRegistry.TryGet(target.Instance.Id) is not null,
            followed);
    }

    private static byte[] Summary(
        LogTarget target,
        string fileName,
        int lines,
        bool running,
        bool followed)
        => ModuleJson.Envelope(writer =>
        {
            writer.WriteString("instanceId", target.Instance.Id);
            writer.WriteString("fileName", fileName);
            writer.WriteNumber("lines", lines);
            writer.WriteBoolean("running", running);
            writer.WriteBoolean("followed", followed);
        });

    /// <summary>
    /// Splits newly read bytes into whole lines and forwards them in batches.
    /// A trailing partial line is kept for the next read.
    /// </summary>
    private static void AppendCompleteLines(
        ReadOnlySpan<byte> data,
        List<byte> carry,
        Action<string> emit)
    {
        foreach (var value in data)
        {
            carry.Add(value);
        }

        var batch = new StringBuilder();
        var start = 0;
        for (var index = 0; index < carry.Count; index++)
        {
            // A newline byte can never be part of a multi-byte UTF-8 sequence,
            // so decoding each complete line is safe even across reads.
            if (carry[index] != (byte)'\n') continue;
            batch.Append(ClampLine(Decode(carry, start, index - start))).Append('\n');
            start = index + 1;
            if (batch.Length < MaxChunkLength) continue;
            emit(batch.ToString().TrimEnd('\n'));
            batch.Clear();
        }

        carry.RemoveRange(0, start);
        if (batch.Length > 0) emit(batch.ToString().TrimEnd('\n'));
    }

    private static void FlushCarry(List<byte> carry, Action<string> emit)
    {
        if (carry.Count == 0) return;
        emit(ClampLine(Decode(carry, 0, carry.Count)));
        carry.Clear();
    }

    private static string Decode(List<byte> bytes, int index, int count)
    {
        if (count <= 0) return string.Empty;
        var span = CollectionsMarshal.AsSpan(bytes).Slice(index, count);
        return Encoding.UTF8.GetString(span).TrimEnd('\r');
    }

    private static string ClampLine(string line)
        => line.Length <= MaxLineLength ? line : line[..MaxLineLength] + "…";

    private static int CountLines(string chunk)
    {
        var count = 1;
        foreach (var character in chunk)
        {
            if (character == '\n') count++;
        }

        return count;
    }

    /// <summary>
    /// Reads the last <paramref name="lineCount"/> lines of a log without
    /// loading the whole file into memory.
    /// </summary>
    private static LogSnapshot ReadTail(string path, int lineCount)
    {
        using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);
        var size = stream.Length;
        var start = Math.Max(0, size - InitialTailBytes);
        stream.Seek(start, SeekOrigin.Begin);
        var bytes = new byte[size - start];
        var offset = 0;
        while (offset < bytes.Length)
        {
            var read = stream.Read(bytes, offset, bytes.Length - offset);
            if (read <= 0) break;
            offset += read;
        }

        var text = Encoding.UTF8.GetString(bytes, 0, offset)
            .Replace("\r\n", "\n", StringComparison.Ordinal);
        var all = text.Split('\n');
        // A read that started mid-line loses that first fragment.
        var first = start > 0 ? 1 : 0;
        var end = all.Length;
        if (end > first && all[^1].Length == 0) end--;
        var available = Math.Max(0, end - first);
        var take = Math.Min(lineCount, available);
        var lines = new List<string>(take);
        for (var index = available - take; index < available; index++)
        {
            lines.Add(ClampLine(all[first + index]));
        }

        return new LogSnapshot(Path.GetFileName(path), lines, start > 0, size);
    }

    private static IEnumerable<FileInfo> EnumerateLogs(string directory)
    {
        if (!Directory.Exists(directory)) return [];
        try
        {
            return new DirectoryInfo(directory)
                .GetFiles("launch-*.log", SearchOption.TopDirectoryOnly)
                .OrderByDescending(file => file.LastWriteTimeUtc)
                .ThenByDescending(file => file.Name, StringComparer.Ordinal)
                .ToList();
        }
        catch (Exception exception) when (
            exception is DirectoryNotFoundException or IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private static long SafeLength(FileInfo file)
    {
        try
        {
            return file.Length;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            return 0;
        }
    }

    /// <summary>
    /// Picks the requested log, else the running launch's log, else the newest.
    /// A caller-supplied name is treated as a bare file name inside the log
    /// directory; separators and traversal are rejected.
    /// </summary>
    private static string? ResolveLogPath(string? paramsJson, LogTarget target)
    {
        var requested = ReadOptional(paramsJson, "fileName");
        if (!string.IsNullOrWhiteSpace(requested))
        {
            if (Path.GetFileName(requested) != requested ||
                requested.Contains("..", StringComparison.Ordinal))
            {
                throw new ArgumentException("fileName must be a plain log file name");
            }

            var candidate = Path.Combine(target.Directory, requested);
            if (!File.Exists(candidate))
            {
                throw new LaunchException(
                    "LOG_NOT_FOUND",
                    $"log file not found: {requested}");
            }

            return candidate;
        }

        var running = LaunchProcessRegistry.TryGet(target.Instance.Id);
        if (running is not null && File.Exists(running.LogPath))
        {
            return running.LogPath;
        }

        return EnumerateLogs(target.Directory).FirstOrDefault()?.FullName;
    }

    private async Task<LogTarget> ResolveAsync(
        string? paramsJson,
        CancellationToken cancellationToken)
    {
        var instanceId = ReadRequired(paramsJson, "instanceId");
        var instance = (await _instances
                .ListAsync(cancellationToken)
                .ConfigureAwait(false))
            .FirstOrDefault(item => string.Equals(item.Id, instanceId, StringComparison.Ordinal))
            ?? throw new LaunchException(
                "INSTANCE_NOT_FOUND",
                $"instance not found: {instanceId}");

        var paths = LaunchPaths.Resolve(instance.RootPath);
        return new LogTarget(
            instance,
            Path.Combine(paths.InstanceRoot, ".archoera", "logs"));
    }

    private static bool ReadBool(string? json, string name)
    {
        if (string.IsNullOrWhiteSpace(json)) return false;
        using var document = JsonDocument.Parse(json);
        return document.RootElement.TryGetProperty(name, out var element) &&
               element.ValueKind is JsonValueKind.True or JsonValueKind.False &&
               element.GetBoolean();
    }

    private static int? ReadInt(string? json, string name)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty(name, out var element)) return null;
        return element.ValueKind == JsonValueKind.Number && element.TryGetInt32(out var value)
            ? value
            : null;
    }

    private static string? ReadOptional(string? json, string name)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        using var document = JsonDocument.Parse(json);
        return document.RootElement.TryGetProperty(name, out var element) &&
               element.ValueKind == JsonValueKind.String
            ? element.GetString()
            : null;
    }

    private static string ReadRequired(string? json, string name)
    {
        var value = ReadOptional(json, name);
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"{name} is required");
        }

        return value;
    }

    private sealed record LogTarget(GameInstanceRecord Instance, string Directory);

    private sealed record LogSnapshot(
        string? FileName,
        IReadOnlyList<string> Lines,
        bool Truncated,
        long Size);
}
