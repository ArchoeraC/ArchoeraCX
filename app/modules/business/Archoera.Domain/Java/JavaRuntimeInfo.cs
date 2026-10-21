namespace Archoera.Domain.Java;

/// <summary>
/// A validated Java runtime discovered on the local machine.
/// </summary>
/// <param name="Path">Absolute path to the <c>java</c> executable.</param>
/// <param name="Version">Full version reported by the JVM (for example <c>21.0.4</c>).</param>
/// <param name="Major">Feature release, normalizing legacy <c>1.x</c> versions.</param>
/// <param name="Vendor">Vendor string reported by the JVM, or a best-effort fallback.</param>
/// <param name="Arch">Value of <c>os.arch</c>, or a best-effort fallback.</param>
/// <param name="Source">Discovery source, for example <c>JAVA_HOME</c>, <c>PATH</c> or <c>SYSTEM</c>.</param>
/// <param name="Home">Value of <c>java.home</c> when the JVM reported it; never shown in the ABI envelope.</param>
public sealed record JavaRuntimeInfo(
    string Path,
    string Version,
    int Major,
    string Vendor,
    string Arch,
    string Source,
    string? Home = null);
