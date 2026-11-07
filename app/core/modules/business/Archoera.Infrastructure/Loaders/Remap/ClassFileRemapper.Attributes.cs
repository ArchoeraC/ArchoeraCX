using System.Buffers.Binary;
using System.Text;

namespace Archoera.Infrastructure.Loaders.Remap;

/// <summary>
/// Attribute ordering, payload rewriting and attribute-name allocation for
/// <see cref="ClassFileRemapper"/>. Kept in a partial file so the class-file
/// writer stays reviewable.
/// </summary>
internal sealed partial class ClassFileRemapper
{
    private byte[]? RewriteAttribute(
        string name,
        byte[] info,
        AttributeOwner owner,
        string className,
        string? methodName,
        string? methodDescriptor,
        bool isStatic)
    {
        if (name is "RuntimeVisibleParameterAnnotations" or "RuntimeInvisibleParameterAnnotations")
        {
            var fix = FixParameterAnnotations(info, methodName, methodDescriptor);
            return fix.Applied
                ? fix.Payload
                : AnnotationRewriter.Rewrite(
                    info,
                    _source,
                    _context,
                    AnnotationPayloadKind.ParameterAnnotations);
        }

        if (name == "Code")
        {
            var changed = false;
            var payload = CodeAttributeRewriter.Rewrite(
                info,
                _source,
                _context,
                className,
                methodName ?? string.Empty,
                methodDescriptor ?? string.Empty,
                isStatic,
                ref changed,
                Fixes(ArtFixers.Identifiers));
            if (changed)
            {
                Changed = true;
            }

            return payload;
        }

        return name switch
        {
            "Signature" => RewriteSignature(info, owner),
            "SourceFile" => RewriteSourceFile(info),
            "ConstantValue" => RewriteConstantValue(info),
            "MethodParameters" => RewriteMethodParameters(info),
            "LineNumberTable" or "Synthetic" or "Deprecated" => info,
            "Exceptions" => RewriteClassList(info),
            "InnerClasses" => RewriteInnerClasses(info),
            "EnclosingMethod" => RewriteEnclosingMethod(info),
            "NestHost" => RewriteSingleClass(info),
            "NestMembers" or "PermittedSubclasses" => RewriteClassList(info),
            "RuntimeVisibleAnnotations" or "RuntimeInvisibleAnnotations" =>
                AnnotationRewriter.Rewrite(
                    info,
                    _source,
                    _context,
                    AnnotationPayloadKind.Annotations),
            "RuntimeVisibleTypeAnnotations" or "RuntimeInvisibleTypeAnnotations" =>
                AnnotationRewriter.Rewrite(
                    info,
                    _source,
                    _context,
                    AnnotationPayloadKind.TypeAnnotations),
            "AnnotationDefault" =>
                AnnotationRewriter.Rewrite(
                    info,
                    _source,
                    _context,
                    AnnotationPayloadKind.DefaultValue),
            "BootstrapMethods" => _context.Bootstraps.Write(),
            "Record" => RewriteRecord(info),
            _ => info,
        };
    }

    /// <summary>
    /// ART's <c>ParameterAnnotationFixer</c>: obfuscators can keep the synthetic
    /// constructor parameters in the annotation table, so the entries are
    /// shifted left by the synthetic prefix. Returns <c>Applied = true</c> with a
    /// null payload when the whole attribute disappears.
    /// </summary>
    private (bool Applied, byte[]? Payload) FixParameterAnnotations(
        byte[] info,
        string? methodName,
        string? methodDescriptor)
    {
        if (!Fixes(ArtFixers.Annotations) ||
            methodName != "<init>" ||
            _parameterShift == 0 ||
            methodDescriptor is null ||
            !DescriptorStartsWithSyntheticParams(methodDescriptor))
        {
            return (false, null);
        }

        var argumentCount = CountArguments(methodDescriptor);
        var slots = ReadParameterAnnotationSlots(info);
        if (!slots.Exists(HasAnnotations))
        {
            // ASM never emits the attribute when no parameter carries an
            // annotation, so a table of empty entries disappears.
            return (true, null);
        }

        if (!_alwaysShiftParameterAnnotations && slots.Count != argumentCount)
        {
            // 1.0.6 / 2.0.3 shift only when the table declares one entry per
            // argument; otherwise they leave the table untouched.
            return (false, null);
        }

        var shifted = new List<byte[]>(argumentCount - _parameterShift);
        for (var index = _parameterShift; index < argumentCount; index++)
        {
            // 0.1.22 sizes the array from the descriptor, so a short table is
            // padded with empty entries before the synthetic prefix is dropped.
            shifted.Add(index < slots.Count ? slots[index] : EmptySlot);
        }

        Changed = true;
        if (!shifted.Exists(HasAnnotations))
        {
            return (true, null);
        }

        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        writer.Write((byte)shifted.Count);
        foreach (var slot in shifted)
        {
            writer.Write(slot);
        }

        writer.Flush();
        return (true, stream.ToArray());
    }

