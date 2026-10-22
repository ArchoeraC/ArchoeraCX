using Archoera.Application.Downloads;
using Archoera.Domain.Downloads;
using Archoera.Domain.Mojang;

namespace Archoera.Application.Mojang;

/// <summary>
/// Produces the vanilla <see cref="DownloadPlan"/> for one fully resolved
/// version JSON. It never downloads files, never runs Java and never merges an
/// <c>inheritsFrom</c> chain.
/// </summary>
/// <remarks>
/// <para>
/// Paths follow the official launcher layout:
/// <c>versions/&lt;id&gt;/&lt;id&gt;.jar</c>, <c>libraries/&lt;maven path&gt;</c>,
/// <c>assets/indexes/&lt;id&gt;.json</c>,
/// <c>assets/objects/&lt;xx&gt;/&lt;hash&gt;</c> and <c>logging/&lt;file&gt;</c>.
/// </para>
/// <para>
/// Every emitted entry has a SHA-1 and a size. A required file with missing
/// metadata fails with <c>MISSING_HASH</c>/<c>MISSING_SIZE</c> instead of
/// producing a plan the Rust executor must reject.
/// </para>
/// </summary>
public static class VanillaDownloadPlanBuilder
{
    /// <summary>Official asset object host (path is <c>&lt;xx&gt;/&lt;sha1&gt;</c>).</summary>
    public const string ResourcesBase = "https://resources.download.minecraft.net/";

    /// <summary>Community mirror used only after the official URL fails.</summary>
    public const string BmclApiBase = "https://bmclapi2.bangbang93.com/";

    private const string LibrariesPrefix = "libraries/";
    private const string AssetsPrefix = "assets/";

    /// <summary>
    /// Builds a vanilla plan.
    /// </summary>
    /// <param name="root">Absolute instance/shared root.</param>
    /// <param name="version">Fully resolved version JSON (no <c>inheritsFrom</c>).</param>
    /// <param name="assetIndex">
    /// Parsed asset index. When null the plan omits assets entirely, so callers
    /// that want a complete vanilla install must fetch it first.
    /// </param>
    /// <param name="platform">Target OS/arch/features used for rule evaluation.</param>
    /// <param name="assetIndexDownload">
    /// Optional download ref. Defaults to <see cref="MojangVersion.AssetIndex"/>.
    /// </param>
    /// <exception cref="MojangException">
    /// <c>UNSUPPORTED_INHERITS</c>, <c>INVALID_METADATA</c>, <c>MISSING_HASH</c>,
    /// <c>MISSING_SIZE</c>, <c>DUPLICATE_DEST</c> or <c>INVALID_REQUEST</c>.
    /// </exception>
    /// <exception cref="DownloadPlanException">The final plan fails plan validation.</exception>
    public static DownloadPlan Build(
        string root,
        MojangVersion version,
        MojangAssetIndex? assetIndex,
        MojangPlatform platform,
        MojangAssetIndexRef? assetIndexDownload = null,
        DownloadSourceMode sourceMode = DownloadSourceMode.Auto)
    {
        ArgumentNullException.ThrowIfNull(version);
        ArgumentNullException.ThrowIfNull(platform);

        if (DownloadPlanPaths.ValidateRoot(root) is { } rootIssue)
        {
            throw new MojangException(MojangErrorCode.InvalidRequest, rootIssue.Message);
        }

        if (!string.IsNullOrWhiteSpace(version.InheritsFrom))
        {
            throw new MojangException(
                MojangErrorCode.UnsupportedInherits,
                $"version '{version.Id}' inherits from '{version.InheritsFrom}'; " +
                "merge the chain first (docs/08 01 §3)");
        }

        if (string.IsNullOrWhiteSpace(version.Id)
            || !DownloadPlanPaths.IsPlainFileName(version.Id))
        {
            throw InvalidMetadata("version id is missing or not a plain file name");
        }

        var builder = new DownloadPlanBuilder(root);
        var destinations = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void AddFile(string? url, string dest, string? sha1, long? size, string what)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                throw InvalidMetadata($"{what}: download URL is missing");
            }

            if (string.IsNullOrWhiteSpace(sha1))
            {
                throw new MojangException(
                    MojangErrorCode.MissingHash,
                    $"{what}: sha1 is missing; refusing to emit an unverifiable download");
            }

            if (size is null)
            {
                throw new MojangException(
                    MojangErrorCode.MissingSize,
                    $"{what}: size is missing; the executor requires it");
            }

            if (size < 0)
            {
                throw InvalidMetadata($"{what}: negative size {size}");
            }

            var destIssue = DownloadPlanPaths.TryNormalizeDest(dest, out var normalized);
            if (destIssue is not null)
            {
                throw InvalidMetadata($"{what}: {destIssue.Message}");
            }

            if (!string.Equals(normalized, dest, StringComparison.Ordinal))
            {
                throw InvalidMetadata($"{what}: destination is not normalized");
            }

