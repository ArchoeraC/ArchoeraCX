using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using Archoera.Infrastructure.Loaders.Native;

namespace Archoera.Tests;

public sealed class ZipDeflateTests
{
    [Fact]
    public void Managed_fallback_round_trips_raw_deflate()
    {
        var data = new byte[200_000];
        new Random(1234).NextBytes(data);

        var compressed = ZipDeflate.CompressManaged(data);

        using var input = new MemoryStream(compressed);
        using var inflater = new DeflateStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        inflater.CopyTo(output);
        Assert.Equal(data, output.ToArray());
    }

    [Fact]
    public void Managed_fallback_matches_zlib_for_an_empty_entry()
    {
        Assert.Equal(new byte[] { 0x03, 0x00 }, ZipDeflate.CompressManaged([]));
    }

    [Theory]
    [InlineData("ARCHOERACX_SPLIT_SLIM_1201")]
    [InlineData("ARCHOERACX_SPLIT_SLIM_1211")]
    public void Matches_the_zlib_level_6_streams_in_a_real_slim_jar(string variable)
    {
        var path = Environment.GetEnvironmentVariable(variable);
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return;
        }

        var jar = File.ReadAllBytes(path);
        using var archive = new ZipArchive(new MemoryStream(jar), ZipArchiveMode.Read);
        var checkedEntries = 0;
        foreach (var entry in ReadCentralDirectory(jar))
        {
            if (!entry.Name.EndsWith(".class", StringComparison.Ordinal))
            {
                continue;
            }

            var zipEntry = archive.GetEntry(entry.Name);
            Assert.NotNull(zipEntry);
            var uncompressed = ReadEntry(zipEntry!);
            var raw = jar.AsSpan(
                entry.LocalOffset + 30 + entry.LocalNameLength + entry.LocalExtraLength,
                entry.CompressedSize).ToArray();

            var compressed = ZipDeflate.Compress(uncompressed);
            Assert.True(
                raw.AsSpan().SequenceEqual(compressed),
                $"{entry.Name}: zlib {raw.Length} bytes, ours {compressed.Length} bytes");
            checkedEntries++;
            if (checkedEntries >= 16)
            {
                break;
            }
        }

        Assert.True(checkedEntries > 0);
    }

    private static IEnumerable<CentralEntry> ReadCentralDirectory(byte[] jar)
    {
        var end = -1;
        for (var index = jar.Length - 22; index >= 0; index--)
        {
            if (jar[index] == 0x50 && jar[index + 1] == 0x4B &&
                jar[index + 2] == 0x05 && jar[index + 3] == 0x06)
            {
                end = index;
                break;
            }
        }

        Assert.True(end >= 0, "end of central directory not found");
        var count = BinaryPrimitives.ReadUInt16LittleEndian(jar.AsSpan(end + 10));
        var offset = BinaryPrimitives.ReadInt32LittleEndian(jar.AsSpan(end + 16));
        for (var index = 0; index < count; index++)
        {
            Assert.Equal(0x02014B50u, BinaryPrimitives.ReadUInt32LittleEndian(jar.AsSpan(offset)));
            var compressedSize = BinaryPrimitives.ReadInt32LittleEndian(jar.AsSpan(offset + 20));
            var nameLength = BinaryPrimitives.ReadUInt16LittleEndian(jar.AsSpan(offset + 28));
            var extraLength = BinaryPrimitives.ReadUInt16LittleEndian(jar.AsSpan(offset + 30));
            var commentLength = BinaryPrimitives.ReadUInt16LittleEndian(jar.AsSpan(offset + 32));
            var localOffset = BinaryPrimitives.ReadInt32LittleEndian(jar.AsSpan(offset + 42));
            var name = Encoding.UTF8.GetString(jar, offset + 46, nameLength);
            var localNameLength = BinaryPrimitives.ReadUInt16LittleEndian(jar.AsSpan(localOffset + 26));
            var localExtraLength = BinaryPrimitives.ReadUInt16LittleEndian(jar.AsSpan(localOffset + 28));
            yield return new CentralEntry(
                name,
                localOffset,
                compressedSize,
                localNameLength,
                localExtraLength);
            offset += 46 + nameLength + extraLength + commentLength;
        }
    }

    private static byte[] ReadEntry(ZipArchiveEntry entry)
    {
        using var stream = entry.Open();
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }

    private readonly record struct CentralEntry(
        string Name,
        int LocalOffset,
        int CompressedSize,
        int LocalNameLength,
        int LocalExtraLength);
}
