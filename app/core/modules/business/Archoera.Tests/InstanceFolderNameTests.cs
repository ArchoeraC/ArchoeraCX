using Archoera.Infrastructure.Instances;

namespace Archoera.Tests;

public sealed class InstanceFolderNameTests
{
    [Theory]
    [InlineData("26.2-猫猫整合包Fabric", "26.2-猫猫整合包Fabric")]
    [InlineData("生存整合包", "生存整合包")]
    [InlineData("Café Pack", "Café Pack")]
    [InlineData("😺 Pack", "😺 Pack")]
    [InlineData("A/B:C", "A-B-C")]
    [InlineData("Name. ", "Name")]
    [InlineData("  Name  ", "Name")]
    [InlineData(".hidden", "hidden")]
    [InlineData(" . . ", "instance")]
    [InlineData("A\u0001B", "AB")]
    [InlineData("CON", "_CON")]
    [InlineData("com1.txt", "_com1.txt")]
    [InlineData("...", "instance")]
    [InlineData("", "instance")]
    public void FromDisplayName_keeps_unicode_and_fixes_only_unsafe_segments(
        string name,
        string expected)
    {
        Assert.Equal(expected, InstanceFolderName.FromDisplayName(name));
    }

    [Fact]
    public void FromDisplayName_truncates_long_names()
    {
        var folder = InstanceFolderName.FromDisplayName(new string('a', 100));

        Assert.Equal(InstanceFolderName.MaxLength, folder.Length);
    }
}
