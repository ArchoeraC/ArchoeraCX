using System.Buffers.Binary;
using System.Text;
using Archoera.Infrastructure.Loaders.Native;

namespace Archoera.Tests;

public sealed class BinaryPatchTests
{
    [Fact]
    public void Gdiff_applies_add_and_absolute_copy()
    {
        var patch = new byte[]
        {
            0xD1, 0xFF, 0xD1, 0xFF, 0x04,
            0x05, (byte)'h', (byte)'e', (byte)'l', (byte)'l', (byte)'o',
            0xF9, 0x00, 0x00, 0x05, // copy 5 bytes from absolute offset 0
            0x00,
        };

        var result = GDiff.Apply(Encoding.ASCII.GetBytes("hello"), patch);

        Assert.Equal("hellohello", Encoding.ASCII.GetString(result));
    }

    [Fact]
    public void Gdiff_rejects_a_copy_past_the_source()
    {
        var patch = new byte[]
        {
            0xD1, 0xFF, 0xD1, 0xFF, 0x04,
            0xF9, 0x00, 0x02, 0x04,
            0x00,
        };

        Assert.Throws<InvalidDataException>(
            () => GDiff.Apply(new byte[] { 1, 2, 3 }, patch));
    }

    [Fact]
    public void Gdiff_rejects_a_bad_magic()
    {
        Assert.Throws<InvalidDataException>(
            () => GDiff.Apply([], [0xD1, 0xFF, 0xD1, 0xFF, 0x03, 0x00]));
    }

    [Fact]
    public void Patch_record_round_trips_the_v1_header()
    {
        var delta = new byte[] { 0xD1, 0xFF, 0xD1, 0xFF, 0x04, 0x00 };
        var record = BuildRecord("a/b", "a/b", exists: true, checksum: 0x12345678, delta);

        var patch = BinaryPatch.Parse(record);

        Assert.Equal("a/b", patch.ObfuscatedName);
        Assert.Equal("a/b", patch.MappedName);
        Assert.True(patch.Exists);
        Assert.Equal(0x12345678u, patch.Checksum);
        Assert.Equal(delta, patch.Delta);
    }

    [Fact]
    public void Patch_record_without_a_source_has_no_checksum()
    {
        var record = BuildRecord("new/Class", "new/Class", exists: false, checksum: 0, []);

        var patch = BinaryPatch.Parse(record);

        Assert.False(patch.Exists);
        Assert.Equal(0u, patch.Checksum);
        Assert.Empty(patch.Delta);
    }

    [Fact]
    public void Patch_record_rejects_an_unknown_version()
    {
        var record = BuildRecord("a", "a", exists: false, checksum: 0, []);
        record[0] = 2;

        Assert.Throws<InvalidDataException>(() => BinaryPatch.Parse(record));
    }

    private static byte[] BuildRecord(
        string obfuscated,
        string mapped,
        bool exists,
        uint checksum,
        byte[] delta)
    {
        using var stream = new MemoryStream();
        stream.WriteByte(1);
        WriteUtf(stream, obfuscated);
        WriteUtf(stream, mapped);
        stream.WriteByte((byte)(exists ? 1 : 0));
        if (exists)
        {
            WriteU4(stream, checksum);
        }

        WriteS4(stream, delta.Length);
        stream.Write(delta);
        return stream.ToArray();
    }

    private static void WriteUtf(Stream stream, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        WriteU2(stream, (ushort)bytes.Length);
        stream.Write(bytes);
    }

    private static void WriteU2(Stream stream, ushort value)
    {
        Span<byte> buffer = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(buffer, value);
        stream.Write(buffer);
    }

    private static void WriteU4(Stream stream, uint value)
    {
        Span<byte> buffer = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(buffer, value);
        stream.Write(buffer);
    }

    private static void WriteS4(Stream stream, int value)
    {
        Span<byte> buffer = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(buffer, value);
        stream.Write(buffer);
    }
}
