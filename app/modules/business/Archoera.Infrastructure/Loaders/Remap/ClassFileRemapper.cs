using System.Buffers.Binary;
using System.Text;

namespace Archoera.Infrastructure.Loaders.Remap;

/// <summary>
/// Rename pass for ART 0.1.22: copies the input constant pool and appends the
/// mapped constants in ASM visit order. Attribute payloads are rewritten in
/// place and emitted in ASM's fixed attribute order.
/// </summary>
internal sealed partial class ClassFileRemapper
{
    private readonly ClassFileModel _model;
    private readonly RemapContext _context;
    private readonly ConstantPool _source;
    private readonly ArtFixers _fixers;
    private readonly bool _forceRecordFlag;
    private readonly bool _isRecord;
    private readonly bool _alwaysShiftParameterAnnotations;
    private readonly int _parameterShift;
    private readonly string? _parameterShiftOuter;

    public ClassFileRemapper(
        ClassFileModel model,
        TsrgMapping mapping,
        ArtProfile profile,
        ClassHierarchy? hierarchy = null,
        ArtFixers? fixers = null)
        : this(
            model,
            mapping,
            hierarchy,
            profile.PoolMode,
            fixers ?? profile.DefaultFixers,
            profile.ForceRecordFlag,
            profile.AlwaysShiftParameterAnnotations)
    {
    }

    public ClassFileRemapper(
        ClassFileModel model,
        TsrgMapping mapping,
        ClassHierarchy? hierarchy = null,
        ConstantPoolMode poolMode = ConstantPoolMode.CopyFromInput,
        ArtFixers fixers = ArtFixers.None,
        bool forceRecordFlag = false,
        bool alwaysShiftParameterAnnotations = false)
    {
        _model = model ?? throw new ArgumentNullException(nameof(model));
        _source = model.Pool;
        _fixers = fixers;
        _forceRecordFlag = forceRecordFlag;
        _alwaysShiftParameterAnnotations = alwaysShiftParameterAnnotations;
        _isRecord = model.SuperClass != 0 &&
            model.Pool.GetClassName(model.SuperClass) == "java/lang/Record";
        if ((model.Access & 0x4000) != 0)
        {
            // Enum constructors take the synthetic name and ordinal first.
            _parameterShift = 2;
        }
        else if (TryGetInnerOuter(model, out var outer))
        {
            // Non-static named/local inner classes take the outer instance first.
            _parameterShift = 1;
            _parameterShiftOuter = outer;
        }
        _context = new RemapContext(
            model.Pool,
            mapping,
            new ConstantPoolBuilder(model.Pool, poolMode == ConstantPoolMode.CopyFromInput))
        {
            Hierarchy = hierarchy ?? ClassHierarchy.Empty,
        };
    }

    /// <summary>
    /// True when a fixer changed this class. The pipeline discards the rewritten
    /// bytes in that case so untouched classes stay byte-identical.
    /// </summary>
    public bool Changed { get; private set; }

    public byte[] Rewrite()
    {
        Allocate();
        AllocateAttributeNames();
        return Write();
    }

    private bool Fixes(ArtFixers fixer) => (_fixers & fixer) != 0;

    /// <summary>ART's <c>SourceFixer.JAVA</c>: outermost simple class name + ".java".</summary>
    private string DerivedSourceName()
    {
        var name = _model.ClassName;
        var slash = name.LastIndexOf('/');
        if (slash >= 0)
        {
            name = name[(slash + 1)..];
        }

        var dollar = name.IndexOf('$');
        if (dollar >= 0)
        {
            name = name[..dollar];
        }

        return name + ".java";
    }

