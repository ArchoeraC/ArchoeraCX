using Archoera.Infrastructure.Java;

namespace Archoera.Tests;

public sealed class JavaRuntimeSelectionTests
{
    [Theory]
    [InlineData("26.2", 25)]
    [InlineData("1.21.1", 21)]
    [InlineData("1.20.6", 21)]
    [InlineData("1.20.1", 17)]
    [InlineData("1.19.4", 17)]
    [InlineData("1.18.2", 17)]
    [InlineData("1.17.1", 16)]
    [InlineData("1.16.5", 8)]
    [InlineData("1.12.2", 8)]
    [InlineData("unknown", null)]
    public void InferMajor_covers_loader_processor_requirements(
        string gameVersion,
        int? expected)
    {
        Assert.Equal(expected, JavaRuntimeSelectionService.InferMajor(gameVersion));
    }
}
