using System.Buffers.Binary;
using System.Globalization;

namespace Archoera.Infrastructure.Loaders.Remap;

/// <summary>
/// Rewrites a Code attribute: the instruction stream is re-encoded by
/// <see cref="CodeRelocator"/>, and every offset that refers to a code position
/// (exception handlers, line numbers, local variable ranges and stack map
/// frames) is translated to the new layout.
/// </summary>
internal static class CodeAttributeRewriter
{
    public static byte[] Rewrite(
        byte[] info,
        ConstantPool sourcePool,
        RemapContext context,
        string className,
        string methodName,
        string methodDescriptor,
        bool isStatic,
        ref bool changed,
        bool fixIdentifiers = false)
    {
        ArgumentNullException.ThrowIfNull(info);
        ArgumentNullException.ThrowIfNull(sourcePool);
        ArgumentNullException.ThrowIfNull(context);

        var reader = new BigEndianReader(info);
        var maxStack = reader.U2();
        var maxLocals = reader.U2();
        var codeLength = reader.U4();
        var source = reader.Bytes(checked((int)codeLength));
        var relocation = CodeRelocator.Relocate(
            source,
            sourcePool,
            context,
            className,
            methodName);
        var code = relocation.Code;

        var handlerCount = reader.U2();
        var handlers = reader.Bytes(handlerCount * 8);
        PatchExceptionTable(handlers, relocation, context);

        var nestedCount = reader.U2();
        var nested = new List<(int NameIndex, string Name, byte[] Payload)>(nestedCount);
        for (var index = 0; index < nestedCount; index++)
        {
            var nameIndex = reader.U2();
            var length = reader.U4();
            var payload = reader.Bytes(checked((int)length));
            nested.Add((nameIndex, sourcePool.GetUtf8(nameIndex), payload));
        }

        if (!reader.AtEnd)
        {
            throw new ClassFileException($"Code attribute has {reader.Remaining} trailing bytes");
        }

        var lineNumbers = nested.FirstOrDefault(item => item.Name == "LineNumberTable");
        var stackMap = nested.FirstOrDefault(item => item.Name == "StackMapTable");
        var localVariables = LocalVariableTables.Read(
            nested.FirstOrDefault(item => item.Name == "LocalVariableTable").Payload,
            nested.FirstOrDefault(item => item.Name == "LocalVariableTypeTable").Payload);
        var extras = nested
            .Where(item => item.Name is not (
                "LineNumberTable" or "StackMapTable" or
                "LocalVariableTable" or "LocalVariableTypeTable"))
            .ToList();
        foreach (var extra in extras)
        {
            if (extra.Name is "RuntimeVisibleTypeAnnotations" or "RuntimeInvisibleTypeAnnotations")
            {
                throw new NotSupportedException(
                    $"code attribute {extra.Name} is not implemented yet");
            }
        }

        var hasLineNumbers = lineNumbers.Payload is { Length: > 0 } &&
            BinaryPrimitives.ReadUInt16BigEndian(lineNumbers.Payload) > 0;
        var hasStackMap = stackMap.Payload is { Length: > 0 } &&
            BinaryPrimitives.ReadUInt16BigEndian(stackMap.Payload) > 0;
        var hasLocalVariables = localVariables.Count > 0;
        var hasTypeTable = localVariables.Exists(entry => entry.SignatureIndex >= 0);

        using var output = new MemoryStream();
        using var writer = new BigEndianWriter(output);
        writer.U2(maxStack);
        writer.U2(maxLocals);
        writer.U4((uint)code.Length);
        writer.Bytes(code);
        writer.U2(handlerCount);
        writer.Bytes(handlers);

        var codeAttributeCount = extras.Count +
            (hasStackMap ? 1 : 0) +
            (hasLineNumbers ? 1 : 0) +
            (hasLocalVariables ? 1 : 0) +
            (hasTypeTable ? 1 : 0);
        writer.U2(codeAttributeCount);

        // ASM writes code attributes in JVMS order, not in input order.
        if (hasStackMap)
        {
            var rewritten = RewriteStackMapTable(
                stackMap.Payload!,
                relocation,
                sourcePool,
                context);
            WriteAttribute(writer, NameIndex(stackMap.NameIndex, stackMap.Name, context), rewritten);
        }

        if (hasLineNumbers)
        {
            var rewritten = RewriteLineNumbers(lineNumbers.Payload!, relocation);
            WriteAttribute(writer, NameIndex(lineNumbers.NameIndex, lineNumbers.Name, context), rewritten);
        }

        if (hasLocalVariables)
        {
            var (table, typeTable) = RewriteLocalTables(
                localVariables,
                relocation,
                sourcePool,
                context,
                className,
                methodName,
                methodDescriptor,
                isStatic,
                fixIdentifiers,
                ref changed);
            var local = nested.First(item => item.Name == "LocalVariableTable");
            WriteAttribute(writer, NameIndex(local.NameIndex, local.Name, context), table);
            if (hasTypeTable)
            {
                var type = nested.First(item => item.Name == "LocalVariableTypeTable");
                WriteAttribute(writer, NameIndex(type.NameIndex, type.Name, context), typeTable);
            }
        }

        foreach (var extra in extras)
        {
            WriteAttribute(writer, NameIndex(extra.NameIndex, extra.Name, context), extra.Payload);
        }

        writer.Flush();
        return output.ToArray();
    }

