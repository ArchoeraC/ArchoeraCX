namespace Archoera.Domain.Accounts;

public enum AccountKind
{
    Offline,
    Microsoft,
    Authlib,
}

/// <summary>
/// User-visible account metadata. Tokens never live in this record and never
/// cross the C ABI; they are held by the C# credential store.
/// </summary>
public sealed record AccountRecord(
    string Id,
    AccountKind Kind,
    string DisplayName,
    string? Uuid,
    string? Xuid,
    string? SkinUrl,
    string? CapeUrl,
    bool Active,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? LastLoginAt)
{
    public const int CurrentSchemaVersion = 1;
}
