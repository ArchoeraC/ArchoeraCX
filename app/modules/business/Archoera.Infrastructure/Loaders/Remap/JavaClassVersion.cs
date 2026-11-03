namespace Archoera.Infrastructure.Loaders.Remap;

/// <summary>
/// Class-file features that depend on the Java version that produced the input
/// bytes. ART itself does not change for these, but the attribute set and the
/// bytecode forms it must preserve do.
/// </summary>
internal static class JavaClassVersion
{
    public const int Java5 = 49;
    public const int Java6 = 50;
    public const int Java7 = 51;
    public const int Java8 = 52;
    public const int Java9 = 53;
    public const int Java11 = 55;
    public const int Java16 = 60;
    public const int Java17 = 61;
    public const int Java21 = 65;

    public static bool HasStackMapTable(int major) => major >= Java6;

    public static bool HasTypeAnnotations(int major) => major >= Java8;

    public static bool HasMethodParameters(int major) => major >= Java8;

    public static bool HasModules(int major) => major >= Java9;

    public static bool HasNestmates(int major) => major >= Java11;

    public static bool HasDynamicConstants(int major) => major >= Java11;

    public static bool HasRecords(int major) => major >= Java16;

    public static bool HasSealedClasses(int major) => major >= Java17;

    /// <summary>
    /// Java 9+ compilers lower string concatenation to <c>invokedynamic</c>
    /// (StringConcatFactory); Java 8 uses StringBuilder. The remapper must
    /// handle both, so this only documents the expected form.
    /// </summary>
    public static bool UsesIndyStringConcat(int major) => major >= Java9;

    public static bool UsesLambdaMetafactory(int major) => major >= Java8;
}
