using System.Buffers.Binary;

namespace Archoera.Infrastructure.Loaders.Remap;

/// <summary>
/// Mirrors ASM's bootstrap-method interning. ASM rebuilds the BootstrapMethods
/// attribute from the methods referenced by invokedynamic instructions, in the
/// order they are first visited; the input attribute order is not authoritative.
/// </summary>
internal sealed class BootstrapMethodsTable
{
    private readonly List<(int Handle, int[] Arguments)> _entries = [];
    private readonly Dictionary<string, int> _index = new(StringComparer.Ordinal);

    public int Count => _entries.Count;

    public (int Handle, int[] Arguments) this[int index] => _entries[index];

    public void Seed(int[][] sourceEntries)
    {
        foreach (var entry in sourceEntries)
        {
            if (entry.Length == 0)
            {
                continue;
            }

            Intern(entry[0], entry[1..]);
        }
    }

    public static int[][] ReadSourceEntries(byte[]? payload)
    {
        if (payload is null)
        {
            return [];
        }

        var count = BinaryPrimitives.ReadUInt16BigEndian(payload);
        var result = new int[count][];
        var offset = 2;
        for (var index = 0; index < count; index++)
        {
            var handle = BinaryPrimitives.ReadUInt16BigEndian(payload.AsSpan(offset));
            var arguments = BinaryPrimitives.ReadUInt16BigEndian(payload.AsSpan(offset + 2));
            offset += 4;
            var values = new int[arguments + 1];
            values[0] = handle;
            for (var argument = 0; argument < arguments; argument++)
            {
                values[argument + 1] = BinaryPrimitives.ReadUInt16BigEndian(payload.AsSpan(offset));
                offset += 2;
            }

            result[index] = values;
        }

        return result;
    }

    public int Intern(int handle, int[] arguments)
    {
        var key = $"{handle}:{string.Join(',', arguments)}";
        if (_index.TryGetValue(key, out var existing))
        {
            return existing;
        }

        var index = _entries.Count;
        _entries.Add((handle, arguments));
        _index[key] = index;
        return index;
    }

    public byte[] Write()
    {
        var length = 2 + _entries.Sum(entry => 4 + entry.Arguments.Length * 2);
        var result = new byte[length];
        BinaryPrimitives.WriteUInt16BigEndian(result, (ushort)_entries.Count);
        var offset = 2;
        foreach (var (handle, arguments) in _entries)
        {
            BinaryPrimitives.WriteUInt16BigEndian(result.AsSpan(offset), (ushort)handle);
            BinaryPrimitives.WriteUInt16BigEndian(result.AsSpan(offset + 2), (ushort)arguments.Length);
            offset += 4;
            foreach (var argument in arguments)
            {
                BinaryPrimitives.WriteUInt16BigEndian(result.AsSpan(offset), (ushort)argument);
                offset += 2;
            }
        }

        return result;
    }
}