    private static int NameIndex(int original, string name, RemapContext context)
        => context.Pool.CopiesSource ? original : context.Pool.Utf8(name);

    /// <summary>
    /// Allocates the nested attribute name Utf8 entries in the order
    /// <c>MethodWriter.computeMethodInfoSize</c> adds them.
    /// </summary>
    public static void AllocateAttributeNames(
        byte[] info,
        ConstantPool source,
        RemapContext context)
    {
        var reader = new BigEndianReader(info);
        _ = reader.U2();
        _ = reader.U2();
        var codeLength = checked((int)reader.U4());
        _ = reader.Bytes(codeLength);
        var handlerCount = reader.U2();
        _ = reader.Bytes(handlerCount * 8);
        var nestedCount = reader.U2();
        var nested = new List<(int NameIndex, string Name, byte[] Payload)>(nestedCount);
        for (var index = 0; index < nestedCount; index++)
        {
            var nameIndex = reader.U2();
            var length = reader.U4();
            nested.Add((nameIndex, source.GetUtf8(nameIndex), reader.Bytes(checked((int)length))));
        }

        var stackMap = nested.FirstOrDefault(item => item.Name == "StackMapTable");
        if (stackMap.Payload is { Length: > 0 } &&
            BinaryPrimitives.ReadUInt16BigEndian(stackMap.Payload) > 0)
        {
            _ = context.Pool.Utf8(stackMap.Name);
        }

        var lineNumbers = nested.FirstOrDefault(item => item.Name == "LineNumberTable");
        if (lineNumbers.Payload is { Length: > 0 } &&
            BinaryPrimitives.ReadUInt16BigEndian(lineNumbers.Payload) > 0)
        {
            _ = context.Pool.Utf8(lineNumbers.Name);
        }

        var localVariables = LocalVariableTables.Read(
            nested.FirstOrDefault(item => item.Name == "LocalVariableTable").Payload,
            nested.FirstOrDefault(item => item.Name == "LocalVariableTypeTable").Payload);
        if (localVariables.Count > 0)
        {
            _ = context.Pool.Utf8("LocalVariableTable");
            if (localVariables.Exists(entry => entry.SignatureIndex >= 0))
            {
                _ = context.Pool.Utf8("LocalVariableTypeTable");
            }
        }

        foreach (var extra in nested)
        {
            if (extra.Name is (
                "StackMapTable" or "LineNumberTable" or
                "LocalVariableTable" or "LocalVariableTypeTable"))
            {
                continue;
            }

            _ = context.Pool.Utf8(extra.Name);
        }
    }

    private static void WriteAttribute(BigEndianWriter writer, int nameIndex, byte[] payload)
    {
        writer.U2(nameIndex);
        writer.U4((uint)payload.Length);
        writer.Bytes(payload);
    }

    private static void PatchExceptionTable(
        byte[] handlers,
        CodeRelocator.Result relocation,
        RemapContext context)
    {
        for (var offset = 0; offset + 8 <= handlers.Length; offset += 8)
        {
            var start = BinaryPrimitives.ReadUInt16BigEndian(handlers.AsSpan(offset));
            var end = BinaryPrimitives.ReadUInt16BigEndian(handlers.AsSpan(offset + 2));
            var handler = BinaryPrimitives.ReadUInt16BigEndian(handlers.AsSpan(offset + 4));
            BinaryPrimitives.WriteUInt16BigEndian(
                handlers.AsSpan(offset),
                (ushort)relocation.MapOffset(start));
            BinaryPrimitives.WriteUInt16BigEndian(
                handlers.AsSpan(offset + 2),
                (ushort)relocation.MapOffset(end));
            BinaryPrimitives.WriteUInt16BigEndian(
                handlers.AsSpan(offset + 4),
                (ushort)relocation.MapOffset(handler));

            var catchType = BinaryPrimitives.ReadUInt16BigEndian(handlers.AsSpan(offset + 6));
            if (catchType != 0)
            {
                var mapped = context.MapClassIndex(catchType);
                BinaryPrimitives.WriteUInt16BigEndian(handlers.AsSpan(offset + 6), (ushort)mapped);
            }
        }
    }

