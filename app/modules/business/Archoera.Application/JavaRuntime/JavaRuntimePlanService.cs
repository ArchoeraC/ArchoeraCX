using Archoera.Domain.Downloads;
using Archoera.Domain.Java;
using Archoera.Domain.Mojang;

namespace Archoera.Application.JavaRuntime;

/// <summary>
/// Result of a high-level runtime plan build. The plan is the only value sent
/// to the downloader; the remaining fields exist for diagnostics/UI.
/// </summary>
/// <param name="Plan">Validated download plan rooted at the caller's root.</param>
/// <param name="PlatformKey">Selected <c>all.json</c> platform key.</param>
/// <param name="Selection">Java major plus resolved component.</param>
/// <param name="Component">Catalog entry used to fetch the manifest.</param>
/// <param name="Manifest">Verified runtime manifest used to build the plan.</param>
public sealed record JavaRuntimePlanResult(
    DownloadPlan Plan,
    string PlatformKey,
    JavaRuntimeSelection Selection,
    JavaRuntimeComponent Component,
    JavaRuntimeManifest Manifest)
{
    /// <summary>Resolved component name, for example <c>java-runtime-delta</c>.</summary>
    public string ComponentName => Selection.Component;

    /// <summary>Runtime version, for example <c>21.0.7</c>.</summary>
    public string VersionName => Component.Version.Name;

    /// <summary>Runtime publication date, when the catalog provides one.</summary>
    public DateTimeOffset? Released => Component.Version.Released;

    /// <summary>Number of files in <see cref="Plan"/>.</summary>
    public int FileCount => Plan.Files.Count;

    /// <summary>Sum of planned file sizes.</summary>
    public long TotalBytes => Plan.TotalBytes;
}

/// <summary>
/// High-level Java-runtime entry point: catalog → deterministic platform/
/// component selection → verified manifest → <see cref="DownloadPlan"/>.
/// It writes no files, extracts nothing and never runs Java.
/// </summary>
public sealed class JavaRuntimePlanService
{
    private readonly IJavaRuntimeMetadataClient _client;

    /// <summary>Creates the service over a metadata client.</summary>
    public JavaRuntimePlanService(IJavaRuntimeMetadataClient client)
    {
        ArgumentNullException.ThrowIfNull(client);
        _client = client;
    }

    /// <summary>
    /// Builds the plan for <paramref name="majorVersion"/> on
    /// <paramref name="platform"/>.
    /// </summary>
    /// <param name="root">Absolute shared/data root; <c>runtime/java-&lt;major&gt;</c> lives below it.</param>
    /// <param name="majorVersion">Java feature release, for example <c>21</c>.</param>
    /// <param name="platform">Target OS/arch.</param>
    /// <param name="component">
    /// Explicit component. When null, 8/17/21/25 use the launcher defaults and
    /// any other major fails with <c>UNSUPPORTED_JAVA_VERSION</c>.
    /// </param>
    /// <param name="cancellationToken">Cancels catalog/manifest requests.</param>
    /// <exception cref="JavaRuntimeException">
    /// <c>INVALID_REQUEST</c>, <c>UNSUPPORTED_PLATFORM</c>,
    /// <c>UNSUPPORTED_JAVA_VERSION</c>, <c>NO_RUNTIME_AVAILABLE</c>, transport
    /// and verification codes.
    /// </exception>
    /// <exception cref="DownloadPlanException">The final plan fails validation.</exception>
    public async Task<JavaRuntimePlanResult> BuildAsync(
        string root,
        int majorVersion,
        MojangPlatform platform,
        string? component = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(platform);

        if (DownloadPlanPaths.ValidateRoot(root) is { } rootIssue)
        {
            throw new JavaRuntimeException(
                JavaRuntimeErrorCode.InvalidRequest,
                rootIssue.Message);
        }

        var selection = JavaRuntimeSelection.ForMajor(majorVersion, component);
        var platformKey = JavaRuntimePlatform.FromMojang(platform);

        var catalog = await _client
            .GetCatalogAsync(cancellationToken)
            .ConfigureAwait(false);

        var entry = catalog.Select(platformKey, selection.Component)
            ?? throw NoRuntimeAvailable(catalog, platformKey, selection.Component);

        var manifest = await _client
            .GetRuntimeManifestAsync(entry, cancellationToken)
            .ConfigureAwait(false);

        var plan = JavaRuntimePlanBuilder.Build(
            root,
            majorVersion,
            catalog,
            manifest,
            platform,
            selection.Component);

        return new JavaRuntimePlanResult(plan, platformKey, selection, entry, manifest);
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
}
