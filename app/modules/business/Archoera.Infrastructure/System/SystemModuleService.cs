using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Json;
using Archoera.Application.Modules;
using Archoera.Infrastructure.Build;
using Archoera.Infrastructure.Modules;

namespace Archoera.Infrastructure.SystemInfo;

/// <summary>
/// Physical memory used by the settings page and the build identity badge.
/// Machine load (CPU, disk, uptime, process working set) is owned by the C++
/// core (`system.status`) so the platform APIs live in one place.
/// </summary>
public sealed class SystemModuleService : IModuleMethodHandler
{
    private static readonly string[] SupportedMethods =
        ["system.memory", "system.identity"];

    public IReadOnlyCollection<string> Methods => SupportedMethods;

    public Task<byte[]> HandleAsync(
        string method,
        string? paramsJson,
        CancellationToken cancellationToken)
    {
        try
        {
            return method switch
            {
                "system.memory" => Task.FromResult(Envelope(WriteMemory)),
                "system.identity" => Task.FromResult(Envelope(WriteIdentity)),
                _ => Task.FromResult(
                    ModuleJson.Error("E_UNSUPPORTED", $"unsupported system method '{method}'")),
            };
        }
        catch (Exception exception)
        {
            return Task.FromResult(ModuleJson.Error("E_INTERNAL", exception.Message));
        }
    }

    private static void WriteIdentity(Utf8JsonWriter writer)
    {
        // Only the hash leaves the module: the client id itself stays in CI
        // secrets and in the compiled binary.
        var identity = OfficialBuildIdentity.Describe(MicrosoftBuildConfig.ClientId);
        writer.WriteString("channel", identity.Channel);
        writer.WriteBoolean("official", identity.Official);
        writer.WriteString("clientIdSha256", identity.ClientIdSha256);
        writer.WriteString("clientIdFingerprint", identity.ClientIdFingerprint);
    }

    private static void WriteMemory(Utf8JsonWriter writer)
    {
        var (totalMb, availableMb) = ReadMemory();
        writer.WriteNumber("totalMb", totalMb);
        if (availableMb is { } available)
        {
            writer.WriteNumber("availableMb", available);
        }
        else
        {
            writer.WriteNull("availableMb");
        }

        writer.WriteString("platform", PlatformName());
    }

    private static string PlatformName() => OperatingSystem.IsWindows()
        ? "windows"
        : OperatingSystem.IsMacOS() ? "macos" : "linux";

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
}
