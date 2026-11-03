namespace Archoera.Infrastructure.Loaders.Remap;

/// <summary>
/// Maps one class file through a TSRG table and an append-only constant pool.
/// The context is intentionally immutable during a pass so allocation order is
/// the only mutable state.
/// </summary>
internal sealed class RemapContext
{
    private readonly TsrgMapping _mapping;
    private readonly ConstantPool _source;

    public RemapContext(ConstantPool source, TsrgMapping mapping, ConstantPoolBuilder pool)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _mapping = mapping ?? throw new ArgumentNullException(nameof(mapping));
        Pool = pool ?? throw new ArgumentNullException(nameof(pool));
        Descriptors = new DescriptorRemapper(MapClass);
    }

    public ConstantPoolBuilder Pool { get; }

    public DescriptorRemapper Descriptors { get; }

    public ClassHierarchy Hierarchy { get; set; } = ClassHierarchy.Empty;

    public BootstrapMethodsTable Bootstraps { get; } = new();

    private int[][] _sourceBootstraps = [];

    public void SetSourceBootstrapMethods(int[][] entries)
    {
        _sourceBootstraps = entries ?? [];
        if (Pool.CopiesSource)
        {
            // Copy mode keeps the input BootstrapMethods verbatim; rebuild mode
            // starts empty and only records the methods invokedynamic refers to.
            Bootstraps.Seed(_sourceBootstraps);
        }
    }

    public string MapClass(string name)
        => name.Length > 0 && name[0] == '['
            ? Descriptors.MapDescriptor(name)
            : _mapping.TryMapClass(name, out var mapped) ? mapped : name;

    public string MapFieldName(string owner, string name, string descriptor)
    {
        if (_mapping.TryMapField(owner, name, descriptor, out var mapped))
        {
            return mapped;
        }

        foreach (var ancestor in Hierarchy.FieldAncestors(owner))
        {
            if (_mapping.TryMapField(ancestor, name, descriptor, out mapped))
            {
                return mapped;
            }
        }

        return name;
    }

    public string MapMethodName(string owner, string name, string descriptor)
    {
        if (_mapping.TryMapMethod(owner, name, descriptor, out var mapped))
        {
            return mapped;
        }

        foreach (var ancestor in Hierarchy.MethodAncestors(owner))
        {
            if (_mapping.TryMapMethod(ancestor, name, descriptor, out mapped))
            {
                return mapped;
            }
        }

        return name;
    }

    public bool TryMapParameter(
        string owner,
        string name,
        string descriptor,
        bool isStatic,
        int localIndex,
        out string mapped)
        => _mapping.TryMapParameter(owner, name, descriptor, isStatic, localIndex, out mapped);

    /// <summary>
    /// ASM's ClassRemapper replaces the InnerClasses inner_name with the simple
    /// name of the remapped class when that name contains '$'.
    /// </summary>
    public string MapInnerClassName(string innerClassInternalName, string fallback)
    {
        var mapped = MapClass(innerClassInternalName);
        var separator = mapped.LastIndexOf('$');
        if (separator < 0)
        {
            return fallback;
        }

        var index = separator + 1;
        while (index < mapped.Length && char.IsDigit(mapped[index]))
        {
            index++;
        }

        return index < mapped.Length ? mapped[index..] : fallback;
    }

    public int MapClassIndex(int oldIndex)
        => Pool.Class(MapClass(_source.GetClassName(oldIndex)));

    public int MapMemberReference(int oldIndex)
    {
        var entry = _source[oldIndex];
        if (entry.Tag is not (ConstantPoolTag.Fieldref or ConstantPoolTag.Methodref or ConstantPoolTag.InterfaceMethodref))
        {
            throw new ClassFileException($"constant pool index {oldIndex} is not a member reference");
        }

        var owner = _source.GetClassName(entry.First);
        var name = _source.GetNameAndType(entry.Second, out var descriptor);
        var mappedOwner = MapClass(owner);
        var mappedDescriptor = Descriptors.MapDescriptor(descriptor);
        var mappedName = entry.Tag == ConstantPoolTag.Fieldref
            ? MapFieldName(owner, name, descriptor)
            : MapMethodName(owner, name, descriptor);

        return Pool.MemberRef(entry.Tag, mappedOwner, mappedName, mappedDescriptor);
    }

    public int MapNameAndType(int oldIndex)
    {
        var entry = _source[oldIndex];
        if (entry.Tag != ConstantPoolTag.NameAndType)
        {
            throw new ClassFileException($"constant pool index {oldIndex} is not a NameAndType");
        }

        var name = _source.GetUtf8(entry.First);
        var descriptor = _source.GetUtf8(entry.Second);
        // Callers that know the owner must use MapMemberReference; this fallback
        // only maps the descriptor (used by EnclosingMethod).
        return Pool.NameAndType(name, Descriptors.MapDescriptor(descriptor));
    }

    public int MapInvokeDynamic(int oldIndex)
    {
        var entry = _source[oldIndex];
        if (entry.Tag != ConstantPoolTag.InvokeDynamic)
        {
            throw new ClassFileException($"constant pool index {oldIndex} is not an InvokeDynamic");
        }

        var name = _source.GetNameAndType(entry.Second, out var descriptor);
        if (entry.First < 0 || entry.First >= _sourceBootstraps.Length)
        {
            throw new ClassFileException(
                $"invokedynamic bootstrap index {entry.First} is out of range");
        }

        var sourceBootstrap = _sourceBootstraps[entry.First];
        // ASM adds the bootstrap method arguments before the method handle, so
        // that a Constant_Dynamic argument can register its own bootstrap first.
        var arguments = new int[sourceBootstrap.Length - 1];
        for (var index = 1; index < sourceBootstrap.Length; index++)
        {
            arguments[index - 1] = MapBootstrapValue(sourceBootstrap[index]);
        }

        var handle = MapBootstrapValue(sourceBootstrap[0]);

        var bootstrapIndex = Bootstraps.Intern(handle, arguments);
        var mappedName = MapLambdaName(name, descriptor, sourceBootstrap);
        var mappedDescriptor = Descriptors.MapDescriptor(descriptor);
        return Pool.InvokeDynamic(mappedName, mappedDescriptor, bootstrapIndex);
    }

    public int MapBootstrapValue(int oldIndex)
    {
        var entry = _source[oldIndex];
        return entry.Tag switch
        {
            ConstantPoolTag.Integer => Pool.Integer(entry.First),
            ConstantPoolTag.Float => Pool.Float(BitConverter.Int32BitsToSingle(entry.First)),
            ConstantPoolTag.Long => Pool.Long(entry.Wide),
            ConstantPoolTag.Double => Pool.Double(BitConverter.Int64BitsToDouble(entry.Wide)),
            ConstantPoolTag.String => Pool.StringConstant(_source.GetUtf8(entry.First)),
            ConstantPoolTag.Class => Pool.Class(MapClass(_source.GetUtf8(entry.First))),
            ConstantPoolTag.MethodType => Pool.MethodType(
                Descriptors.MapDescriptor(_source.GetUtf8(entry.First))),
            ConstantPoolTag.MethodHandle => MapMethodHandle(entry),
            ConstantPoolTag.Dynamic => throw new NotSupportedException(
                "constant dynamic bootstrap values are not implemented yet"),
            _ => throw new ClassFileException(
                $"constant pool tag {entry.Tag} cannot be a bootstrap argument"),
        };
    }

    private int MapMethodHandle(ConstantPoolEntry entry)
    {
        var reference = _source[entry.Second];
        var owner = _source.GetClassName(reference.First);
        var name = _source.GetNameAndType(reference.Second, out var descriptor);
        var isInterface = reference.Tag == ConstantPoolTag.InterfaceMethodref;
        var mappedOwner = MapClass(owner);
        var mappedDescriptor = Descriptors.MapDescriptor(descriptor);
        var mappedName = reference.Tag == ConstantPoolTag.Fieldref
            ? MapFieldName(owner, name, descriptor)
            : MapMethodName(owner, name, descriptor);
        return Pool.MethodHandle(entry.First, mappedOwner, mappedName, mappedDescriptor, isInterface);
    }

    private string MapLambdaName(string name, string descriptor, int[] sourceBootstrap)
    {
        if (sourceBootstrap.Length < 2)
        {
            return name;
        }

        var handleEntry = _source[sourceBootstrap[0]];
        if (handleEntry.Tag != ConstantPoolTag.MethodHandle)
        {
            return name;
        }

        var reference = _source[handleEntry.Second];
        var owner = _source.GetClassName(reference.First);
        var handleName = _source.GetNameAndType(reference.Second, out _);
        if (owner != "java/lang/invoke/LambdaMetafactory" ||
            handleName is not ("metafactory" or "altMetafactory"))
        {
            return name;
        }

        var returnType = descriptor[(descriptor.IndexOf(')') + 1)..];
        if (returnType.Length < 3 || returnType[0] != 'L' || returnType[^1] != ';')
        {
            return name;
        }

        var lambdaOwner = returnType[1..^1];
        var methodType = _source[sourceBootstrap[1]];
        if (methodType.Tag != ConstantPoolTag.MethodType)
        {
            return name;
        }

        var samDescriptor = _source.GetUtf8(methodType.First);
        return MapMethodName(lambdaOwner, name, samDescriptor);
    }
}
