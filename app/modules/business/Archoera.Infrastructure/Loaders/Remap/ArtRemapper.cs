namespace Archoera.Infrastructure.Loaders.Remap;

/// <summary>
/// Replicates ART's processing pipeline for one class: the rename pass followed
/// by the optional fixer passes. ART applies each fixer as an independent
/// <c>ClassReader → ClassWriter(reader, 0)</c> pass and keeps the rewritten
/// bytes only when the fixer reports a change, so untouched classes stay
/// byte-identical.
/// </summary>
internal static class ArtRemapper
{
    public static byte[] Rewrite(
        byte[] classBytes,
        TsrgMapping mapping,
        ClassHierarchy hierarchy,
        ArtProfile profile,
        ArtFixers? fixers = null)
    {
        ArgumentNullException.ThrowIfNull(classBytes);
        ArgumentNullException.ThrowIfNull(mapping);
        ArgumentNullException.ThrowIfNull(hierarchy);
        ArgumentNullException.ThrowIfNull(profile);

        var options = fixers ?? profile.DefaultFixers;
        var model = ClassFileReader.Read(classBytes);
        var current = new ClassFileRemapper(
            model,
            mapping,
            hierarchy,
            profile.PoolMode,
            ArtFixers.None,
            forceRecordFlag: false).Rewrite();

        current = ApplyFixer(
            current,
            profile,
            options,
            ArtFixers.Annotations,
            alwaysRewrite: profile.AlwaysRewriteAnnotations);
        current = ApplyFixer(current, profile, options, ArtFixers.Record, alwaysRewrite: false);
        current = ApplyFixer(current, profile, options, ArtFixers.Identifiers, alwaysRewrite: false);
        current = ApplyFixer(current, profile, options, ArtFixers.SourceFile, alwaysRewrite: false);
        return current;
    }

    private static byte[] ApplyFixer(
        byte[] classBytes,
        ArtProfile profile,
        ArtFixers options,
        ArtFixers fixer,
        bool alwaysRewrite)
    {
        if ((options & fixer) == 0)
        {
            return classBytes;
        }

        var model = ClassFileReader.Read(classBytes);
        var remapper = new ClassFileRemapper(
            model,
            TsrgMapping.Empty,
            ClassHierarchy.Empty,
            ConstantPoolMode.CopyFromInput,
            fixer,
            profile.ForceRecordFlag,
            profile.AlwaysShiftParameterAnnotations);
        var rewritten = remapper.Rewrite();
        return alwaysRewrite || remapper.Changed ? rewritten : classBytes;
    }
}