    /// <summary>
    /// ART's <c>getExpectedSyntheticParams</c>: enums have two synthetic
    /// constructor parameters, non-static named/local inner classes have the
    /// outer instance. Anonymous and static nested classes are skipped.
    /// </summary>
    private static bool TryGetInnerOuter(ClassFileModel model, out string outer)
    {
        outer = string.Empty;
        var attribute = model.Attributes.FirstOrDefault(
            item => item.Name(model.Pool) == "InnerClasses");
        if (attribute is null)
        {
            return false;
        }

        var info = attribute.Info;
        var count = BinaryPrimitives.ReadUInt16BigEndian(info);
        for (var index = 0; index < count; index++)
        {
            var offset = 2 + index * 8;
            var inner = BinaryPrimitives.ReadUInt16BigEndian(info.AsSpan(offset));
            var outerIndex = BinaryPrimitives.ReadUInt16BigEndian(info.AsSpan(offset + 2));
            var innerName = BinaryPrimitives.ReadUInt16BigEndian(info.AsSpan(offset + 4));
            var access = BinaryPrimitives.ReadUInt16BigEndian(info.AsSpan(offset + 6));
            if (model.Pool.GetClassName(inner) != model.ClassName)
            {
                continue;
            }

            if ((access & 0x208) != 0 || innerName == 0)
            {
                return false;
            }

            if (outerIndex != 0)
            {
                outer = model.Pool.GetClassName(outerIndex);
                return true;
            }

            var dollar = model.ClassName.LastIndexOf('$');
            if (dollar < 0)
            {
                return false;
            }

            outer = model.ClassName[..dollar];
            return true;
        }

        return false;
    }

    private bool DescriptorStartsWithSyntheticParams(string descriptor)
    {
        if (_parameterShift == 2)
        {
            return descriptor.StartsWith("(Ljava/lang/String;I", StringComparison.Ordinal);
        }

        return _parameterShift == 1 &&
            _parameterShiftOuter is not null &&
            descriptor.StartsWith("(L" + _parameterShiftOuter + ";", StringComparison.Ordinal);
    }

    private void Allocate()
    {
        var pool = _context.Pool;
        _context.SetSourceBootstrapMethods(
            BootstrapMethodsTable.ReadSourceEntries(
                Find(_model.Attributes, "BootstrapMethods")?.Info));
        pool.Class(_context.MapClass(_model.ClassName));

        // ClassRemapper.visit maps name, then the class signature, then super and
        // interfaces, so constants are appended in that order.
        var classSignature = Find(_model.Attributes, "Signature");
        if (classSignature is not null)
        {
            pool.Utf8(_context.Descriptors.MapSignature(
                _source.GetUtf8(BinaryPrimitives.ReadUInt16BigEndian(classSignature.Info)),
                typeSignature: false));
        }

        if (_model.SuperClass != 0)
        {
            pool.Class(_context.MapClass(_source.GetClassName(_model.SuperClass)));
        }

        foreach (var interfaceIndex in _model.Interfaces)
        {
            pool.Class(_context.MapClass(_source.GetClassName(interfaceIndex)));
        }

        var sourceFile = Find(_model.Attributes, "SourceFile");
        if (sourceFile is not null)
        {
            var original = _source.GetUtf8(
                BinaryPrimitives.ReadUInt16BigEndian(sourceFile.Info));
            var name = Fixes(ArtFixers.SourceFile) ? DerivedSourceName() : original;
            if (!string.Equals(original, name, StringComparison.Ordinal))
            {
                Changed = true;
            }

            pool.Utf8(name);
        }

        AllocateNestHost();
        AllocateEnclosingMethod();
        AllocateClassAnnotations();
        AllocateNestMembers();
        AllocatePermittedSubclasses();
        AllocateInnerClasses();
        AllocateRecordComponents();
        AllocateFields();
        AllocateMethods();
    }

    private void AllocateClassAnnotations()
    {
        AllocateAnnotationAttribute(
            _model.Attributes,
            "RuntimeVisibleAnnotations",
            AnnotationPayloadKind.Annotations);
        AllocateAnnotationAttribute(
            _model.Attributes,
            "RuntimeInvisibleAnnotations",
            AnnotationPayloadKind.Annotations);
        AllocateAnnotationAttribute(
            _model.Attributes,
            "RuntimeVisibleTypeAnnotations",
            AnnotationPayloadKind.TypeAnnotations);
        AllocateAnnotationAttribute(
            _model.Attributes,
            "RuntimeInvisibleTypeAnnotations",
            AnnotationPayloadKind.TypeAnnotations);
    }

