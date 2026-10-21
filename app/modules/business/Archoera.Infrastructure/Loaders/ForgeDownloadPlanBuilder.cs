using System.IO.Compression;
using System.Text.Json;
using Archoera.Application.Loaders;
using Archoera.Domain.Downloads;
using Archoera.Domain.Loaders;

namespace Archoera.Infrastructure.Loaders;

/// <summary>
/// Produces the remote-input <see cref="DownloadPlan"/> for a Forge/NeoForge
/// install: the installer jar plus every Maven library the installer profile
/// and the loader version JSON declare with a non-empty
/// <c>downloads.artifact.url</c>.
/// </summary>
/// <remarks>
/// <para>
/// Generated artifacts (<c>PATCHED</c>, <c>MC_SLIM</c>, <c>MC_EXTRA</c>,
/// <c>MC_SRG</c>, <c>MC_OFF</c>, mappings) and installer-embedded files
/// (<c>data/*.lzma</c>, <c>maven/*</c>) are never emitted: they are produced or
/// extracted locally by the loader transaction (docs/08 09 §3, docs/10 §4.3).
/// Empty or absent URLs therefore become warnings, not downloads.
/// </para>
/// <para>
/// Every file has a hash and a size. A remote library without them fails with
/// <c>MISSING_HASH</c>/<c>MISSING_SIZE</c>; identical duplicate destinations are
/// collapsed, while a hash/size conflict fails with <c>DUPLICATE_DEST</c>.
/// </para>
/// <para>
/// This type performs no network I/O, never runs Java and never writes a file.
/// The caller owns the <see cref="ZipArchive"/>.
/// </para>
/// </remarks>
public static class ForgeDownloadPlanBuilder
{
    /// <summary>Relative prefix for downloaded libraries.</summary>
    public const string LibrariesPrefix = "libraries/";