            if (!destinations.Add(normalized))
            {
                throw new MojangException(
                    MojangErrorCode.DuplicateDest,
                    $"{what}: duplicate destination '{dest}'");
            }

            var (primary, mirrors) = ResolveSource(version, dest, url, sourceMode);
            builder.AddFile(
                primary,
                dest,
                DownloadHashAlgorithm.Sha1,
                sha1,
                size.Value,
                mirrors: mirrors);
        }

        static (string Primary, IReadOnlyList<string>? Mirrors) ResolveSource(
            MojangVersion version,
            string dest,
            string officialUrl,
            DownloadSourceMode sourceMode)
        {
            var mirror = MirrorFor(version, dest, officialUrl);
            if (mirror is null)
            {
                return (officialUrl, null);
            }

            return sourceMode switch
            {
                // Mirror first, official as the fallback.
                DownloadSourceMode.Mirror => (mirror, [officialUrl]),
                // Explicit "official" and "auto" both put the official host
                // first; the mirror stays available as a fallback.
                _ => (officialUrl, [mirror]),
            };
        }

        static string? MirrorFor(
            MojangVersion version,
            string dest,
            string officialUrl)
        {
            if (officialUrl.StartsWith(BmclApiBase, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            if (string.Equals(
                    dest,
                    $"versions/{version.Id}/{version.Id}.jar",
                    StringComparison.OrdinalIgnoreCase))
            {
                return $"{BmclApiBase}version/{Uri.EscapeDataString(version.Id)}/client";
            }

            if (dest.StartsWith(LibrariesPrefix, StringComparison.OrdinalIgnoreCase))
            {
                return $"{BmclApiBase}libraries/{dest[LibrariesPrefix.Length..]}";
            }

            if (dest.StartsWith(AssetsPrefix + "objects/", StringComparison.OrdinalIgnoreCase))
            {
                return $"{BmclApiBase}assets/{dest[(AssetsPrefix + "objects/").Length..]}";
            }

            return null;
        }

        // Client jar is mandatory for a client install.
        var client = version.Downloads?.Client;
        if (client is null)
        {
            throw InvalidMetadata($"version '{version.Id}' has no downloads.client");
        }

        AddFile(
            client.Url,
            $"versions/{version.Id}/{version.Id}.jar",
            client.Sha1,
            client.Size,
            "client jar");

        foreach (var library in version.Libraries ?? [])
        {
            if (library is null)
            {
                continue;
            }

            if (!MojangRuleEvaluator.IsAllowed(library.Rules, platform))
            {
                continue;
            }

            var artifact = library.Downloads?.Artifact;
            if (artifact is not null && !string.IsNullOrWhiteSpace(artifact.Url))
            {
                var path = ResolveLibraryPath(library, artifact, classifier: null);
                AddFile(
                    artifact.Url,
                    LibrariesPrefix + path,
                    artifact.Sha1,
                    artifact.Size,
                    $"library '{library.Name}'");
            }

            var nativeClassifier = ResolveNativeClassifier(library, platform);
            if (nativeClassifier is null)
            {
                continue;
            }

            if (!TryGetClassifier(library.Downloads?.Classifiers, nativeClassifier, out var native))
            {
                throw InvalidMetadata(
                    $"library '{library.Name}': natives classifier " +
                    $"'{nativeClassifier}' has no downloads.classifiers entry");
            }

            var nativePath = ResolveLibraryPath(library, native, nativeClassifier);
            AddFile(
                native.Url,
                LibrariesPrefix + nativePath,
                native.Sha1,
                native.Size,
                $"native library '{library.Name}' ({nativeClassifier})");

            // NEXT STEP: extraction of the natives archive (respecting
            // extract.exclude and skipping META-INF/) is a C# continuation
            // after the download completes. Rust never unpacks archives.
        }

        if (assetIndex is not null)
        {
            var reference = assetIndexDownload ?? version.AssetIndex;
            if (reference is null)
            {
                throw new MojangException(
                    MojangErrorCode.MissingHash,
                    $"asset index '{assetIndex.Id}': no download metadata " +
                    "(assetIndex/url/sha1/size); pass the version's assetIndex block");
            }

            var indexId = string.IsNullOrWhiteSpace(assetIndex.Id)
                ? reference.Id
                : assetIndex.Id;

            if (!string.IsNullOrWhiteSpace(reference.Id)
                && !string.Equals(reference.Id, indexId, StringComparison.Ordinal))
            {
                throw InvalidMetadata(
                    $"asset index id mismatch: contents '{assetIndex.Id}' vs " +
                    $"download '{reference.Id}'");
            }

            if (string.IsNullOrWhiteSpace(indexId)
                || !DownloadPlanPaths.IsPlainFileName(indexId))
            {
                throw InvalidMetadata("asset index id is missing or unsafe");
            }

            AddFile(
                reference.Url,
                $"{AssetsPrefix}indexes/{indexId}.json",
                reference.Sha1,
                reference.Size,
                "asset index");

            var seenHashes = new HashSet<string>(StringComparer.Ordinal);
            foreach (var (name, assetObject) in assetIndex.Objects)
            {
                if (assetObject is null)
                {
                    throw InvalidMetadata($"asset '{name}': object entry is missing");
                }

                var hash = (assetObject.Hash ?? string.Empty).Trim().ToLowerInvariant();
                if (!IsSha1(hash))
                {
                    throw new MojangException(
                        MojangErrorCode.MissingHash,
                        $"asset '{name}': hash is missing or not a SHA-1");
                }

                if (assetObject.Size is null)
                {
                    throw new MojangException(
                        MojangErrorCode.MissingSize,
                        $"asset '{name}': size is missing; the executor requires it");
                }

                if (assetObject.Size < 0)
                {
                    throw InvalidMetadata($"asset '{name}': negative size {assetObject.Size}");
                }

                // Multiple logical names can point at identical bytes.
                if (!seenHashes.Add(hash))
                {
                    continue;
                }

                var prefix = hash[..2];
                AddFile(
                    $"{ResourcesBase}{prefix}/{hash}",
                    $"{AssetsPrefix}objects/{prefix}/{hash}",
                    hash,
                    assetObject.Size,
                    $"asset '{name}'");
            }

            // NEXT STEP: virtual/map_to_resources materialization is a C# step;
            // the plan only downloads content-addressed objects.
        }

        if (version.Logging?.Client is { } logging)
        {
            var fileName = ResolveLoggingFileName(logging);
            AddFile(
                logging.Url,
                $"logging/{fileName}",
                logging.Sha1,
                logging.Size,
                "logging client config");
        }

        // NEXT STEP: Java runtime plan from Mojang all.json
        // (launchermeta.mojang.com/v1/products/java-runtime/.../all.json).
        // This slice only covers the game metadata plan.
        return builder.Build();
    }

    private static string ResolveLibraryPath(
        MojangLibrary library,
        MojangDownload download,
        string? classifier)
    {
        if (!string.IsNullOrWhiteSpace(download.Path))
        {
            return download.Path;
        }

        if (MojangMavenPath.TryResolve(library.Name, classifier, out var resolved))
        {
            return resolved;
        }

        throw InvalidMetadata(
            $"library '{library.Name}': no downloads path and the maven " +
            "coordinate cannot be resolved");
    }

    private static string? ResolveNativeClassifier(
        MojangLibrary library,
        MojangPlatform platform)
    {
        if (library.Natives is null
            || !TryGetValue(library.Natives, platform.OsName, out var raw)
            || string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        return raw.Replace("${arch}", platform.NativeArchBits, StringComparison.Ordinal);
    }

    private static bool TryGetClassifier(
        IReadOnlyDictionary<string, MojangDownload>? classifiers,
        string name,
        out MojangDownload download)
    {
        download = null!;
        if (classifiers is null)
        {
            return false;
        }

        if (classifiers.TryGetValue(name, out var direct))
        {
            download = direct;
            return true;
        }

        foreach (var (key, value) in classifiers)
        {
            if (string.Equals(key, name, StringComparison.OrdinalIgnoreCase))
            {
                download = value;
                return true;
            }
        }

        return false;
    }

    private static bool TryGetValue(
        IReadOnlyDictionary<string, string> map,
        string key,
        out string value)
    {
        if (map.TryGetValue(key, out var direct))
        {
            value = direct;
            return true;
        }

        foreach (var (candidate, candidateValue) in map)
        {
            if (string.Equals(candidate, key, StringComparison.OrdinalIgnoreCase))
            {
                value = candidateValue;
                return true;
            }
        }

        value = string.Empty;
        return false;
    }

    private static string ResolveLoggingFileName(MojangLoggingFile logging)
    {
        if (DownloadPlanPaths.IsPlainFileName(logging.Id))
        {
            return logging.Id!;
        }

        if (Uri.TryCreate(logging.Url, UriKind.Absolute, out var uri))
        {
            var fromUrl = Uri.UnescapeDataString(Path.GetFileName(uri.AbsolutePath));
            if (DownloadPlanPaths.IsPlainFileName(fromUrl))
            {
                return fromUrl;
            }
        }

        throw InvalidMetadata("logging client file has no safe file name");
    }

    private static bool IsSha1(string? hash)
    {
        if (hash is null || hash.Length != 40)
        {
            return false;
        }

        foreach (var character in hash)
        {
            if (character is not ((>= '0' and <= '9') or (>= 'a' and <= 'f')))
            {
                return false;
            }
        }

        return true;
    }

    private static MojangException InvalidMetadata(string message)
        => new(MojangErrorCode.InvalidMetadata, message);
}
