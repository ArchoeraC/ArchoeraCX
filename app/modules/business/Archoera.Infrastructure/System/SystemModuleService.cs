using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Json;
using Archoera.Application.Modules;

namespace Archoera.Infrastructure.SystemInfo;

/// <summary>
/// Native system facts used by settings UI. This stays in C# so Flutter never
/// reads /proc, registry, or platform-specific memory APIs directly.
/// </summary>
public sealed class SystemModuleService : IModuleMethodHandler
{
    private static readonly string[] SupportedMethods = ["system.memory"];

    public IReadOnlyCollection<string> Methods => SupportedMethods;

    public Task<byte[]> HandleAsync(
        string method,
        string? paramsJson,
        CancellationToken cancellationToken)
    {
        if (method != "system.memory")
        {
            return Task.FromResult(Error("E_UNSUPPORTED", $"unsupported system method '{method}'"));
        }

        try
        {
            var (totalMb, availableMb) = ReadMemory();
            return Task.FromResult(Envelope(writer =>
            {
                writer.WriteNumber("totalMb", totalMb);
                writer.WriteNumber("availableMb", availableMb);
                writer.WriteString("platform", OperatingSystem.IsWindows()
                    ? "windows"
                    : OperatingSystem.IsMacOS() ? "macos" : "linux");
            }));
        }
        catch (Exception exception)
        {
            return Task.FromResult(Error("E_INTERNAL", exception.Message));
        }
    }

    private static (long TotalMb, long AvailableMb) ReadMemory()
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

        return (0, 0);
    }

    private static (long TotalMb, long AvailableMb) ReadLinuxMemory()
    {
        long totalKb = 0;
        long availableKb = 0;
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

        return (totalKb / 1024, availableKb / 1024);
    }

    private static long ParseKb(string line)
    {
        var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length >= 2 && long.TryParse(parts[1], out var value) ? value : 0;
    }

    private static (long TotalMb, long AvailableMb) ReadWindowsMemory()
    {
        var status = new MemoryStatusEx { Length = (uint)Marshal.SizeOf<MemoryStatusEx>() };
        if (!GlobalMemoryStatusEx(ref status)) return (0, 0);
        return (
            (long)(status.TotalPhys / 1024 / 1024),
            (long)(status.AvailPhys / 1024 / 1024));
    }

    private static (long TotalMb, long AvailableMb) ReadMacMemory()
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

            using var vmStat = Process.Start(new ProcessStartInfo
            {
                FileName = "/usr/bin/vm_stat",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            });
            vmStat?.WaitForExit(2000);
            long freePages = 0;
            foreach (var line in (vmStat?.StandardOutput.ReadToEnd() ?? string.Empty)
                         .Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                if (!line.StartsWith("Pages free:", StringComparison.Ordinal)) continue;
                var digits = new string(line.Where(char.IsDigit).ToArray());
                long.TryParse(digits, out freePages);
            }

            // vm_stat page size is commonly 4096; estimate availability.
            var available = freePages * 4096 / 1024 / 1024;
            return (total, available > 0 ? available : total / 2);
        }
        catch
        {
            return (0, 0);
        }
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

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx buffer);

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
