namespace Archoera.Domain.Downloads;

/// <summary>
/// Proxy handed to the executor explicitly (docs/10 §9.5). The host resolves
/// the platform/system proxy; Rust never reads environment proxies on its own,
/// so both HTTP stacks agree on routing.
/// </summary>
/// <param name="Url">`http://` or `https://` proxy URL, optionally with userinfo.</param>
/// <param name="NoProxy">Host suffixes that bypass the proxy.</param>
public sealed record DownloadProxyOptions(string Url, IReadOnlyList<string> NoProxy)
{
    /// <summary>Empty bypass list.</summary>
    public static DownloadProxyOptions FromUrl(string url) => new(url, []);
}
