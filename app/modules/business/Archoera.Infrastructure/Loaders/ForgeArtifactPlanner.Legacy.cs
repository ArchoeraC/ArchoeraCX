using System.IO.Compression;
using System.Text.Json;
using Archoera.Application.Loaders;
using Archoera.Domain.Loaders;

namespace Archoera.Infrastructure.Loaders;

/// <summary>
/// Pre-1.7 installer half of <see cref="ForgeArtifactPlanner"/>: those
/// installers have no <c>version.json</c> and keep the universal jar at the
/// archive root instead of under <c>maven/</c>.
/// </summary>
public sealed partial class ForgeArtifactPlanner
{
    /// <summary>
    /// The loader version JSON. Modern installers carry a separate
    /// <c>version.json</c>; pre-1.7 installers only embed it as
    /// <c>install_profile.json.versionInfo</c>.
    /// </summary>
    private static JsonDocument ReadVersionDocument(ZipArchive archive, JsonElement profile)
    {
        if (FindEntry(archive, "version.json") is not null)
        {
            return ReadJsonEntry(archive, "version.json");
        }

        if (profile.ValueKind == JsonValueKind.Object &&
            profile.TryGetProperty("versionInfo", out var versionInfo) &&
            versionInfo.ValueKind == JsonValueKind.Object)
        {
            return JsonDocument.Parse(versionInfo.GetRawText());
        }

        throw new LoaderManifestException(
            "E_INSTALLER_ENTRY_MISSING",
            "installer does not contain version.json or install_profile.json.versionInfo");
    }
}

internal sealed partial class PlanBuilder
{
    /// <summary>
    /// Pre-1.7 installers keep the universal jar at the archive root and name
    /// it from <c>install.filePath</c>. Extract it to the maven path from
    /// <c>install.path</c> so the merged profile finds it where the launcher
    /// looks for the coordinate.
    /// </summary>
    private void AddLegacyUniversal(JsonElement profile)
    {
        if (profile.ValueKind != JsonValueKind.Object ||
            !profile.TryGetProperty("install", out var install) ||
            install.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        var filePath = ReadString(install, "filePath");
        var coordinateText = ReadString(install, "path");
        if (string.IsNullOrWhiteSpace(filePath) ||
            string.IsNullOrWhiteSpace(coordinateText) ||
            !MavenPath.TryParse(coordinateText, out var coordinate))
        {
            return;
        }

        if (ForgeArtifactPlanner.FindEntry(_archive, filePath) is null)
        {
            return;
        }

        var (sha1, size) = GetEntryDigest(filePath);
        AddArtifact(new LoaderArtifact(
            $"installer:{filePath}",
            coordinate.RelativePath,
            sha1,
            size,
            ProducerIndex: -1,
            RequiredForLaunch: true,
            Kind: LoaderArtifactKind.Extracted,
            Location: LoaderArtifactLocation.Libraries));
    }
}
