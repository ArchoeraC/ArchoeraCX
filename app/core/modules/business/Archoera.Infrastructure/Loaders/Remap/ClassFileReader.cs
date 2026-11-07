using System.Buffers.Binary;

namespace Archoera.Infrastructure.Loaders.Remap;

/// <summary>
/// Bounds-checked class file reader. It intentionally keeps attribute payloads
/// opaque: the profile-specific writer knows which attributes it must rebuild.
/// </summary>
internal static class ClassFileReader
{
    private const uint Magic = 0xCAFEBABE;

    public static ClassFileModel Read(ReadOnlySpan<byte> data)
    {
        var cursor = new Cursor(data);

        if (cursor.U4() != Magic)
        {
            throw new ClassFileException("class file magic is not CAFEBABE");
        }

        var minor = cursor.U2();
        var major = cursor.U2();
        if (major < 45)
        {
            throw new ClassFileException($"class file major version {major} is not supported");
        }

        var pool = ReadConstantPool(ref cursor);
        var access = cursor.U2();
        var thisClass = cursor.U2();
        var superClass = cursor.U2();
        RequireClass(pool, thisClass, "this_class");
        if (superClass != 0)
        {
            RequireClass(pool, superClass, "super_class");
        }

        var interfaceCount = cursor.U2();
        cursor.Ensure(interfaceCount * 2, "interfaces");
        var interfaces = new int[interfaceCount];
        for (var index = 0; index < interfaceCount; index++)
        {
            interfaces[index] = cursor.U2();
            RequireClass(pool, interfaces[index], "interface");
        }

        var fields = ReadMembers(ref cursor, pool, "field");
        var methods = ReadMembers(ref cursor, pool, "method");
        var attributes = ReadAttributes(ref cursor, pool);

        if (!cursor.AtEnd)
        {
            throw new ClassFileException("class file has trailing bytes");
        }

        return new ClassFileModel
        {
            MinorVersion = minor,
            MajorVersion = major,
            Pool = pool,
            Access = access,
            ThisClass = thisClass,
            SuperClass = superClass,
            Interfaces = interfaces,
            Fields = fields,
            Methods = methods,
            Attributes = attributes,
        };
    }

    private static ConstantPool ReadConstantPool(ref Cursor cursor)
    {
        var count = cursor.U2();
        if (count == 0)
        {
            throw new ClassFileException("constant pool count is zero");
        }

        var pool = new ConstantPool();
        var index = 1;
        while (index < count)
        {
            var tag = (ConstantPoolTag)cursor.U1();
            ConstantPoolEntry entry;

            switch (tag)
            {
                case ConstantPoolTag.Utf8:
                {
                    var length = cursor.U2();
                    var bytes = cursor.Bytes(length);
                    var text = ConstantPool.DecodeModifiedUtf8(bytes);
                    entry = ConstantPoolEntry.Utf8(text, bytes.ToArray());
                    break;
                }

                case ConstantPoolTag.Integer:
                case ConstantPoolTag.Float:
                    entry = ConstantPoolEntry.Single(tag, unchecked((int)cursor.U4()));
                    break;

                case ConstantPoolTag.Long:
                case ConstantPoolTag.Double:
                    entry = ConstantPoolEntry.WideValue(tag, unchecked((long)cursor.U8()));
                    break;

                case ConstantPoolTag.Class:
                case ConstantPoolTag.String:
                case ConstantPoolTag.MethodType:
                case ConstantPoolTag.Module:
                case ConstantPoolTag.Package:
                    entry = ConstantPoolEntry.Single(tag, cursor.U2());
                    break;

                case ConstantPoolTag.Fieldref:
                case ConstantPoolTag.Methodref:
                case ConstantPoolTag.InterfaceMethodref:
                case ConstantPoolTag.NameAndType:
                case ConstantPoolTag.Dynamic:
                case ConstantPoolTag.InvokeDynamic:
                    entry = ConstantPoolEntry.Pair(tag, cursor.U2(), cursor.U2());
                    break;

                case ConstantPoolTag.MethodHandle:
                    entry = ConstantPoolEntry.Pair(tag, cursor.U1(), cursor.U2());
                    break;

                default:
                    throw new ClassFileException($"unknown constant pool tag {(int)tag} at index {index}");
            }

            pool.Add(entry);
            index += entry.IsWide ? 2 : 1;
        }

        if (index != count)
        {
            throw new ClassFileException("constant pool has an invalid wide entry at the end");
        }

        ValidateConstantPool(pool);
        return pool;
    }

