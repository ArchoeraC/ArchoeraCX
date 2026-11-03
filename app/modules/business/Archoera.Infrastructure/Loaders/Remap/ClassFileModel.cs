namespace Archoera.Infrastructure.Loaders.Remap;

/// <summary>Raised when a class file is truncated or structurally invalid.</summary>
internal sealed class ClassFileException : Exception
{
    public ClassFileException(string message)
        : base(message)
    {
    }

    public ClassFileException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>One raw attribute, kept as bytes until the writer decides to re-encode it.</summary>
internal sealed record ClassAttributeModel(int NameIndex, byte[] Info)
{
    public string Name(ConstantPool pool) => pool.GetUtf8(NameIndex);
}

internal sealed class ClassMemberModel
{
    public int Access { get; init; }

    public int NameIndex { get; init; }

    public int DescriptorIndex { get; init; }

    public List<ClassAttributeModel> Attributes { get; } = [];

    public string Name(ConstantPool pool) => pool.GetUtf8(NameIndex);

    public string Descriptor(ConstantPool pool) => pool.GetUtf8(DescriptorIndex);
}

/// <summary>
/// Parsed class file. Attribute payloads stay raw so unknown attributes survive
/// a round trip without being interpreted.
/// </summary>
internal sealed class ClassFileModel
{
    public required int MinorVersion { get; init; }

    public required int MajorVersion { get; init; }

    public required ConstantPool Pool { get; init; }

    public required int Access { get; init; }

    public required int ThisClass { get; init; }

    public required int SuperClass { get; init; }

    public required int[] Interfaces { get; init; }

    public required List<ClassMemberModel> Fields { get; init; }

    public required List<ClassMemberModel> Methods { get; init; }

    public required List<ClassAttributeModel> Attributes { get; init; }

    public string ClassName => Pool.GetClassName(ThisClass);
}