    /// <summary>
    /// Builds a plan from a downloaded-or-opened installer.
    /// </summary>
    /// <param name="root">Absolute instance root; all destinations are relative to it.</param>
    /// <param name="kind">Forge or NeoForge (the installer profile dialect).</param>
    /// <param name="gameVersion">Minecraft version, for example <c>1.20.1</c>.</param>
    /// <param name="loaderVersion">Loader version, for example <c>47.2.0</c>.</param>
    /// <param name="installer">Installer URL/hash/size and optional mirrors.</param>
    /// <param name="archive">
    /// Open installer zip. When null only the installer jar is planned and a
    /// warning explains that the profile was unavailable.
    /// </param>
    /// <param name="options">Destination/client-jar knobs; defaults are safe.</param>
    /// <param name="cancellationToken">Cancels parsing between entries.</param>
    /// <exception cref="ForgeDownloadException">
    /// <c>INVALID_REQUEST</c>, <c>INVALID_METADATA</c>, <c>MISSING_HASH</c>,
    /// <c>MISSING_SIZE</c>, <c>DUPLICATE_DEST</c> or <c>INSTALLER_INVALID</c>.
    /// </exception>
    /// <exception cref="DownloadPlanException">The final plan fails plan validation.</exception>
    public static ForgeDownloadPlanResult Build(
        string root,
        LoaderKind kind,
        string gameVersion,
        string loaderVersion,
        ForgeInstallerDescriptor installer,
        ZipArchive? archive,
        ForgeDownloadPlanOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(installer);
        cancellationToken.ThrowIfCancellationRequested();

        if (DownloadPlanPaths.ValidateRoot(root) is { } rootIssue)
        {
            throw new ForgeDownloadException(
                ForgeDownloadErrorCode.InvalidRequest,
                rootIssue.Message);
        }

        // Validates the loader kind and both version segments even when the
        // caller supplies a custom installer destination.
        _ = ForgeMavenUrls.InstallerPath(kind, gameVersion, loaderVersion);

        var resolvedOptions = options ?? ForgeDownloadPlanOptions.Default;
        var installerSha1 = LoaderPaths.NormalizeSha1(installer.Sha1)
                            ?? throw new ForgeDownloadException(
                                ForgeDownloadErrorCode.MissingHash,
                                "installer: sha1 is missing or is not a 40-character hex digest");

        if (installer.Size is null)
        {
            throw new ForgeDownloadException(
                ForgeDownloadErrorCode.MissingSize,
                "installer: size is missing; the executor requires it");
        }

        if (installer.Size < 0)
        {
            throw new ForgeDownloadException(
                ForgeDownloadErrorCode.InvalidMetadata,
                $"installer: negative size {installer.Size}");
        }

        if (string.IsNullOrWhiteSpace(installer.Url))
        {
            throw new ForgeDownloadException(
                ForgeDownloadErrorCode.InvalidMetadata,
                "installer: download URL is missing");
        }

        var installerDest = resolvedOptions.InstallerRelativePath
                            ?? ForgeMavenUrls.DefaultInstallerRelativePath(
                                kind,
                                gameVersion,
                                loaderVersion);
        installerDest = NormalizeDest(installerDest, "installer destination");

        var warnings = new List<ForgeDownloadPlanWarning>();
        var planBuilder = new DownloadPlanBuilder(root);
        var destinations = new Dictionary<string, (DownloadHash Hash, long Size, string What)>(
            DestinationComparer);

        void AddFile(
            string url,
            string dest,
            DownloadHash hash,
            long size,
            IReadOnlyList<string>? mirrors,
            string what)
        {
            if (destinations.TryGetValue(dest, out var existing))
            {
                var sameHash = existing.Hash.Algorithm == hash.Algorithm
                               && string.Equals(existing.Hash.Hex, hash.Hex, StringComparison.Ordinal);
                if (!sameHash || existing.Size != size)
                {
                    throw new ForgeDownloadException(
                        ForgeDownloadErrorCode.DuplicateDest,
                        $"{dest}: '{existing.What}' and '{what}' disagree on hash/size");
                }

                // Byte-identical duplicate (typically install_profile vs
                // version.json): keep the first URL, do not emit it twice.
                return;
            }

            destinations[dest] = (hash, size, what);
            var sources = ResolveSources(url, mirrors, resolvedOptions.MavenMirror);
            planBuilder.AddFile(
                sources.Primary,
                dest,
                hash,
                size,
                sources.Mirrors);
        }

        AddFile(
            installer.Url,
            installerDest,
            DownloadHash.Create(DownloadHashAlgorithm.Sha1, installerSha1),
            installer.Size.Value,
            installer.Mirrors,
            "installer");

        if (archive is null)
        {
            warnings.Add(new ForgeDownloadPlanWarning(
                ForgeDownloadWarningCode.InstallerMetadataUnavailable,
                "installer metadata was not provided; the plan contains the installer jar only"));
            return new ForgeDownloadPlanResult(planBuilder.Build(), warnings);
        }

        using var profile = ReadJsonEntry(archive, "install_profile.json");
        using var version = ReadJsonEntry(archive, "version.json");

        AddLibraries(
            profile.RootElement,
            "install_profile.json",
            AddFile,
            warnings,
            cancellationToken);

        AddLibraries(
            version.RootElement,
            "version.json",
            AddFile,
            warnings,
            cancellationToken);

        if (resolvedOptions.IncludeClientJar)
        {
            AddVersionClient(version.RootElement, gameVersion, kind, loaderVersion, AddFile, warnings);
        }

        return new ForgeDownloadPlanResult(planBuilder.Build(), warnings);
    }

    private static (string Primary, IReadOnlyList<string> Mirrors) ResolveSources(
        string url,
        IReadOnlyList<string>? mirrors,
        Uri? mavenMirror)
    {
        if (mavenMirror is not null &&
            ForgeMavenMirror.TryRewrite(url, mavenMirror, out var primary, out var fallback))
        {
            var combined = new List<string>(fallback);
            if (mirrors is not null)
            {
                foreach (var mirror in mirrors)
                {
                    if (!string.IsNullOrWhiteSpace(mirror) &&
                        !combined.Contains(mirror, StringComparer.Ordinal))
                    {
                        combined.Add(mirror);
                    }
                }
            }

            return (primary, combined);
        }

        return (url, mirrors ?? []);
    }

