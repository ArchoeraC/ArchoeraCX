using System.Collections.Concurrent;
using Archoera.Application.JavaRuntime;
using Archoera.Application.Modules;
using Archoera.Domain.Downloads;
using Archoera.Infrastructure.Downloads;

namespace Archoera.Infrastructure.Java.Distributions;

/// <summary>
/// Read/install facade for third-party JDK archives.
///
/// Catalogue calls are cached because the page asks for majors and builds in
/// sequence; the install itself runs the Rust download executor and extracts
/// the verified archive in C#. The executor stays DownloadPlan-only.
/// </summary>
internal sealed partial class JavaDistributionService
{
    /// <summary>Minecraft's current Java baseline across supported loaders.</summary>
    public const int MinecraftRecommendedMajor =
        JavaDistributionProviderBase.MinecraftRecommendedMajor;

    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(10);

    private readonly JavaDistributionRegistry _registry;
    private readonly IDownloadPlanRunner _runner;
    private readonly IJavaCveSource? _cve;
    private readonly HttpClient? _http;
    private readonly string _dataRoot;
    private readonly ConcurrentDictionary<string, CacheEntry> _cache =
        new(StringComparer.Ordinal);

    public JavaDistributionService(
        JavaDistributionRegistry registry,
        string dataRoot,
        IDownloadPlanRunner? runner = null,
        IJavaCveSource? cve = null,
        HttpClient? http = null)
    {
        _registry = registry;
        _dataRoot = Path.GetFullPath(dataRoot);
        _runner = runner ?? new DownloadPlanRunner();
        _cve = cve;
        _http = http;
    }

    public JavaDistributionPlatform Platform => JavaDistributionPlatform.Current();

    public IReadOnlyList<JavaDistributionPlatform> Platforms
        => JavaDistributionPlatform.Supported;

