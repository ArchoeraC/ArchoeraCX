using System.Buffers.Binary;
using System.Text;

namespace Archoera.Infrastructure.Loaders.Native;

/// <summary>
/// One <c>.binpatch</c> record. The wire format is big-endian; strings use
/// <c>DataInputStream.readUTF</c> (modified UTF-8) and the only integrity check
/// is the Adler-32 of the source bytes.
/// </summary>
internal sealed record BinaryPatch(
    string ObfuscatedName,
    string MappedName,
    bool Exists,
    uint Checksum,
    byte[] Delta)
{
    public static BinaryPatch Parse(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        var offset = 0;
        if (data.Length < 1 || data[offset++] != 1)
        {
            throw new InvalidDataException("unsupported binary patch version");
        }

        var obfuscated = ReadUtf(data, ref offset);
        var mapped = ReadUtf(data, ref offset);
        if (offset >= data.Length)
        {
            throw new InvalidDataException("truncated binary patch header");
        }

        var exists = data[offset++] != 0;
        var checksum = 0u;
        if (exists)
        {
            checksum = ReadU4(data, ref offset);
        }

        var length = ReadS4(data, ref offset);
        if (length < 0 || length > data.Length - offset)
        {
            throw new InvalidDataException("invalid binary patch payload length");
        }

        var delta = data.AsSpan(offset, length).ToArray();
        return new BinaryPatch(obfuscated, mapped, exists, checksum, delta);
    }

    private static string ReadUtf(byte[] data, ref int offset)
    {
        if (offset + 2 > data.Length)
        {
            throw new InvalidDataException("truncated binary patch string");
        }

        var length = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(offset));
        offset += 2;
        if (offset + length > data.Length)
        {
            throw new InvalidDataException("truncated binary patch string");
        }

        var text = DecodeModifiedUtf8(data.AsSpan(offset, length));
        offset += length;
        return text;
    }

    private static string DecodeModifiedUtf8(ReadOnlySpan<byte> bytes)
    {
        var builder = new StringBuilder(bytes.Length);
        for (var index = 0; index < bytes.Length;)
        {
            var first = bytes[index++];
            if (first < 0x80)
            {
                builder.Append((char)first);
                continue;
            }

            if ((first & 0xE0) == 0xC0 && index < bytes.Length)
            {
                var second = bytes[index++];
                builder.Append((char)(((first & 0x1F) << 6) | (second & 0x3F)));
                continue;
            }

            if ((first & 0xF0) == 0xE0 && index + 1 < bytes.Length)
            {
                var second = bytes[index++];
                var third = bytes[index++];
                builder.Append((char)(((first & 0x0F) << 12) |
                    ((second & 0x3F) << 6) |
                    (third & 0x3F)));
                continue;
            }

            throw new InvalidDataException("invalid modified UTF-8 in binary patch");
        }

        return builder.ToString();
    }

    private static uint ReadU4(byte[] data, ref int offset)
    {
        if (offset + 4 > data.Length)
        {
            throw new InvalidDataException("truncated binary patch checksum");
        }

        var value = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(offset));
        offset += 4;
        return value;
    }

    private static int ReadS4(byte[] data, ref int offset)
    {
        if (offset + 4 > data.Length)
        {
            throw new InvalidDataException("truncated binary patch length");
        }

        var value = BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(offset));
        offset += 4;
        return value;
    }
}
