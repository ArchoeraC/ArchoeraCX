namespace Archoera.Domain.Downloads;

/// <summary>
/// Hash algorithms accepted by <c>archoera_dl</c> (docs/10 §9.2).
/// </summary>
public enum DownloadHashAlgorithm
{
    Sha1,
    Sha256,
    Sha512,
}

/// <summary>What the executor does when <c>dest</c> already exists.</summary>
public enum DownloadFileMode
{
    /// <summary>Keep an existing file when it matches the plan; otherwise download.</summary>
    Ensure,

    /// <summary>Always download and replace.</summary>
    Replace,

    /// <summary>Link to the content store (reserved; the v1 executor rejects it).</summary>
    Link,

    /// <summary>Symlink to the content store (reserved; the v1 executor rejects it).</summary>
    Symlink,

    /// <summary>Never download; report the file as skipped.</summary>
    Skip,
}

/// <summary>How an existing destination is compared before skipping a download.</summary>
public enum DownloadVerifyExisting
{
    Size,
    Hash,
    None,
}

/// <summary>Durability policy applied when committing a downloaded file.</summary>
public enum DownloadDurability
{
    Normal,
    Full,
    Fast,
}

/// <summary>
/// Wire names shared with <c>app/modules/downloader/src/plan.rs</c>. Parsing is
/// case-insensitive; serialization always emits the canonical lower-case name.
/// </summary>
public static class DownloadWire
{
    public static string ToWire(this DownloadHashAlgorithm algorithm) => algorithm switch
    {
        DownloadHashAlgorithm.Sha1 => "sha1",
        DownloadHashAlgorithm.Sha256 => "sha256",
        DownloadHashAlgorithm.Sha512 => "sha512",
        _ => throw new ArgumentOutOfRangeException(nameof(algorithm)),
    };

    /// <summary>Accepts both <c>sha1</c> and <c>sha-1</c> (Rust behaviour).</summary>
    public static bool TryParseHashAlgorithm(string? value, out DownloadHashAlgorithm algorithm)
    {
        switch (value?.Trim().ToLowerInvariant())
        {
            case "sha1" or "sha-1":
                algorithm = DownloadHashAlgorithm.Sha1;
                return true;
            case "sha256" or "sha-256":
                algorithm = DownloadHashAlgorithm.Sha256;
                return true;
            case "sha512" or "sha-512":
                algorithm = DownloadHashAlgorithm.Sha512;
                return true;
            default:
                algorithm = default;
                return false;
        }
    }

    public static string ToWire(this DownloadFileMode mode) => mode switch
    {
        DownloadFileMode.Ensure => "ensure",
        DownloadFileMode.Replace => "replace",
        DownloadFileMode.Link => "link",
        DownloadFileMode.Symlink => "symlink",
        DownloadFileMode.Skip => "skip",
        _ => throw new ArgumentOutOfRangeException(nameof(mode)),
    };

    public static bool TryParseFileMode(string? value, out DownloadFileMode mode)
    {
        switch (value?.Trim().ToLowerInvariant())
        {
            case "ensure":
                mode = DownloadFileMode.Ensure;
                return true;
            case "replace":
                mode = DownloadFileMode.Replace;
                return true;
            case "link":
                mode = DownloadFileMode.Link;
                return true;
            case "symlink":
                mode = DownloadFileMode.Symlink;
                return true;
            case "skip":
                mode = DownloadFileMode.Skip;
                return true;
            default:
                mode = default;
                return false;
        }
    }

    public static string ToWire(this DownloadVerifyExisting verify) => verify switch
    {
        DownloadVerifyExisting.Size => "size",
        DownloadVerifyExisting.Hash => "hash",
        DownloadVerifyExisting.None => "none",
        _ => throw new ArgumentOutOfRangeException(nameof(verify)),
    };

    public static bool TryParseVerifyExisting(string? value, out DownloadVerifyExisting verify)
    {
        switch (value?.Trim().ToLowerInvariant())
        {
            case "size":
                verify = DownloadVerifyExisting.Size;
                return true;
            case "hash":
                verify = DownloadVerifyExisting.Hash;
                return true;
            case "none":
                verify = DownloadVerifyExisting.None;
                return true;
            default:
                verify = default;
                return false;
        }
    }

    public static string ToWire(this DownloadDurability durability) => durability switch
    {
        DownloadDurability.Normal => "normal",
        DownloadDurability.Full => "full",
        DownloadDurability.Fast => "fast",
        _ => throw new ArgumentOutOfRangeException(nameof(durability)),
    };

    public static bool TryParseDurability(string? value, out DownloadDurability durability)
    {
        switch (value?.Trim().ToLowerInvariant())
        {
            case "normal":
                durability = DownloadDurability.Normal;
                return true;
            case "full":
                durability = DownloadDurability.Full;
                return true;
            case "fast":
                durability = DownloadDurability.Fast;
                return true;
            default:
                durability = default;
                return false;
        }
    }
}
