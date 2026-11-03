using System.Buffers.Binary;
using System.Text;

namespace Archoera.Infrastructure.Loaders.Native;

/// <summary>One entry to write into a jar.</summary>
internal readonly record struct JarEntry(string Name, byte[] Data);

/// <summary>
/// Reproduces the ZIP layout of the Forge/NeoForge installer tools:
/// version 20, flags <c>0x0808</c> (data descriptor + UTF-8), raw deflate level
/// 6, a fixed 1989-11-26 timestamp, zeroed local sizes and a signed data
/// descriptor. The installer profiles hash these jars, so the layout must match
/// byte for byte.
/// </summary>
internal static class JarWriter
{
    private const uint LocalHeaderSignature = 0x04034B50;
    private const uint CentralHeaderSignature = 0x02014B50;
    private const uint EndOfCentralDirectorySignature = 0x06054B50;
    private const uint DataDescriptorSignature = 0x08074B50;
    private const ushort Flags = 0x0808;
    private const ushort Method = 8;
    private const ushort DosTime = 0;
    private const ushort DosDate = 0x137A; // 1989-11-26

    public static void Write(Stream output, IReadOnlyList<JarEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(entries);
        if (entries.Count > ushort.MaxValue)
        {
            throw new InvalidOperationException("too many jar entries for a non-Zip64 archive");
        }

        var central = new List<CentralRecord>(entries.Count);
        foreach (var entry in entries)
        {
            var name = Encoding.UTF8.GetBytes(entry.Name);
            if (name.Length > ushort.MaxValue)
            {
                throw new InvalidOperationException($"jar entry name is too long: {entry.Name}");
            }

            var compressed = ZipDeflate.Compress(entry.Data);
            var crc = Crc32.Compute(entry.Data);
            var offset = checked((uint)output.Position);
            WriteLocal(output, name, compressed, crc, entry.Data.Length);
            central.Add(new CentralRecord(
                name,
                crc,
                compressed.Length,
                entry.Data.Length,
                offset));
        }

        var centralOffset = checked((uint)output.Position);
        foreach (var record in central)
        {
            WriteCentral(output, record);
        }

        var centralSize = checked((uint)output.Position - centralOffset);
        WriteEnd(output, central.Count, centralSize, centralOffset);
    }

    private static void WriteLocal(
        Stream output,
        byte[] name,
        byte[] compressed,
        uint crc,
        int size)
    {
        var header = new byte[30];
        BinaryPrimitives.WriteUInt32LittleEndian(header, LocalHeaderSignature);
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(4), 20);
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(6), Flags);
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(8), Method);
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(10), DosTime);
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(12), DosDate);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(14), 0);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(18), 0);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(22), 0);
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(26), (ushort)name.Length);
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(28), 0);
        output.Write(header);
        output.Write(name);
        output.Write(compressed);

        var descriptor = new byte[16];
        BinaryPrimitives.WriteUInt32LittleEndian(descriptor, DataDescriptorSignature);
        BinaryPrimitives.WriteUInt32LittleEndian(descriptor.AsSpan(4), crc);
        BinaryPrimitives.WriteUInt32LittleEndian(descriptor.AsSpan(8), (uint)compressed.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(descriptor.AsSpan(12), (uint)size);
        output.Write(descriptor);
    }

    private static void WriteCentral(Stream output, CentralRecord record)
    {
        var header = new byte[46];
        BinaryPrimitives.WriteUInt32LittleEndian(header, CentralHeaderSignature);
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(4), 0x0014);
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(6), 20);
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(8), Flags);
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(10), Method);
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(12), DosTime);
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(14), DosDate);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(16), record.Crc);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(20), (uint)record.CompressedSize);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(24), (uint)record.Size);
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(28), (ushort)record.Name.Length);
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(30), 0);
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(32), 0);
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(34), 0);
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(36), 0);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(38), 0);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(42), record.Offset);
        output.Write(header);
        output.Write(record.Name);
    }

    private static void WriteEnd(Stream output, int count, uint size, uint offset)
    {
        var end = new byte[22];
        BinaryPrimitives.WriteUInt32LittleEndian(end, EndOfCentralDirectorySignature);
        BinaryPrimitives.WriteUInt16LittleEndian(end.AsSpan(4), 0);
        BinaryPrimitives.WriteUInt16LittleEndian(end.AsSpan(6), 0);
        BinaryPrimitives.WriteUInt16LittleEndian(end.AsSpan(8), (ushort)count);
        BinaryPrimitives.WriteUInt16LittleEndian(end.AsSpan(10), (ushort)count);
        BinaryPrimitives.WriteUInt32LittleEndian(end.AsSpan(12), size);
        BinaryPrimitives.WriteUInt32LittleEndian(end.AsSpan(16), offset);
        BinaryPrimitives.WriteUInt16LittleEndian(end.AsSpan(20), 0);
        output.Write(end);
    }

    private readonly record struct CentralRecord(
        byte[] Name,
        uint Crc,
        int CompressedSize,
        int Size,
        uint Offset);
}