    private static byte[] RewriteLineNumbers(byte[] payload, CodeRelocator.Result relocation)
    {
        var count = BinaryPrimitives.ReadUInt16BigEndian(payload);
        var entries = new List<(int Start, int Line)>(count);
        for (var index = 0; index < count; index++)
        {
            var offset = 2 + index * 4;
            entries.Add((
                BinaryPrimitives.ReadUInt16BigEndian(payload.AsSpan(offset)),
                BinaryPrimitives.ReadUInt16BigEndian(payload.AsSpan(offset + 2))));
        }

        // ASM visits line numbers in bytecode order.
        entries.Sort((left, right) => left.Start.CompareTo(right.Start));
        using var stream = new MemoryStream(2 + entries.Count * 4);
        using var writer = new BigEndianWriter(stream);
        writer.U2(entries.Count);
        foreach (var (start, line) in entries)
        {
            writer.U2(relocation.MapOffset(start));
            writer.U2(line);
        }

        writer.Flush();
        return stream.ToArray();
    }

    private static (byte[] Table, byte[] TypeTable) RewriteLocalTables(
        List<LocalVariableTables.Entry> entries,
        CodeRelocator.Result relocation,
        ConstantPool source,
        RemapContext context,
        string className,
        string methodName,
        string methodDescriptor,
        bool isStatic,
        bool fixIdentifiers,
        ref bool changed)
    {
        using var tableStream = new MemoryStream(2 + entries.Count * 10);
        using var tableWriter = new BigEndianWriter(tableStream);
        tableWriter.U2(entries.Count);

        using var typeStream = new MemoryStream();
        using var typeWriter = new BigEndianWriter(typeStream);
        var typeCount = 0;
        Dictionary<int, int>? renamedSlots = fixIdentifiers ? [] : null;

        foreach (var entry in entries)
        {
            var (start, length) = relocation.MapRange(
                entry.StartPc,
                entry.StartPc + entry.Length);
            var name = source.GetUtf8(entry.NameIndex);
            if (context.TryMapParameter(
                    className,
                    methodName,
                    methodDescriptor,
                    isStatic,
                    entry.Slot,
                    out var mappedName))
            {
                name = mappedName;
            }

            if (renamedSlots is not null)
            {
                var original = name;
                name = FixIdentifierName(name, entry.Slot, renamedSlots);
                if (!string.Equals(name, original, StringComparison.Ordinal))
                {
                    changed = true;
                }
            }

            var nameIndex = context.Pool.Utf8(name);
            var descriptor = context.Pool.Utf8(context.Descriptors.MapDescriptor(
                source.GetUtf8(entry.DescriptorIndex)));
            tableWriter.U2(start);
            tableWriter.U2(length);
            tableWriter.U2(nameIndex);
            tableWriter.U2(descriptor);
            tableWriter.U2(entry.Slot);

            if (entry.SignatureIndex >= 0)
            {
                var signature = context.Pool.Utf8(context.Descriptors.MapSignature(
                    source.GetUtf8(entry.SignatureIndex),
                    typeSignature: true));
                typeWriter.U2(start);
                typeWriter.U2(length);
                typeWriter.U2(nameIndex);
                typeWriter.U2(signature);
                typeWriter.U2(entry.Slot);
                typeCount++;
            }
        }

        tableWriter.Flush();
        if (typeCount > 0)
        {
            // The count is the first field, so rewrite the buffer with it.
            var body = typeStream.ToArray();
            var result = new byte[body.Length + 2];
            BinaryPrimitives.WriteUInt16BigEndian(result, (ushort)typeCount);
            body.CopyTo(result.AsSpan(2));
            return (tableStream.ToArray(), result);
        }

        return (tableStream.ToArray(), []);
    }

