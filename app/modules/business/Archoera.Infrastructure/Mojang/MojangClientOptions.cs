namespace Archoera.Infrastructure.Mojang;

/// <summary>
/// Configuration for <see cref="MojangClient"/>. The base address and manifest
/// path are configurable so a mirror/gateway can be substituted, but plain HTTP
/// is only accepted for loopback tests.
/// </summary>
public sealed record MojangClientOptions
{
    /// <summary>Default Mojang metadata root.</summary>
    public static Uri DefaultBaseAddress { get; } = new("https://piston-meta.mojang.com/");

    /// <summary>Relative path of the version manifest under <see cref="BaseAddress"/>.</summary>
    public const string DefaultManifestPath = "mc/game/version_manifest_v2.json";

    /// <summary>Metadata root; relative endpoints (the manifest) resolve against it.</summary>
    public Uri BaseAddress { get; init; } = DefaultBaseAddress;

    /// <summary>Relative manifest path. Must not be absolute or contain <c>..</c>.</summary>
    public string ManifestPath { get; init; } = DefaultManifestPath;

    /// <summary>
    /// Identifiable application user agent, for example
    /// <c>ArchoeraCX/0.0.1+1 (contact@example.com)</c>. Required.
    /// </summary>
    public string UserAgent { get; init; } = string.Empty;

    /// <summary>Per-request timeout (connect + headers + body).</summary>
    public TimeSpan RequestTimeout { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>Extra attempts after a 429/5xx/transport failure. Default: 2.</summary>
    public int MaxRetries { get; init; } = 2;

    /// <summary>Base delay for exponential backoff (also used when 429 has no Retry-After).</summary>
    public TimeSpan RetryDelay { get; init; } = TimeSpan.FromMilliseconds(500);

    /// <summary>Upper bound for any retry delay.</summary>
    public TimeSpan MaxRetryDelay { get; init; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Maximum accepted metadata response size. Version JSON is small; the limit
    /// prevents a broken mirror from streaming unbounded data into memory.
    /// </summary>
    public long MaxMetadataBytes { get; init; } = 32L * 1024 * 1024;

    /// <summary>Builds the canonical <c>ArchoeraCX/&lt;version&gt; (&lt;contact&gt;)</c> user agent.</summary>
    public static string CreateUserAgent(string appVersion, string contact)
    {
        if (string.IsNullOrWhiteSpace(appVersion) || string.IsNullOrWhiteSpace(contact))
        {
            throw new ArgumentException("appVersion and contact are required");
        }

        return $"ArchoeraCX/{appVersion.Trim()} ({contact.Trim()})";
    }

    internal MojangClientOptions Normalize()
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

        if (string.IsNullOrWhiteSpace(ManifestPath)
            || ManifestPath.Contains('\\')
            || ManifestPath.Contains("..", StringComparison.Ordinal)
            || Uri.TryCreate(ManifestPath, UriKind.Absolute, out _))
        {
            throw new ArgumentException(
                "ManifestPath must be a relative path without '..'",
                nameof(ManifestPath));
        }

        ValidateUserAgent(UserAgent);

        if (RequestTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(RequestTimeout));
        }

        if (MaxRetries is < 0 or > 5)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxRetries));
        }

        if (RetryDelay <= TimeSpan.Zero || MaxRetryDelay < RetryDelay)
        {
            throw new ArgumentOutOfRangeException(nameof(RetryDelay));
        }

        if (MaxMetadataBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxMetadataBytes));
        }

        var baseAddress = BaseAddress.AbsoluteUri.EndsWith('/')
            ? BaseAddress
            : new Uri(BaseAddress.AbsoluteUri + "/", UriKind.Absolute);

        var manifestPath = ManifestPath.TrimStart('/');

        return this with { BaseAddress = baseAddress, ManifestPath = manifestPath };
    }

    private static void ValidateUserAgent(string? userAgent)
    {
        if (string.IsNullOrWhiteSpace(userAgent) || userAgent.Trim().Length < 8)
        {
            throw new ArgumentException(
                "A non-empty, identifiable UserAgent is required " +
                "(for example 'ArchoeraCX/0.0.1+1 (contact@example.com)')",
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
