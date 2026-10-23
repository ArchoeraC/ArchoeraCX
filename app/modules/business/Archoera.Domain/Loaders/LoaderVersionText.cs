namespace Archoera.Domain.Loaders;

/// <summary>
/// Normalizes the loader-version spellings found in imported instances to the
/// bare build number used by Maven URLs, installer names and launch profiles.
/// </summary>
/// <remarks>
/// Launchers disagree on what "the loader version" is: SJMCL/LauncherX store
/// the full profile id (<c>1.20.1-forge-47.4.18</c>,
/// <c>fabric-loader-0.18.4-1.21.8</c>), while Forge's Maven uses the
/// game-prefixed version (<c>1.20.1-47.4.18</c>) and Fabric's meta uses the
/// bare build. Repair must accept all of them, otherwise the first hashless
/// imported instance downloads a nonexistent installer path.
/// </remarks>
public static class LoaderVersionText
{
    /// <summary>
    /// Returns the bare loader build for <paramref name="loader"/>, or the
    /// trimmed input when no known wrapper matches.
    /// </summary>
    public static string Normalize(string? loader, string? gameVersion, string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;

        var text = value.Trim();
        var kind = loader?.Trim().ToLowerInvariant() ?? string.Empty;
        var game = gameVersion?.Trim() ?? string.Empty;

        if (kind is "forge" or "neoforge")
        {
            var markers = kind == "neoforge"
                ? new[] { "neoforge-", "neoforge:", "forge-", "forge:" }
                : ["forge-", "forge:"];

            // Covers "1.20.1-forge-47.4.18", "forge-1.20.1-47.4.18" and
            // NeoForge's "1.21.1-neoforge-21.1.248".
            text = StripMarkers(text, markers);
            text = StripGamePrefix(text, game);
            text = StripMarkers(text, markers);
            text = StripGamePrefix(text, game);
            return text.Trim();
        }

        if (kind is "fabric" or "quilt")
        {
            var marker = kind == "fabric" ? "fabric-loader" : "quilt-loader";
            text = StripMarkers(text, [marker + "-", marker + ":"]);

            // Profile ids append the game version: "0.18.4-1.21.8".
            if (game.Length > 0 &&
                text.EndsWith("-" + game, StringComparison.OrdinalIgnoreCase))
            {
                text = text[..^(game.Length + 1)];
            }

            return text.Trim();
        }

        return text;
    }

    private static string StripMarkers(string text, IReadOnlyList<string> markers)
    {
        foreach (var marker in markers)
        {
            if (text.StartsWith(marker, StringComparison.OrdinalIgnoreCase) &&
                text.Length > marker.Length)
            {
                return text[marker.Length..].Trim();
            }
        }

        return text;
    }

    private static string StripGamePrefix(string text, string game)
    {
        if (game.Length == 0) return text;
        foreach (var separator in new[] { "-", ":" })
        {
            var prefix = game + separator;
            if (text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
                text.Length > prefix.Length)
            {
                return text[prefix.Length..].Trim();
            }
        }

        return text;
    }
}
