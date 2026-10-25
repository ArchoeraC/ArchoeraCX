using System.Text.Json;
using Archoera.Infrastructure.Modules;

namespace Archoera.Tests;

public sealed class ModulePathsTests
{
    [Fact]
    public void Core_directory_is_read_and_normalized()
    {
        var directory = Path.Combine(Path.GetTempPath(), "archoera-core-dir");
        var json = JsonSerializer.Serialize(new { coreDir = directory });

        Assert.Equal(Path.GetFullPath(directory), ModulePaths.ParseNativeDirectory(json));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("{}")]
    [InlineData("{\"coreDir\":42}")]
    [InlineData("{\"coreDir\":\"   \"}")]
    public void Missing_or_invalid_core_directory_is_ignored(string? json)
    {
        Assert.Null(ModulePaths.ParseNativeDirectory(json));
    }
}