    private void AllocateNestHost()
    {
        var attribute = Find(_model.Attributes, "NestHost");
        if (attribute is not null)
        {
            _context.Pool.Class(_context.MapClass(
                _source.GetClassName(BinaryPrimitives.ReadUInt16BigEndian(attribute.Info))));
        }
    }

    private void AllocateEnclosingMethod()
    {
        var attribute = Find(_model.Attributes, "EnclosingMethod");
        if (attribute is null)
        {
            return;
        }

        var classIndex = BinaryPrimitives.ReadUInt16BigEndian(attribute.Info);
        var nameAndType = BinaryPrimitives.ReadUInt16BigEndian(attribute.Info.AsSpan(2));
        var owner = _source.GetClassName(classIndex);
        _context.Pool.Class(_context.MapClass(owner));
        if (nameAndType == 0)
        {
            return;
        }

        var name = _source.GetNameAndType(nameAndType, out var descriptor);
        _context.Pool.NameAndType(
            _context.MapMethodName(owner, name, descriptor),
            _context.Descriptors.MapDescriptor(descriptor));
    }

    private void AllocateNestMembers()
        => AllocateClassListAttribute("NestMembers");

    private void AllocatePermittedSubclasses()
        => AllocateClassListAttribute("PermittedSubclasses");

    private void AllocateClassListAttribute(string name)
    {
        var attribute = Find(_model.Attributes, name);
        if (attribute is not null)
        {
            AllocateClassIndexList(attribute.Info);
        }
    }

    private void AllocateClassIndexList(byte[] info)
    {
        var count = BinaryPrimitives.ReadUInt16BigEndian(info);
        var offset = 2;
        for (var index = 0; index < count; index++)
        {
            _context.Pool.Class(_context.MapClass(
                _source.GetClassName(
                    BinaryPrimitives.ReadUInt16BigEndian(info.AsSpan(offset)))));
            offset += 2;
        }
    }

    private void AllocateInnerClasses()
    {
        var attribute = Find(_model.Attributes, "InnerClasses");
        if (attribute is null)
        {
            return;
        }

        var reader = new SpanReader(attribute.Info);
        var count = reader.U2();
        for (var index = 0; index < count; index++)
        {
            var inner = reader.U2();
            var outer = reader.U2();
            var innerName = reader.U2();
            _ = reader.U2();
            if (inner != 0)
            {
                var mapped = _context.MapClass(_source.GetClassName(inner));
                _context.Pool.Class(mapped);
            }

            if (outer != 0)
            {
                _context.Pool.Class(_context.MapClass(_source.GetClassName(outer)));
            }

            if (inner != 0 && innerName != 0)
            {
                _context.Pool.Utf8(_context.MapInnerClassName(
                    _source.GetClassName(inner),
                    _source.GetUtf8(innerName)));
            }
        }
    }

    private void AllocateRecordComponents()
    {
        var attribute = Find(_model.Attributes, "Record");
        if (attribute is null)
        {
            return;
        }

        var reader = new SpanReader(attribute.Info);
        var count = reader.U2();
        for (var index = 0; index < count; index++)
        {
            var name = _source.GetUtf8(reader.U2());
            var descriptor = _source.GetUtf8(reader.U2());
            _context.Pool.Utf8(_context.MapFieldName(_model.ClassName, name, descriptor));
            _context.Pool.Utf8(_context.Descriptors.MapDescriptor(descriptor));

            var attributeCount = reader.U2();
            var attributes = new List<ClassAttributeModel>(attributeCount);
            for (var nested = 0; nested < attributeCount; nested++)
            {
                var nameIndex = reader.U2();
                var length = checked((int)reader.U4());
                attributes.Add(new ClassAttributeModel(nameIndex, reader.Bytes(length)));
            }

            AllocateMemberAttributes(attributes, AttributeOwner.RecordComponent);
        }
    }

