namespace Archoera.Domain.Loaders;

/// <summary>
/// One Forge/NeoForge install processor, resolved for the client side.
/// </summary>
/// <remarks>
/// <para>All paths are stored relative so a manifest can move with an instance:
/// <see cref="JarRelativePath"/> and <see cref="ClassPath"/> are relative to the
/// manifest libraries root; <see cref="Arguments"/> use <c>{ROOT}</c>,
/// <c>{LIBRARIES}</c>, <c>{INSTALLER}</c> and <c>{MINECRAFT_JAR}</c> placeholders.</para>
/// <para>The repair service expands paths before handing the step to an
/// <c>IProcessorRunner</c>. A runner therefore sees absolute path fields.</para>
/// </remarks>
/// <param name="Index">Zero-based position after side filtering.</param>
/// <param name="JarRelativePath">Processor jar, relative to the libraries root.</param>
/// <param name="ClassPath">Processor classpath entries, relative to the libraries root.</param>
/// <param name="MainClass">Main class read from the processor jar manifest, or empty when unavailable.</param>
/// <param name="Arguments">Program arguments with relocatable placeholders.</param>
/// <param name="Outputs">Artifacts this processor must produce.</param>
public sealed record ProcessorStep(
    int Index,
    string JarRelativePath,
    IReadOnlyList<string> ClassPath,
    string MainClass,
    IReadOnlyList<string> Arguments,
    IReadOnlyList<LoaderArtifact> Outputs);