    private static readonly byte[] EmptySlot = [0, 0];

    private static bool HasAnnotations(byte[] slot)
        => BinaryPrimitives.ReadUInt16BigEndian(slot) != 0;

    private static List<byte[]> ReadParameterAnnotationSlots(byte[] info)
    {
        var count = info[0];
        var slots = new List<byte[]>(count);
        var offset = 1;
        for (var index = 0; index < count; index++)
        {
            var start = offset;
            var annotations = BinaryPrimitives.ReadUInt16BigEndian(info.AsSpan(offset));
            offset += 2;
            for (var annotation = 0; annotation < annotations; annotation++)
            {
                offset = SkipAnnotation(info, offset);
            }

            slots.Add(info.AsSpan(start, offset - start).ToArray());
        }

        return slots;
    }

    private static int SkipAnnotation(byte[] data, int offset)
    {
        offset += 2; // type_index
        var pairs = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(offset));
        offset += 2;
        for (var index = 0; index < pairs; index++)
        {
            offset += 2; // element_name_index
            offset = SkipElementValue(data, offset);
        }

        return offset;
    }

    private static int SkipElementValue(byte[] data, int offset)
    {
        var tag = (char)data[offset++];
        return tag switch
        {
            'B' or 'C' or 'D' or 'F' or 'I' or 'J' or 'S' or 'Z' or 's' or 'c' => offset + 2,
            'e' => offset + 4,
            '@' => SkipAnnotation(data, offset),
            '[' => SkipArray(data, offset),
            _ => throw new ClassFileException($"unknown annotation element tag '{tag}'"),
        };
    }

    private static int SkipArray(byte[] data, int offset)
    {
        var count = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(offset));
        offset += 2;
        for (var index = 0; index < count; index++)
        {
            offset = SkipElementValue(data, offset);
        }

        return offset;
    }

    private static int CountArguments(string descriptor)
    {
        var count = 0;
        var index = 1;
        while (index < descriptor.Length && descriptor[index] != ')')
        {
            if (descriptor[index] == '[')
            {
                index++;
                continue;
            }

            if (descriptor[index] == 'L')
            {
                index = descriptor.IndexOf(';', index) + 1;
            }
            else
            {
                index++;
            }

            count++;
        }

        return count;
    }

    private byte[] RewriteSignature(byte[] info, AttributeOwner owner)
    {
        var descriptor = _source.GetUtf8(BinaryPrimitives.ReadUInt16BigEndian(info));
        var mapped = _context.Pool.Utf8(_context.Descriptors.MapSignature(
            descriptor,
            typeSignature: owner is AttributeOwner.Field or AttributeOwner.RecordComponent));
        var result = new byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(result, (ushort)mapped);
        return result;
    }

    private byte[] RewriteClassList(byte[] info)
    {
        var copy = (byte[])info.Clone();
        var count = BinaryPrimitives.ReadUInt16BigEndian(copy);
        var offset = 2;
        for (var index = 0; index < count; index++)
        {
            var mapped = _context.MapClassIndex(
                BinaryPrimitives.ReadUInt16BigEndian(copy.AsSpan(offset)));
            BinaryPrimitives.WriteUInt16BigEndian(copy.AsSpan(offset), (ushort)mapped);
            offset += 2;
        }

        return copy;
    }

    private byte[] RewriteSingleClass(byte[] info)
    {
        var mapped = _context.MapClassIndex(BinaryPrimitives.ReadUInt16BigEndian(info));
        var result = new byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(result, (ushort)mapped);
        return result;
    }

    private byte[] RewriteInnerClasses(byte[] info)
    {
        var copy = (byte[])info.Clone();
        var count = BinaryPrimitives.ReadUInt16BigEndian(copy);
        var offset = 2;
        for (var index = 0; index < count; index++)
        {
            var inner = BinaryPrimitives.ReadUInt16BigEndian(copy.AsSpan(offset));
            var outer = BinaryPrimitives.ReadUInt16BigEndian(copy.AsSpan(offset + 2));
            var innerName = BinaryPrimitives.ReadUInt16BigEndian(copy.AsSpan(offset + 4));
            if (inner != 0)
            {
                BinaryPrimitives.WriteUInt16BigEndian(
                    copy.AsSpan(offset),
                    (ushort)_context.MapClassIndex(inner));
                if (innerName != 0)
                {
                    var mappedName = _context.MapInnerClassName(
                        _source.GetClassName(inner),
                        _source.GetUtf8(innerName));
                    BinaryPrimitives.WriteUInt16BigEndian(
                        copy.AsSpan(offset + 4),
                        (ushort)_context.Pool.Utf8(mappedName));
                }
            }

            if (outer != 0)
            {
                BinaryPrimitives.WriteUInt16BigEndian(
                    copy.AsSpan(offset + 2),
                    (ushort)_context.MapClassIndex(outer));
            }

            offset += 8;
        }

        return copy;
    }

    private byte[] RewriteRecord(byte[] info)
    {
        var reader = new SpanReader(info);
        var count = reader.U2();
        using var stream = new MemoryStream(info.Length + 32);
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        WriteU2(writer, (ushort)count);
        for (var index = 0; index < count; index++)
        {
            var name = _source.GetUtf8(reader.U2());
            var descriptor = _source.GetUtf8(reader.U2());
            WriteU2(writer, (ushort)_context.Pool.Utf8(
                _context.MapFieldName(_model.ClassName, name, descriptor)));
            WriteU2(writer, (ushort)_context.Pool.Utf8(
                _context.Descriptors.MapDescriptor(descriptor)));

            var attributeCount = reader.U2();
            var attributes = new List<ClassAttributeModel>(attributeCount);
            for (var nested = 0; nested < attributeCount; nested++)
            {
                var nameIndex = reader.U2();
                var length = checked((int)reader.U4());
                attributes.Add(new ClassAttributeModel(nameIndex, reader.Bytes(length)));
            }

            WriteAttributes(
                writer,
                attributes,
                AttributeOwner.RecordComponent,
                className: _model.ClassName);
        }

        writer.Flush();
        return stream.ToArray();
    }

    private byte[] RewriteSourceFile(byte[] info)
    {
        var name = Fixes(ArtFixers.SourceFile)
            ? DerivedSourceName()
            : _source.GetUtf8(BinaryPrimitives.ReadUInt16BigEndian(info));
        var mapped = _context.Pool.Utf8(name);
        var result = new byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(result, (ushort)mapped);
        return result;
    }

    private byte[] RewriteConstantValue(byte[] info)
    {
        var index = BinaryPrimitives.ReadUInt16BigEndian(info);
        if (index == 0)
        {
            return info;
        }

        var entry = _source[index];
        var mapped = entry.Tag switch
        {
            ConstantPoolTag.Integer => _context.Pool.Integer(entry.First),
            ConstantPoolTag.Float => _context.Pool.Float(BitConverter.Int32BitsToSingle(entry.First)),
            ConstantPoolTag.Long => _context.Pool.Long(entry.Wide),
            ConstantPoolTag.Double => _context.Pool.Double(BitConverter.Int64BitsToDouble(entry.Wide)),
            ConstantPoolTag.String => _context.Pool.StringConstant(_source.GetUtf8(entry.First)),
            _ => throw new ClassFileException($"constant value tag {entry.Tag} is not supported"),
        };
        var result = new byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(result, (ushort)mapped);
        return result;
    }

    private byte[] RewriteMethodParameters(byte[] info)
    {
        var copy = (byte[])info.Clone();
        var count = copy[0];
        var offset = 1;
        for (var index = 0; index < count; index++)
        {
            var nameIndex = BinaryPrimitives.ReadUInt16BigEndian(copy.AsSpan(offset));
            if (nameIndex != 0)
            {
                var mapped = _context.Pool.Utf8(_source.GetUtf8(nameIndex));
                BinaryPrimitives.WriteUInt16BigEndian(copy.AsSpan(offset), (ushort)mapped);
            }

            offset += 4;
        }

        return copy;
    }

    private byte[] RewriteEnclosingMethod(byte[] info)
    {
        var classIndex = BinaryPrimitives.ReadUInt16BigEndian(info);
        var nameAndType = BinaryPrimitives.ReadUInt16BigEndian(info.AsSpan(2));
        var result = new byte[4];
        BinaryPrimitives.WriteUInt16BigEndian(result, (ushort)_context.MapClassIndex(classIndex));
        if (nameAndType == 0)
        {
            return result;
        }

        var owner = _source.GetClassName(classIndex);
        var name = _source.GetNameAndType(nameAndType, out var descriptor);
        var mappedName = _context.MapMethodName(owner, name, descriptor);
        var mappedDescriptor = _context.Descriptors.MapDescriptor(descriptor);
        BinaryPrimitives.WriteUInt16BigEndian(
            result.AsSpan(2),
            (ushort)_context.Pool.NameAndType(mappedName, mappedDescriptor));
        return result;
    }

    private static string[] KnownNames(AttributeOwner owner) => owner switch
    {
        AttributeOwner.Class => new[]
        {
            "InnerClasses", "EnclosingMethod", "Signature", "SourceFile",
            "SourceDebugExtension", "Deprecated", "RuntimeVisibleAnnotations",
            "RuntimeInvisibleAnnotations", "RuntimeVisibleTypeAnnotations",
            "RuntimeInvisibleTypeAnnotations", "BootstrapMethods", "NestHost",
            "NestMembers", "PermittedSubclasses", "Record",
        },
        AttributeOwner.Method => new[]
        {
            "Code", "Exceptions", "Signature", "Deprecated",
            "RuntimeVisibleAnnotations", "RuntimeInvisibleAnnotations",
            "RuntimeVisibleTypeAnnotations", "RuntimeInvisibleTypeAnnotations",
            "RuntimeVisibleParameterAnnotations", "RuntimeInvisibleParameterAnnotations",
            "AnnotationDefault", "MethodParameters",
        },
        AttributeOwner.RecordComponent => new[]
        {
            "Signature", "RuntimeVisibleAnnotations", "RuntimeInvisibleAnnotations",
            "RuntimeVisibleTypeAnnotations", "RuntimeInvisibleTypeAnnotations",
        },
        _ => new[]
        {
            "ConstantValue", "Signature", "Deprecated", "RuntimeVisibleAnnotations",
            "RuntimeInvisibleAnnotations", "RuntimeVisibleTypeAnnotations",
            "RuntimeInvisibleTypeAnnotations",
        },
    };

    private List<ClassAttributeModel> Order(List<ClassAttributeModel> attributes, AttributeOwner owner)
    {
        var order = KnownNames(owner);
        var result = new List<ClassAttributeModel>(attributes.Count);
        foreach (var name in order)
        {
            if (name == "BootstrapMethods" && _context.Bootstraps.Count == 0)
            {
                continue;
            }

            result.AddRange(attributes.Where(attribute => attribute.Name(_source) == name));
        }

        // ASM prepends custom attributes, so emit them in reverse visit order.
        for (var index = attributes.Count - 1; index >= 0; index--)
        {
            if (!order.Contains(attributes[index].Name(_source)))
            {
                result.Add(attributes[index]);
            }
        }

        return result;
    }

    /// <summary>
    /// Allocates attribute name Utf8 entries in the order <c>ClassWriter</c>'s
    /// size computation adds them: fields, methods (with their code attributes),
    /// class attributes, record components, then the <c>Record</c> wrapper.
    /// </summary>
    private void AllocateAttributeNames()
    {
        foreach (var field in _model.Fields)
        {
            AllocateMemberAttributeNames(field.Attributes, AttributeOwner.Field);
        }

        foreach (var method in _model.Methods)
        {
            AllocateMemberAttributeNames(method.Attributes, AttributeOwner.Method);
        }

        AllocateClassAttributeNames();
    }

    private void AllocateMemberAttributeNames(
        List<ClassAttributeModel> attributes,
        AttributeOwner owner)
    {
        var order = KnownNames(owner);
        var code = Find(attributes, "Code");
        if (code is not null)
        {
            _context.Pool.Utf8("Code");
            CodeAttributeRewriter.AllocateAttributeNames(code.Info, _source, _context);
        }

        for (var index = 0; index < order.Length; index++)
        {
            var name = order[index];
            if (name == "Code")
            {
                continue;
            }

            if (name == "BootstrapMethods" && _context.Bootstraps.Count == 0)
            {
                continue;
            }

            if (Find(attributes, name) is not null)
            {
                _context.Pool.Utf8(name);
            }
        }

        for (var index = attributes.Count - 1; index >= 0; index--)
        {
            var name = attributes[index].Name(_source);
            if (!order.Contains(name))
            {
                _context.Pool.Utf8(name);
            }
        }
    }

    private void AllocateClassAttributeNames()
    {
        var order = KnownNames(AttributeOwner.Class);
        foreach (var name in order)
        {
            if (name == "Record")
            {
                continue;
            }

            if (name == "BootstrapMethods")
            {
                if (_context.Bootstraps.Count > 0)
                {
                    _context.Pool.Utf8(name);
                }

                continue;
            }

            if (Find(_model.Attributes, name) is not null)
            {
                _context.Pool.Utf8(name);
            }
        }

        var record = Find(_model.Attributes, "Record");
        if (record is not null)
        {
            foreach (var component in ParseRecordComponents(record.Info))
            {
                AllocateMemberAttributeNames(component, AttributeOwner.RecordComponent);
            }

            _context.Pool.Utf8("Record");
        }

        for (var index = _model.Attributes.Count - 1; index >= 0; index--)
        {
            var name = _model.Attributes[index].Name(_source);
            if (!order.Contains(name))
            {
                _context.Pool.Utf8(name);
            }
        }
    }

    private List<List<ClassAttributeModel>> ParseRecordComponents(byte[] info)
    {
        var reader = new SpanReader(info);
        var count = reader.U2();
        var result = new List<List<ClassAttributeModel>>(count);
        for (var index = 0; index < count; index++)
        {
            _ = reader.U2();
            _ = reader.U2();
            var attributeCount = reader.U2();
            var attributes = new List<ClassAttributeModel>(attributeCount);
            for (var nested = 0; nested < attributeCount; nested++)
            {
                var nameIndex = reader.U2();
                var length = checked((int)reader.U4());
                attributes.Add(new ClassAttributeModel(nameIndex, reader.Bytes(length)));
            }

            result.Add(attributes);
        }

        return result;
    }

    private void AllocateConstantValue(byte[] info)
    {
        var index = BinaryPrimitives.ReadUInt16BigEndian(info);
        if (index == 0)
        {
            return;
        }

        var entry = _source[index];
        switch (entry.Tag)
        {
            case ConstantPoolTag.Integer:
                _ = _context.Pool.Integer(entry.First);
                break;
            case ConstantPoolTag.Float:
                _ = _context.Pool.Float(BitConverter.Int32BitsToSingle(entry.First));
                break;
            case ConstantPoolTag.Long:
                _ = _context.Pool.Long(entry.Wide);
                break;
            case ConstantPoolTag.Double:
                _ = _context.Pool.Double(BitConverter.Int64BitsToDouble(entry.Wide));
                break;
            case ConstantPoolTag.String:
                _ = _context.Pool.StringConstant(_source.GetUtf8(entry.First));
                break;
            default:
                throw new ClassFileException(
                    $"constant value tag {entry.Tag} is not supported");
        }
    }

    private void AllocateMethodParameters(byte[] info)
    {
        var reader = new SpanReader(info);
        var count = reader.U1();
        for (var index = 0; index < count; index++)
        {
            var nameIndex = reader.U2();
            _ = reader.U2();
            if (nameIndex != 0)
            {
                _ = _context.Pool.Utf8(_source.GetUtf8(nameIndex));
            }
        }
    }

}
