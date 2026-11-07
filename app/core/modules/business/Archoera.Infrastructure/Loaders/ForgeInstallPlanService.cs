using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
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
    private readonly ILegacyForgeLibraryResolver _legacyResolver;

    public ForgeInstallPlanService(
        IMojangMetadataClient mojang,
        LibraryArtifactResolver? libraryResolver = null,
        HttpClient? http = null,
        ILegacyForgeLibraryResolver? legacyResolver = null)
    {
        ArgumentNullException.ThrowIfNull(mojang);
        _mojang = mojang;
        _vanillaPlan = new MojangPlanService(mojang, libraryResolver);
        _legacyResolver = legacyResolver ?? new LegacyForgeLibraryResolver(http);
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
        var (loaderJson, legacy) = await ReadLoaderJsonAsync(installerFull, cancellationToken)
            .ConfigureAwait(false);
        string? universalCoordinate = null;
        if (legacy)
        {
            // Pre-1.7 profiles repeat the vanilla libraries. Keeping the
            // loader copy would replace the Mojang entries (and their native
            // classifiers) during the merge, so drop the duplicates first.
            universalCoordinate = ReadLegacyUniversalCoordinate(installerFull);
            loaderJson = FilterVanillaLibraries(loaderJson, vanillaJson);
        }

        var loaderId = ReadVersionId(loaderJson) ?? $"{gameVersion}-{kind}-{loaderVersion}";
        var effective = _merge.Merge(
        [
            new VersionLayer(gameVersion, Encoding.UTF8.GetString(vanillaJson), "mojang"),
            new VersionLayer(loaderId, loaderJson, "loader"),
        ]);

        var vanillaPlan = legacy
            ? await _vanillaPlan
                .BuildFromEffectiveAsync(
                    root,
                    // The raw 1.5.2 metadata repeats jinput-platform; merging
                    // one layer collapses duplicate coordinates the same way
                    // the real install chain does.
                    _merge.Merge(
                    [
                        new VersionLayer(
                            gameVersion,
                            Encoding.UTF8.GetString(vanillaJson),
                            "mojang"),
                    ]),
                    platform,
                    sourceMode,
                    cancellationToken,
                    clientMirrorId: gameVersion)
                .ConfigureAwait(false)
            : await _vanillaPlan
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
                cancellationToken,
                versionJsonOverride: legacy ? loaderJson : null);
        }

        var plans = new List<DownloadPlan> { vanillaPlan, forge.Plan };
        if (legacy)
        {
            var legacyPlan = await BuildLegacyLibraryPlanAsync(
                    root,
                    loaderJson,
                    universalCoordinate,
                    sourceMode,
                    cancellationToken)
                .ConfigureAwait(false);
            if (legacyPlan is not null)
            {
                plans.Add(legacyPlan);
            }
        }

        var combined = DownloadPlanCombiner.Combine(plans);
        return new ForgeInstallPlan(combined, effective, forge);
    }

    /// <summary>
    /// Reads the loader version JSON. Modern installers carry a separate
    /// <c>version.json</c>; pre-1.7 installers only embed it as
    /// <c>install_profile.json.versionInfo</c>.
    /// </summary>
    private static async Task<(string Json, bool Legacy)> ReadLoaderJsonAsync(
        string archivePath,
        CancellationToken cancellationToken)
    {
        using var archive = ZipFile.OpenRead(archivePath);
        var version = FindEntry(archive, "version.json");
        if (version is not null)
        {
            return (await ReadEntryTextAsync(version, cancellationToken).ConfigureAwait(false), false);
        }

        var profile = FindEntry(archive, "install_profile.json")
                      ?? throw new ForgeDownloadException(
                          ForgeDownloadErrorCode.InstallerInvalid,
                          "installer does not contain install_profile.json");
        var profileText = await ReadEntryTextAsync(profile, cancellationToken).ConfigureAwait(false);
        using var document = JsonDocument.Parse(profileText);
        if (!document.RootElement.TryGetProperty("versionInfo", out var versionInfo) ||
            versionInfo.ValueKind != JsonValueKind.Object)
        {
            throw new ForgeDownloadException(
                ForgeDownloadErrorCode.InstallerInvalid,
                "installer does not contain version.json or install_profile.json.versionInfo");
        }

        return (versionInfo.GetRawText(), true);
    }

    private static string? ReadLegacyUniversalCoordinate(string archivePath)
    {
        using var archive = ZipFile.OpenRead(archivePath);
        var profile = FindEntry(archive, "install_profile.json");
        if (profile is null)
        {
            return null;
        }

        using var stream = profile.Open();
        using var document = JsonDocument.Parse(stream);
        return document.RootElement.TryGetProperty("install", out var install) &&
               install.ValueKind == JsonValueKind.Object &&
               install.TryGetProperty("path", out var path) &&
               path.ValueKind == JsonValueKind.String
            ? path.GetString()
            : null;
    }

    /// <summary>
    /// Drops loader libraries that the vanilla layer already provides. The
    /// legacy profile repeats them without <c>downloads</c>, which would
    /// otherwise overwrite the Mojang entries during the merge.
    /// </summary>
    private static string FilterVanillaLibraries(string loaderJson, byte[] vanillaJson)
    {
        var loader = JsonNode.Parse(loaderJson) as JsonObject
                     ?? throw new ForgeDownloadException(
                         ForgeDownloadErrorCode.InstallerInvalid,
                         "loader version JSON is not an object");
        var vanilla = JsonNode.Parse(vanillaJson);

        var vanillaCoordinates = new HashSet<string>(StringComparer.Ordinal);
        if (vanilla?["libraries"] is JsonArray vanillaLibraries)
        {
            foreach (var library in vanillaLibraries)
            {
                if (library?["name"]?.GetValue<string>() is { } name &&
                    CoordinateKey(name) is { } key)
                {
                    vanillaCoordinates.Add(key);
                }
            }
        }

        if (loader["libraries"] is JsonArray libraries)
        {
            for (var index = libraries.Count - 1; index >= 0; index--)
            {
                if (libraries[index]?["name"]?.GetValue<string>() is { } name &&
                    CoordinateKey(name) is { } key &&
                    vanillaCoordinates.Contains(key))
                {
                    libraries.RemoveAt(index);
                }
            }
        }

        return loader.ToJsonString();
    }

    private static string? CoordinateKey(string name)
    {
        var parts = name.Split(':');
        return parts.Length >= 3 ? $"{parts[0]}:{parts[1]}:{parts[2]}" : null;
    }

    /// <summary>
    /// Downloads the loader-only libraries of a pre-1.7 profile. The universal
    /// jar is extracted from the installer by repair, so it is skipped here.
    /// </summary>
    private async Task<DownloadPlan?> BuildLegacyLibraryPlanAsync(
        string root,
        string loaderJson,
        string? universalCoordinate,
        DownloadSourceMode sourceMode,
        CancellationToken cancellationToken)
    {
        using var document = JsonDocument.Parse(loaderJson);
        if (!document.RootElement.TryGetProperty("libraries", out var libraries) ||
            libraries.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var universalKey = universalCoordinate is null ? null : CoordinateKey(universalCoordinate);
        var builder = new DownloadPlanBuilder(root);
        var count = 0;
        foreach (var library in libraries.EnumerateArray())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var name = ReadString(library, "name");
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            var key = CoordinateKey(name);
            if (universalKey is not null &&
                string.Equals(key, universalKey, StringComparison.Ordinal))
            {
                continue;
            }

            if (library.TryGetProperty("clientreq", out var clientreq) &&
                clientreq.ValueKind == JsonValueKind.False)
            {
                continue;
            }

            if (!MavenPath.TryParse(name, out var coordinate))
            {
                throw new ForgeDownloadException(
                    ForgeDownloadErrorCode.InvalidMetadata,
                    $"legacy library coordinate '{name}' is not maven-shaped");
            }

            var checksums = new List<string>();
            if (library.TryGetProperty("checksums", out var checks) &&
                checks.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in checks.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.String &&
                        item.GetString() is { } value)
                    {
                        checksums.Add(value);
                    }
                }
            }

            var resolved = await _legacyResolver
                .ResolveAsync(
                    name,
                    ReadString(library, "url"),
                    checksums,
                    sourceMode,
                    cancellationToken)
                .ConfigureAwait(false);
            builder.AddFile(
                resolved.Url,
                ForgeDownloadPlanBuilder.LibrariesPrefix + coordinate.RelativePath,
                resolved.Hash,
                resolved.Size,
                resolved.Mirrors);
            count++;
        }

        return count == 0 ? null : builder.Build();
    }

    private static ZipArchiveEntry? FindEntry(ZipArchive archive, string name)
    {
        foreach (var entry in archive.Entries)
        {
            if (string.Equals(entry.FullName, name, StringComparison.OrdinalIgnoreCase))
            {
                return entry;
            }
        }

        return null;
    }

    private static async Task<string> ReadEntryTextAsync(
        ZipArchiveEntry entry,
        CancellationToken cancellationToken)
    {
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

    private static string? ReadString(JsonElement element, string name)
        => element.ValueKind == JsonValueKind.Object &&
           element.TryGetProperty(name, out var value) &&
           value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

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
