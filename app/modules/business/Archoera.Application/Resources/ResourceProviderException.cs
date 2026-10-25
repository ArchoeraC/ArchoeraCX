namespace Archoera.Application.Resources;

/// <summary>Stable provider error codes surfaced to the UI/log layer.</summary>
public static class ResourceErrorCode
{
    /// <summary>401/407: provider credentials missing or rejected.</summary>
    public const string ProviderAuth = "PROVIDER_AUTH";

    /// <summary>
    /// The provider is known but not registered. CurseForge online access and
    /// its zip import both need an API key; the provider remains unavailable
    /// until a key-gated implementation is wired into the registry.
    /// </summary>
    public const string ProviderUnavailable = "PROVIDER_UNAVAILABLE";

    /// <summary>404: project/version/file no longer exists.</summary>
    public const string NotFound = "NOT_FOUND";

    /// <summary>429 after the retry budget: back off and retry later.</summary>
    public const string RateLimited = "RATE_LIMITED";

    /// <summary>403, or a version/file without a downloadable URL.</summary>
    public const string DistributionDenied = "DISTRIBUTION_DENIED";

    /// <summary>No version satisfies the exact game version/loader target.</summary>
    public const string NoCompatibleBuild = "NO_COMPATIBLE_BUILD";

    /// <summary>Provider returned malformed/unexpected JSON.</summary>
    public const string InvalidResponse = "INVALID_RESPONSE";

    /// <summary>Caller supplied an unusable request.</summary>
    public const string InvalidRequest = "INVALID_REQUEST";

    /// <summary>Required dependency graph contains an incompatible edge.</summary>
    public const string DependencyConflict = "DEPENDENCY_CONFLICT";

    /// <summary>Transport failure (DNS/TLS/timeout) before an HTTP status.</summary>
    public const string Network = "NETWORK_ERROR";
}

/// <summary>
/// Provider failure with a stable code. HTTP status and <c>Retry-After</c> are
/// retained for diagnostics; messages never contain API keys or tokens.
/// </summary>
public sealed class ResourceProviderException : Exception
{
    public ResourceProviderException(
        string code,
        string message,
        int? statusCode = null,
        TimeSpan? retryAfter = null,
        Exception? innerException = null)
        : base(message, innerException)
    {
        Code = code;
        StatusCode = statusCode;
        RetryAfter = retryAfter;
    }

    /// <summary>One of <see cref="ResourceErrorCode"/>.</summary>
    public string Code { get; }

    /// <summary>HTTP status when the failure came from a response.</summary>
    public int? StatusCode { get; }

    /// <summary>Server-requested delay for 429 responses.</summary>
    public TimeSpan? RetryAfter { get; }
}
