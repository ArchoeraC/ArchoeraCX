using Archoera.Application.Downloads;

namespace Archoera.Application.Resources;

/// <summary>
/// MCIM (<c>mod.mcimirror.top</c>) mirrors <c>cdn.modrinth.com</c> with the same
/// path, which makes it a drop-in fallback for mainland-China networks where
/// the official CDN is throttled. It is a community service: keep the official
/// URL as the fallback and only prefer the mirror when the user asks for it.
/// </summary>
public static class ModrinthMirror
{
    public const string OfficialHost = "cdn.modrinth.com";
    public const string MirrorRoot = "https://mod.mcimirror.top";

    /// <summary>Mirror URL for a Modrinth CDN file, or null for other hosts.</summary>
    public static string? MirrorUrl(string officialUrl)
    {
        if (!Uri.TryCreate(officialUrl, UriKind.Absolute, out var uri) ||
            !string.Equals(uri.Host, OfficialHost, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return $"{MirrorRoot}{uri.AbsolutePath}{uri.Query}";
    }

    /// <summary>
    /// Primary URL plus fallbacks for one Modrinth file. `mirror` puts MCIM
    /// first; `auto`/`official` keep the official CDN first and only fall back.
    /// </summary>
    public static (string Primary, string[] Mirrors) Resolve(
        string officialUrl,
        DownloadSourceMode mode)
    {
        var mirror = MirrorUrl(officialUrl);
        if (mirror is null || string.Equals(mirror, officialUrl, StringComparison.Ordinal))
        {
            return (officialUrl, []);
        }

        return mode == DownloadSourceMode.Mirror
            ? (mirror, [officialUrl])
            : (officialUrl, [mirror]);
    }
}
