using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Archoera.Application.Downloads;
using Archoera.Application.Loaders;
using Archoera.Application.Mojang;
using Archoera.Domain.Downloads;
using Archoera.Domain.Loaders;
using Archoera.Domain.Mojang;

namespace Archoera.Infrastructure.Loaders;

/// <summary>
/// One combined install-download plan for a Forge/NeoForge instance.
/// </summary>
/// <param name="Plan">Vanilla + loader dependencies in one executable plan.</param>
/// <param name="Version">Merged effective version (no <c>inheritsFrom</c>).</param>
/// <param name="Forge">Loader-only plan/warnings for diagnostics.</param>
public sealed record ForgeInstallPlan(
    DownloadPlan Plan,
    EffectiveVersion Version,
    ForgeDownloadPlanResult Forge);

/// <summary>
/// Builds the download phase of a Forge/NeoForge install from a local installer:
/// Mojang client/libraries/assets/logging + loader libraries + installer.
/// Processors are intentionally not run here; generated artifacts are created
/// by loader.repair after the download completes.
/// </summary>
public sealed class ForgeInstallPlanService
{
    private readonly IMojangMetadataClient _mojang;
    private readonly MojangPlanService _vanillaPlan;
    private readonly VersionMergeService _merge = new();

    public ForgeInstallPlanService(
        IMojangMetadataClient mojang,
        LibraryArtifactResolver? libraryResolver = null)
    {
        ArgumentNullException.ThrowIfNull(mojang);
        _mojang = mojang;
        _vanillaPlan = new MojangPlanService(mojang, libraryResolver);
    }

    /// <summary>
    /// Builds a combined plan from a local installer. Metadata requests are
    /// limited to the Mojang manifest/version/asset index; the installer is
    /// read locally and is never downloaded by this service.
    /// </summary>
    public async Task<ForgeInstallPlan> BuildAsync(
        string root,
        LoaderKind kind,
        string gameVersion,
        string loaderVersion,
        string installerPath,
        MojangPlatform platform,
        CancellationToken cancellationToken = default,
        ForgeDownloadPlanOptions? options = null,
        DownloadSourceMode sourceMode = DownloadSourceMode.Auto)
    {
        ArgumentNullException.ThrowIfNull(platform);
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        ArgumentException.ThrowIfNullOrWhiteSpace(installerPath);

        var installerFull = Path.GetFullPath(installerPath);
        if (!File.Exists(installerFull))
        {
            throw new ForgeDownloadException(
                ForgeDownloadErrorCode.InstallerMissing,
                $"installer jar not found: {installerFull}");
        }

        var vanillaJson = await _mojang
            .GetVersionJsonAsync(gameVersion, cancellationToken)
            .ConfigureAwait(false);
        var loaderJson = await ReadEntryAsync(
            installerFull,
            "version.json",
            cancellationToken).ConfigureAwait(false);

        var loaderId = ReadVersionId(loaderJson) ?? $"{gameVersion}-{kind}-{loaderVersion}";
        var effective = _merge.Merge(
        [
            new VersionLayer(gameVersion, Encoding.UTF8.GetString(vanillaJson), "mojang"),
            new VersionLayer(loaderId, loaderJson, "loader"),
        ]);

        var vanillaPlan = await _vanillaPlan
            .BuildFromEffectiveAsync(
                root,
                effective,
                platform,
                sourceMode,
                cancellationToken,
                clientMirrorId: gameVersion)
            .ConfigureAwait(false);

        var sha1 = await LoaderPaths
            .Sha1FileAsync(installerFull, cancellationToken)
            .ConfigureAwait(false);
        var installerUri = ForgeMavenUrls.Installer(kind, gameVersion, loaderVersion);
        var descriptor = new ForgeInstallerDescriptor(
            installerUri.AbsoluteUri,
            sha1,
            new FileInfo(installerFull).Length);

        ForgeDownloadPlanResult forge;
        using (var archive = ZipFile.OpenRead(installerFull))
        {
            forge = ForgeDownloadPlanBuilder.Build(
                root,
                kind,
                gameVersion,
                loaderVersion,
                descriptor,
                archive,
                options,
                cancellationToken);
        }

        var combined = DownloadPlanCombiner.Combine([vanillaPlan, forge.Plan]);
        return new ForgeInstallPlan(combined, effective, forge);
    }

    private static async Task<string> ReadEntryAsync(
        string archivePath,
        string entryName,
        CancellationToken cancellationToken)
    {
        using var archive = ZipFile.OpenRead(archivePath);
        var entry = archive.Entries.FirstOrDefault(
            candidate => string.Equals(
                candidate.FullName,
                entryName,
                StringComparison.OrdinalIgnoreCase))
            ?? throw new ForgeDownloadException(
                ForgeDownloadErrorCode.InstallerInvalid,
                $"installer does not contain {entryName}");

        await using var stream = entry.Open();
        using var memory = new MemoryStream();
        await stream.CopyToAsync(memory, cancellationToken).ConfigureAwait(false);
        var bytes = memory.ToArray();
        var offset = bytes.Length >= 3 &&
                     bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF
            ? 3
            : 0;
        return Encoding.UTF8.GetString(bytes, offset, bytes.Length - offset);
    }

    private static string? ReadVersionId(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.ValueKind == JsonValueKind.Object &&
                   document.RootElement.TryGetProperty("id", out var id) &&
                   id.ValueKind == JsonValueKind.String &&
                   !string.IsNullOrWhiteSpace(id.GetString())
                ? id.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
