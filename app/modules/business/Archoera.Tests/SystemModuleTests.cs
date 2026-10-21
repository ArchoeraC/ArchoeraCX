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
        var available = data.GetProperty("availableMb").GetInt64();
        Assert.True(total > 0);
        Assert.InRange(available, 0, total);
        Assert.Contains(
            data.GetProperty("platform").GetString(),
            new[] { "linux", "windows", "macos" });
    }
}
