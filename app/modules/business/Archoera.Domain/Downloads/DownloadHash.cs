namespace Archoera.Domain.Downloads;

/// <summary>
/// Canonical file hash: a supported algorithm plus lower-case hex.
/// </summary>
/// <param name="Algorithm">SHA-1, SHA-256 or SHA-512.</param>
/// <param name="Hex">Lower-case hex digest, exactly <see cref="ExpectedHexLength"/> characters.</param>
public sealed record DownloadHash(DownloadHashAlgorithm Algorithm, string Hex)
{
    /// <summary>Expected digest length in hex characters for <see cref="Algorithm"/>.</summary>
    public int ExpectedHexLength => Algorithm switch
    {
        DownloadHashAlgorithm.Sha1 => 40,
        DownloadHashAlgorithm.Sha256 => 64,
        DownloadHashAlgorithm.Sha512 => 128,
        _ => 0,
    };

    /// <summary>Canonical wire name, for example <c>sha1</c>.</summary>
    public string Algo => Algorithm.ToWire();

    /// <summary>
    /// Creates a hash from raw producer input. The digest is trimmed and
    /// lower-cased; existence/length is validated by
    /// <see cref="DownloadPlanValidator"/>.
    /// </summary>
    public static DownloadHash Create(DownloadHashAlgorithm algorithm, string? hex)
        => new(algorithm, (hex ?? string.Empty).Trim().ToLowerInvariant());

    /// <summary>Parses a producer-supplied algorithm name and digest.</summary>
    public static bool TryCreate(string? algorithm, string? hex, out DownloadHash hash)
    {
        if (DownloadWire.TryParseHashAlgorithm(algorithm, out var parsed))
        {
            hash = Create(parsed, hex);
            return true;
        }

        hash = null!;
        return false;
    }

    /// <summary>True when the digest contains only lower-case hex digits.</summary>
    public bool IsCanonicalHex()
    {
        if (Hex.Length != ExpectedHexLength)
        {
            return false;
        }

        foreach (var character in Hex)
        {
            var isLowerHex = character is (>= '0' and <= '9') or (>= 'a' and <= 'f');
            if (!isLowerHex)
            {
                return false;
            }
        }

        return true;
    }

    /// <inheritdoc />
    public override string ToString() => $"{Algo}:{Hex}";
}