    private static void ValidateConstantPool(ConstantPool pool)
    {
        for (var index = 1; index < pool.Count; index++)
        {
            var entry = pool.TryGet(index);
            if (entry is not { } value)
            {
                continue;
            }

            switch (value.Tag)
            {
                case ConstantPoolTag.Class:
                case ConstantPoolTag.String:
                case ConstantPoolTag.MethodType:
                case ConstantPoolTag.Module:
                case ConstantPoolTag.Package:
                    RequireUtf8(pool, value.First, value.Tag.ToString());
                    break;

                case ConstantPoolTag.Fieldref:
                case ConstantPoolTag.Methodref:
                case ConstantPoolTag.InterfaceMethodref:
                    RequireTag(pool, value.First, ConstantPoolTag.Class, value.Tag.ToString());
                    RequireTag(pool, value.Second, ConstantPoolTag.NameAndType, value.Tag.ToString());
                    break;

                case ConstantPoolTag.NameAndType:
                    RequireUtf8(pool, value.First, "name");
                    RequireUtf8(pool, value.Second, "descriptor");
                    break;

                case ConstantPoolTag.MethodHandle:
                    if (value.First is >= 1 and <= 4)
                    {
                        RequireTag(pool, value.Second, ConstantPoolTag.Fieldref, "method handle field reference");
                    }
                    else if (value.First is >= 5 and <= 8)
                    {
                        RequireOneOf(
                            pool,
                            value.Second,
                            "method handle method reference",
                            ConstantPoolTag.Methodref,
                            ConstantPoolTag.InterfaceMethodref);
                    }
                    else if (value.First == 9)
                    {
                        RequireTag(
                            pool,
                            value.Second,
                            ConstantPoolTag.InterfaceMethodref,
                            "method handle interface reference");
                    }
                    else
                    {
                        throw new ClassFileException($"unknown method handle kind {value.First}");
                    }

                    break;

                case ConstantPoolTag.Dynamic:
                case ConstantPoolTag.InvokeDynamic:
                    RequireTag(pool, value.Second, ConstantPoolTag.NameAndType, value.Tag.ToString());
                    break;
            }
        }
    }

    private static List<ClassMemberModel> ReadMembers(ref Cursor cursor, ConstantPool pool, string kind)
    {
        var count = cursor.U2();
        cursor.Ensure(count * 8, $"{kind}s");
        var members = new List<ClassMemberModel>(count);
        for (var index = 0; index < count; index++)
        {
            var access = cursor.U2();
            var name = cursor.U2();
            var descriptor = cursor.U2();
            RequireUtf8(pool, name, $"{kind} name");
            RequireUtf8(pool, descriptor, $"{kind} descriptor");

            var member = new ClassMemberModel
            {
                Access = access,
                NameIndex = name,
                DescriptorIndex = descriptor,
            };
            member.Attributes.AddRange(ReadAttributes(ref cursor, pool));
            members.Add(member);
        }

        return members;
    }

    private static List<ClassAttributeModel> ReadAttributes(ref Cursor cursor, ConstantPool pool)
    {
        var count = cursor.U2();
        cursor.Ensure(count * 6L, "attributes");
        var attributes = new List<ClassAttributeModel>(count);
        for (var index = 0; index < count; index++)
        {
            var name = cursor.U2();
            RequireUtf8(pool, name, "attribute name");

            var length = cursor.U4();
            if (length > int.MaxValue)
            {
                throw new ClassFileException($"attribute '{pool.GetUtf8(name)}' is too large");
            }

            var info = cursor.Bytes((int)length).ToArray();
            attributes.Add(new ClassAttributeModel(name, info));
        }

        return attributes;
    }

    private static void RequireUtf8(ConstantPool pool, int index, string what)
    {
        if (pool.TryGet(index) is not { Tag: ConstantPoolTag.Utf8 })
        {
            throw new ClassFileException($"{what} index {index} is not a Utf8 entry");
        }
    }

    private static void RequireTag(
        ConstantPool pool,
        int index,
        ConstantPoolTag expected,
        string what)
    {
        var entry = pool.TryGet(index);
        if (entry is null || entry.Value.Tag != expected)
        {
            throw new ClassFileException($"{what} index {index} is not {expected}");
        }
    }

    private static void RequireOneOf(
        ConstantPool pool,
        int index,
        string what,
        params ConstantPoolTag[] expected)
    {
        var entry = pool.TryGet(index);
        if (entry is null || !expected.Contains(entry.Value.Tag))
        {
            throw new ClassFileException($"{what} index {index} has an unexpected tag");
        }
    }

    private static void RequireClass(ConstantPool pool, int index, string what)
    {
        if (index == 0)
        {
            return;
        }

        var entry = pool.TryGet(index);
        if (entry is null || entry.Value.Tag != ConstantPoolTag.Class)
        {
            throw new ClassFileException($"{what} index {index} is not a Class entry");
        }

        RequireUtf8(pool, entry.Value.First, what);
    }

    private ref struct Cursor(ReadOnlySpan<byte> data)
    {
        private readonly ReadOnlySpan<byte> _data = data;
        private int _offset;

        public bool AtEnd => _offset == _data.Length;

        public byte U1()
        {
            Ensure(1, "u1");
            return _data[_offset++];
        }

        public int U2()
        {
            Ensure(2, "u2");
            var value = BinaryPrimitives.ReadUInt16BigEndian(_data[_offset..]);
            _offset += 2;
            return value;
        }

        public uint U4()
        {
            Ensure(4, "u4");
            var value = BinaryPrimitives.ReadUInt32BigEndian(_data[_offset..]);
            _offset += 4;
            return value;
        }

        public ulong U8()
        {
            Ensure(8, "u8");
            var value = BinaryPrimitives.ReadUInt64BigEndian(_data[_offset..]);
            _offset += 8;
            return value;
        }

        public ReadOnlySpan<byte> Bytes(int length)
        {
            if (length < 0)
            {
                throw new ClassFileException("negative byte length");
            }

            Ensure(length, "payload");
            var value = _data.Slice(_offset, length);
            _offset += length;
            return value;
        }

        public void Ensure(long length, string what)
        {
            if (length < 0 || length > _data.Length - _offset)
            {
                throw new ClassFileException($"class file is truncated while reading {what}");
            }
        }
    }
}
