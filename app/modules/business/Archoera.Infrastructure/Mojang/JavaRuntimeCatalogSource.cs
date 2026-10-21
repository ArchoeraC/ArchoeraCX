namespace Archoera.Infrastructure.Mojang;

/// <summary>
/// Location of Mojang's Java-runtime catalog. Defaults to the official
/// <c>all.json</c>; a mirror/gateway can override the base address and path,
/// but plain HTTP is only accepted for loopback tests.
/// </summary>
public sealed record JavaRuntimeCatalogSource
{
    /// <summary>Default catalog host.</summary>
    public const string DefaultBaseAddress = "https://launchermeta.mojang.com/";

    /// <summary>
    /// Catalog path used by the official launcher. The hash segment identifies
    /// the current runtime product document.
    /// </summary>
    public const string DefaultCatalogPath =
        "v1/products/java-runtime/2ec0cc96c44e5a76b9c8b7c39df7210883d12871/all.json";

    private static readonly Uri DefaultBaseUri = new(DefaultBaseAddress, UriKind.Absolute);

    /// <summary>Catalog root; a trailing slash is added when missing.</summary>
    public Uri BaseAddress { get; init; } = DefaultBaseUri;

    /// <summary>Relative path below <see cref="BaseAddress"/>.</summary>
    public string CatalogPath { get; init; } = DefaultCatalogPath;

    /// <summary>Resolves and validates the absolute catalog URL.</summary>
    internal Uri Resolve()
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

        if (string.IsNullOrWhiteSpace(CatalogPath)
            || CatalogPath.Contains('\\')
            || CatalogPath.Contains("..", StringComparison.Ordinal)
            || Uri.TryCreate(CatalogPath, UriKind.Absolute, out _))
        {
            throw new ArgumentException(
                "CatalogPath must be a relative path without '..'",
                nameof(CatalogPath));
        }

        var baseAddress = BaseAddress.AbsoluteUri.EndsWith('/')
            ? BaseAddress
            : new Uri(BaseAddress.AbsoluteUri + "/", UriKind.Absolute);

        return new Uri(baseAddress, CatalogPath.TrimStart('/'));
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
