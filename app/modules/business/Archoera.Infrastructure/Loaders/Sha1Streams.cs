using System.Net.Http;
using System.Security.Cryptography;

namespace Archoera.Infrastructure.Loaders;

/// <summary>
/// Streaming SHA-1 helper shared by the installer bootstrap and the loader
/// repair. The digest always covers the bytes that were actually copied, so a
/// mirror's claim (or its absence) can never stand in for verification.
/// </summary>
internal static class Sha1Streams
{
    /// <summary>
    /// Copies <paramref name="source"/> into <paramref name="destination"/>
    /// while computing the SHA-1 of every byte that reaches the destination.
    /// The optional byte limit is enforced while copying so an oversized
    /// download cannot fill the disk.
    /// </summary>
    /// <returns>Lowercase hex SHA-1 of the copied bytes and the total count.</returns>
    /// <exception cref="HttpRequestException"><paramref name="maxBytes"/> was exceeded.</exception>
    public static async Task<(string Sha1, long Bytes)> CopyAsync(
        Stream source,
        Stream destination,
        long? maxBytes = null,
        string? context = null,
        CancellationToken cancellationToken = default)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA1);
        var buffer = new byte[81920];
        long total = 0;
        int read;
        while ((read = await source
                   .ReadAsync(buffer, cancellationToken)
                   .ConfigureAwait(false)) > 0)
        {
            total += read;
            if (maxBytes is { } limit && total > limit)
            {
                throw new HttpRequestException(
                    $"{context} returned an oversized installer");
            }

            hash.AppendData(buffer, 0, read);
            await destination
                .WriteAsync(buffer.AsMemory(0, read), cancellationToken)
                .ConfigureAwait(false);
        }

        return (LoaderPaths.Hex(hash.GetHashAndReset()), total);
    }
}