    /// <summary>
    /// Some vendor catalogues (Zulu is the known case) round the archive size
    /// to the nearest thousand. The executor compares the HTTP total against
    /// the plan size, so a rounded value aborts before the first byte. HEAD the
    /// final archive URL and use the exact Content-Length when available.
    /// </summary>
    private async Task<JavaDistributionArchive> ReconcileArchiveSizeAsync(
        JavaDistributionArchive archive,
        CancellationToken cancellationToken)
    {
        if (_http is null) return archive;
        try
        {
            var uri = JavaDistributionHttp.RequireHttps(
                archive.Url,
                "JDK archive");
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(15));
            var actual = await JavaDistributionHttp
                .GetContentLengthAsync(_http, uri, timeout.Token)
                .ConfigureAwait(false);
            return actual == archive.Size
                ? archive
                : archive with { Size = actual };
        }
        catch (Exception exception) when (
            exception is JavaDistributionException or HttpRequestException or IOException)
        {
            // Keep the catalogue value; a provider that blocks HEAD can still
            // be downloaded when its size was already exact.
            return archive;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return archive;
        }
    }

    public IReadOnlyList<JavaDistributionProviderInfo> Providers
        => _registry.Providers
            .Select(provider => new JavaDistributionProviderInfo(
                provider.Id,
                provider.DisplayName,
                provider.ShortName,
                provider.Vendor,
                provider.Homepage,
                provider.ListsAllVersions,
                provider.RecommendedMajor))
            .ToArray();

    public string DefaultRoot => ManagedJavaRuntimes.Root(_dataRoot);

    public int RecommendedMajor(string providerId)
        => _registry.Get(providerId).RecommendedMajor;

    public async Task<IReadOnlyList<int>> GetMajorsAsync(
        string providerId,
        CancellationToken cancellationToken,
        JavaDistributionPlatform? platform = null)
    {
        var provider = _registry.Get(providerId);
        var targetPlatform = platform ?? Platform;
        var key = $"{provider.Id}|majors|{targetPlatform.Os}|" +
                  $"{targetPlatform.Arch}|{targetPlatform.ArchiveType}";
        if (TryGet<IReadOnlyList<int>>(key, out var cached)) return cached;

        var majors = await provider
            .GetMajorsAsync(targetPlatform, cancellationToken)
            .ConfigureAwait(false);
        Put(key, majors);
        return majors;
    }

    public async Task<IReadOnlyList<JavaDistributionRelease>> GetReleasesAsync(
        string providerId,
        int major,
        CancellationToken cancellationToken,
        JavaDistributionPlatform? platform = null)
    {
        if (major is < 8 or > 99)
        {
            throw new JavaDistributionException(
                "JAVA_VERSION_UNKNOWN",
                $"Java {major} is outside the supported range");
        }

        var provider = _registry.Get(providerId);
        var targetPlatform = platform ?? Platform;
        var key = $"{provider.Id}|releases|{targetPlatform.Os}|" +
                  $"{targetPlatform.Arch}|{targetPlatform.ArchiveType}|{major}";
        if (TryGet<IReadOnlyList<JavaDistributionRelease>>(key, out var cached))
        {
            return cached;
        }

        var releases = await provider
            .GetReleasesAsync(major, targetPlatform, cancellationToken)
            .ConfigureAwait(false);
        Put(key, releases);
        return releases;
    }

    /// <summary>
    /// CVE lookup for the build currently selected on the install page. The
    /// version is resolved from the provider catalogue so the UI cannot make
    /// the lookup describe a different archive than the one it installs.
    /// </summary>
    public async Task<JavaCveReport> GetAdvisoriesAsync(
        string providerId,
        int major,
        string versionId,
        CancellationToken cancellationToken,
        JavaDistributionPlatform? platform = null)
    {
        var provider = _registry.Get(providerId);
        var releases = await GetReleasesAsync(
                providerId,
                major,
                cancellationToken,
                platform)
            .ConfigureAwait(false);
        var release = releases.FirstOrDefault(item =>
            string.Equals(item.VersionId, versionId, StringComparison.Ordinal));
        if (release is null)
        {
            throw new JavaDistributionException(
                "JAVA_VERSION_UNKNOWN",
                "the selected build is no longer in the provider catalogue");
        }

        if (_cve is null)
        {
            return JavaCveReport.Unavailable(
                provider.Id,
                release.Version,
                "JAVA_CVE_UNAVAILABLE",
                DateTimeOffset.UtcNow);
        }

        // Query the newest patch's version decade so an older selected build
        // still sees the CVEs that were fixed after it, while keeping the NVD
        // keyword response scoped to this major.
        var baseline = releases
            .FirstOrDefault(item => item.LatestPatch)?.Version ?? release.Version;
        return await _cve
            .GetReportAsync(
                provider.Id,
                release.Version,
                baseline,
                cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<JavaDistributionInstallResult> InstallAsync(
        string providerId,
        string versionId,
        string? directory,
        bool autoExtract,
        bool autoDelete,
        bool allowOutdated,
        bool acknowledgeCves,
        CancellationToken cancellationToken,
        JavaDistributionPlatform? platform = null,
        bool allowIncompatible = false)
    {
        if (!autoExtract) autoDelete = false;

        var provider = _registry.Get(providerId);
        var targetPlatform = platform ?? Platform;
        if (!targetPlatform.IsCurrentHost && !allowIncompatible)
        {
            throw new JavaDistributionException(
                "JAVA_PLATFORM_INCOMPATIBLE",
                "the selected JDK targets a different platform; confirm the " +
                "incompatibility warning before installing");
        }

        var archive = await provider
            .ResolveAsync(versionId, targetPlatform, cancellationToken)
            .ConfigureAwait(false);
        archive = await ReconcileArchiveSizeAsync(archive, cancellationToken)
            .ConfigureAwait(false);

        if (!allowOutdated)
        {
            var releases = await GetReleasesAsync(
                    providerId,
                    archive.Major,
                    cancellationToken,
                    targetPlatform)
                .ConfigureAwait(false);
            var selected = releases
                .FirstOrDefault(release =>
                    string.Equals(
                        release.VersionId,
                        versionId,
                        StringComparison.Ordinal));
            if (releases.Count > 0 &&
                (selected is null || !selected.LatestPatch))
            {
                throw new JavaDistributionException(
                    "JAVA_VERSION_OUTDATED",
                    "the selected build is not the latest patch and may contain " +
                    "known CVEs; enable historical builds to continue");
            }
        }

        if (_cve is not null)
        {
            // The lookup is best-effort: an NVD/OSV outage must not block an
            // otherwise verified install. When the feed does answer and finds
            // CVEs, the caller has to acknowledge them explicitly.
            JavaCveReport report;
            try
            {
                // Reuse the report the page already loaded for this build.
                report = await GetAdvisoriesAsync(
                        providerId,
                        archive.Major,
                        versionId,
                        cancellationToken,
                        targetPlatform)
                    .ConfigureAwait(false);
            }
            catch (Exception exception) when (
                exception is JavaCveFetchException or JavaDistributionException)
            {
                report = JavaCveReport.Unavailable(
                    provider.Id,
                    archive.Version,
                    "JAVA_CVE_UNAVAILABLE",
                    DateTimeOffset.UtcNow);
            }

            if (report.Status == JavaCveReport.OkStatus &&
                report.Advisories.Count > 0 &&
                !acknowledgeCves)
            {
                throw new JavaDistributionException(
                    "JAVA_CVE_UNACKNOWLEDGED",
                    $"{report.Advisories.Count} known CVE(s) affect " +
                    $"{archive.Version}; acknowledge them to continue");
            }
        }

        var folderProvider = targetPlatform.IsCurrentHost
            ? provider.Id
            : $"{provider.Id}-{targetPlatform.Os}-{targetPlatform.Arch}";
        var target = JavaInstallTarget.Resolve(
            directory,
            folderProvider,
            archive.Version,
            _dataRoot,
            DefaultRoot);
        var parent = Path.GetDirectoryName(target)
            ?? throw new JavaDistributionException(
                "JAVA_DIR_INVALID",
                "install directory has no parent");
        Directory.CreateDirectory(parent);
        if (autoExtract)
        {
            JavaInstallTarget.EnsureAvailable(target);
        }

        var staging = Path.Combine(
            parent,
            $".{Path.GetFileName(target)}.installing-{Guid.NewGuid():N}");
        Directory.CreateDirectory(staging);
        try
        {
            var archiveName = archive.ArchiveType == "zip" ? "jdk.zip" : "jdk.tar.gz";
            var plan = new DownloadPlanBuilder(staging)
                .AddFile(
                    archive.Url,
                    archiveName,
                    archive.HashAlgorithm,
                    archive.Hash,
                    archive.Size)
                .Build();

            ModuleProgress.Report(
                0,
                archive.Size,
                0,
                $"{provider.DisplayName} {archive.Version}");
            var result = await _runner
                .ExecuteAsync(
                    plan,
                    cancellationToken,
                    DownloadProfile.Archive)
                .ConfigureAwait(false);
            if (!result.Ok || !result.Completed)
            {
                var detail = !string.IsNullOrWhiteSpace(result.FailedSummary)
                    ? result.FailedSummary
                    : !string.IsNullOrWhiteSpace(result.Stderr)
                        ? result.Stderr.Trim()
                        : $"executor status {result.Status}";
                throw new JavaDistributionException(
                    "JAVA_DOWNLOAD_FAILED",
                    $"JDK archive download failed: {detail}");
            }

            var archivePath = Path.Combine(staging, archiveName);
            if (!File.Exists(archivePath) ||
                new FileInfo(archivePath).Length != archive.Size)
            {
                throw new JavaDistributionException(
                    "JAVA_DOWNLOAD_FAILED",
                    "the executor completed without the expected archive");
            }

            if (!autoExtract)
            {
                var kept = JavaArchiveStore.Keep(
                    archivePath,
                    parent,
                    folderProvider,
                    archive.Version,
                    archive.ArchiveType);
                return new JavaDistributionInstallResult(
                    provider.Id,
                    archive.Version,
                    archive.Major,
                    string.Empty,
                    string.Empty,
                    archive.Size,
                    kept,
                    Extracted: false);
            }

            ModuleProgress.Report(archive.Size, archive.Size, 0, "Extracting JDK archive");
            var extracted = Path.Combine(staging, "extracted");
            JavaArchiveExtractor.Extract(
                archivePath,
                archive.ArchiveType,
                extracted,
                cancellationToken);

            var home = JavaInstallTarget.FindJavaHome(extracted, targetPlatform)
                ?? throw new JavaDistributionException(
                    "JAVA_ARCHIVE_INVALID",
                    "the archive does not contain bin/java");
            JavaInstallTarget.NormalizeExecutables(home);
            JavaInstallTarget.MoveIntoPlace(home, target);

            var executable = Path.Combine(
                target,
                "bin",
                targetPlatform.IsWindows ? "java.exe" : "java");
            if (!File.Exists(executable))
            {
                throw new JavaDistributionException(
                    "JAVA_ARCHIVE_INVALID",
                    "the installed JDK has no bin/java executable");
            }

            var keptArchive = autoDelete
                ? null
                : JavaArchiveStore.Keep(
                    archivePath,
                    parent,
                    folderProvider,
                    archive.Version,
                    archive.ArchiveType);
            return new JavaDistributionInstallResult(
                provider.Id,
                archive.Version,
                archive.Major,
                target,
                executable,
                archive.Size,
                keptArchive,
                Extracted: true);
        }
        finally
        {
            JavaInstallTarget.TryDeleteDirectory(staging);
        }
    }

    /// <summary>
    /// Custom directory semantics: the picker selects the JDK home itself, so
    /// an existing non-empty folder is refused instead of merged. Without a
    /// custom path the runtime lands under the launcher-managed root, which the
    /// scanner already probes.
    /// </summary>
    private bool TryGet<T>(string key, out T value)
    {
        if (_cache.TryGetValue(key, out var entry) &&
            entry.Expires > DateTimeOffset.UtcNow)
        {
            value = (T)entry.Value;
            return true;
        }

        value = default!;
        return false;
    }

    private void Put<T>(string key, T value)
    {
        if (_cache.Count > 128)
        {
            var now = DateTimeOffset.UtcNow;
            foreach (var pair in _cache)
            {
                if (pair.Value.Expires <= now) _cache.TryRemove(pair.Key, out _);
            }
        }

        _cache[key] = new CacheEntry(DateTimeOffset.UtcNow.Add(CacheTtl), value!);
    }

    private sealed record CacheEntry(DateTimeOffset Expires, object Value);
}
