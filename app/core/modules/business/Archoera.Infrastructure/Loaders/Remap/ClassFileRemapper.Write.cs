using System.Buffers.Binary;
using System.Text;

namespace Archoera.Infrastructure.Loaders.Remap;

/// <summary>
/// Class-file serialisation for <see cref="ClassFileRemapper"/>. Kept in a
/// partial file so the allocation pass and the writer stay reviewable on their
/// own.
/// </summary>
internal sealed partial class ClassFileRemapper
{
    private byte[] Write()
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        writer.Write((byte)0xCA);
        writer.Write((byte)0xFE);
        writer.Write((byte)0xBA);
        writer.Write((byte)0xBE);
        WriteU2(writer, (ushort)_model.MinorVersion);
        WriteU2(writer, (ushort)_model.MajorVersion);
        _context.Pool.WritePool(writer);

        var classAccess = _model.Access;
        if (_forceRecordFlag && _isRecord && (classAccess & 0x10000) == 0)
        {
            classAccess |= 0x10000;
            Changed = true;
        }

        WriteU2(writer, (ushort)classAccess);
        WriteU2(writer, (ushort)_context.MapClassIndex(_model.ThisClass));
        WriteU2(writer, _model.SuperClass == 0
            ? (ushort)0
            : (ushort)_context.MapClassIndex(_model.SuperClass));
        WriteU2(writer, (ushort)_model.Interfaces.Length);
        foreach (var interfaceIndex in _model.Interfaces)
        {
            WriteU2(writer, (ushort)_context.MapClassIndex(interfaceIndex));
        }

        WriteMembers(writer, _model.Fields, isMethod: false);
        WriteMembers(writer, _model.Methods, isMethod: true);
        WriteAttributes(writer, _model.Attributes, AttributeOwner.Class, className: _model.ClassName);

        writer.Flush();
        return stream.ToArray();
    }

    private void WriteMembers(BinaryWriter writer, List<ClassMemberModel> members, bool isMethod)
    {
        WriteU2(writer, (ushort)members.Count);
        foreach (var member in members)
        {
            var name = member.Name(_source);
            var descriptor = member.Descriptor(_source);
            var mappedName = isMethod
                ? _context.MapMethodName(_model.ClassName, name, descriptor)
                : _context.MapFieldName(_model.ClassName, name, descriptor);

            var access = member.Access;
            if (!isMethod &&
                _isRecord &&
                Fixes(ArtFixers.Record) &&
                (access & 0x18) == 0x10)
            {
                // RecordFixer forces record component fields to private final.
                var fixedAccess = (access & ~6) | 2;
                if (fixedAccess != access)
                {
                    access = fixedAccess;
                    Changed = true;
                }
            }

            WriteU2(writer, (ushort)access);
            WriteU2(writer, (ushort)_context.Pool.Utf8(mappedName));
            WriteU2(writer, (ushort)_context.Pool.Utf8(
                _context.Descriptors.MapDescriptor(descriptor)));
            WriteAttributes(
                writer,
                member.Attributes,
                isMethod ? AttributeOwner.Method : AttributeOwner.Field,
                className: _model.ClassName,
                methodName: isMethod ? name : null,
                methodDescriptor: isMethod ? descriptor : null,
                isStatic: isMethod && (member.Access & 0x0008) != 0);
        }
    }

    private void WriteAttributes(
        BinaryWriter writer,
        List<ClassAttributeModel> attributes,
        AttributeOwner owner,
        string className,
        string? methodName = null,
        string? methodDescriptor = null,
        bool isStatic = false)
    {
        var ordered = Order(attributes, owner);
        var payloads = new List<(int NameIndex, byte[] Payload)>(ordered.Count);
        foreach (var attribute in ordered)
        {
            var name = attribute.Name(_source);
            var payload = RewriteAttribute(
                name,
                attribute.Info,
                owner,
                className,
                methodName,
                methodDescriptor,
                isStatic);
            if (payload is null)
            {
                // The parameter annotation fixer can drop an attribute entirely.
                continue;
            }

            var nameIndex = _context.Pool.CopiesSource
                ? attribute.NameIndex
                : _context.Pool.Utf8(name);
            payloads.Add((nameIndex, payload));
        }

        WriteU2(writer, (ushort)payloads.Count);
        foreach (var (nameIndex, payload) in payloads)
        {
            WriteU2(writer, (ushort)nameIndex);
            WriteU4(writer, (uint)payload.Length);
            writer.Write(payload);
        }
    }

    private ClassAttributeModel? Find(List<ClassAttributeModel> attributes, string name)
        => attributes.FirstOrDefault(attribute => attribute.Name(_source) == name);

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

    private enum AttributeOwner
    {
        Class,
        Field,
        Method,
        RecordComponent,
    }

    private ref struct SpanReader(byte[] data)
    {
        private readonly byte[] _data = data;
        private int _offset;

        public bool AtEnd => _offset == _data.Length;

        public int Remaining => _data.Length - _offset;

        public byte U1() => _data[_offset++];

        public int U2()
        {
            var value = BinaryPrimitives.ReadUInt16BigEndian(_data.AsSpan(_offset));
            _offset += 2;
            return value;
        }

        public uint U4()
        {
            var value = BinaryPrimitives.ReadUInt32BigEndian(_data.AsSpan(_offset));
            _offset += 4;
            return value;
        }

        public byte[] Bytes(int length)
        {
            var value = _data.AsSpan(_offset, length).ToArray();
            _offset += length;
            return value;
        }
    }
}
