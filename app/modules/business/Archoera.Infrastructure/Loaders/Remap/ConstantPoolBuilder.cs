using System.Buffers.Binary;

namespace Archoera.Infrastructure.Loaders.Remap;

/// <summary>
/// Append-only constant pool used by the ART writers. In
/// <see cref="ConstantPoolMode.CopyFromInput"/> mode the input pool is copied
/// verbatim and renamed constants are appended with ASM's deduplication rules;
/// in <see cref="ConstantPoolMode.Rebuild"/> mode the pool starts empty and
/// every constant is added in visit order, exactly like
/// <c>new ClassWriter(0)</c>.
/// </summary>
internal sealed class ConstantPoolBuilder
{
    private readonly ConstantPool _source;
    private readonly bool _copiesSource;
    private readonly List<ConstantPoolEntry> _appended = [];
    private readonly Dictionary<string, int> _utf8 = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _classes = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _nameAndTypes = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _members = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _methodHandles = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _methodTypes = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _strings = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _numbers = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _dynamics = new(StringComparer.Ordinal);
    public ConstantPoolBuilder(ConstantPool source, bool copiesSource)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _copiesSource = copiesSource;
        if (copiesSource)
        {
            Seed(source);
        }
    }

    public bool CopiesSource => _copiesSource;

    public int Count => FirstAppendedIndex + _appended.Count;

    private int FirstAppendedIndex => _copiesSource ? _source.Count : 1;

    public ConstantPoolEntry Entry(int index)
        => _copiesSource && index < _source.Count
            ? _source[index]
            : _appended[index - FirstAppendedIndex];

    public int Utf8(string text)
    {
        if (_utf8.TryGetValue(text, out var existing))
        {
            return existing;
        }

        var index = AddAppended(ConstantPoolEntry.Utf8(
            text,
            ConstantPool.EncodeModifiedUtf8(text)));
        _utf8[text] = index;
        return index;
    }

    public int Class(string internalName)
    {
        if (_classes.TryGetValue(internalName, out var existing))
        {
            return existing;
        }

        var nameIndex = Utf8(internalName);
        var index = AddAppended(ConstantPoolEntry.Single(ConstantPoolTag.Class, nameIndex));
        _classes[internalName] = index;
        return index;
    }

    public int NameAndType(string name, string descriptor)
    {
        var key = $"{name}\0{descriptor}";
        if (_nameAndTypes.TryGetValue(key, out var existing))
        {
            return existing;
        }

        var nameIndex = Utf8(name);
        var descriptorIndex = Utf8(descriptor);
        var index = AddAppended(
            ConstantPoolEntry.Pair(ConstantPoolTag.NameAndType, nameIndex, descriptorIndex));
        _nameAndTypes[key] = index;
        return index;
    }

    public int MemberRef(ConstantPoolTag tag, string owner, string name, string descriptor)
    {
        var key = $"{tag}\0{owner}\0{name}\0{descriptor}";
        if (_members.TryGetValue(key, out var existing))
        {
            return existing;
        }

        var classIndex = Class(owner);
        var nameAndTypeIndex = NameAndType(name, descriptor);
        var index = AddAppended(
            ConstantPoolEntry.Pair(tag, classIndex, nameAndTypeIndex));
        _members[key] = index;
        return index;
    }

    public int MethodHandle(int kind, string owner, string name, string descriptor, bool isInterface)
    {
        var key = $"{kind}\0{owner}\0{name}\0{descriptor}\0{isInterface}";
        if (_methodHandles.TryGetValue(key, out var existing))
        {
            return existing;
        }

        // ASM adds a Fieldref for H_GETFIELD..H_PUTSTATIC and a method ref
        // otherwise, so the handle's referenced tag depends on its kind.
        var referenceTag = kind <= 4
            ? ConstantPoolTag.Fieldref
            : isInterface ? ConstantPoolTag.InterfaceMethodref : ConstantPoolTag.Methodref;
        var reference = MemberRef(referenceTag, owner, name, descriptor);
        var index = AddAppended(ConstantPoolEntry.Pair(ConstantPoolTag.MethodHandle, kind, reference));
        _methodHandles[key] = index;
        return index;
    }

    public int MethodType(string descriptor)
    {
        if (_methodTypes.TryGetValue(descriptor, out var existing))
        {
            return existing;
        }

        var index = AddAppended(
            ConstantPoolEntry.Single(ConstantPoolTag.MethodType, Utf8(descriptor)));
        _methodTypes[descriptor] = index;
        return index;
    }

    public int InvokeDynamic(string name, string descriptor, int bootstrapIndex)
    {
        var key = $"{bootstrapIndex}\0{name}\0{descriptor}";
        if (_dynamics.TryGetValue(key, out var existing))
        {
            return existing;
        }

        var nameAndType = NameAndType(name, descriptor);
        var index = AddAppended(
            ConstantPoolEntry.Pair(ConstantPoolTag.InvokeDynamic, bootstrapIndex, nameAndType));
        _dynamics[key] = index;
        return index;
    }

    public int StringConstant(string value)
    {
        if (_strings.TryGetValue(value, out var existing))
        {
            return existing;
        }

        var index = AddAppended(
            ConstantPoolEntry.Single(ConstantPoolTag.String, Utf8(value)));
        _strings[value] = index;
        return index;
    }

    public int Integer(int value) => Number($"Integer{value}", ConstantPoolTag.Integer, value, 0);

    public int Float(float value)
        => Number($"Float{BitConverter.SingleToInt32Bits(value)}", ConstantPoolTag.Float, BitConverter.SingleToInt32Bits(value), 0);

    public int Long(long value) => Number($"Long{value}", ConstantPoolTag.Long, 0, value);

    public int Double(double value)
        => Number($"Double{BitConverter.DoubleToInt64Bits(value)}", ConstantPoolTag.Double, 0, BitConverter.DoubleToInt64Bits(value));

    public void WritePool(BinaryWriter writer)
    {
        var count = Count;
        WriteU2(writer, checked((ushort)count));

        if (_copiesSource)
        {
            for (var index = 1; index < _source.Count; index++)
            {
                var entry = _source.TryGet(index);
                if (entry is null)
                {
                    continue;
                }

                WriteEntry(writer, entry.Value);
            }
        }

        foreach (var entry in _appended)
        {
            if (entry.Tag == 0)
            {
                continue;
            }

            WriteEntry(writer, entry);
        }
    }

    private int Number(string key, ConstantPoolTag tag, int integerBits, long wideBits)
    {
        if (_numbers.TryGetValue(key, out var existing))
        {
            return existing;
        }

        var entry = tag is ConstantPoolTag.Long or ConstantPoolTag.Double
            ? ConstantPoolEntry.WideValue(tag, wideBits)
            : ConstantPoolEntry.Single(tag, integerBits);
        var index = AddAppended(entry);
        _numbers[key] = index;
        return index;
    }

    private int AddAppended(ConstantPoolEntry entry)
    {
        var index = FirstAppendedIndex + _appended.Count;
        _appended.Add(entry);
        if (entry.IsWide)
        {
            _appended.Add(default);
        }

        return index;
    }

    private void Seed(ConstantPool source)
    {
        for (var index = 1; index < source.Count; index++)
        {
            var nullable = source.TryGet(index);
            if (nullable is not { } entry)
            {
                continue;
            }

            switch (entry.Tag)
            {
                case ConstantPoolTag.Utf8:
                    _utf8.TryAdd(entry.Text!, index);
                    break;
                case ConstantPoolTag.Class:
                    _classes.TryAdd(source.GetUtf8(entry.First), index);
                    break;
                case ConstantPoolTag.String:
                    _strings.TryAdd(source.GetUtf8(entry.First), index);
                    break;
                case ConstantPoolTag.MethodType:
                    _methodTypes.TryAdd(source.GetUtf8(entry.First), index);
                    break;
                case ConstantPoolTag.NameAndType:
                    _nameAndTypes.TryAdd(
                        $"{source.GetUtf8(entry.First)}\0{source.GetUtf8(entry.Second)}",
                        index);
                    break;
                case ConstantPoolTag.Fieldref:
                case ConstantPoolTag.Methodref:
                case ConstantPoolTag.InterfaceMethodref:
                {
                    var owner = source.GetClassName(entry.First);
                    var name = source.GetNameAndType(entry.Second, out var descriptor);
                    _members.TryAdd($"{entry.Tag}\0{owner}\0{name}\0{descriptor}", index);
                    break;
                }

                case ConstantPoolTag.MethodHandle:
                {
                    var reference = source.TryGet(entry.Second);
                    if (reference is { } target &&
                        target.Tag is ConstantPoolTag.Fieldref or ConstantPoolTag.Methodref or ConstantPoolTag.InterfaceMethodref)
                    {
                        var owner = source.GetClassName(target.First);
                        var name = source.GetNameAndType(target.Second, out var descriptor);
                        var isInterface = target.Tag == ConstantPoolTag.InterfaceMethodref;
                        _methodHandles.TryAdd(
                            $"{entry.First}\0{owner}\0{name}\0{descriptor}\0{isInterface}",
                            index);
                    }

                    break;
                }
                case ConstantPoolTag.Integer:
                case ConstantPoolTag.Float:
                    _numbers.TryAdd($"{entry.Tag}{entry.First}", index);
                    break;
                case ConstantPoolTag.Long:
                case ConstantPoolTag.Double:
                    _numbers.TryAdd($"{entry.Tag}{entry.Wide}", index);
                    break;
                case ConstantPoolTag.Module:
                case ConstantPoolTag.Package:
                    _utf8.TryAdd(source.GetUtf8(entry.First), index);
                    break;
                case ConstantPoolTag.InvokeDynamic:
                {
                    var name = source.GetNameAndType(entry.Second, out var descriptor);
                    _dynamics.TryAdd($"{entry.First}\0{name}\0{descriptor}", index);
                    break;
                }
            }
        }
    }

    private static void WriteEntry(BinaryWriter writer, ConstantPoolEntry entry)
    {
        writer.Write((byte)entry.Tag);
        switch (entry.Tag)
        {
            case ConstantPoolTag.Utf8:
            {
                var bytes = entry.TextBytes ?? ConstantPool.EncodeModifiedUtf8(entry.Text!);
                WriteU2(writer, checked((ushort)bytes.Length));
                writer.Write(bytes);
                break;
            }

            case ConstantPoolTag.Integer:
            case ConstantPoolTag.Float:
                WriteU4(writer, unchecked((uint)entry.First));
                break;
            case ConstantPoolTag.Long:
            case ConstantPoolTag.Double:
                WriteU8(writer, unchecked((ulong)entry.Wide));
                break;
            case ConstantPoolTag.Class:
            case ConstantPoolTag.String:
            case ConstantPoolTag.MethodType:
            case ConstantPoolTag.Module:
            case ConstantPoolTag.Package:
                WriteU2(writer, (ushort)entry.First);
                break;
            case ConstantPoolTag.Fieldref:
            case ConstantPoolTag.Methodref:
            case ConstantPoolTag.InterfaceMethodref:
            case ConstantPoolTag.NameAndType:
            case ConstantPoolTag.Dynamic:
            case ConstantPoolTag.InvokeDynamic:
                WriteU2(writer, (ushort)entry.First);
                WriteU2(writer, (ushort)entry.Second);
                break;
            case ConstantPoolTag.MethodHandle:
                writer.Write((byte)entry.First);
                WriteU2(writer, (ushort)entry.Second);
                break;
            default:
                throw new ClassFileException($"cannot write constant pool tag {entry.Tag}");
        }
    }

    private static void WriteU2(BinaryWriter writer, ushort value)
    {
        writer.Write((byte)(value >> 8));
        writer.Write((byte)value);
    }

    private static void WriteU4(BinaryWriter writer, uint value)
    {
        writer.Write((byte)(value >> 24));
        writer.Write((byte)(value >> 16));
        writer.Write((byte)(value >> 8));
        writer.Write((byte)value);
    }

    private static void WriteU8(BinaryWriter writer, ulong value)
    {
        writer.Write((byte)(value >> 56));
        writer.Write((byte)(value >> 48));
        writer.Write((byte)(value >> 40));
        writer.Write((byte)(value >> 32));
        writer.Write((byte)(value >> 24));
        writer.Write((byte)(value >> 16));
        writer.Write((byte)(value >> 8));
        writer.Write((byte)value);
    }
}
