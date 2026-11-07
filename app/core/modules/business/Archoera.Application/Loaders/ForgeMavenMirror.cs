namespace Archoera.Application.Loaders;

/// <summary>
/// Optional mirror mapping for Forge/NeoForge Maven artifacts. The official
/// <c>maven.neoforged.net</c> host is unreachable from some mainland-China
/// networks; BMCLAPI mirrors the same repository and is used as a fallback
/// source. Hashes always come from the installer metadata, never the mirror.
/// </summary>
public static class ForgeMavenMirror
{
    /// <summary>Preferred BMCLAPI Mirror root (USTC).</summary>
    public static Uri Bmclapi { get; } = new("https://mirrors.ustc.edu.cn/bmclapi/");

    private static readonly Uri[] InstallerMirrorRoots =
    [
        Bmclapi,
        new("https://mirror.nju.edu.cn/bmclapi/"),
        new("https://mirrors.cernet.edu.cn/bmclapi/"),
        new("https://bmclapi2.bangbang93.com/maven/"),
    ];

    /// <summary>Maps a Forge/NeoForge Maven URL to one BMCLAPI mirror URL.</summary>
    public static bool TryMap(Uri source, Uri mirrorRoot, out Uri mapped)
    {
        mapped = null!;
        if (!TryGetRelativePath(source, out var relative))
        {
            return false;
        }

        if (!mirrorRoot.IsAbsoluteUri ||
            mirrorRoot.Scheme != Uri.UriSchemeHttps)
        {
            return false;
        }

        mapped = new Uri(mirrorRoot, relative);
        return true;
    }

    /// <summary>
    /// Rewrites a plan URL when a mirror root is configured. The mirror becomes
    /// primary and the official URL remains the final fallback.
    /// </summary>
    public static bool TryRewrite(
        string officialUrl,
        Uri? mirrorRoot,
        out string primary,
        out IReadOnlyList<string> mirrors)
    {
        primary = officialUrl;
        mirrors = [];
        if (mirrorRoot is null ||
            !Uri.TryCreate(officialUrl, UriKind.Absolute, out var source) ||
            !TryMap(source, mirrorRoot, out var mapped) ||
            string.Equals(mapped.AbsoluteUri, source.AbsoluteUri, StringComparison.Ordinal))
        {
            return false;
        }

        primary = mapped.AbsoluteUri;
        mirrors = [source.AbsoluteUri];
        return true;
    }

    /// <summary>
    /// Direct installer download fallbacks used only when the official Maven
    /// sidecar is unreachable. The caller computes and records the SHA-1.
    /// </summary>
    public static IReadOnlyList<Uri> InstallerMirrors(Uri officialUrl)
    {
        if (!TryGetRelativePath(officialUrl, out var relative))
        {
            return [];
        }

        return InstallerMirrorRoots
            .Select(root => new Uri(root, relative))
            .ToArray();
    }

    private static bool TryGetRelativePath(Uri source, out string relative)
    {
        relative = string.Empty;
        if (!source.IsAbsoluteUri ||
            source.Scheme != Uri.UriSchemeHttps ||
            !string.IsNullOrEmpty(source.Query) ||
            !string.IsNullOrEmpty(source.Fragment))
        {
            return false;
        }

        var path = source.AbsolutePath;
        if (string.Equals(source.Host, "maven.neoforged.net", StringComparison.OrdinalIgnoreCase))
        {
            const string releases = "/releases/";
            if (!path.StartsWith(releases, StringComparison.Ordinal))
            {
                return false;
            }

            relative = path[releases.Length..];
            return relative.Length > 0;
        }

        if (string.Equals(source.Host, "maven.minecraftforge.net", StringComparison.OrdinalIgnoreCase))
        {
            relative = path.TrimStart('/');
            return relative.Length > 0;
        }

        return false;
    }
}
