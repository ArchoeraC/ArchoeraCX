namespace Archoera.Domain.Accounts;

/// <summary>
/// Sensitive account material. This type is only used behind the C# boundary;
/// it is never serialized into API responses or log fields.
/// </summary>
public sealed record AccountCredential(
    string AccountId,
    string? MicrosoftRefreshToken,
    string? MinecraftAccessToken,
    DateTimeOffset? MinecraftAccessTokenExpiresAt,
    string? CertificatesJson,
    DateTimeOffset UpdatedAt,
    string? AuthlibServer = null,
    string? AuthlibAccessToken = null,
    string? AuthlibClientToken = null,
    string? AuthlibMetadataJson = null,
    string? AuthlibTermsUrl = null,
    DateTimeOffset? AuthlibTermsAcceptedAt = null)
{
    public bool HasRefreshToken => !string.IsNullOrWhiteSpace(MicrosoftRefreshToken);
    public bool HasAuthlibToken => !string.IsNullOrWhiteSpace(AuthlibAccessToken);
}
