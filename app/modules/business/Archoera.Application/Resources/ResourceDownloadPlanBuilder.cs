using Archoera.Domain.Downloads;
using Archoera.Domain.Resources;

namespace Archoera.Application.Resources;

/// <summary>
/// Turns one selected provider file into a single-file <see cref="DownloadPlan"/>.
/// The caller owns root selection and version/file selection: this class never
/// picks "latest" and never falls back to a mismatched build.
/// </summary>
public static class ResourceDownloadPlanBuilder
{
    /// <summary>Instance-relative install folders.</summary>
    public static class Folders
    {
        public const string Mods = "mods";
        public const string ShaderPacks = "shaderpacks";
        public const string ResourcePacks = "resourcepacks";
    }

    /// <summary>
    /// Builds a one-file plan with <c>mode=ensure</c>, no mirrors and the
    /// provider hash (SHA-512 preferred, else SHA-1).
    /// </summary>
    /// <param name="root">Absolute instance root supplied by the caller.</param>
    /// <param name="kind">Project kind; decides the install folder and compatibility rule.</param>
    /// <param name="target">Exact game version plus loader/shader loader.</param>
    /// <param name="version">Selected provider version.</param>
    /// <param name="file">Selected file; defaults to the version's primary file.</param>
    /// <exception cref="ResourceProviderException">
    /// Incompatible version (<c>NO_COMPATIBLE_BUILD</c>) or a file without a URL
    /// (<c>DISTRIBUTION_DENIED</c>).
    /// </exception>
    /// <exception cref="DownloadPlanException">Plan shape/hash/path rejection.</exception>
    public static DownloadPlan Build(
        string root,
        ResourceKind kind,
        ResourceTarget target,
        ResourceVersion version,
        ResourceFile? file = null)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(version);

        if (!ResourceCompatibility.IsCompatible(version, kind, target))
        {
            throw new ResourceProviderException(
                ResourceErrorCode.NoCompatibleBuild,
                $"version {version.Id} is not compatible with " +
                $"{kind.ToWire()}/{target.GameVersion}");
        }

        var selected = file ?? version.PrimaryFile;
        if (selected is null)
        {
            throw new ResourceProviderException(
                ResourceErrorCode.DistributionDenied,
                $"version {version.Id} has no files");
        }

        if (!selected.HasUrl)
        {
            throw new ResourceProviderException(
                ResourceErrorCode.DistributionDenied,
                $"version {version.Id} file '{selected.FileName}' has no download URL");
        }

        if (!DownloadPlanPaths.IsPlainFileName(selected.FileName))
        {
            throw new ResourceProviderException(
                ResourceErrorCode.InvalidResponse,
                $"version {version.Id} returned an unsafe file name");
        }

        var hash = selected.PreferredHash;
        if (hash is null)
        {
            throw new DownloadPlanException(
                DownloadPlanErrorCode.InvalidHash,
                $"{selected.FileName}: provider supplied neither sha512 nor sha1");
        }

        if (selected.Size < 0)
        {
            throw new ResourceProviderException(
                ResourceErrorCode.InvalidResponse,
                $"version {version.Id} returned a negative file size");
        }

        var dest = $"{FolderFor(kind)}/{selected.FileName}";

        return new DownloadPlanBuilder(root)
            .AddFile(
                selected.Url,
                dest,
                hash,
                selected.Size,
                mode: DownloadFileMode.Ensure)
            .Build();
    }

    /// <summary>
    /// Instance-relative folder for a kind. Minecraft stores resource packs in
    /// <c>resourcepacks/</c>; the brief's <c>resources/</c> example is mapped to
    /// the real game directory.
    /// </summary>
    public static string FolderFor(ResourceKind kind) => kind switch
    {
        ResourceKind.Mod => Folders.Mods,
        ResourceKind.Shader => Folders.ShaderPacks,
        ResourceKind.ResourcePack => Folders.ResourcePacks,
        ResourceKind.Modpack => throw new ResourceProviderException(
            ResourceErrorCode.InvalidRequest,
            "modpacks are installed through modpack.install, not a single-file plan"),
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    // NEXT STEP (dependency resolution, docs/08 §3.3): recursively resolve
    // ResourceVersion.DependencyList with depth <= 8, cycle key
    // (provider, projectId), lock the chosen version/file ids, install
    // `required` only and report `optional` as a suggestion. Deliberately not
    // implemented in this slice.
}
