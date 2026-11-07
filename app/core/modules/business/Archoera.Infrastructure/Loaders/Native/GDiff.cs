using System.Buffers.Binary;

namespace Archoera.Infrastructure.Loaders.Native;

/// <summary>
/// Applies a <c>com.nothome.delta</c> GDIFF v4 stream. Only ADD and COPY exist;
/// COPY offsets are absolute and every command is big-endian.
/// </summary>
internal static class GDiff
{
    private static readonly byte[] Magic = [0xD1, 0xFF, 0xD1, 0xFF, 0x04];

    /// <summary>Refuses absurd expansions before allocating them.</summary>
    private const int MaxOutputLength = 64 * 1024 * 1024;

    public static byte[] Apply(ReadOnlySpan<byte> source, ReadOnlySpan<byte> patch)
    {
        if (patch.Length < Magic.Length || !patch[..Magic.Length].SequenceEqual(Magic))
        {
            throw new InvalidDataException("binary patch magic not found");
        }

        using var output = new MemoryStream();
        var offset = Magic.Length;
        while (true)
        {
            if (offset >= patch.Length)
            {
                throw new InvalidDataException("truncated binary patch stream");
            }

            var command = patch[offset++];
            switch (command)
            {
                case 0:
                    return output.ToArray();
                case <= 246:
                    CopyLiteral(patch, ref offset, command, output);
                    break;
                case 247:
                    CopyLiteral(patch, ref offset, ReadU2(patch, ref offset), output);
                    break;
                case 248:
                {
                    var length = ReadS4(patch, ref offset);
                    if (length > 0)
                    {
                        CopyLiteral(patch, ref offset, length, output);
                    }

                    break;
                }

                case 249:
                    Copy(source, ReadU2(patch, ref offset), ReadU1(patch, ref offset), output);
                    break;
                case 250:
                    Copy(source, ReadU2(patch, ref offset), ReadU2(patch, ref offset), output);
                    break;
                case 251:
                    Copy(source, ReadU2(patch, ref offset), ReadS4(patch, ref offset), output);
                    break;
                case 252:
                    Copy(source, ReadS4(patch, ref offset), ReadU1(patch, ref offset), output);
                    break;
                case 253:
                    Copy(source, ReadS4(patch, ref offset), ReadU2(patch, ref offset), output);
                    break;
                case 254:
                    Copy(source, ReadS4(patch, ref offset), ReadS4(patch, ref offset), output);
                    break;
                case 255:
                    Copy(source, ReadS8(patch, ref offset), ReadS4(patch, ref offset), output);
                    break;
                default:
                    throw new InvalidDataException($"unknown binary patch command {command}");
            }
        }
    }

    private static void CopyLiteral(
        ReadOnlySpan<byte> patch,
        ref int offset,
        int length,
        MemoryStream output)
    {
        if (length < 0 || length > patch.Length - offset)
        {
            throw new InvalidDataException("truncated binary patch literal");
        }

        EnsureCapacity(output, length);
        output.Write(patch.Slice(offset, length));
        offset += length;
    }

    private static void Copy(
        ReadOnlySpan<byte> source,
        long offset,
        int length,
        MemoryStream output)
    {
        if (length <= 0)
        {
            return;
        }

        if (offset < 0 || offset + length > source.Length)
        {
            throw new InvalidDataException($"binary patch copy {offset} {length} is out of range");
        }

        EnsureCapacity(output, length);
        output.Write(source.Slice((int)offset, length));
    }

    private static void EnsureCapacity(MemoryStream output, int length)
    {
        if (output.Length + length > MaxOutputLength)
        {
            throw new InvalidDataException("binary patch expands past the size limit");
        }
    }

    private static byte ReadU1(ReadOnlySpan<byte> data, ref int offset)
    {
        if (offset >= data.Length)
        {
            throw new InvalidDataException("truncated binary patch command");
        }

        return data[offset++];
    }

    private static int ReadU2(ReadOnlySpan<byte> data, ref int offset)
    {
        if (offset + 2 > data.Length)
        {
            throw new InvalidDataException("truncated binary patch command");
        }

        var value = BinaryPrimitives.ReadUInt16BigEndian(data[offset..]);
        offset += 2;
        return value;
    }

    private static int ReadS4(ReadOnlySpan<byte> data, ref int offset)
    {
        if (offset + 4 > data.Length)
        {
            throw new InvalidDataException("truncated binary patch command");
        }

        var value = BinaryPrimitives.ReadInt32BigEndian(data[offset..]);
        offset += 4;
        return value;
    }

    private static long ReadS8(ReadOnlySpan<byte> data, ref int offset)
    {
        if (offset + 8 > data.Length)
        {
            throw new InvalidDataException("truncated binary patch command");
        }

        var value = BinaryPrimitives.ReadInt64BigEndian(data[offset..]);
        offset += 8;
        return value;
    }
}
