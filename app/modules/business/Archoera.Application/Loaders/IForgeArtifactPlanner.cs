using Archoera.Domain.Loaders;

namespace Archoera.Application.Loaders;

/// <summary>
/// Parses a Forge/NeoForge installer into a relocatable artifact manifest.
/// Never starts Java and never downloads a third-party client jar.
/// </summary>
public interface IForgeArtifactPlanner
{
    /// <summary>
    /// Reads <c>install_profile.json</c> and <c>version.json</c> from
    /// <paramref name="installerJar"/>, resolves client-side data variables,
    /// maven coordinates and installer entries, and returns the expected
    /// artifact set plus processor steps.
    /// </summary>
    /// <param name="installerJar">Installer zip to read. May live outside the root.</param>
    /// <param name="gameDir">Instance root. Manifest paths are relative to it.</param>
    /// <param name="librariesDir">Libraries directory; must be inside <paramref name="gameDir"/>.</param>
    /// <param name="kind">Forge or NeoForge.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<LoaderArtifactManifest> ParseAsync(
        string installerJar,
        string gameDir,
        string librariesDir,
        LoaderKind kind,
        CancellationToken cancellationToken = default);
}