    private void AllocateFields()
    {
        foreach (var field in _model.Fields)
        {
            var name = field.Name(_source);
            var descriptor = field.Descriptor(_source);
            _context.Pool.Utf8(_context.MapFieldName(_model.ClassName, name, descriptor));
            _context.Pool.Utf8(_context.Descriptors.MapDescriptor(descriptor));
            AllocateMemberAttributes(field.Attributes, AttributeOwner.Field);
        }
    }

    private void AllocateMethods()
    {
        foreach (var method in _model.Methods)
        {
            var name = method.Name(_source);
            var descriptor = method.Descriptor(_source);
            _context.Pool.Utf8(_context.MapMethodName(_model.ClassName, name, descriptor));
            _context.Pool.Utf8(_context.Descriptors.MapDescriptor(descriptor));

            var signature = Find(method.Attributes, "Signature");
            if (signature is not null)
            {
                _context.Pool.Utf8(_context.Descriptors.MapSignature(
                    _source.GetUtf8(BinaryPrimitives.ReadUInt16BigEndian(signature.Info)),
                    typeSignature: false));
            }

            var exceptions = Find(method.Attributes, "Exceptions");
            if (exceptions is not null)
            {
                AllocateClassIndexList(exceptions.Info);
            }

            AllocateMemberAttributes(method.Attributes, AttributeOwner.Method);

            var code = Find(method.Attributes, "Code");
            if (code is not null)
            {
                AllocateCode(
                    code.Info,
                    name,
                    descriptor,
                    (method.Access & 0x0008) != 0);
            }
        }
    }

    /// <summary>
    /// Allocates the member attributes in the order <c>ClassReader</c> visits
    /// them, which is fixed by JVMS section 4.7 rather than by the input order.
    /// </summary>
    private void AllocateMemberAttributes(List<ClassAttributeModel> attributes, AttributeOwner owner)
    {
        var signature = Find(attributes, "Signature");
        if (signature is not null)
        {
            _context.Pool.Utf8(_context.Descriptors.MapSignature(
                _source.GetUtf8(BinaryPrimitives.ReadUInt16BigEndian(signature.Info)),
                typeSignature: owner != AttributeOwner.Method));
        }

        if (owner == AttributeOwner.Field)
        {
            var constantValue = Find(attributes, "ConstantValue");
            if (constantValue is not null)
            {
                AllocateConstantValue(constantValue.Info);
            }
        }
        else if (owner == AttributeOwner.Method)
        {
            var parameters = Find(attributes, "MethodParameters");
            if (parameters is not null)
            {
                AllocateMethodParameters(parameters.Info);
            }
        }

        AllocateAnnotationAttribute(attributes, "AnnotationDefault", AnnotationPayloadKind.DefaultValue);
        AllocateAnnotationAttribute(attributes, "RuntimeVisibleAnnotations", AnnotationPayloadKind.Annotations);
        AllocateAnnotationAttribute(attributes, "RuntimeInvisibleAnnotations", AnnotationPayloadKind.Annotations);
        AllocateAnnotationAttribute(attributes, "RuntimeVisibleTypeAnnotations", AnnotationPayloadKind.TypeAnnotations);
        AllocateAnnotationAttribute(attributes, "RuntimeInvisibleTypeAnnotations", AnnotationPayloadKind.TypeAnnotations);
        if (owner == AttributeOwner.Method)
        {
            AllocateAnnotationAttribute(
                attributes,
                "RuntimeVisibleParameterAnnotations",
                AnnotationPayloadKind.ParameterAnnotations);
            AllocateAnnotationAttribute(
                attributes,
                "RuntimeInvisibleParameterAnnotations",
                AnnotationPayloadKind.ParameterAnnotations);
        }
    }

    private void AllocateAnnotationAttribute(
        List<ClassAttributeModel> attributes,
        string name,
        AnnotationPayloadKind kind)
    {
        var attribute = Find(attributes, name);
        if (attribute is not null)
        {
            AnnotationRewriter.Allocate(attribute.Info, _source, _context, kind);
        }
    }

