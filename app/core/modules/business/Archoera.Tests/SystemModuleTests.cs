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

}
