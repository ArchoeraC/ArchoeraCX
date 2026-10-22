using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Json;
using Archoera.Application.Modules;

namespace Archoera.Infrastructure.SystemInfo;

/// <summary>
/// Native system facts used by the Wiki page and settings UI. This stays in C#
/// so Flutter never reads /proc, the registry, or platform memory APIs.
/// </summary>
public sealed class SystemModuleService : IModuleMethodHandler
{
    private static readonly string[] SupportedMethods = ["system.memory", "system.status"];

    /// <summary>CPU sampling window for a one-shot load reading.</summary>
    private static readonly TimeSpan CpuSampleWindow = TimeSpan.FromMilliseconds(150);

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
                "system.memory" => Envelope(writer => WriteMemory(writer)),
                "system.status" => await StatusAsync(paramsJson, cancellationToken)
                    .ConfigureAwait(false),
                _ => Error("E_UNSUPPORTED", $"unsupported system method '{method}'"),
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            return Error("E_INTERNAL", exception.Message);
        }
    }

    private static void WriteMemory(Utf8JsonWriter writer)
    {
        var (totalMb, availableMb) = ReadMemory();
        writer.WriteNumber("totalMb", totalMb);
        if (availableMb is { } available) writer.WriteNumber("availableMb", available);
        else writer.WriteNull("availableMb");
        writer.WriteString("platform", PlatformName());
    }

    /// <summary>
    /// Snapshot of machine load for the Wiki page: CPU, memory, disk, launcher
    /// process and uptime. Unknown values are written as null so the UI can
    /// hide a row instead of showing a fake zero.
    /// </summary>
    private async Task<byte[]> StatusAsync(
        string? paramsJson,
        CancellationToken cancellationToken)
    {
        var path = ReadOptional(paramsJson, "path");
        var (totalMb, availableMb) = ReadMemory();
        var usage = await ReadCpuUsageAsync(cancellationToken).ConfigureAwait(false);

        return Envelope(writer =>
        {
            writer.WriteString("platform", PlatformName());
            writer.WriteString("os", RuntimeInformation.OSDescription);
            writer.WriteNumber("cores", Environment.ProcessorCount);

            writer.WriteStartObject("cpu");
            WriteNullableNumber(writer, "usagePercent", usage);
            WriteNullableNumber(writer, "loadAverage1m", ReadLoadAverage());
            writer.WriteEndObject();

            writer.WriteStartObject("memory");
            writer.WriteNumber("totalMb", totalMb);
            if (availableMb is { } available)
            {
                writer.WriteNumber("availableMb", available);
                writer.WriteNumber("usedMb", Math.Max(0, totalMb - available));
                WriteNullableNumber(
                    writer,
                    "usagePercent",
                    totalMb > 0 ? (totalMb - available) * 100.0 / totalMb : null);
            }
            else
            {
                writer.WriteNull("availableMb");
                writer.WriteNull("usedMb");
                writer.WriteNull("usagePercent");
            }

            writer.WriteEndObject();

            writer.WriteStartObject("disk");
            var disk = ReadDisk(path);
            writer.WriteString("path", disk.Path);
            WriteNullableNumber(writer, "totalBytes", disk.TotalBytes);
            WriteNullableNumber(writer, "freeBytes", disk.FreeBytes);
            WriteNullableNumber(writer, "usagePercent", disk.UsagePercent);
            writer.WriteEndObject();

            writer.WriteStartObject("process");
            writer.WriteNumber("workingSetMb", Environment.WorkingSet / 1024 / 1024);
            writer.WriteNumber("managedMb", GC.GetTotalMemory(false) / 1024 / 1024);
            WriteNullableNumber(writer, "uptimeSeconds", ReadLauncherUptimeSeconds());
            writer.WriteEndObject();

            writer.WriteStartObject("system");
            WriteNullableNumber(writer, "uptimeSeconds", ReadSystemUptimeSeconds());
            writer.WriteEndObject();
        });
    }

    private static string PlatformName() => OperatingSystem.IsWindows()
        ? "windows"
        : OperatingSystem.IsMacOS() ? "macos" : "linux";

    private static void WriteNullableNumber(Utf8JsonWriter writer, string name, double? value)
    {
        if (value is null || double.IsNaN(value.Value) || double.IsInfinity(value.Value))
        {
            writer.WriteNull(name);
            return;
        }

        writer.WriteNumber(name, Math.Round(value.Value, 1));
    }

    private static async Task<double?> ReadCpuUsageAsync(CancellationToken cancellationToken)
    {
        try
        {
            if (OperatingSystem.IsLinux())
            {
                var first = ReadLinuxCpuTimes();
                await Task.Delay(CpuSampleWindow, cancellationToken).ConfigureAwait(false);
                var second = ReadLinuxCpuTimes();
                return Percent(first, second);
            }

            if (OperatingSystem.IsWindows())
            {
                var first = ReadWindowsCpuTimes();
                await Task.Delay(CpuSampleWindow, cancellationToken).ConfigureAwait(false);
                var second = ReadWindowsCpuTimes();
                return Percent(first, second);
            }

            if (OperatingSystem.IsMacOS())
            {
                return ReadMacCpuUsage();
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            // Load reporting is advisory; never fail the whole status call.
        }

        return null;
    }

    private static double? Percent((long Idle, long Total) first, (long Idle, long Total) second)
    {
        var idle = second.Idle - first.Idle;
        var total = second.Total - first.Total;
        if (total <= 0) return null;
        return Math.Clamp((total - idle) * 100.0 / total, 0, 100);
    }

    /// <summary>Reads the aggregate <c>cpu</c> line of <c>/proc/stat</c>.</summary>
    private static (long Idle, long Total) ReadLinuxCpuTimes()
    {
        foreach (var line in File.ReadLines("/proc/stat"))
        {
            if (!line.StartsWith("cpu ", StringComparison.Ordinal)) continue;
            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            long total = 0;
            for (var index = 1; index < parts.Length && index <= 8; index++)
            {
                if (long.TryParse(parts[index], out var value)) total += value;
            }

            var idle = 0L;
            if (parts.Length > 4 && long.TryParse(parts[4], out var idleTicks)) idle = idleTicks;
            if (parts.Length > 5 && long.TryParse(parts[5], out var ioWait)) idle += ioWait;
            return (idle, total);
        }

        return (0, 0);
    }

    private static (long Idle, long Total) ReadWindowsCpuTimes()
    {
        if (!GetSystemTimes(out var idle, out var kernel, out var user)) return (0, 0);
        var idleTicks = ToTicks(idle);
        // Kernel time already includes idle time.
        var total = ToTicks(kernel) + ToTicks(user);
        return (idleTicks, total);
    }

    private static long ToTicks(FileTime time)
        => ((long)time.High << 32) | time.Low;

    private static double? ReadMacCpuUsage()
    {
        using var top = Process.Start(new ProcessStartInfo
        {
            FileName = "/usr/bin/top",
            ArgumentList = { "-l", "2", "-n", "0", "-s", "1" },
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        });
        if (top is null) return null;
        var output = top.StandardOutput.ReadToEnd();
        if (!top.WaitForExit(4000))
        {
            try
            {
                top.Kill(entireProcessTree: true);
            }
            catch
            {
                // Best effort.
            }

            return null;
        }

        double? usage = null;
        foreach (var line in output.Split('\n'))
        {
            if (!line.StartsWith("CPU usage:", StringComparison.Ordinal)) continue;
            var percent = line.IndexOf('%');
            if (percent < 0) continue;
            var tail = line[..percent];
            var start = tail.LastIndexOf(' ') + 1;
            if (double.TryParse(
                    tail[start..],
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out var idle))
            {
                usage = Math.Clamp(100 - idle, 0, 100);
            }
        }

        return usage;
    }

    private static double? ReadLoadAverage()
    {
        try
        {
            if (OperatingSystem.IsLinux())
            {
                var text = File.ReadAllText("/proc/loadavg");
                var first = text.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
                return double.TryParse(
                    first,
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out var value)
                    ? value
                    : null;
            }

            if (OperatingSystem.IsMacOS())
            {
                return RunForNumber("/usr/sbin/sysctl", ["-n", "vm.loadavg"]);
            }
        }
        catch
        {
            // Optional metric.
        }

        return null;
    }

    private static double? RunForNumber(string fileName, string[] arguments)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = fileName,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                ArgumentList = { arguments[0], arguments[1] },
            });
            if (process is null) return null;
            var text = process.StandardOutput.ReadToEnd();
            process.WaitForExit(2000);
            var digits = new string(text.Where(c => char.IsDigit(c) || c is '.' or '-').ToArray());
            return double.TryParse(
                digits,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var value)
                ? value
                : null;
        }
        catch
        {
            return null;
        }
    }

    private static double? ReadSystemUptimeSeconds()
    {
        try
        {
            if (OperatingSystem.IsLinux())
            {
                var text = File.ReadAllText("/proc/uptime");
                var first = text.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
                return double.TryParse(
                    first,
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out var seconds)
                    ? seconds
                    : null;
            }

            if (OperatingSystem.IsWindows())
            {
                // TickCount64 wraps every ~584 million years; good enough here.
                return Environment.TickCount64 / 1000.0;
            }
        }
        catch
        {
            // Optional metric.
        }

        return null;
    }

    private static double? ReadLauncherUptimeSeconds()
    {
        try
        {
            using var process = Process.GetCurrentProcess();
            return Math.Max(0, (DateTime.Now - process.StartTime).TotalSeconds);
        }
        catch
        {
            return null;
        }
    }

    private static DiskFacts ReadDisk(string? path)
    {
        var resolved = string.IsNullOrWhiteSpace(path)
            ? AppContext.BaseDirectory
            : path;
        try
        {
            var full = Path.GetFullPath(resolved);
            var root = Path.GetPathRoot(full);
            if (string.IsNullOrEmpty(root)) return new DiskFacts(resolved, null, null, null);
            var drive = new DriveInfo(root);
            if (!drive.IsReady) return new DiskFacts(full, null, null, null);
            var total = drive.TotalSize;
            var free = drive.AvailableFreeSpace;
            return new DiskFacts(
                full,
                total,
                free,
                total > 0 ? (total - free) * 100.0 / total : null);
        }
        catch
        {
            return new DiskFacts(resolved, null, null, null);
        }
    }

    private sealed record DiskFacts(
        string Path,
        long? TotalBytes,
        long? FreeBytes,
        double? UsagePercent);

    /// <summary>Total and available memory in MB; available is null when unknown.</summary>
    private static (long TotalMb, long? AvailableMb) ReadMemory()
    {
        if (OperatingSystem.IsWindows())
        {
            return ReadWindowsMemory();
        }

        if (OperatingSystem.IsLinux())
        {
            return ReadLinuxMemory();
        }

        if (OperatingSystem.IsMacOS())
        {
            return ReadMacMemory();
        }

        return (0, null);
    }

    private static (long TotalMb, long? AvailableMb) ReadLinuxMemory()
    {
        long totalKb = 0;
        long? availableKb = null;
        foreach (var line in File.ReadLines("/proc/meminfo"))
        {
            if (line.StartsWith("MemTotal:", StringComparison.Ordinal))
            {
                totalKb = ParseKb(line);
            }
            else if (line.StartsWith("MemAvailable:", StringComparison.Ordinal))
            {
                availableKb = ParseKb(line);
            }
        }

        return (totalKb / 1024, availableKb is > 0 ? availableKb / 1024 : null);
    }

    private static long ParseKb(string line)
    {
        var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length >= 2 && long.TryParse(parts[1], out var value) ? value : 0;
    }

    private static (long TotalMb, long? AvailableMb) ReadWindowsMemory()
    {
        var status = new MemoryStatusEx { Length = (uint)Marshal.SizeOf<MemoryStatusEx>() };
        if (!GlobalMemoryStatusEx(ref status)) return (0, null);
        return (
            (long)(status.TotalPhys / 1024 / 1024),
            (long)(status.AvailPhys / 1024 / 1024));
    }

    private static (long TotalMb, long? AvailableMb) ReadMacMemory()
    {
        try
        {
            using var sysctl = Process.Start(new ProcessStartInfo
            {
                FileName = "/usr/sbin/sysctl",
                ArgumentList = { "-n", "hw.memsize" },
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            });
            sysctl?.WaitForExit(2000);
            var total = long.TryParse(
                sysctl?.StandardOutput.ReadToEnd().Trim(),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var bytes)
                ? bytes / 1024 / 1024
                : 0;

            if (total <= 0) return (0, null);

            using var vmStat = Process.Start(new ProcessStartInfo
            {
                FileName = "/usr/bin/vm_stat",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            });
            vmStat?.WaitForExit(2000);
            var output = vmStat?.StandardOutput.ReadToEnd() ?? string.Empty;

            // Page size comes from the vm_stat header ("page size of 16384 bytes").
            long pageSize = 4096;
            var headerIndex = output.IndexOf("page size of", StringComparison.Ordinal);
            if (headerIndex >= 0)
            {
                var header = output[headerIndex..];
                var digits = new string(header.TakeWhile(c => !c.Equals('\n'))
                    .Where(char.IsDigit)
                    .ToArray());
                if (long.TryParse(digits, out var parsed) && parsed > 0) pageSize = parsed;
            }

            // Free + inactive + speculative approximates what the OS can hand out.
            long availablePages = 0;
            foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                if (!line.StartsWith("Pages free:", StringComparison.Ordinal) &&
                    !line.StartsWith("Pages inactive:", StringComparison.Ordinal) &&
                    !line.StartsWith("Pages speculative:", StringComparison.Ordinal))
                {
                    continue;
                }

                var digits = new string(line.Where(char.IsDigit).ToArray());
                if (long.TryParse(digits, out var pages)) availablePages += pages;
            }

            if (availablePages <= 0) return (total, null);
            var availableMb = availablePages * pageSize / 1024 / 1024;
            return (total, Math.Min(total, availableMb));
        }
        catch
        {
            return (0, null);
        }
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

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MemoryStatusEx
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhys;
        public ulong AvailPhys;
        public ulong TotalPageFile;
        public ulong AvailPageFile;
        public ulong TotalVirtual;
        public ulong AvailVirtual;
        public ulong AvailExtendedVirtual;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FileTime
    {
        public uint Low;
        public uint High;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx buffer);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetSystemTimes(
        out FileTime idleTime,
        out FileTime kernelTime,
        out FileTime userTime);

    private static byte[] Envelope(Action<Utf8JsonWriter> data)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteBoolean("ok", true);
            writer.WriteStartObject("data");
            data(writer);
            writer.WriteEndObject();
            writer.WriteStartObject("meta");
            writer.WriteNumber("schemaVersion", 1);
            writer.WriteEndObject();
            writer.WriteEndObject();
        }
        return stream.ToArray();
    }

    private static byte[] Error(string code, string message)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteBoolean("ok", false);
            writer.WriteStartObject("error");
            writer.WriteString("code", code);
            writer.WriteString("message", message);
            writer.WriteEndObject();
            writer.WriteEndObject();
        }
        return stream.ToArray();
    }
}