    /// <summary>
    /// Mirrors <c>Character.isJavaIdentifierStart</c> /
    /// <c>Character.isJavaIdentifierPart</c>: letters, letter numbers, '_' and
    /// '$' can start a name; digits, combining marks and connector punctuation
    /// can continue it.
    /// </summary>
    private static bool IsJavaIdentifier(string name)
    {
        if (name.Length == 0 || !IsJavaIdentifierStart(name[0]))
        {
            return false;
        }

        for (var index = 1; index < name.Length; index++)
        {
            if (!IsJavaIdentifierPart(name[index]))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// ART's <c>IdentifierFixer</c>: invalid local names become
    /// <c>lvt_&lt;slot&gt;_&lt;n&gt;_</c>, numbered per method and slot.
    /// </summary>
    internal static string FixIdentifierName(
        string name,
        int slot,
        Dictionary<int, int>? renamedSlots)
    {
        if (renamedSlots is null || IsJavaIdentifier(name))
        {
            return name;
        }

        var version = renamedSlots.GetValueOrDefault(slot) + 1;
        renamedSlots[slot] = version;
        return $"lvt_{slot}_{version}_";
    }

    private static bool IsJavaIdentifierStart(char value)
        => value is '$' or '_' ||
            char.IsLetter(value) ||
            char.GetUnicodeCategory(value) == UnicodeCategory.LetterNumber;

    private static bool IsJavaIdentifierPart(char value)
    {
        if (IsJavaIdentifierStart(value))
        {
            return true;
        }

        return char.GetUnicodeCategory(value) is
            UnicodeCategory.DecimalDigitNumber or
            UnicodeCategory.NonSpacingMark or
            UnicodeCategory.SpacingCombiningMark or
            UnicodeCategory.ConnectorPunctuation or
            UnicodeCategory.Format;
    }

    /// <summary>
    /// Re-encodes the stack map frames. ASM keeps the original frame kind
    /// (same / same1 / chop / append / full) but recomputes the offset delta from
    /// the new code layout, and it narrows extended same frames back to the
    /// one-byte form when the delta fits.
    /// </summary>
    private static byte[] RewriteStackMapTable(
        byte[] payload,
        CodeRelocator.Result relocation,
        ConstantPool source,
        RemapContext context)
    {
        var reader = new BigEndianReader(payload);
        var count = reader.U2();
        var frames = new List<StackMapFrame>(count);
        var oldOffset = -1;
        for (var index = 0; index < count; index++)
        {
            var frameType = (int)reader.U1();
            var frame = new StackMapFrame();
            if (frameType <= 63)
            {
                oldOffset += frameType + 1;
                frame.Kind = FrameKind.Same;
            }
            else if (frameType <= 127)
            {
                oldOffset += frameType - 63;
                frame.Kind = FrameKind.Same1;
                frame.Stack.Add(ReadVerificationType(reader));
            }
            else
            {
                switch (frameType)
                {
                    case 247:
                        oldOffset += reader.U2() + 1;
                        frame.Kind = FrameKind.Same1;
                        frame.Stack.Add(ReadVerificationType(reader));
                        break;
                    case >= 248 and <= 250:
                        oldOffset += reader.U2() + 1;
                        frame.Kind = FrameKind.Chop;
                        frame.Count = 251 - frameType;
                        break;
                    case 251:
                        oldOffset += reader.U2() + 1;
                        frame.Kind = FrameKind.Same;
                        break;
                    case >= 252 and <= 254:
                        oldOffset += reader.U2() + 1;
                        frame.Kind = FrameKind.Append;
                        frame.Count = frameType - 251;
                        for (var local = 0; local < frame.Count; local++)
                        {
                            frame.Locals.Add(ReadVerificationType(reader));
                        }

                        break;
                    case 255:
                        oldOffset += reader.U2() + 1;
                        frame.Kind = FrameKind.Full;
                        var locals = reader.U2();
                        for (var local = 0; local < locals; local++)
                        {
                            frame.Locals.Add(ReadVerificationType(reader));
                        }

                        var stacks = reader.U2();
                        for (var stack = 0; stack < stacks; stack++)
                        {
                            frame.Stack.Add(ReadVerificationType(reader));
                        }

                        break;
                    default:
                        throw new ClassFileException($"unknown stack map frame type {frameType}");
                }
            }

            frame.NewOffset = relocation.MapOffset(oldOffset);
            frames.Add(frame);
        }

        if (!reader.AtEnd)
        {
            throw new ClassFileException(
                $"StackMapTable has {reader.Remaining} trailing bytes");
        }

        using var output = new MemoryStream();
        using var writer = new BigEndianWriter(output);
        writer.U2((ushort)frames.Count);
        var previous = -1;
        foreach (var frame in frames)
        {
            var delta = frame.NewOffset - previous - 1;
            previous = frame.NewOffset;
            if (delta < 0)
            {
                if (frame.Kind == FrameKind.Same)
                {
                    // ASM silently drops duplicate same frames.
                    continue;
                }

                throw new ClassFileException("stack map frame offsets went backwards");
            }

            switch (frame.Kind)
            {
                case FrameKind.Same:
                    if (delta < 64)
                    {
                        writer.U1((byte)delta);
                    }
                    else
                    {
                        writer.U1(251);
                        writer.U2(delta);
                    }

                    break;
                case FrameKind.Same1:
                    if (delta < 64)
                    {
                        writer.U1((byte)(64 + delta));
                    }
                    else
                    {
                        writer.U1(247);
                        writer.U2(delta);
                    }

                    WriteVerificationType(writer, frame.Stack[0], relocation, context);
                    break;
                case FrameKind.Chop:
                    writer.U1((byte)(251 - frame.Count));
                    writer.U2(delta);
                    break;
                case FrameKind.Append:
                    writer.U1((byte)(251 + frame.Count));
                    writer.U2(delta);
                    foreach (var local in frame.Locals)
                    {
                        WriteVerificationType(writer, local, relocation, context);
                    }

                    break;
                case FrameKind.Full:
                    writer.U1(255);
                    writer.U2(delta);
                    writer.U2(frame.Locals.Count);
                    foreach (var local in frame.Locals)
                    {
                        WriteVerificationType(writer, local, relocation, context);
                    }

                    writer.U2(frame.Stack.Count);
                    foreach (var item in frame.Stack)
                    {
                        WriteVerificationType(writer, item, relocation, context);
                    }

                    break;
                default:
                    throw new ClassFileException($"unknown stack map frame kind {frame.Kind}");
            }
        }

        writer.Flush();
        return output.ToArray();
    }

    private static VerificationType ReadVerificationType(BigEndianReader reader)
    {
        var tag = reader.U1();
        return tag switch
        {
            7 => VerificationType.Class(reader.U2()),
            8 => VerificationType.Uninitialized(reader.U2()),
            _ => VerificationType.Plain(tag),
        };
    }

    private static void WriteVerificationType(
        BigEndianWriter writer,
        VerificationType type,
        CodeRelocator.Result relocation,
        RemapContext context)
    {
        writer.U1(type.Tag);
        switch (type.Tag)
        {
            case 7:
                writer.U2(context.MapClassIndex(type.Index));
                break;
            case 8:
                writer.U2(relocation.MapOffset(type.Index));
                break;
        }
    }

    private sealed class StackMapFrame
    {
        public FrameKind Kind;
        public int Count;
        public int NewOffset;
        public List<VerificationType> Locals { get; } = [];
        public List<VerificationType> Stack { get; } = [];
    }

    private readonly record struct VerificationType(byte Tag, int Index)
    {
        public static VerificationType Class(int index) => new(7, index);

        public static VerificationType Uninitialized(int index) => new(8, index);

        public static VerificationType Plain(byte tag) => new(tag, 0);
    }

    private enum FrameKind
    {
        Same,
        Same1,
        Chop,
        Append,
        Full,
    }

    private sealed class BigEndianReader(byte[] data)
    {
        private readonly byte[] _data = data;
        private int _offset;

        public bool AtEnd => _offset == _data.Length;

        public int Remaining => _data.Length - _offset;

        public byte U1()
        {
            Ensure(1);
            return _data[_offset++];
        }

        public int U2()
        {
            Ensure(2);
            var value = BinaryPrimitives.ReadUInt16BigEndian(_data.AsSpan(_offset));
            _offset += 2;
            return value;
        }

        public uint U4()
        {
            Ensure(4);
            var value = BinaryPrimitives.ReadUInt32BigEndian(_data.AsSpan(_offset));
            _offset += 4;
            return value;
        }

        public byte[] Bytes(int length)
        {
            Ensure(length);
            var value = _data.AsSpan(_offset, length).ToArray();
            _offset += length;
            return value;
        }

        private void Ensure(int length)
        {
            if (length < 0 || _offset + length > _data.Length)
            {
                throw new ClassFileException("code attribute is truncated");
            }
        }
    }

    private sealed class BigEndianWriter(Stream stream) : IDisposable
    {
        private readonly Stream _stream = stream;

        public void U1(byte value) => _stream.WriteByte(value);

        public void U2(int value)
        {
            _stream.WriteByte((byte)(value >> 8));
            _stream.WriteByte((byte)value);
        }

        public void U4(uint value)
        {
            _stream.WriteByte((byte)(value >> 24));
            _stream.WriteByte((byte)(value >> 16));
            _stream.WriteByte((byte)(value >> 8));
            _stream.WriteByte((byte)value);
        }

        public void Bytes(byte[] value) => _stream.Write(value, 0, value.Length);

        public void Flush() => _stream.Flush();

        public void Dispose() => _stream.Dispose();
    }
}
