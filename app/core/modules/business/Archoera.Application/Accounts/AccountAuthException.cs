namespace Archoera.Application.Accounts;

/// <summary>
/// Stable, user-presentable authentication failure. Codes are intentionally
/// transport-independent so the UI can localize them.
/// </summary>
public sealed class AccountAuthException : Exception
{
    public AccountAuthException(string code, string message, string? detail = null)
        : base(message)
    {
        Code = code;
        Detail = detail;
    }

    public string Code { get; }
    public string? Detail { get; }
}
