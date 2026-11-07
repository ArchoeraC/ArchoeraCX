namespace Archoera.Domain.Java;

/// <summary>
/// Stable Java-runtime error codes. They deliberately do not reuse the Mojang
/// version-plan codes because runtime metadata has its own failure modes
/// (unsupported platform, LZMA-only files, no matching component).
/// </summary>
public static class JavaRuntimeErrorCode
{
    /// <summary>The caller supplied an unusable root/major/component.</summary>
    public const string InvalidRequest = "INVALID_REQUEST";

    /// <summary>Malformed JSON, an unexpected shape or an unexpected HTTP 4xx.</summary>
    public const string InvalidResponse = "INVALID_RESPONSE";

    /// <summary>Structurally valid metadata that cannot produce a safe plan.</summary>
    public const string InvalidMetadata = "INVALID_METADATA";

    /// <summary>404, or a requested runtime/component is absent from the catalog.</summary>
    public const string NotFound = "NOT_FOUND";

    /// <summary>Downloaded bytes do not match the published SHA-1 or size.</summary>
    public const string HashMismatch = "HASH_MISMATCH";

    /// <summary>Transport failure (DNS/TLS/timeout) or exhausted 5xx retries.</summary>
    public const string Network = "NETWORK_ERROR";

    /// <summary>429 after the retry budget.</summary>
    public const string RateLimited = "RATE_LIMITED";

    /// <summary>A runtime manifest reference or file has no SHA-1.</summary>
    public const string MissingHash = "MISSING_HASH";

    /// <summary>A runtime manifest reference or file has no size.</summary>
    public const string MissingSize = "MISSING_SIZE";

    /// <summary>No <c>all.json</c> platform key exists for the target OS/arch.</summary>
    public const string UnsupportedPlatform = "UNSUPPORTED_PLATFORM";

    /// <summary>No default component is known for the requested Java major.</summary>
    public const string UnsupportedJavaVersion = "UNSUPPORTED_JAVA_VERSION";

    /// <summary>The catalog has no runtime for the selected platform/component.</summary>
    public const string NoRuntimeAvailable = "NO_RUNTIME_AVAILABLE";

    /// <summary>A file only publishes an LZMA download; this slice cannot unpack it.</summary>
    public const string LzmaOnlyUnsupported = "LZMA_ONLY_UNSUPPORTED";

    /// <summary>Two manifest paths resolved to the same destination.</summary>
    public const string DuplicateDest = "DUPLICATE_DEST";

    /// <summary>A manifest path is absolute or escapes through <c>..</c>.</summary>
    public const string PathEscape = "PATH_ESCAPE";
}

/// <summary>
/// Java-runtime metadata/plan failure with a stable code. Messages contain
/// runtime names and URLs, never credentials or local absolute paths.
/// </summary>
public sealed class JavaRuntimeException : Exception
{
    public JavaRuntimeException(
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

    /// <summary>One of <see cref="JavaRuntimeErrorCode"/>.</summary>
    public string Code { get; }

    /// <summary>HTTP status when the failure came from a response.</summary>
    public int? StatusCode { get; }

    /// <summary>Server-requested delay for a 429 response.</summary>
    public TimeSpan? RetryAfter { get; }
}
