using System.Buffers.Binary;

namespace Archoera.Infrastructure.Loaders.Remap;

internal enum AnnotationPayloadKind
{
    Annotations,
    ParameterAnnotations,
    TypeAnnotations,
    DefaultValue,
}

/// <summary>
/// Rewrites the four annotation attribute families. Type and enum class
/// descriptors are remapped; annotation element names, enum constant names and
/// string values are preserved, matching ART's Remapper defaults.
/// </summary>
internal static class AnnotationRewriter
{
    public static void Allocate(
        byte[] info,
        ConstantPool source,
        RemapContext context,
        AnnotationPayloadKind kind)
        => Process(info, source, context, kind, output: null);

    public static byte[] Rewrite(
        byte[] info,
        ConstantPool source,
        RemapContext context,
        AnnotationPayloadKind kind)
    {
        using var stream = new MemoryStream(info.Length + 16);
        var output = new Output(stream);
        Process(info, source, context, kind, output);
        output.Flush();
        return stream.ToArray();
    }

    private static void Process(
        byte[] info,
        ConstantPool source,
        RemapContext context,
        AnnotationPayloadKind kind,
        Output? output)
    {
        var reader = new Reader(info);
        switch (kind)
        {
            case AnnotationPayloadKind.Annotations:
            {
                var count = reader.U2();
                output?.U2(count);
                for (var index = 0; index < count; index++)
                {
                    Annotation(reader, source, context, output);
                }

                break;
            }

            case AnnotationPayloadKind.ParameterAnnotations:
            {
                var parameters = reader.U1();
                output?.U1(parameters);
                for (var parameter = 0; parameter < parameters; parameter++)
                {
                    var count = reader.U2();
                    output?.U2(count);
                    for (var index = 0; index < count; index++)
                    {
                        Annotation(reader, source, context, output);
                    }
                }

                break;
            }

            case AnnotationPayloadKind.TypeAnnotations:
            {
                var count = reader.U2();
                output?.U2(count);
                for (var index = 0; index < count; index++)
                {
                    TypeAnnotation(reader, source, context, output);
                }

                break;
            }

            case AnnotationPayloadKind.DefaultValue:
                ElementValue(reader, source, context, output);
                break;
        }

        if (!reader.AtEnd)
        {
            throw new ClassFileException($"annotation payload has {reader.Remaining} trailing bytes");
        }
    }

    private static void Annotation(
        Reader reader,
        ConstantPool source,
        RemapContext context,
        Output? output)
    {
        var typeIndex = reader.U2();
        var mappedType = context.Pool.Utf8(
            context.Descriptors.MapDescriptor(source.GetUtf8(typeIndex)));
        output?.U2(mappedType);

        var pairs = reader.U2();
        output?.U2(pairs);
        for (var index = 0; index < pairs; index++)
        {
            var nameIndex = reader.U2();
            var mappedName = context.Pool.Utf8(source.GetUtf8(nameIndex));
            output?.U2(mappedName);
            ElementValue(reader, source, context, output);
        }
    }

