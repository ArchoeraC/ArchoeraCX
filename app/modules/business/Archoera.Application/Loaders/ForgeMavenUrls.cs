using Archoera.Domain.Downloads;
using Archoera.Domain.Loaders;

namespace Archoera.Application.Loaders;

/// <summary>
/// Official Forge/NeoForge Maven URL construction (docs/08 10 §2.3). Only the
/// documented endpoints are produced; callers may substitute a mirror root but
/// the relative path is never guessed.
/// </summary>
public static class ForgeMavenUrls
{
    /// <summary>Official Forge Maven root.</summary>
    public static Uri ForgeBase { get; } = new("https://maven.minecraftforge.net/");

    /// <summary>Official NeoForge Maven root (the <c>releases</c> repository).</summary>
    public static Uri NeoForgeBase { get; } = new("https://maven.neoforged.net/releases/");

    /// <summary>Default Maven root for a loader kind.</summary>
    public static Uri BaseFor(LoaderKind kind) => kind switch
    {
        LoaderKind.Forge => ForgeBase,
        LoaderKind.NeoForge => NeoForgeBase,
        _ => throw new ForgeDownloadException(
            ForgeDownloadErrorCode.InvalidRequest,
            $"unsupported loader kind '{kind}'"),
    };

    /// <summary>
    /// Repository-relative installer path:
    /// <c>net/minecraftforge/forge/&lt;game&gt;-&lt;loader&gt;/forge-&lt;game&gt;-&lt;loader&gt;-installer.jar</c>
    /// or
    /// <c>net/neoforged/neoforge/&lt;loader&gt;/neoforge-&lt;loader&gt;-installer.jar</c>.
    /// </summary>
    /// <exception cref="ForgeDownloadException"><c>INVALID_REQUEST</c> for unsafe versions.</exception>
    public static string InstallerPath(LoaderKind kind, string gameVersion, string loaderVersion)
    {
        var game = RequireVersionSegment(gameVersion, "game version");
        var loader = RequireVersionSegment(loaderVersion, "loader version");

        return kind switch
        {
            LoaderKind.Forge =>
                $"net/minecraftforge/forge/{game}-{loader}/forge-{game}-{loader}-installer.jar",
            LoaderKind.NeoForge when game == "1.20.1" =>
                $"net/neoforged/forge/{NeoForgeLegacyVersion(game, loader)}/forge-{NeoForgeLegacyVersion(game, loader)}-installer.jar",
            LoaderKind.NeoForge =>
                $"net/neoforged/neoforge/{loader}/neoforge-{loader}-installer.jar",
            _ => throw new ForgeDownloadException(
                ForgeDownloadErrorCode.InvalidRequest,
                $"unsupported loader kind '{kind}'"),
        };
    }

    /// <summary>
    /// Absolute installer URL. <paramref name="baseAddress"/> overrides the
    /// official root (for mirrors); it must already be an allowed Maven root.
    /// </summary>
    /// <exception cref="ForgeDownloadException"><c>INVALID_REQUEST</c>.</exception>
    public static Uri Installer(
        LoaderKind kind,
        string gameVersion,
        string loaderVersion,
        Uri? baseAddress = null)
    {
        var root = NormalizeBase(baseAddress ?? BaseFor(kind));
        var relative = InstallerPath(kind, gameVersion, loaderVersion);
        return new Uri(root, relative);
    }

    /// <summary>
    /// The Maven <c>.sha1</c> sidecar for an artifact URL. The suffix is added
    /// to the path so a query string (if any) is preserved.
    /// </summary>
    public static Uri Sidecar(Uri artifactUri)
    {
        ArgumentNullException.ThrowIfNull(artifactUri);

        if (!artifactUri.IsAbsoluteUri)
        {
            throw new ForgeDownloadException(
                ForgeDownloadErrorCode.InvalidRequest,
                $"artifact URL must be absolute: {artifactUri}");
        }

        var builder = new UriBuilder(artifactUri);
        builder.Path += ".sha1";
        return builder.Uri;
    }

    /// <summary>Default instance-relative installer destination.</summary>
    public static string DefaultInstallerRelativePath(
        LoaderKind kind,
        string gameVersion,
        string loaderVersion)
    {
        var game = RequireVersionSegment(gameVersion, "game version");
        var loader = RequireVersionSegment(loaderVersion, "loader version");
        var token = kind switch
        {
            LoaderKind.Forge => "forge",
            LoaderKind.NeoForge => "neoforge",
            _ => throw new ForgeDownloadException(
                ForgeDownloadErrorCode.InvalidRequest,
                $"unsupported loader kind '{kind}'"),
        };

        return $"downloads/loaders/{token}/{game}-{loader}-installer.jar";
    }

    private static string NeoForgeLegacyVersion(string game, string loader)
        => loader.StartsWith(game + "-", StringComparison.Ordinal)
            ? loader
            : $"{game}-{loader}";

    private static Uri NormalizeBase(Uri baseAddress)
    {
        if (!baseAddress.IsAbsoluteUri)
        {
            throw new ForgeDownloadException(
                ForgeDownloadErrorCode.InvalidRequest,
                $"Maven base URL must be absolute: {baseAddress}");
        }

        var issue = DownloadPlanPaths.TryValidateUrl(baseAddress.AbsoluteUri, out var validated);
        if (issue is not null || validated is null)
        {
            throw new ForgeDownloadException(
                ForgeDownloadErrorCode.InvalidRequest,
                $"Maven base URL is not allowed: {issue?.Message ?? baseAddress.AbsoluteUri}");
        }

        if (!string.IsNullOrEmpty(validated.Query) || !string.IsNullOrEmpty(validated.Fragment))
        {
            throw new ForgeDownloadException(
                ForgeDownloadErrorCode.InvalidRequest,
                "Maven base URL must not contain a query or fragment");
        }

        var builder = new UriBuilder(validated);
        if (!builder.Path.EndsWith('/'))
        {
            builder.Path += "/";
        }

        return builder.Uri;
    }

    private static string RequireVersionSegment(string? value, string what)
    {
        var text = value?.Trim() ?? string.Empty;
        if (text.Length == 0 || text is "." or "..")
        {
            throw new ForgeDownloadException(
                ForgeDownloadErrorCode.InvalidRequest,
                $"{what} is required");
        }

        foreach (var character in text)
        {
            var allowed = char.IsAsciiLetterOrDigit(character)
                          || character is '.' or '-' or '_' or '+';
            if (!allowed)
            {
                throw new ForgeDownloadException(
                    ForgeDownloadErrorCode.InvalidRequest,
                    $"{what} '{text}' cannot be used as a Maven path segment");
            }
        }

        return text;
    }
}
