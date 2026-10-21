using Archoera.Domain.Downloads;

namespace Archoera.Domain.Resources;

/// <summary>
/// One downloadable file attached to a <see cref="ResourceVersion"/>. The
/// provider CDN URL is used verbatim; this client never guesses third-party
/// mirrors (docs/08 §2.5).
/// </summary>
/// <param name="FileName">Provider file name; used as the last path segment.</param>
/// <param name="Url">CDN URL as returned by the provider; may be empty when distribution is denied.</param>
/// <param name="Size">Size in bytes.</param>
/// <param name="Sha1">Lower-case SHA-1 when the provider exposes it.</param>
/// <param name="Sha512">Lower-case SHA-512 when the provider exposes it.</param>
/// <param name="Primary">True for the provider-designated primary file.</param>
public sealed record ResourceFile(
    string FileName,
    string Url,
    long Size,
    string? Sha1 = null,
    string? Sha512 = null,
    bool Primary = false)
{
    /// <summary>SHA-512 when present, otherwise SHA-1 (docs/08 §4.4).</summary>
    public DownloadHash? PreferredHash
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(Sha512))
            {
                return DownloadHash.Create(DownloadHashAlgorithm.Sha512, Sha512);
            }

            if (!string.IsNullOrWhiteSpace(Sha1))
            {
                return DownloadHash.Create(DownloadHashAlgorithm.Sha1, Sha1);
            }

            return null;
        }
    }

    /// <summary>True when the provider returned a usable download URL.</summary>
    public bool HasUrl => !string.IsNullOrWhiteSpace(Url);
}
