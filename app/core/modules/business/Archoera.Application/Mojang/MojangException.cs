namespace Archoera.Application.Mojang;

/// <summary>Stable Mojang metadata/plan error codes surfaced to logs and UI.</summary>
public static class MojangErrorCode
{
    /// <summary>404, or an id absent from the version manifest.</summary>
    public const string NotFound = "NOT_FOUND";

    /// <summary>Malformed JSON, unexpected shape or unexpected HTTP 4xx.</summary>
    public const string InvalidResponse = "INVALID_RESPONSE";

    /// <summary>Downloaded bytes do not match the published SHA-1 or size.</summary>
    public const string HashMismatch = "HASH_MISMATCH";

    /// <summary>Transport failure (DNS/TLS/timeout) or exhausted 5xx retries.</summary>
    public const string Network = "NETWORK_ERROR";

    /// <summary>429 after the retry budget.</summary>
    public const string RateLimited = "RATE_LIMITED";

    /// <summary>The caller supplied an unusable id/root/platform.</summary>
    public const string InvalidRequest = "INVALID_REQUEST";

    /// <summary>Metadata is structurally valid but cannot produce a safe plan.</summary>
    public const string InvalidMetadata = "INVALID_METADATA";

    /// <summary>A file that must be downloaded has no SHA-1.</summary>
    public const string MissingHash = "MISSING_HASH";

    /// <summary>A file that must be downloaded has no size.</summary>
    public const string MissingSize = "MISSING_SIZE";

    /// <summary>The version JSON has <c>inheritsFrom</c>; merging is not implemented yet.</summary>
    public const string UnsupportedInherits = "UNSUPPORTED_INHERITS";

    /// <summary>Two plan entries resolved to the same destination.</summary>
    public const string DuplicateDest = "DUPLICATE_DEST";
}

/// <summary>
/// Mojang client/plan failure with a stable code. Messages contain metadata
/// ids and URLs, never credentials.
/// </summary>
public sealed class MojangException : Exception
{
    public MojangException(
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

    /// <summary>One of <see cref="MojangErrorCode"/>.</summary>
    public string Code { get; }

    /// <summary>HTTP status when the failure came from a response.</summary>
    public int? StatusCode { get; }

    /// <summary>Server-requested delay for a 429 response.</summary>
    public TimeSpan? RetryAfter { get; }
}
