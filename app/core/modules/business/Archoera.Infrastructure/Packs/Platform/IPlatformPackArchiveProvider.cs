namespace Archoera.Infrastructure.Packs.Platform;

/// <summary>
/// A platform pack that is published as one direct zip instead of a Solder
/// manifest. The platform supplies no digest for this file; the installer
/// still validates every ZIP entry's CRC while extracting it.
/// </summary>
/// <param name="Url">HTTPS archive URL returned by the platform API.</param>
/// <param name="VersionId">Provider-native version string.</param>
/// <param name="Name">Pack display name.</param>
/// <param name="Summary">Short description, if the platform supplies one.</param>
/// <param name="GameVersion">Minecraft version declared by the platform.</param>
/// <param name="Size">Archive size in bytes, discovered with HEAD/Range.</param>
internal sealed record PlatformPackArchive(
    string Url,
    string VersionId,
    string Name,
    string Summary,
    string GameVersion,
    long Size);

/// <summary>
/// Optional capability for providers whose packs can also be a single zip.
/// Returning <c>null</c> means the provider's normal manifest path applies.
/// </summary>
internal interface IPlatformPackArchiveProvider
{
    Task<PlatformPackArchive?> ResolveArchiveAsync(
        string packId,
        string versionId,
        CancellationToken cancellationToken);
}