    private static void ElementValue(
        Reader reader,
        ConstantPool source,
        RemapContext context,
        Output? output)
    {
        var tag = reader.U1();
        output?.U1(tag);

        switch ((char)tag)
        {
            case 'B':
            case 'C':
            case 'I':
            case 'S':
            case 'Z':
            {
                var index = reader.U2();
                var mapped = context.Pool.Integer(source[index].First);
                output?.U2(mapped);
                break;
            }

            case 'D':
            {
                var index = reader.U2();
                var mapped = context.Pool.Double(BitConverter.Int64BitsToDouble(source[index].Wide));
                output?.U2(mapped);
                break;
            }

            case 'F':
            {
                var index = reader.U2();
                var mapped = context.Pool.Float(BitConverter.Int32BitsToSingle(source[index].First));
                output?.U2(mapped);
                break;
            }

            case 'J':
            {
                var index = reader.U2();
                var mapped = context.Pool.Long(source[index].Wide);
                output?.U2(mapped);
                break;
            }

            case 's':
            {
                var index = reader.U2();
                var mapped = context.Pool.Utf8(source.GetUtf8(index));
                output?.U2(mapped);
                break;
            }

            case 'e':
            {
                var typeIndex = reader.U2();
                var nameIndex = reader.U2();
                var mappedType = context.Pool.Utf8(
                    context.Descriptors.MapDescriptor(source.GetUtf8(typeIndex)));
                var mappedName = context.Pool.Utf8(source.GetUtf8(nameIndex));
                output?.U2(mappedType);
                output?.U2(mappedName);
                break;
            }

            case 'c':
            {
                var index = reader.U2();
                var mapped = context.Pool.Utf8(
                    context.Descriptors.MapDescriptor(source.GetUtf8(index)));
                output?.U2(mapped);
                break;
            }

            case '@':
                Annotation(reader, source, context, output);
                break;

            case '[':
            {
                var count = reader.U2();
                output?.U2(count);
                for (var index = 0; index < count; index++)
                {
                    ElementValue(reader, source, context, output);
                }

                break;
            }

            default:
                throw new ClassFileException($"unknown annotation element tag '{tag}'");
        }
    }

    private static void TypeAnnotation(
        Reader reader,
        ConstantPool source,
        RemapContext context,
        Output? output)
    {
        // The reads must happen even when output is null (allocation pass), so
        // every field is stored in a local before being written.
        var targetType = reader.U1();
        output?.U1(targetType);

        switch (targetType)
        {
            case 0x00:
            case 0x01:
            {
                var index = reader.U1();
                output?.U1(index);
                break;
            }

            case 0x10:
            {
                var index = reader.U2();
                output?.U2(index);
                break;
            }

            case 0x11:
            case 0x12:
            {
                var first = reader.U1();
                var second = reader.U1();
                output?.U1(first);
                output?.U1(second);
                break;
            }

            case 0x13:
            case 0x14:
            case 0x15:
                break;

            case 0x16:
            {
                var index = reader.U1();
                output?.U1(index);
                break;
            }

            case 0x17:
            {
                var index = reader.U2();
                output?.U2(index);
                break;
            }

            case 0x40:
            case 0x41:
            {
                var length = reader.U2();
                output?.U2(length);
                for (var index = 0; index < length; index++)
                {
                    var start = reader.U2();
                    var span = reader.U2();
                    var slot = reader.U2();
                    output?.U2(start);
                    output?.U2(span);
                    output?.U2(slot);
                }

                break;
            }

            case 0x42:
            {
                var index = reader.U2();
                output?.U2(index);
                break;
            }

            case 0x43:
            case 0x44:
            case 0x45:
            case 0x46:
            {
                var offset = reader.U2();
                output?.U2(offset);
                break;
            }

            case 0x47:
            case 0x48:
            case 0x49:
            case 0x4A:
            case 0x4B:
            {
                var offset = reader.U2();
                var argument = reader.U1();
                output?.U2(offset);
                output?.U1(argument);
                break;
            }

            default:
                throw new ClassFileException($"unknown type annotation target {targetType}");
        }

        var pathLength = reader.U1();
        output?.U1(pathLength);
        for (var index = 0; index < pathLength; index++)
        {
            var kind = reader.U1();
            var argument = reader.U1();
            output?.U1(kind);
            output?.U1(argument);
        }

        Annotation(reader, source, context, output);
    }

    private sealed class Reader(byte[] data)
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
    }

    private sealed class Output(Stream stream)
    {
        private readonly Stream _stream = stream;

        public void U1(int value) => _stream.WriteByte((byte)value);

        public void U2(int value)
        {
            _stream.WriteByte((byte)(value >> 8));
            _stream.WriteByte((byte)value);
        }

        public void Flush() => _stream.Flush();
    }
}
