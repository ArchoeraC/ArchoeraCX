namespace Archoera.Domain.Loaders;

/// <summary>
/// Result of checking a manifest against a directory tree.
/// </summary>
/// <param name="Ok">True when every artifact exists, is safe, and matches its hash/size.</param>
/// <param name="Missing">Artifacts whose file does not exist.</param>
/// <param name="Mismatched">Artifacts that exist but fail a size or SHA-1 check.</param>
/// <param name="Details">Human-readable diagnostics; paths are relative, never absolute.</param>
/// <param name="Unsafe">Artifacts whose path escapes its base directory or is otherwise invalid.</param>
public sealed record LoaderVerification(
    bool Ok,
    IReadOnlyList<LoaderArtifact> Missing,
    IReadOnlyList<LoaderArtifact> Mismatched,
    IReadOnlyList<string> Details,
    IReadOnlyList<LoaderArtifact>? Unsafe = null)
{
    /// <summary>An empty, successful verification.</summary>
    public static LoaderVerification Valid { get; } = new(
        true,
        [],
        [],
        [],
        []);
}
