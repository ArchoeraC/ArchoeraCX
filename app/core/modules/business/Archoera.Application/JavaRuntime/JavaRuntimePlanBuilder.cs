using Archoera.Domain.Downloads;
using Archoera.Domain.Java;
using Archoera.Domain.Mojang;

namespace Archoera.Application.JavaRuntime;

/// <summary>
/// Produces the <see cref="DownloadPlan"/> for one Mojang Java runtime.
/// </summary>
/// <remarks>
/// <para>
/// Destinations are <c>runtime/java-&lt;major&gt;/&lt;manifest path&gt;</c>. Only
/// <c>type=file</c> entries with <c>downloads.raw</c> are planned; directory and
/// link entries are skipped. The <c>executable</c> flag is copied to
/// <see cref="PlanFile.Executable"/>.
/// </para>
/// <para>
/// <b>LZMA-only limitation:</b> this slice does not decompress LZMA. A required
/// file that only publishes <c>downloads.lzma</c> fails with
/// <c>LZMA_ONLY_UNSUPPORTED</c> instead of silently downloading compressed
/// bytes into an uncompressed destination. The C# continuation for LZMA
/// unpacking is intentionally out of scope; when it lands, this branch is the
/// single place that must change.
/// </para>
/// <para>
/// Every emitted file carries the raw SHA-1 and size. Missing metadata fails
/// with <c>MISSING_HASH</c>/<c>MISSING_SIZE</c>. Unsafe paths (absolute,
/// <c>..</c>, backslashes, Windows reserved names, sidecar names) are rejected
/// through the same rules the Rust executor applies.
/// </para>
/// </remarks>
public static class JavaRuntimePlanBuilder
{
    /// <summary>Prefix below <c>root</c> shared with the managed-runtime locator.</summary>
    /// <param name="majorVersion">Java feature release, for example <c>21</c>.</param>
    public static string DestPrefix(int majorVersion)
        => $"runtime/java-{majorVersion}/";

    /// <summary>
    /// Builds a runtime plan. The catalog supplies platform/component selection;
    /// <paramref name="manifest"/> is the already verified document for that
    /// selection.
    /// </summary>
    /// <param name="root">Absolute shared/data root.</param>
    /// <param name="majorVersion">Java feature release used for selection and the dest prefix.</param>
    /// <param name="catalog">Parsed <c>all.json</c>.</param>
    /// <param name="manifest">Verified runtime manifest for the selected entry.</param>
    /// <param name="platform">Target OS/arch.</param>
    /// <param name="component">
    /// Explicit component; when null the <see cref="JavaRuntimeSelection"/>
    /// defaults are used.
    /// </param>
    /// <exception cref="JavaRuntimeException">
    /// <c>UNSUPPORTED_PLATFORM</c>, <c>UNSUPPORTED_JAVA_VERSION</c>,
    /// <c>NO_RUNTIME_AVAILABLE</c>, <c>LZMA_ONLY_UNSUPPORTED</c>,
    /// <c>MISSING_HASH</c>, <c>MISSING_SIZE</c>, <c>DUPLICATE_DEST</c>,
    /// <c>PATH_ESCAPE</c> or <c>INVALID_METADATA</c>.
    /// </exception>
    /// <exception cref="DownloadPlanException">The final plan fails validation.</exception>
    public static DownloadPlan Build(
        string root,
        int majorVersion,
        JavaRuntimeCatalog catalog,
        JavaRuntimeManifest manifest,
        MojangPlatform platform,
        string? component = null)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(platform);

        var selection = JavaRuntimeSelection.ForMajor(majorVersion, component);
        var platformKey = JavaRuntimePlatform.FromMojang(platform);

        _ = catalog.Select(platformKey, selection.Component)
            ?? throw NoRuntimeAvailable(catalog, platformKey, selection.Component);

        var prefix = DestPrefix(majorVersion);
        var builder = new DownloadPlanBuilder(root);
        var destinations = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var planned = 0;

        foreach (var (manifestPath, entry) in manifest.Files
            .OrderBy(static pair => pair.Key, StringComparer.Ordinal))
        {
            if (entry is null)
            {
                throw InvalidMetadata($"runtime manifest entry '{manifestPath}' is null");
            }

            if (entry.IsDirectory || entry.IsLink)
            {
                continue;
            }

            if (!entry.IsFile)
            {
                throw InvalidMetadata(
                    $"runtime manifest entry '{manifestPath}' has unsupported type '{entry.Type}'");
            }

            var raw = entry.Raw;
            if (raw is null)
            {
                if (entry.Lzma is not null)
                {
                    throw new JavaRuntimeException(
                        JavaRuntimeErrorCode.LzmaOnlyUnsupported,
                        $"runtime file '{manifestPath}' only publishes an LZMA download; " +
                        "this slice cannot decompress LZMA");
                }

                throw InvalidMetadata(
                    $"runtime file '{manifestPath}' has neither downloads.raw nor downloads.lzma");
            }

            if (string.IsNullOrWhiteSpace(raw.Url))
            {
                throw InvalidMetadata($"runtime file '{manifestPath}': download URL is missing");
            }

            if (string.IsNullOrWhiteSpace(raw.Sha1))
            {
                throw new JavaRuntimeException(
                    JavaRuntimeErrorCode.MissingHash,
                    $"runtime file '{manifestPath}': sha1 is missing; " +
                    "refusing to emit an unverifiable download");
            }

            if (raw.Size is null)
            {
                throw new JavaRuntimeException(
                    JavaRuntimeErrorCode.MissingSize,
                    $"runtime file '{manifestPath}': size is missing; " +
                    "the executor requires it");
            }

            if (raw.Size < 0)
            {
                throw InvalidMetadata($"runtime file '{manifestPath}': negative size {raw.Size}");
            }

            var destIssue = DownloadPlanPaths.TryNormalizeDest(manifestPath, out var normalized);
            if (destIssue is not null)
            {
                throw new JavaRuntimeException(
                    destIssue.Code,
                    $"runtime file '{manifestPath}': {destIssue.Message}");
            }

            if (!string.Equals(normalized, manifestPath, StringComparison.Ordinal))
            {
                throw InvalidMetadata(
                    $"runtime file '{manifestPath}': manifest path is not normalized");
            }

            var dest = prefix + normalized;
            if (!destinations.Add(dest))
            {
                throw new JavaRuntimeException(
                    JavaRuntimeErrorCode.DuplicateDest,
                    $"runtime file '{manifestPath}': duplicate destination '{dest}'");
            }

            builder.AddFile(
                raw.Url,
                dest,
                DownloadHashAlgorithm.Sha1,
                raw.Sha1,
                raw.Size.Value,
                executable: entry.Executable);

            planned++;
        }

        if (planned == 0)
        {
            throw InvalidMetadata(
                $"runtime manifest for '{platformKey}/{selection.Component}' " +
                "contains no downloadable files");
        }

        return builder.Build();
    }

    private static JavaRuntimeException NoRuntimeAvailable(
        JavaRuntimeCatalog catalog,
        string platformKey,
        string component)
    {
        var available = catalog.GetComponentNames(platformKey);
        var hint = available.Count == 0
            ? "no components for this platform"
            : $"available components: {string.Join(", ", available)}";

        return new JavaRuntimeException(
            JavaRuntimeErrorCode.NoRuntimeAvailable,
            $"no Java runtime '{component}' for platform '{platformKey}' ({hint})");
    }

    private static JavaRuntimeException InvalidMetadata(string message)
        => new(JavaRuntimeErrorCode.InvalidMetadata, message);
}
