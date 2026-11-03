using System.Text;

namespace Archoera.Infrastructure.Loaders.Remap;

/// <summary>JVM constant pool tags from the class file specification.</summary>
internal enum ConstantPoolTag : byte
{
    Utf8 = 1,
    Integer = 3,
    Float = 4,
    Long = 5,
    Double = 6,
    Class = 7,
    String = 8,
    Fieldref = 9,
    Methodref = 10,
    InterfaceMethodref = 11,
    NameAndType = 12,
    MethodHandle = 15,
    MethodType = 16,
    Dynamic = 17,
    InvokeDynamic = 18,
    Module = 19,
    Package = 20,
}

/// <summary>
/// One constant pool slot. Long and double entries occupy two indices; the
/// second slot is represented by <see langword="null"/> in the pool.
/// </summary>
internal readonly record struct ConstantPoolEntry(
    ConstantPoolTag Tag,
    int First,
    int Second,
    long Wide,
    string? Text,
    byte[]? TextBytes)
{
    public bool IsWide => Tag is ConstantPoolTag.Long or ConstantPoolTag.Double;

    public static ConstantPoolEntry Utf8(string text, byte[]? raw = null)
        => new(ConstantPoolTag.Utf8, 0, 0, 0, text, raw);

    public static ConstantPoolEntry Pair(ConstantPoolTag tag, int first, int second)
        => new(tag, first, second, 0, null, null);

    public static ConstantPoolEntry Single(ConstantPoolTag tag, int first)
        => new(tag, first, 0, 0, null, null);

    public static ConstantPoolEntry WideValue(ConstantPoolTag tag, long value)
        => new(tag, 0, 0, value, null, null);
}

/// <summary>
/// Constant pool with 1-based indexing. The pool never hands out mutable
/// arrays, so callers cannot corrupt a parsed class file by accident.
/// </summary>
internal sealed class ConstantPool
{
    private readonly List<ConstantPoolEntry?> _entries = [null];

    public int Count => _entries.Count;

    public ConstantPoolEntry this[int index]
    {
        get
        {
            if (index <= 0 || index >= _entries.Count)
            {
                throw new ClassFileException($"constant pool index {index} is out of range");
            }

            return _entries[index]
                   ?? throw new ClassFileException($"constant pool index {index} is a wide-slot placeholder");
        }
    }

    public int Add(ConstantPoolEntry entry)
    {
        var index = _entries.Count;
        _entries.Add(entry);
        if (entry.IsWide)
        {
            _entries.Add(null);
        }

        return index;
    }

    public void AddNullSlot()
    {
        _entries.Add(null);
    }

    public ConstantPoolEntry? TryGet(int index)
        => index > 0 && index < _entries.Count ? _entries[index] : null;

    public string GetUtf8(int index)
    {
        var entry = this[index];
        if (entry.Tag != ConstantPoolTag.Utf8)
        {
            throw new ClassFileException($"constant pool index {index} is {entry.Tag}, not Utf8");
        }

        return entry.Text!;
    }

    public string GetClassName(int classIndex)
    {
        var entry = this[classIndex];
        if (entry.Tag != ConstantPoolTag.Class)
        {
            throw new ClassFileException($"constant pool index {classIndex} is {entry.Tag}, not Class");
        }

        return GetUtf8(entry.First);
    }

    public string GetNameAndType(int index, out string descriptor)
    {
        var entry = this[index];
        if (entry.Tag != ConstantPoolTag.NameAndType)
        {
            throw new ClassFileException($"constant pool index {index} is {entry.Tag}, not NameAndType");
        }

        descriptor = GetUtf8(entry.Second);
        return GetUtf8(entry.First);
    }

    /// <summary>
    /// Reads a class file modified UTF-8 string. Supplementary characters are
    /// encoded as surrogate pairs (CESU-8), which is what DataOutputStream uses.
    /// </summary>
    public static string DecodeModifiedUtf8(ReadOnlySpan<byte> bytes)
    {
        var builder = new StringBuilder(bytes.Length);
        var index = 0;

        while (index < bytes.Length)
        {
            var first = bytes[index++];
            if (first == 0)
            {
                throw new ClassFileException("modified UTF-8 contains a raw zero byte");
            }

            if (first < 0x80)
            {
                builder.Append((char)first);
                continue;
            }

            if (first == 0xC0)
            {
                if (index >= bytes.Length || bytes[index] != 0x80)
                {
                    throw new ClassFileException("modified UTF-8 has an invalid C0 sequence");
                }

                index++;
                builder.Append('\0');
                continue;
            }

            if ((first & 0xE0) == 0xC0)
            {
                if (index >= bytes.Length)
                {
                    throw new ClassFileException("modified UTF-8 is truncated");
                }

                var second = bytes[index++];
                if ((second & 0xC0) != 0x80)
                {
                    throw new ClassFileException("modified UTF-8 has an invalid continuation byte");
                }

                builder.Append((char)(((first & 0x1F) << 6) | (second & 0x3F)));
                continue;
            }

            if ((first & 0xF0) == 0xE0)
            {
                if (index + 1 >= bytes.Length)
                {
                    throw new ClassFileException("modified UTF-8 is truncated");
                }

                var second = bytes[index++];
                var third = bytes[index++];
                if ((second & 0xC0) != 0x80 || (third & 0xC0) != 0x80)
                {
                    throw new ClassFileException("modified UTF-8 has an invalid continuation byte");
                }

                builder.Append((char)(((first & 0x0F) << 12) | ((second & 0x3F) << 6) | (third & 0x3F)));
                continue;
            }

            throw new ClassFileException("modified UTF-8 has an invalid leading byte");
        }

        return builder.ToString();
    }

    /// <summary>Encodes a string the way DataOutputStream.writeUTF does.</summary>
    public static byte[] EncodeModifiedUtf8(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        var length = 0;
        foreach (var character in value)
        {
            length += character switch
            {
                >= '\u0001' and <= '\u007F' => 1,
                <= '\u07FF' => 2,
                _ => 3,
            };
        }

        if (length > ushort.MaxValue)
        {
            throw new ClassFileException("modified UTF-8 string exceeds 65535 bytes");
        }

        var bytes = new byte[length];
        var offset = 0;
        foreach (var character in value)
        {
            if (character is >= '\u0001' and <= '\u007F')
            {
                bytes[offset++] = (byte)character;
            }
            else if (character <= '\u07FF')
            {
                bytes[offset++] = (byte)(0xC0 | (character >> 6));
                bytes[offset++] = (byte)(0x80 | (character & 0x3F));
            }
            else
            {
                bytes[offset++] = (byte)(0xE0 | (character >> 12));
                bytes[offset++] = (byte)(0x80 | ((character >> 6) & 0x3F));
                bytes[offset++] = (byte)(0x80 | (character & 0x3F));
            }
        }

        return bytes;
    }

}