    private static void AddLibraries(
        JsonElement document,
        string source,
        Action<string, string, DownloadHash, long, IReadOnlyList<string>?, string> addFile,
        List<ForgeDownloadPlanWarning> warnings,
        CancellationToken cancellationToken)
    {
        if (!document.TryGetProperty("libraries", out var libraries)
            || libraries.ValueKind == JsonValueKind.Null)
        {
            return;
        }

        if (libraries.ValueKind != JsonValueKind.Array)
        {
            throw new ForgeDownloadException(
                ForgeDownloadErrorCode.InvalidMetadata,
                $"{source}: libraries must be an array");
        }

        var index = 0;
        foreach (var library in libraries.EnumerateArray())
        {
            cancellationToken.ThrowIfCancellationRequested();

            var what = $"{source}.libraries[{index}]";
            index++;

            if (library.ValueKind != JsonValueKind.Object)
            {
                throw new ForgeDownloadException(
                    ForgeDownloadErrorCode.InvalidMetadata,
                    $"{what}: entry must be an object");
            }

            AddLibrary(library, what, addFile, warnings);
        }
    }

    private static void AddLibrary(
        JsonElement library,
        string what,
        Action<string, string, DownloadHash, long, IReadOnlyList<string>?, string> addFile,
        List<ForgeDownloadPlanWarning> warnings)
    {
        var name = ReadString(library, "name");
        var label = string.IsNullOrWhiteSpace(name) ? what : $"{what} '{name}'";

        if (!library.TryGetProperty("downloads", out var downloads)
            || downloads.ValueKind != JsonValueKind.Object
            || !downloads.TryGetProperty("artifact", out var artifact)
            || artifact.ValueKind != JsonValueKind.Object)
        {
            warnings.Add(new ForgeDownloadPlanWarning(
                ForgeDownloadWarningCode.EmbeddedLibrary,
                $"{label}: no downloads.artifact; treated as installer-embedded/generated and not downloaded"));
            return;
        }

        var url = ReadString(artifact, "url");
        if (string.IsNullOrWhiteSpace(url))
        {
            warnings.Add(new ForgeDownloadPlanWarning(
                ForgeDownloadWarningCode.EmptyUrl,
                $"{label}: downloads.artifact.url is empty; treated as a local/generated artifact and not downloaded"));
            return;
        }

        var relativePath = ResolveLibraryPath(name, artifact, label);
        var hash = ResolveHash(artifact, label);
        var size = ResolveSize(artifact, label);

        addFile(url, LibrariesPrefix + relativePath, hash, size, null, label);
    }

    private static void AddVersionClient(
        JsonElement version,
        string gameVersion,
        LoaderKind kind,
        string loaderVersion,
        Action<string, string, DownloadHash, long, IReadOnlyList<string>?, string> addFile,
        List<ForgeDownloadPlanWarning> warnings)
    {
        if (!version.TryGetProperty("downloads", out var downloads)
            || downloads.ValueKind != JsonValueKind.Object
            || !downloads.TryGetProperty("client", out var client)
            || client.ValueKind != JsonValueKind.Object)
        {
            warnings.Add(new ForgeDownloadPlanWarning(
                ForgeDownloadWarningCode.EmbeddedLibrary,
                "version.json has no downloads.client; the vanilla jar belongs to the Mojang plan"));
            return;
        }

        var url = ReadString(client, "url");
        if (string.IsNullOrWhiteSpace(url))
        {
            warnings.Add(new ForgeDownloadPlanWarning(
                ForgeDownloadWarningCode.EmptyUrl,
                "version.json downloads.client.url is empty; not downloading it"));
            return;
        }

        var versionId = ReadString(version, "id");
        if (string.IsNullOrWhiteSpace(versionId)
            || !DownloadPlanPaths.IsPlainFileName(versionId))
        {
            versionId = $"{gameVersion}-{kind.ToString().ToLowerInvariant()}-{loaderVersion}";
        }

        var hash = ResolveHash(client, "version.json downloads.client");
        var size = ResolveSize(client, "version.json downloads.client");
        addFile(
            url,
            $"versions/{versionId}/{versionId}.jar",
            hash,
            size,
            null,
            "vanilla client jar");
    }

