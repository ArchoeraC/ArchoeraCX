using System.Text;
using Archoera.Infrastructure.Loaders.Remap;

namespace Archoera.Tests;

public sealed class NativeArtClassFileReaderTests
{
    [Fact]
    public void Empty_class_parses_identity()
    {
        var model = ClassFileReader.Read(NativeArtFixtureBuilder.EmptyClass());

        Assert.Equal(65, model.MajorVersion);
        Assert.Equal("fixture/Sample", model.ClassName);
        Assert.Equal(0x0021, model.Access);
        Assert.Empty(model.Interfaces);
        Assert.Empty(model.Fields);
        Assert.Empty(model.Methods);
        Assert.Empty(model.Attributes);
        Assert.Equal(5, model.Pool.Count);
        Assert.Equal("java/lang/Object", model.Pool.GetClassName(model.SuperClass));
    }

    [Fact]
    public void Class_attribute_is_kept_opaque()
    {
        var model = ClassFileReader.Read(
            NativeArtFixtureBuilder.ClassWithClassAttribute("CustomAttribute"));

        var attribute = Assert.Single(model.Attributes);
        Assert.Equal("CustomAttribute", attribute.Name(model.Pool));
        Assert.Empty(attribute.Info);
    }

    [Theory]
    [InlineData("plain")]
    [InlineData("a\u0000b")]
    [InlineData("caf\u00e9")]
    [InlineData("\ud83d\ude00")]
    public void Modified_utf8_round_trips(string value)
    {
        var encoded = ConstantPool.EncodeModifiedUtf8(value);
        Assert.Equal(value, ConstantPool.DecodeModifiedUtf8(encoded));
    }

    [Fact]
    public void Truncated_class_is_rejected()
    {
        var bytes = NativeArtFixtureBuilder.EmptyClass();

        var error = Assert.Throws<ClassFileException>(
            () => ClassFileReader.Read(bytes.AsSpan(0, bytes.Length - 1)));

        Assert.Contains("truncated", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Trailing_bytes_are_rejected()
    {
        var bytes = NativeArtFixtureBuilder.EmptyClass();
        var extended = new byte[bytes.Length + 1];
        bytes.CopyTo(extended, 0);

        var error = Assert.Throws<ClassFileException>(() => ClassFileReader.Read(extended));
        Assert.Contains("trailing", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Raw_zero_byte_in_modified_utf8_is_rejected()
    {
        var error = Assert.Throws<ClassFileException>(
            () => ConstantPool.DecodeModifiedUtf8([0x41, 0x00]));

        Assert.Contains("zero", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Wrong_magic_is_rejected()
    {
        var bytes = NativeArtFixtureBuilder.EmptyClass();
        bytes[0] = 0;

        var error = Assert.Throws<ClassFileException>(() => ClassFileReader.Read(bytes));
        Assert.Contains("magic", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Utf8_string_is_preserved_byte_for_byte()
    {
        var model = ClassFileReader.Read(NativeArtFixtureBuilder.EmptyClass());
        var entry = model.Pool[1];

        Assert.Equal(ConstantPoolTag.Utf8, entry.Tag);
        Assert.NotNull(entry.TextBytes);
        Assert.Equal(
            Encoding.UTF8.GetBytes("fixture/Sample"),
            entry.TextBytes);
    }
}
