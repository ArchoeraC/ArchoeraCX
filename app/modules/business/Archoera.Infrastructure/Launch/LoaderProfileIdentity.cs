using Archoera.Domain.Instances;
using Archoera.Domain.Loaders;
using Archoera.Domain.Mojang;

namespace Archoera.Infrastructure.Launch;

/// <summary>
/// Decides whether a version profile belongs to an instance's game and loader
/// build.
///
/// Launch repair and the dependency check look at every profile under the
/// shared <c>versions/</c> root, which also holds the profiles of other
/// instances. A name-only substring test must never pick a build from another
/// instance: <c>neoforge-21.1.264</c> is not <c>21.1.2</c>,
/// <c>fabric-loader-0.16.14</c> is not <c>0.16.1</c>, and
/// <c>1.21.1-forge-58.1.26</c> is not <c>58.1.2</c>. Versions are compared as
/// whole <c>-</c>/<c>:</c> separated tokens, and the profile content
/// (<c>inheritsFrom</c>, <c>--fml.*</c> arguments, loader libraries) must not
/// contradict the instance.
/// </summary>
internal static class LoaderProfileIdentity
{
    private static readonly char[] Separators =
        ['-', '_', ':', '/', '+', ',', ';', ' ', '\t'];

    /// <summary>
    /// True when <paramref name="version"/> is the vanilla profile for
    /// <paramref name="gameVersion"/>, or a loader profile for the instance's
    /// exact loader build.
    /// </summary>
    public static bool Matches(GameInstanceRecord instance, MojangVersion version)
    {
        ArgumentNullException.ThrowIfNull(instance);
        ArgumentNullException.ThrowIfNull(version);

        var game = instance.GameVersion?.Trim() ?? string.Empty;
        if (game.Length == 0 || string.IsNullOrWhiteSpace(version.Id)) return false;

        var loader = instance.Loader?.Trim().ToLowerInvariant() ?? string.Empty;
        if (loader is "" or "vanilla")
        {
            return string.Equals(version.Id, game, StringComparison.OrdinalIgnoreCase) &&
                   !HasLoaderLibraries(version);
        }

        if (!MentionsLoader(version, loader)) return false;

        // A profile that still inherits must inherit this game version. This is
        // the strongest signal for Forge/NeoForge installer profiles and for
        // merged profiles written next to their parent.
        if (!string.IsNullOrWhiteSpace(version.InheritsFrom) &&
            !string.Equals(
                version.InheritsFrom.Trim(),
                game,
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var gameInId = HasToken(version.Id, game);
        var gameInArguments = string.Equals(
            ArgumentValue(version, "--fml.mcVersion"),
            game,
            StringComparison.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(version.InheritsFrom))
        {
            gameInArguments = true;
        }

        var loaderVersion = LoaderVersionText.Normalize(
            loader,
            game,
            instance.LoaderVersion);
        var loaderInId = LoaderVersionInId(version.Id, loader);
        var loaderInArguments =
            ArgumentValue(version, "--fml.neoForgeVersion") ??
            ArgumentValue(version, "--fml.forgeVersion");

        if (loaderVersion.Length > 0)
        {
            // A profile that names a concrete loader build must name exactly
            // this one. Prefixes are not enough: every patch level is a
            // different classpath.
            if (loaderInId is not null && !SameBuild(loaderInId, loaderVersion))
            {
                return false;
            }

            if (loaderInArguments is not null &&
                !SameBuild(loaderInArguments, loaderVersion))
            {
                return false;
            }
        }

        if (gameInId || gameInArguments) return true;

        // NeoForge ids omit the game version ("neoforge-21.1.264"). Such a
        // profile is only identifiable by its loader build, which pins one
        // game version; without that evidence it must not be borrowed.
        return loaderVersion.Length > 0 && loaderInId is not null;
    }

    /// <summary>True when <paramref name="text"/> contains <paramref name="value"/> as a whole token.</summary>
    public static bool HasToken(string? text, string? value)
    {
        if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var wanted = value.Trim();
        foreach (var token in Tokens(text))
        {
            if (string.Equals(token, wanted, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Loader build named by a profile id, for example <c>47.2.0</c> in
    /// <c>1.20.1-forge-47.2.0</c>, or null when the id names no build.
    /// </summary>
    public static string? LoaderVersionInId(string? id, string loader)
    {
        if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(loader)) return null;

        var tokens = Tokens(id).ToList();
        var family = loader.Trim().ToLowerInvariant();
        for (var index = 0; index < tokens.Count; index++)
        {
            if (!IsFamilyToken(tokens[index], family)) continue;
            for (var next = index + 1; next < tokens.Count; next++)
            {
                if (LooksLikeBuild(tokens[next])) return tokens[next];
            }

            return null;
        }

        return null;
    }

    /// <summary>
    /// Two loader build spellings that name the same build, including Forge's
    /// game-prefixed Maven form (<c>1.20.1-47.2.0</c> vs <c>47.2.0</c>).
    /// </summary>
    public static bool SameBuild(string? left, string? right)
        => HasToken(left, right) || HasToken(right, left);

    private static bool MentionsLoader(MojangVersion version, string loader)
    {
        // Profile content is authoritative: legacy NeoForge (1.20.1) and Forge
        // share ids and "forge" coordinates, so the id token alone cannot tell
        // them apart.
        var content = ContentLoader(version);
        if (content is not null) return content == loader;

        if (HasToken(version.Id, loader)) return true;
        return loader == "neoforge" && HasToken(version.Id, "forge");
    }

    private static string? ContentLoader(MojangVersion version)
    {
        var evidence = Evidence(version);
        if (HasFamilyToken(evidence, "neoforge") ||
            HasFamilyToken(evidence, "neoforged") ||
            ArgumentValue(version, "--fml.neoForgeVersion") is not null)
        {
            return "neoforge";
        }

        if (HasFamilyToken(evidence, "minecraftforge") ||
            ArgumentValue(version, "--fml.forgeVersion") is not null)
        {
            return "forge";
        }

        // A bare "forge" token also appears in Forge's own coordinates
        // (net.minecraftforge:forge), checked after the NeoForge markers above.
        if (HasToken(evidence, "forge")) return "forge";
        // Quilt profiles also carry Fabric libraries (net.fabricmc:intermediary),
        // so the specific loader must win over the compatible one.
        if (HasFamilyToken(evidence, "quilt")) return "quilt";
        if (HasFamilyToken(evidence, "fabric")) return "fabric";
        return null;
    }

    /// <summary>
    /// True when a token names <paramref name="value"/>, optionally with a Maven
    /// group prefix such as <c>net.neoforged</c> or <c>net.minecraftforge</c>.
    /// </summary>
    private static bool HasFamilyToken(string text, string value)
    {
        foreach (var token in Tokens(text))
        {
            if (string.Equals(token, value, StringComparison.OrdinalIgnoreCase) ||
                token.EndsWith("." + value, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>True when the profile lists a loader library (not a vanilla profile).</summary>
    public static bool HasLoaderLibraries(MojangVersion version)
        => version.Libraries.Any(library =>
            library.Name.Contains("forge", StringComparison.OrdinalIgnoreCase) ||
            library.Name.Contains("fabric", StringComparison.OrdinalIgnoreCase) ||
            library.Name.Contains("quilt", StringComparison.OrdinalIgnoreCase));

    private static bool IsFamilyToken(string token, string loader)
    {
        if (string.Equals(token, loader, StringComparison.OrdinalIgnoreCase)) return true;
        // NeoForge publishes legacy 1.20.1 profiles under "forge".
        return loader == "neoforge" &&
               string.Equals(token, "forge", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>True for tokens that look like a version, not a word such as "loader".</summary>
    private static bool LooksLikeBuild(string token)
        => token.Length > 0 &&
           char.IsAsciiDigit(token[0]) &&
           token.Contains('.', StringComparison.Ordinal);

    private static string Evidence(MojangVersion version)
    {
        var builder = new System.Text.StringBuilder(version.Id);
        foreach (var library in version.Libraries)
        {
            builder.Append(' ').Append(library.Name);
        }

        if (version.Arguments is not null)
        {
            foreach (var argument in version.Arguments.Game)
            {
                foreach (var value in argument.Values)
                {
                    builder.Append(' ').Append(value);
                }
            }
        }

        if (!string.IsNullOrWhiteSpace(version.MainClass))
        {
            builder.Append(' ').Append(version.MainClass);
        }

        return builder.ToString();
    }

    private static string? ArgumentValue(MojangVersion version, string name)
    {
        if (version.Arguments is null) return null;
        foreach (var argument in version.Arguments.Game)
        {
            for (var index = 0; index + 1 < argument.Values.Count; index++)
            {
                if (string.Equals(argument.Values[index], name, StringComparison.Ordinal))
                {
                    return argument.Values[index + 1];
                }
            }
        }

        return null;
    }

    private static IEnumerable<string> Tokens(string text)
        => text.Split(Separators, StringSplitOptions.RemoveEmptyEntries);
}
