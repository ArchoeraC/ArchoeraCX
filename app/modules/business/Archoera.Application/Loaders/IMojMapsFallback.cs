namespace Archoera.Application.Loaders;

/// <summary>
/// Replacement for the <c>DOWNLOAD_MOJMAPS</c> processor.
///
/// For installers whose processor has no <c>--sanitize</c> step the processor
/// output is byte-identical to the vanilla version's <c>client_mappings</c>, so
/// the launcher can fetch it directly when the Java tool fails. Installers that
/// sanitize the mappings (Forge 1.21.1) must keep running the processor.
/// </summary>
public interface IMojMapsFallback
{
    /// <summary>
    /// Downloads and verifies the mappings into <paramref name="targetPath"/>.
    /// Returns false when the version has no trusted mappings or the download
    /// does not match its published SHA-1/size.
    /// </summary>
    Task<bool> TryResolveAsync(
        string gameVersion,
        string targetPath,
        CancellationToken cancellationToken);
}
