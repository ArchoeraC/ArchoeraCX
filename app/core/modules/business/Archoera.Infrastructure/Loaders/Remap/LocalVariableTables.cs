using System.Buffers.Binary;

namespace Archoera.Infrastructure.Loaders.Remap;

/// <summary>
/// Merges LocalVariableTable and LocalVariableTypeTable the way ASM's
/// <c>ClassReader</c> does: every local variable table entry is visited once,
/// with the matching type table signature attached (matched by start_pc and
/// slot, ignoring length). Type-table-only entries are dropped, and the type
/// table is rebuilt from the entries that carry a signature.
/// </summary>
internal static class LocalVariableTables
{
    public readonly record struct Entry(
        int StartPc,
        int Length,
        int NameIndex,
        int DescriptorIndex,
        int SignatureIndex,
        int Slot);

    public static List<Entry> Read(byte[]? localTable, byte[]? typeTable)
    {
        var signatures = new List<(int StartPc, int Slot, int Signature)>();
        if (typeTable is not null)
        {
            var count = BinaryPrimitives.ReadUInt16BigEndian(typeTable);
            var offset = 2;
            for (var index = 0; index < count; index++)
            {
                var start = BinaryPrimitives.ReadUInt16BigEndian(typeTable.AsSpan(offset));
                var signature = BinaryPrimitives.ReadUInt16BigEndian(typeTable.AsSpan(offset + 6));
                var slot = BinaryPrimitives.ReadUInt16BigEndian(typeTable.AsSpan(offset + 8));
                signatures.Add((start, slot, signature));
                offset += 10;
            }
        }

        var entries = new List<Entry>();
        if (localTable is null)
        {
            return entries;
        }

        var entryCount = BinaryPrimitives.ReadUInt16BigEndian(localTable);
        var position = 2;
        for (var index = 0; index < entryCount; index++)
        {
            var start = BinaryPrimitives.ReadUInt16BigEndian(localTable.AsSpan(position));
            var length = BinaryPrimitives.ReadUInt16BigEndian(localTable.AsSpan(position + 2));
            var name = BinaryPrimitives.ReadUInt16BigEndian(localTable.AsSpan(position + 4));
            var descriptor = BinaryPrimitives.ReadUInt16BigEndian(localTable.AsSpan(position + 6));
            var slot = BinaryPrimitives.ReadUInt16BigEndian(localTable.AsSpan(position + 8));
            position += 10;

            var signatureIndex = -1;
            foreach (var signature in signatures)
            {
                if (signature.StartPc == start && signature.Slot == slot)
                {
                    signatureIndex = signature.Signature;
                    break;
                }
            }

            entries.Add(new Entry(start, length, name, descriptor, signatureIndex, slot));
        }

        return entries;
    }
}
