using Archoera.Domain.Downloads;
using Archoera.Domain.Mojang;

namespace Archoera.Application.Mojang;

/// <summary>
/// Maven roots for library entries that only carry a coordinate. Vanilla
/// metadata always provides <c>downloads.artifact</c>, but Fabric/Quilt
/// profiles and profiles merged by other launchers may keep only
/// <c>name</c> (and sometimes <c>url</c>).
/// </summary>
public static class MojangRepositories
{
    /// <summary>Mojang's library host; the default when no repository is known.</summary>
    public static Uri MinecraftLibraries { get; } = new("https://libraries.minecraft.net/");

    /// <summary>Maven Central, used for ASM and other non-Mojang plain coordinates.</summary>
    public static Uri MavenCentral { get; } = new("https://repo1.maven.org/maven2/");

    /// <summary>Fabric Maven (also hosts the ASM versions Fabric ships).</summary>
    public static Uri Fabric { get; } = new("https://maven.fabricmc.net/");

    /// <summary>Quilt's release Maven.</summary>
    public static Uri Quilt { get; } = new("https://maven.quiltmc.org/repository/release/");

    /// <summary>Forge Maven.</summary>
    public static Uri Forge { get; } = new("https://maven.minecraftforge.net/");

    /// <summary>NeoForge Maven (releases repository).</summary>
    public static Uri NeoForge { get; } = new("https://maven.neoforged.net/releases/");

    /// <summary>
    /// Picks the Maven root for a bound coordinate. An explicit
    /// <paramref name="repository"/> wins, but a Quilt profile that points
    /// <c>net.fabricmc:*</c> at Quilt Maven is redirected to Fabric Maven:
    /// Quilt does not publish those artifacts (docs/08 10 §2.3).
    /// </summary>
    /// <exception cref="MojangException">
    /// <c>INVALID_METADATA</c> when the repository is not an allowed absolute
    /// HTTPS URL.
    /// </exception>
    public static Uri ForCoordinate(string? repository, MojangLibraryCoordinate coordinate)
    {
        if (!string.IsNullOrWhiteSpace(repository))
        {
            var normalized = NormalizeRepository(repository);
            // Quilt meta points net.fabricmc artifacts at Quilt Maven, which
            // does not publish them. Switch those entries back to Fabric.
            if (StartsWith(coordinate.Group, "net.fabricmc") &&
                string.Equals(
                    normalized.Host,
                    "maven.quiltmc.org",
                    StringComparison.OrdinalIgnoreCase))
            {
                return Fabric;
            }

            return normalized;
        }

        return ForGroup(coordinate.Group);
    }

    private static Uri ForGroup(string group)
    {
        if (StartsWith(group, "net.fabricmc")) return Fabric;
        if (StartsWith(group, "org.quiltmc")) return Quilt;
        if (StartsWith(group, "net.minecraftforge") ||
            StartsWith(group, "cpw.mods") ||
            StartsWith(group, "de.oceanlabs"))
        {
            return Forge;
        }

        if (StartsWith(group, "net.neoforged")) return NeoForge;
        if (StartsWith(group, "org.ow2.asm")) return MavenCentral;
        return MinecraftLibraries;
    }

    private static Uri NormalizeRepository(string repository)
    {
        var issue = DownloadPlanPaths.TryValidateUrl(repository, out var parsed);
        if (issue is not null || parsed is null)
        {
            throw new MojangException(
                MojangErrorCode.InvalidMetadata,
                $"library repository is not allowed: {issue?.Message ?? repository}");
        }

        if (!string.IsNullOrEmpty(parsed.Query) || !string.IsNullOrEmpty(parsed.Fragment))
        {
            throw new MojangException(
                MojangErrorCode.InvalidMetadata,
                $"library repository must not contain a query or fragment: {repository}");
        }

        var builder = new UriBuilder(parsed);
        if (!builder.Path.EndsWith('/'))
        {
            builder.Path += "/";
        }

        return builder.Uri;
    }

    private static bool StartsWith(string value, string prefix)
        => value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
           (value.Length == prefix.Length || value[prefix.Length] == '.' ||
            value[prefix.Length] == '-');
}
