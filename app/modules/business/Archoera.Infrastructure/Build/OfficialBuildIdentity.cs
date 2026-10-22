using System.Security.Cryptography;
using System.Text;

namespace Archoera.Infrastructure.Build;

/// <summary>Build provenance shown by the About page.</summary>
/// <param name="Channel">`official`, `custom` or `unconfigured`.</param>
/// <param name="Official">
/// True when the Microsoft client id compiled into this binary is the project's
/// official one. It states a build configuration, not binary authenticity.
/// </param>
/// <param name="ClientIdSha256">
/// Domain-separated SHA-256 of the compiled-in client id (hex), or null when no
/// id was injected. Publishing this digest lets anyone compare a binary against
/// the official value without the id itself ever being committed.
/// </param>
public sealed record BuildIdentity(
    string Channel,
    bool Official,
    string? ClientIdSha256)
{
    /// <summary>First 12 hex characters, for compact UI display.</summary>
    public string? ClientIdFingerprint
        => ClientIdSha256 is { Length: >= 12 } hash ? hash[..12] : ClientIdSha256;
}

/// <summary>
/// Compares the Microsoft client id injected at compile time with the official
/// one. Official releases are built with
/// <c>-p:MicrosoftClientId=$ARCHOERACX_MICROSOFT_CLIENT_ID</c> from CI secrets;
/// this type only ever handles a digest of that id, so the repository stays free
/// of the raw value.
/// </summary>
/// <remarks>
/// Irreversibility: the digest is SHA-256 over a domain-separated payload
/// (<c>archoeracx/official-client-id/v1:</c> + id). The client id is a UUIDv4
/// with 122 random bits, so recovering it from the digest is a preimage search
/// over ~2^122 values — far beyond any feasible computation. Domain separation
/// additionally keeps the published constant out of generic UUID hash tables,
/// so the digest cannot be recognised as "the hash of some UUID" elsewhere.
/// The ABI never returns the raw id: only this digest and its 12-char prefix
/// leave the module.
/// </remarks>
public static class OfficialBuildIdentity
{
    public const string ChannelOfficial = "official";
    public const string ChannelCustom = "custom";
    public const string ChannelUnconfigured = "unconfigured";

    /// <summary>Public domain separator for the digest.</summary>
    public const string DigestDomain = "archoeracx/official-client-id/v1:";

    /// <summary>
    /// Domain-separated SHA-256 (hex) of the official Microsoft OAuth client
    /// id. Public and stable; rotate it in the same commit that rotates the
    /// client id.
    /// </summary>
    public const string OfficialClientIdSha256 =
        "9b8dd8ece4d8bdc8809209640b6800e4b33c40f94c8f57351e3666ab95f5ccf9";

    /// <summary>Classify the client id compiled into this binary.</summary>
    public static BuildIdentity Describe(string? clientId)
    {
        var hash = Sha256(clientId);
        if (hash is null)
        {
            return new BuildIdentity(ChannelUnconfigured, false, null);
        }

        var official = string.Equals(
            hash,
            OfficialClientIdSha256,
            StringComparison.OrdinalIgnoreCase);
        return new BuildIdentity(
            official ? ChannelOfficial : ChannelCustom,
            official,
            hash);
    }

    /// <summary>Lowercase domain-separated SHA-256 hex of a client id, else null.</summary>
    public static string? Sha256(string? clientId)
    {
        if (string.IsNullOrWhiteSpace(clientId)) return null;
        var bytes = SHA256.HashData(
            Encoding.UTF8.GetBytes(DigestDomain + clientId.Trim()));
        return Convert.ToHexStringLower(bytes);
    }
}