    private void AllocateCode(
        byte[] info,
        string methodName,
        string methodDescriptor,
        bool isStatic)
    {
        var reader = new SpanReader(info);
        _ = reader.U2();
        _ = reader.U2();
        var codeLength = checked((int)reader.U4());
        var code = reader.Bytes(codeLength);

        var handlerCount = reader.U2();
        var handlerTypes = new int[handlerCount];
        for (var index = 0; index < handlerCount; index++)
        {
            _ = reader.U2();
            _ = reader.U2();
            _ = reader.U2();
            handlerTypes[index] = reader.U2();
        }

        var nested = reader.U2();
        var frames = new List<(int Offset, List<int> Classes)>();
        byte[]? localTable = null;
        byte[]? typeTable = null;
        for (var index = 0; index < nested; index++)
        {
            var nameIndex = reader.U2();
            var length = checked((int)reader.U4());
            var payload = reader.Bytes(length);
            var name = _source.GetUtf8(nameIndex);
            if (name == "StackMapTable")
            {
                ReadStackMapClasses(payload, frames);
            }
            else if (name == "LocalVariableTable")
            {
                localTable = payload;
            }
            else if (name == "LocalVariableTypeTable")
            {
                typeTable = payload;
            }
        }

        if (!reader.AtEnd)
        {
            throw new ClassFileException(
                $"{_model.ClassName}: Code attribute has {reader.Remaining} trailing bytes");
        }

        // ASM visits try/catch blocks before the instructions, so handler types
        // are added to the pool before any instruction constant.
        foreach (var catchType in handlerTypes)
        {
            if (catchType != 0)
            {
                _ = _context.MapClassIndex(catchType);
            }
        }

        // ASM visits stack map frames just before the instruction at their offset,
        // so frame constants enter the pool interleaved with instruction constants.
        var frameIndex = 0;
        var offset = 0;
        while (offset < code.Length)
        {
            while (frameIndex < frames.Count && frames[frameIndex].Offset == offset)
            {
                foreach (var classIndex in frames[frameIndex].Classes)
                {
                    _ = _context.MapClassIndex(classIndex);
                }

                frameIndex++;
            }

            var opcode = code[offset];
            switch (opcode)
            {
                case 0x12:
                    AllocateLoadable(code[offset + 1]);
                    break;
                case 0x13:
                case 0x14:
                    AllocateLoadable(BinaryPrimitives.ReadUInt16BigEndian(code.AsSpan(offset + 1)));
                    break;
                case >= 0xB2 and <= 0xB8:
                case 0xB9:
                    _ = _context.MapMemberReference(
                        BinaryPrimitives.ReadUInt16BigEndian(code.AsSpan(offset + 1)));
                    break;
                case 0xBA:
                    _ = _context.MapInvokeDynamic(
                        BinaryPrimitives.ReadUInt16BigEndian(code.AsSpan(offset + 1)));
                    break;
                case 0xBB:
                case 0xBD:
                case 0xC0:
                case 0xC1:
                case 0xC5:
                    _ = _context.MapClassIndex(
                        BinaryPrimitives.ReadUInt16BigEndian(code.AsSpan(offset + 1)));
                    break;
            }

            offset += CodeRelocator.InstructionLength(code, offset);
        }

        if (offset != code.Length)
        {
            throw new ClassFileException(
                $"{_model.ClassName}: bytecode ends at {offset}, expected {code.Length}");
        }

        if (frameIndex != frames.Count)
        {
            throw new ClassFileException(
                $"{_model.ClassName}: stack map frame at {frames[frameIndex].Offset} " +
                "is not on an instruction boundary");
        }

        AllocateLocalVariables(
            localTable,
            typeTable,
            methodName,
            methodDescriptor,
            isStatic);
    }