    private static string ResolveLibraryPath(string? name, JsonElement artifact, string what)
    {
        var path = ReadString(artifact, "path");
        if (string.IsNullOrWhiteSpace(path))
        {
            if (name is null || !MavenPath.TryParse(name, out var coordinate))
            {
                throw new ForgeDownloadException(
                    ForgeDownloadErrorCode.InvalidMetadata,
                    $"{what}: no downloads.artifact.path and no usable Maven coordinate");
            }

            path = coordinate.RelativePath;
        }

        var normalized = LoaderPaths.NormalizeRelative(path, what);
        if (normalized == ".")
        {
            throw new ForgeDownloadException(
                ForgeDownloadErrorCode.InvalidMetadata,
                $"{what}: library path is empty");
        }

        if (normalized.StartsWith(LibrariesPrefix, StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized[LibrariesPrefix.Length..];
        }

        return normalized;
    }

    private static DownloadHash ResolveHash(JsonElement artifact, string what)
    {
        var sha256 = ReadString(artifact, "sha256");
        if (!string.IsNullOrWhiteSpace(sha256))
        {
            return VerifiedHash(DownloadHashAlgorithm.Sha256, sha256, what);
        }

        var sha1 = ReadString(artifact, "sha1");
        if (!string.IsNullOrWhiteSpace(sha1))
        {
            return VerifiedHash(DownloadHashAlgorithm.Sha1, sha1, what);
        }

        throw new ForgeDownloadException(
            ForgeDownloadErrorCode.MissingHash,
            $"{what}: no sha1/sha256; refusing to emit an unverifiable download");
    }

    private static DownloadHash VerifiedHash(
        DownloadHashAlgorithm algorithm,
        string raw,
        string what)
    {
        var hash = DownloadHash.Create(algorithm, raw);
        if (!hash.IsCanonicalHex())
        {
            throw new ForgeDownloadException(
                ForgeDownloadErrorCode.MissingHash,
                $"{what}: {hash.Algo} must be {hash.ExpectedHexLength} hex characters");
        }

        return hash;
    }

    private static long ResolveSize(JsonElement artifact, string what)
    {
        if (!artifact.TryGetProperty("size", out var element)
            || element.ValueKind != JsonValueKind.Number
            || !element.TryGetInt64(out var size))
        {
            throw new ForgeDownloadException(
                ForgeDownloadErrorCode.MissingSize,
                $"{what}: size is missing; the executor requires it");
        }

        if (size < 0)
        {
            throw new ForgeDownloadException(
                ForgeDownloadErrorCode.InvalidMetadata,
                $"{what}: negative size {size}");
        }

        return size;
    }

    private static string NormalizeDest(string dest, string what)
    {
        var issue = DownloadPlanPaths.TryNormalizeDest(dest, out var normalized);
        if (issue is not null)
        {
            throw new ForgeDownloadException(
                ForgeDownloadErrorCode.InvalidMetadata,
                $"{what}: {issue.Message}");
        }

        if (!string.Equals(normalized, dest, StringComparison.Ordinal))
        {
            throw new ForgeDownloadException(
                ForgeDownloadErrorCode.InvalidMetadata,
                $"{what}: '{dest}' is not normalized ('{normalized}')");
        }

        return normalized;
    }

    private static JsonDocument ReadJsonEntry(ZipArchive archive, string name)
    {
        var entry = ForgeArtifactPlanner.FindEntry(archive, name)
                    ?? throw new ForgeDownloadException(
                        ForgeDownloadErrorCode.InstallerInvalid,
                        $"installer does not contain {name}");

        using var stream = entry.Open();
        using var memory = new MemoryStream(checked((int)Math.Max(0, entry.Length)));
        stream.CopyTo(memory);
        var bytes = memory.ToArray();

        var offset = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF
            ? 3
            : 0;

        try
        {
            return JsonDocument.Parse(
                new ReadOnlyMemory<byte>(bytes, offset, bytes.Length - offset));
        }
        catch (JsonException exception)
        {
            throw new ForgeDownloadException(
                ForgeDownloadErrorCode.InstallerInvalid,
                $"{name} is not valid JSON: {exception.Message}",
                innerException: exception);
        }
    }

    private static string? ReadString(JsonElement element, string name)
        => element.ValueKind == JsonValueKind.Object
           && element.TryGetProperty(name, out var value)
           && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static StringComparer DestinationComparer =>
        OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;
}
