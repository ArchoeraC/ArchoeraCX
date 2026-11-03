namespace Archoera.Infrastructure.Loaders.Remap;

/// <summary>How the writer seeds and grows the constant pool.</summary>
internal enum ConstantPoolMode
{
    /// <summary>ART 0.1.22: <c>new ClassWriter(reader, 0)</c> copies the input pool.</summary>
    CopyFromInput,

    /// <summary>ART 1.0.6 / 2.0.3: <c>new ClassWriter(0)</c> rebuilds the pool.</summary>
    Rebuild,
}

/// <summary>
/// ART's optional fixer passes. They run as independent copy-mode passes after
/// the rename, in the order declared by <c>RenamerImpl</c>: annotations,
/// identifiers, source file, record.
/// </summary>
[Flags]
internal enum ArtFixers
{
    None = 0,
    Annotations = 1 << 0,
    Identifiers = 1 << 1,
    SourceFile = 1 << 2,
    Record = 1 << 3,
    All = Annotations | Identifiers | SourceFile | Record,
}

/// <summary>
/// Version-specific ART behaviour. This is deliberately separate from the
/// processor command line: the same ART version can be invoked with different
/// fixer flags, while the writer and remapper algorithm stay fixed.
/// </summary>
internal sealed record ArtProfile(
    string Id,
    ConstantPoolMode PoolMode,
    bool ForceRecordFlag,
    int MaxClassMajor)
{
    /// <summary>Forge 1.20.1 era: ART 0.1.22 / ASM 9.2 / class 61.</summary>
    public static ArtProfile Forge0122 { get; } = new(
        "forge-0.1.22",
        ConstantPoolMode.CopyFromInput,
        ForceRecordFlag: false,
        MaxClassMajor: 62)
    {
        AlwaysRewriteAnnotations = true,
        AlwaysShiftParameterAnnotations = true,
        DefaultFixers = ArtFixers.All,
    };

    /// <summary>Forge 1.21.x era: ART 1.0.6 / ASM 9.5 / class 65.</summary>
    public static ArtProfile Forge106 { get; } = new(
        "forge-1.0.6",
        ConstantPoolMode.Rebuild,
        ForceRecordFlag: true,
        MaxClassMajor: 65)
    {
        DefaultFixers = ArtFixers.All,
    };

    /// <summary>NeoForge 21.1 era: ART 2.0.3 / ASM 9.5 / class 65.</summary>
    public static ArtProfile NeoForge203 { get; } = new(
        "neoforge-2.0.3",
        ConstantPoolMode.Rebuild,
        ForceRecordFlag: true,
        MaxClassMajor: 65)
    {
        DefaultFixers = ArtFixers.All,
    };

    /// <summary>
    /// ART 0.1.22 implements the annotation fixer as a plain transformer that
    /// always rewrites the class; 1.0.6 and 2.0.3 wrap it in
    /// <c>OptionalChangeTransformer</c> and keep the input bytes when nothing
    /// changed.
    /// </summary>
    public bool AlwaysRewriteAnnotations { get; init; }

    /// <summary>
    /// 0.1.22 reads parameter annotations into a <c>ClassNode</c>, whose arrays
    /// are sized by the descriptor, so it always drops the leading synthetic
    /// entries. 1.0.6 and 2.0.3 compare the attribute's declared parameter count
    /// with the descriptor and only shift when they match.
    /// </summary>
    public bool AlwaysShiftParameterAnnotations { get; init; }

    /// <summary>The fixer passes the installer enables by default.</summary>
    public ArtFixers DefaultFixers { get; init; } = ArtFixers.None;

    public static IReadOnlyList<ArtProfile> All { get; } =
        [Forge0122, Forge106, NeoForge203];

    /// <summary>
    /// Maps an ART artifact version (the manifest dependency version) to the
    /// profile that reproduces its byte output. Unknown versions are rejected so
    /// a new ART cannot silently produce a non-matching jar.
    /// </summary>
    public static bool TryResolve(string? artifactVersion, out ArtProfile profile)
    {
        profile = artifactVersion switch
        {
            "0.1.22" => Forge0122,
            "1.0.6" => Forge106,
            "2.0.3" => NeoForge203,
            _ => null!,
        };

        return profile is not null;
    }
}