    private void AllocateLoadable(int index)
    {
        var entry = _source[index];
        _ = entry.Tag switch
        {
            ConstantPoolTag.Integer => _context.Pool.Integer(entry.First),
            ConstantPoolTag.Float => _context.Pool.Float(BitConverter.Int32BitsToSingle(entry.First)),
            ConstantPoolTag.Long => _context.Pool.Long(entry.Wide),
            ConstantPoolTag.Double => _context.Pool.Double(BitConverter.Int64BitsToDouble(entry.Wide)),
            ConstantPoolTag.String => _context.Pool.StringConstant(_source.GetUtf8(entry.First)),
            ConstantPoolTag.Class => _context.MapClassIndex(index),
            ConstantPoolTag.MethodType => _context.Pool.MethodType(
                _context.Descriptors.MapDescriptor(_source.GetUtf8(entry.First))),
            ConstantPoolTag.MethodHandle => _context.MapBootstrapValue(index),
            _ => throw new NotSupportedException($"ldc tag {entry.Tag} is not implemented yet"),
        };
    }

    private void AllocateLocalVariables(
        byte[]? localTable,
        byte[]? typeTable,
        string methodName,
        string methodDescriptor,
        bool isStatic)
    {
        Dictionary<int, int>? renamedSlots = Fixes(ArtFixers.Identifiers) ? [] : null;
        foreach (var entry in LocalVariableTables.Read(localTable, typeTable))
        {
            var name = _source.GetUtf8(entry.NameIndex);
            if (_context.TryMapParameter(
                    _model.ClassName,
                    methodName,
                    methodDescriptor,
                    isStatic,
                    entry.Slot,
                    out var mappedName))
            {
                name = mappedName;
            }

            name = CodeAttributeRewriter.FixIdentifierName(name, entry.Slot, renamedSlots);
            _context.Pool.Utf8(name);
            if (entry.SignatureIndex >= 0)
            {
                _context.Pool.Utf8(_context.Descriptors.MapSignature(
                    _source.GetUtf8(entry.SignatureIndex),
                    typeSignature: true));
            }

            _context.Pool.Utf8(_context.Descriptors.MapDescriptor(
                _source.GetUtf8(entry.DescriptorIndex)));
        }
    }

    /// <summary>
    /// Reads a StackMapTable and records, per frame, the class constants ASM
    /// would allocate when it visits that frame (locals before stack).
    /// </summary>
    private static void ReadStackMapClasses(
        byte[] payload,
        List<(int Offset, List<int> Classes)> frames)
    {
        var reader = new SpanReader(payload);
        var count = reader.U2();
        var offset = -1;
        for (var index = 0; index < count; index++)
        {
            var frame = reader.U1();
            var classes = new List<int>();
            if (frame <= 63)
            {
                offset += frame + 1;
            }
            else if (frame <= 127)
            {
                offset += frame - 63;
                ReadVerificationClass(ref reader, classes);
            }
            else
            {
                switch (frame)
                {
                    case 247:
                        offset += reader.U2() + 1;
                        ReadVerificationClass(ref reader, classes);
                        break;
                    case >= 248 and <= 250:
                        offset += reader.U2() + 1;
                        break;
                    case 251:
                        offset += reader.U2() + 1;
                        break;
                    case >= 252 and <= 254:
                        offset += reader.U2() + 1;
                        for (var local = 0; local < frame - 251; local++)
                        {
                            ReadVerificationClass(ref reader, classes);
                        }

                        break;
                    case 255:
                        offset += reader.U2() + 1;
                        var locals = reader.U2();
                        for (var local = 0; local < locals; local++)
                        {
                            ReadVerificationClass(ref reader, classes);
                        }

                        var stacks = reader.U2();
                        for (var stack = 0; stack < stacks; stack++)
                        {
                            ReadVerificationClass(ref reader, classes);
                        }

                        break;
                    default:
                        throw new ClassFileException($"unknown stack map frame type {frame}");
                }
            }

            frames.Add((offset, classes));
        }

        if (!reader.AtEnd)
        {
            throw new ClassFileException(
                $"StackMapTable has {reader.Remaining} trailing bytes");
        }
    }

    private static void ReadVerificationClass(ref SpanReader reader, List<int> classes)
    {
        var tag = reader.U1();
        if (tag == 7)
        {
            classes.Add(reader.U2());
        }
        else if (tag == 8)
        {
            _ = reader.U2();
        }
    }
}
