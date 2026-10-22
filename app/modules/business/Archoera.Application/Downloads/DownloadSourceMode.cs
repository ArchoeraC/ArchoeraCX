namespace Archoera.Application.Downloads;

/// <summary>
/// Which host a download plan should prefer. Mirrors stay as a fallback in
/// both explicit modes; <see cref="Auto"/> keeps the region-driven default.
/// </summary>
public enum DownloadSourceMode
{
    /// <summary>Follow the detected region (current default behaviour).</summary>
    Auto,

    /// <summary>Official Mojang/Maven hosts first, community mirror second.</summary>
    Official,

    /// <summary>Community mirror first, official hosts second.</summary>
    Mirror,
}

/// <summary>Wire <-> enum helpers for <see cref="DownloadSourceMode"/>.</summary>
public static class DownloadSourceModes
{
    public const string Auto = "auto";
    public const string Official = "official";
    public const string Mirror = "mirror";

    public static DownloadSourceMode Parse(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        Official => DownloadSourceMode.Official,
        Mirror => DownloadSourceMode.Mirror,
        _ => DownloadSourceMode.Auto,
    };

    public static string ToWire(this DownloadSourceMode mode) => mode switch
    {
        DownloadSourceMode.Official => Official,
        DownloadSourceMode.Mirror => Mirror,
        _ => Auto,
    };
}
