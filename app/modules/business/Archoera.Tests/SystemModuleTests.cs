using System.Text.Json;
using Archoera.Infrastructure.SystemInfo;

namespace Archoera.Tests;

public sealed class SystemModuleTests
{
    [Fact]
    public async Task Reports_physical_memory()
    {
        var service = new SystemModuleService();
        using var document = JsonDocument.Parse(
            await service.HandleAsync("system.memory", "{}", CancellationToken.None));
        Assert.True(document.RootElement.GetProperty("ok").GetBoolean());
        var data = document.RootElement.GetProperty("data");
        var total = data.GetProperty("totalMb").GetInt64();
        Assert.True(total > 0);
        // Available memory is nullable: a platform that cannot report it says so.
        var available = data.GetProperty("availableMb");
        if (available.ValueKind == JsonValueKind.Number)
        {
            Assert.InRange(available.GetInt64(), 0, total);
        }
        else
        {
            Assert.Equal(JsonValueKind.Null, available.ValueKind);
        }
        Assert.Contains(
            data.GetProperty("platform").GetString(),
            new[] { "linux", "windows", "macos" });
    }

    [Fact]
    public async Task Reports_machine_load()
    {
        var service = new SystemModuleService();
        using var document = JsonDocument.Parse(
            await service.HandleAsync("system.status", "{}", CancellationToken.None));
        Assert.True(document.RootElement.GetProperty("ok").GetBoolean());

        var data = document.RootElement.GetProperty("data");
        Assert.True(data.GetProperty("cores").GetInt32() > 0);

        var memory = data.GetProperty("memory");
        var total = memory.GetProperty("totalMb").GetInt64();
        Assert.True(total > 0);
        if (memory.GetProperty("usedMb").ValueKind == JsonValueKind.Number)
        {
            var used = memory.GetProperty("usedMb").GetInt64();
            Assert.InRange(used, 0, total);
            Assert.InRange(memory.GetProperty("usagePercent").GetDouble(), 0, 100);
        }
        else
        {
            Assert.Equal(JsonValueKind.Null, memory.GetProperty("usagePercent").ValueKind);
        }

        var disk = data.GetProperty("disk");
        Assert.False(string.IsNullOrWhiteSpace(disk.GetProperty("path").GetString()));
        if (disk.GetProperty("totalBytes").ValueKind == JsonValueKind.Number)
        {
            Assert.True(disk.GetProperty("freeBytes").GetInt64() >= 0);
        }

        // CPU is sampled; a value must be either absent (null) or in range.
        var cpu = data.GetProperty("cpu").GetProperty("usagePercent");
        if (cpu.ValueKind == JsonValueKind.Number)
        {
            Assert.InRange(cpu.GetDouble(), 0, 100);
        }
        else
        {
            Assert.Equal(JsonValueKind.Null, cpu.ValueKind);
        }

        Assert.True(data.GetProperty("process").GetProperty("workingSetMb").GetInt64() > 0);
    }

    [Fact]
    public async Task Reads_disk_for_the_requested_path()
    {
        var service = new SystemModuleService();
        var path = Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar);
        using var document = JsonDocument.Parse(
            await service.HandleAsync(
                "system.status",
                JsonSerializer.Serialize(new { path }),
                CancellationToken.None));
        var disk = document.RootElement.GetProperty("data").GetProperty("disk");
        Assert.StartsWith(path, disk.GetProperty("path").GetString());
    }
}
