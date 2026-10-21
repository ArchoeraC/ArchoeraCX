namespace Archoera.Infrastructure.Resources.Modrinth;

/// <summary>
/// Configuration for <see cref="ModrinthClient"/>. The base address is
/// configurable so tests and a future gateway can point at another host, but
/// plain HTTP is only accepted for loopback addresses.
/// </summary>
public sealed record ModrinthClientOptions
{
    /// <summary>Default public API root.</summary>
    public static Uri DefaultBaseAddress { get; } = new("https://api.modrinth.com/v2/");

    /// <summary>
    /// API root. Must end with <c>/</c>; relative endpoints are resolved
    /// against it.
    /// </summary>
    public Uri BaseAddress { get; init; } = DefaultBaseAddress;

    /// <summary>
    /// Identifiable application user agent, for example
    /// <c>ArchoeraCX/0.1.0 (contact@example.com)</c>. Required: Modrinth rate
    /// limits and blocks unidentified clients.
    /// </summary>
    public string UserAgent { get; init; } = string.Empty;

    /// <summary>
    /// Number of extra attempts after a 429. Only 429 is retried; every other
    /// 4xx fails immediately.
    /// </summary>
    public int RateLimitRetries { get; init; } = 1;

    /// <summary>Delay used when a 429 has no <c>Retry-After</c> header.</summary>
    public TimeSpan DefaultRetryAfter { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>Upper bound applied to a server-provided <c>Retry-After</c>.</summary>
    public TimeSpan MaxRetryAfter { get; init; } = TimeSpan.FromSeconds(60);

    /// <summary>Per-request timeout (connect + headers + body).</summary>
    public TimeSpan RequestTimeout { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>Builds the canonical <c>ArchoeraCX/&lt;version&gt; (&lt;contact&gt;)</c> user agent.</summary>
    public static string CreateUserAgent(string appVersion, string contact)
    {
        if (string.IsNullOrWhiteSpace(appVersion) || string.IsNullOrWhiteSpace(contact))
        {
            throw new ArgumentException("appVersion and contact are required");
        }

        return $"ArchoeraCX/{appVersion.Trim()} ({contact.Trim()})";
    }

    internal ModrinthClientOptions Normalize()
    {
        if (BaseAddress is null || !BaseAddress.IsAbsoluteUri)
        {
            throw new ArgumentException("BaseAddress must be an absolute URI", nameof(BaseAddress));
        }

        var schemeAllowed = BaseAddress.Scheme == Uri.UriSchemeHttps
            || (BaseAddress.Scheme == Uri.UriSchemeHttp && IsLoopbackHost(BaseAddress.Host));
        if (!schemeAllowed)
        {
            throw new ArgumentException(
                "BaseAddress must use HTTPS (loopback HTTP is allowed for tests)",
                nameof(BaseAddress));
        }

        ValidateUserAgent(UserAgent);

        if (RateLimitRetries < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(RateLimitRetries));
        }

        if (RequestTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(RequestTimeout));
        }

        var baseAddress = BaseAddress.AbsoluteUri.EndsWith('/')
            ? BaseAddress
            : new Uri(BaseAddress.AbsoluteUri + "/", UriKind.Absolute);

        return this with { BaseAddress = baseAddress };
    }

    private static void ValidateUserAgent(string? userAgent)
    {
        if (string.IsNullOrWhiteSpace(userAgent) || userAgent.Trim().Length < 8)
        {
            throw new ArgumentException(
                "A non-empty, identifiable UserAgent is required " +
                "(for example 'ArchoeraCX/0.1.0 (contact@example.com)')",
                nameof(UserAgent));
        }

        string[] genericAgents =
        [
            "okhttp",
            "curl/",
            "wget/",
            "python-requests",
            "go-http-client",
            "postmanruntime",
            "insomnia",
            "java/",
            "apache-httpclient",
        ];

        foreach (var generic in genericAgents)
        {
            if (userAgent.Contains(generic, StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException(
                    $"UserAgent '{userAgent}' impersonates a generic HTTP client; " +
                    "identify the application instead",
                    nameof(UserAgent));
            }
        }
    }

    private static bool IsLoopbackHost(string host)
    {
        if (string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var bare = host.TrimStart('[').TrimEnd(']');
        return System.Net.IPAddress.TryParse(bare, out var address)
            && System.Net.IPAddress.IsLoopback(address);
    }
}
