using System.Text.Json;
using System.Text.RegularExpressions;
using Archoera.Application.Mojang;
using Archoera.Application.Modules;
using Archoera.Domain;
using Archoera.Domain.Instances;
using Archoera.Domain.Mojang;
using Archoera.Infrastructure.Instances;
using Archoera.Infrastructure.Launch;

namespace Archoera.Infrastructure.Maintenance;

/// <summary>One deletable group under an install root.</summary>
/// <param name="Kind">"libraries" or "assets".</param>
/// <param name="FileCount">
/// Candidate count for a plan; deleted count for a delete pass.
/// </param>
/// <param name="TotalBytes">
/// Candidate bytes for a plan; freed bytes for a delete pass.
/// </param>
/// <param name="Samples">Relative paths for the UI preview, capped.</param>
internal sealed record UnusedDependencyGroup(
    string Kind,
    int FileCount,
    long TotalBytes,
    IReadOnlyList<string> Samples);

/// <summary>Scan outcome for one configured instance root.</summary>
/// <param name="Root">Normalized absolute root path.</param>
/// <param name="Status">"scanned" or "skipped".</param>
/// <param name="Reason">
/// Skip reason: missing, no_instances, unreadable_profile; null when scanned.
/// </param>
/// <param name="InstanceCount">Instances discovered in the root.</param>
/// <param name="Libraries">
/// Deletable libraries; null when the root was skipped or holds no
/// provably-unused library.
/// </param>
/// <param name="Assets">
/// Deletable asset objects; null when the root was skipped or the scan had to
/// fail closed for assets (a profile without asset metadata or an
/// unreadable local asset index proves nothing).
/// </param>
internal sealed record UnusedDependencyRootReport(
    string Root,
    string Status,
    string? Reason,
    int InstanceCount,
    UnusedDependencyGroup? Libraries,
    UnusedDependencyGroup? Assets);

/// <summary>Aggregate result across all configured roots.</summary>
/// <param name="Roots">Per-root outcome in configuration order.</param>
/// <param name="TotalFiles">
/// Candidate totals for a plan; deleted totals for a delete pass.
/// </param>
/// <param name="TotalBytes">
/// Candidate bytes for a plan; freed bytes for a delete pass.
/// </param>
/// <param name="LimitReached">
/// True when the entry budget ran out: the report is incomplete and the
/// caller must treat it as read-only (the delete pass refuses to run).
/// </param>
internal sealed record UnusedDependencyReport(
    IReadOnlyList<UnusedDependencyRootReport> Roots,
    int TotalFiles,
    long TotalBytes,
    bool LimitReached);

/// <summary>
/// Finds game dependencies no instance references anymore.
///
/// A shared install root accumulates libraries and asset objects for every
/// loader build and game version that was ever installed there; uninstalling
/// an instance leaves them behind. This scanner never guesses:
/// <list type="bullet">
///   <item>the used set is the union of every version JSON under the root's
///         <c>versions/</c> folders plus every discovered instance profile;
///         legacy <c>inheritsFrom</c> chains are followed inside the root;</item>
///   <item>any unreadable or broken profile fails the whole root closed — a
///         scan that cannot prove a file is unused never deletes it;</item>
///   <item>asset candidates must sit at
///         <c>assets/objects/&lt;2 hex&gt;/&lt;hash&gt;</c> with the hash listed
///         by a local <c>assets/indexes/&lt;id&gt;.json</c>;</item>
///   <item>library candidates end in <c>.jar</c> / <c>.pom</c>; download
///         temp files stay with <c>maintenance.cleanPartials</c>;</item>
///   <item>symbolic links are never deleted and never descended into.</item>
/// </list>
/// The delete pass re-runs the full scan immediately before deleting, so a
/// stale preview can never remove a file that became referenced in between.
/// </summary>
internal static class UnusedDependencyScanner
{
    internal const string StatusScanned = "scanned";
    internal const string StatusSkipped = "skipped";
    internal const string ReasonMissing = "missing";
    internal const string ReasonNoInstances = "no_instances";
    internal const string ReasonUnreadableProfile = "unreadable_profile";
    internal const string ReasonScanLimit = "scan_limit";

    internal const string KindLibraries = "libraries";
    internal const string KindAssets = "assets";

    /// <summary>
    /// Filesystem entries visited per call. Instance roots can hold a full
    /// asset tree; the scan stops quietly at this cap and the caller must
    /// treat the report as read-only (the delete pass refuses to run).
    /// </summary>
    internal const int MaxScannedEntries = 200_000;

    /// <summary>Preview paths reported per group.</summary>
    internal const int MaxSamples = 60;

    /// <summary>Maximum <c>inheritsFrom</c> hops followed inside one root.</summary>
    private const int MaxChainDepth = 4;

    private static StringComparer PathComparer => PathSemantics.Comparer;

    private static readonly Regex AssetObjectFile = new(
        "^[0-9a-f]{40,64}$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex AssetObjectDirectory = new(
        "^[0-9a-f]{2}$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>Builds the read-only report shown in the preview dialog.</summary>
    internal static UnusedDependencyReport Plan(
        IReadOnlyList<string> roots,
        MojangPlatform platform,
        CancellationToken cancellationToken,
        int maxEntries = MaxScannedEntries)
    {
        var results = Scan(roots, platform, delete: false, maxEntries, cancellationToken, out var limitReached);
        var files = results.Sum(root => (root.Libraries?.FileCount ?? 0) + (root.Assets?.FileCount ?? 0));
        var bytes = results.Sum(root => (root.Libraries?.TotalBytes ?? 0) + (root.Assets?.TotalBytes ?? 0));
        return new UnusedDependencyReport(results, files, bytes, limitReached);
    }

    /// <summary>
    /// Deletes what a fresh scan proves unused. <see cref="UnusedDependencyGroup.FileCount"/>
    /// then reports what each root actually deleted; a root whose scan failed
    /// closed deletes nothing.
    /// </summary>
    internal static UnusedDependencyReport Delete(
        IReadOnlyList<string> roots,
        MojangPlatform platform,
        CancellationToken cancellationToken,
        int maxEntries = MaxScannedEntries)
    {
        var results = Scan(roots, platform, delete: true, maxEntries, cancellationToken, out var limitReached);
        var files = results.Sum(root => (root.Libraries?.FileCount ?? 0) + (root.Assets?.FileCount ?? 0));
        var bytes = results.Sum(root => (root.Libraries?.TotalBytes ?? 0) + (root.Assets?.TotalBytes ?? 0));
        return new UnusedDependencyReport(results, files, bytes, limitReached);
    }

    private static IReadOnlyList<UnusedDependencyRootReport> Scan(
        IReadOnlyList<string> roots,
        MojangPlatform platform,
        bool delete,
        int maxEntries,
        CancellationToken cancellationToken,
        out bool limitReached)
    {
        var reports = new List<UnusedDependencyRootReport>();
        var budget = new EntryBudget(maxEntries);
        limitReached = false;

        foreach (var root in roots)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var rootFull = NormalizeRoot(root);
            if (rootFull is null)
            {
                reports.Add(new UnusedDependencyRootReport(
                    root ?? string.Empty, StatusSkipped, ReasonMissing, 0, null, null));
                continue;
            }

            var instances = InstanceDiscovery.Discover(rootFull, cancellationToken);
            if (instances.Count == 0 && !HasVersionProfiles(rootFull, ref budget))
            {
                reports.Add(new UnusedDependencyRootReport(
                    rootFull, StatusSkipped, ReasonNoInstances, 0, null, null));
                continue;
            }

            var used = new Dictionary<string, UsedFiles>(PathComparer);
            if (budget.Exhausted)
            {
                reports.Add(new UnusedDependencyRootReport(
                    rootFull, StatusSkipped, ReasonScanLimit, instances.Count, null, null));
                continue;
            }

            if (!CollectUsedFiles(rootFull, instances, platform, used, ref budget))
            {
                reports.Add(new UnusedDependencyRootReport(
                    rootFull, StatusSkipped, ReasonUnreadableProfile, instances.Count, null, null));
                continue;
            }

            var libraries = ScanLibraries(rootFull, used, delete, ref budget);
            var assets = ScanAssets(rootFull, used, delete, ref budget);
            if (budget.Exhausted) limitReached = true;

            reports.Add(new UnusedDependencyRootReport(
                rootFull,
                StatusScanned,
                null,
                instances.Count,
                libraries,
                assets));
        }

        return reports;
    }

    /// <summary>True when the root holds any readable version profile JSON.</summary>
    private static bool HasVersionProfiles(string rootFull, ref EntryBudget budget)
    {
        var versions = Path.Combine(rootFull, "versions");
        if (!Directory.Exists(versions)) return false;
        foreach (var directory in SafeDirectories(versions))
        {
            if (!budget.Consume()) return false;
            if (File.Exists(Path.Combine(directory, $"{Path.GetFileName(directory)}.json")))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Builds the used set for every install root reachable from this
    /// configured root. Returns false when any profile could not be read:
    /// the whole root then stays untouched (fail closed).
    /// </summary>
    private static bool CollectUsedFiles(
        string rootFull,
        IReadOnlyList<GameInstanceRecord> instances,
        MojangPlatform platform,
        Dictionary<string, UsedFiles> used,
        ref EntryBudget budget)
    {
        // Discovered instances outside versions/ (custom game dirs): resolve
        // their own profile first, mirroring the launch discovery. Instances
        // under versions/ are covered by the walk below through their own
        // <dir>/<dir>.json.
        foreach (var instance in instances)
        {
            if (IsUnderVersions(instance.RootPath)) continue;
            var directoryName = Path.GetFileName(
                Path.TrimEndingDirectorySeparator(instance.RootPath));
            var profile = Path.Combine(instance.RootPath, $"{directoryName}.json");
            if (!File.Exists(profile)) return false;
            if (!CollectProfile(instance.RootPath, profile, platform, used, ref budget, depth: 0))
            {
                return false;
            }
        }

        // Every version profile under versions/ counts, instance or not: an
        // instance folder's own merged profile, legacy shared loader profiles
        // and other launchers' installs all keep their libraries alive.
        var versionsRoot = Path.Combine(rootFull, "versions");
        foreach (var directory in SafeDirectories(versionsRoot))
        {
            var profile = Path.Combine(directory, $"{Path.GetFileName(directory)}.json");
            if (!File.Exists(profile)) continue;
            if (!CollectProfile(rootFull, profile, platform, used, ref budget, depth: 0))
            {
                return false;
            }
        }

        // Isolated instances own their libraries inside the instance folder;
        // their install root is the instance directory itself and it is never
        // a cleanup target, but its profile still feeds that bucket so a
        // nested shared root cannot flag the instance's own files.
        foreach (var instance in instances)
        {
            var paths = LaunchPaths.Resolve(instance.RootPath);
            if (PathComparer.Equals(paths.SharedRoot, rootFull)) continue;
            if (!Directory.Exists(Path.Combine(paths.SharedRoot, "libraries"))) continue;
            if (used.ContainsKey(paths.SharedRoot)) continue;
            var directoryName = Path.GetFileName(
                Path.TrimEndingDirectorySeparator(instance.RootPath));
            var profile = Path.Combine(instance.RootPath, $"{directoryName}.json");
            if (!File.Exists(profile) ||
                !CollectProfile(paths.SharedRoot, profile, platform, used, ref budget, depth: 0))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsUnderVersions(string path)
    {
        var parent = Directory.GetParent(Path.TrimEndingDirectorySeparator(path));
        return parent is not null &&
               string.Equals(
                   Path.GetFileName(parent.FullName),
                   "versions",
                   StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Reads one version profile into the bucket of the install root the
    /// profile belongs to, and follows <c>inheritsFrom</c> inside the same
    /// root. Missing or unreadable chain members fail the scan.
    /// </summary>
    private static bool CollectProfile(
        string scopeRoot,
        string profilePath,
        MojangPlatform platform,
        Dictionary<string, UsedFiles> used,
        ref EntryBudget budget,
        int depth)
    {
        if (!budget.Consume()) return false;
        if (depth > MaxChainDepth) return false;

        try
        {
            using var document = JsonDocument.Parse(
                File.ReadAllText(profilePath),
                new JsonDocumentOptions
                {
                    AllowTrailingCommas = true,
                    CommentHandling = JsonCommentHandling.Skip,
                });
            var version = MojangJson.ParseVersion(document.RootElement);
            var paths = LaunchPaths.Resolve(scopeRoot);
            var bucket = BucketFor(paths.SharedRoot, used);
            AddLibraries(version, platform, bucket);
            AddAssetReference(scopeRoot, version, bucket);

            if (!string.IsNullOrWhiteSpace(version.InheritsFrom))
            {
                var baseId = version.InheritsFrom.Trim();
                var found = false;
                foreach (var baseRoot in new[]
                         {
                             Path.GetDirectoryName(profilePath),
                             Path.Combine(scopeRoot, "versions", baseId),
                         })
                {
                    if (string.IsNullOrEmpty(baseRoot)) continue;
                    var baseProfile = Path.Combine(baseRoot, $"{baseId}.json");
                    if (!File.Exists(baseProfile)) continue;
                    if (!CollectProfile(scopeRoot, baseProfile, platform, used, ref budget, depth + 1))
                    {
                        return false;
                    }

                    found = true;
                    break;
                }

                if (!found) return false;
            }

            return true;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or JsonException
                or ArgumentException or InvalidOperationException)
        {
            return false;
        }
    }

    private static UsedFiles BucketFor(string sharedRoot, Dictionary<string, UsedFiles> used)
    {
        if (!used.TryGetValue(sharedRoot, out var bucket))
        {
            bucket = new UsedFiles();
            used[sharedRoot] = bucket;
        }

        return bucket;
    }

    private static void AddLibraries(MojangVersion version, MojangPlatform platform, UsedFiles bucket)
    {
        foreach (var library in version.Libraries)
        {
            var artifact = library.Downloads?.Artifact;
            if (artifact is not null && !string.IsNullOrWhiteSpace(artifact.Path))
            {
                bucket.Libraries.Add(NormalizeRelative(artifact.Path));
            }
            else if (MojangMavenPath.TryResolve(library.Name, classifierOverride: null, out var path))
            {
                bucket.Libraries.Add(NormalizeRelative(path));
            }

            var native = NativeClassifier(library, platform);
            if (native is null) continue;
            var classifiers = library.Downloads?.ClassifierMap;
            var nativeDownload =
                classifiers is not null && classifiers.TryGetValue(native, out var download)
                    ? download
                    : null;
            if (nativeDownload is not null && !string.IsNullOrWhiteSpace(nativeDownload.Path))
            {
                bucket.Libraries.Add(NormalizeRelative(nativeDownload.Path));
            }
            else if (MojangMavenPath.TryResolve(library.Name, classifierOverride: native, out var nativePath))
            {
                bucket.Libraries.Add(NormalizeRelative(nativePath));
            }
        }
    }

    private static string? NativeClassifier(MojangLibrary library, MojangPlatform platform)
    {
        if (library.Natives is null ||
            !library.Natives.TryGetValue(platform.OsName, out var raw) ||
            string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        return raw.Replace("${arch}", platform.NativeArchBits, StringComparison.Ordinal);
    }

    private static void AddAssetReference(string scopeRoot, MojangVersion version, UsedFiles bucket)
    {
        var indexId = version.AssetIndex?.Id ?? version.Assets;
        if (string.IsNullOrWhiteSpace(indexId))
        {
            // No asset metadata at all: nothing proves which objects are used.
            bucket.AssetIndexBroken = true;
            return;
        }

        if (!bucket.AssetIndexIds.Add(indexId)) return;
        var indexPath = Path.Combine(scopeRoot, "assets", "indexes", $"{indexId}.json");
        try
        {
            using var document = JsonDocument.Parse(
                File.ReadAllText(indexPath),
                new JsonDocumentOptions
                {
                    AllowTrailingCommas = true,
                    CommentHandling = JsonCommentHandling.Skip,
                });
            var index = MojangJson.ParseAssetIndex(document.RootElement, indexId);
            foreach (var hash in index.Objects.Values.Select(entry => entry.Hash))
            {
                if (!string.IsNullOrWhiteSpace(hash))
                {
                    bucket.AssetHashes.Add(hash.ToLowerInvariant());
                }
            }
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or JsonException
                or ArgumentException)
        {
            bucket.AssetIndexBroken = true;
        }
    }

    private static UnusedDependencyGroup? ScanLibraries(
        string rootFull,
        Dictionary<string, UsedFiles> used,
        bool delete,
        ref EntryBudget budget)
    {
        var librariesRoot = Path.Combine(rootFull, "libraries");
        if (!Directory.Exists(librariesRoot)) return null;
        if (!used.TryGetValue(rootFull, out var bucket) || bucket.Libraries.Count == 0)
        {
            // No profile proved any library here is used; a root whose
            // profiles declared nothing is still fail-closed territory.
            return null;
        }

        var candidates = new List<string>();
        var pending = new Stack<string>();
        pending.Push(librariesRoot);
        while (pending.Count > 0)
        {
            if (!budget.Consume()) return Group(KindLibraries, candidates, limit: true);
            var directory = pending.Pop();
            foreach (var child in SafeEntries(directory))
            {
                if (!budget.Consume()) return Group(KindLibraries, candidates, limit: true);
                if (IsLink(child)) continue;
                if (child is DirectoryInfo subdirectory)
                {
                    pending.Push(subdirectory.FullName);
                    continue;
                }

                if (child is not FileInfo file) continue;
                var name = file.Name;
                if (!name.EndsWith(".jar", StringComparison.OrdinalIgnoreCase) &&
                    !name.EndsWith(".pom", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var relative = NormalizeRelative(Path.GetRelativePath(librariesRoot, file.FullName));
                if (bucket.Libraries.Contains(relative)) continue;
                candidates.Add(relative);
            }
        }

        return Finish(KindLibraries, librariesRoot, "libraries/", candidates, delete);
    }

    private static UnusedDependencyGroup? ScanAssets(
        string rootFull,
        Dictionary<string, UsedFiles> used,
        bool delete,
        ref EntryBudget budget)
    {
        var objectsRoot = Path.Combine(rootFull, "assets", "objects");
        if (!Directory.Exists(objectsRoot)) return null;
        if (!used.TryGetValue(rootFull, out var bucket)) return null;
        if (bucket.AssetIndexBroken || bucket.AssetHashes.Count == 0)
        {
            // Fail closed: no readable local index proves which objects are
            // alive, so no object may be deleted.
            return null;
        }

        var candidates = new List<string>();
        foreach (var group in SafeDirectories(objectsRoot))
        {
            if (!AssetObjectDirectory.IsMatch(Path.GetFileName(group))) continue;
            foreach (var child in SafeEntries(group))
            {
                if (!budget.Consume()) return Group(KindAssets, candidates, limit: true);
                if (child is not FileInfo file || IsLink(child)) continue;
                if (!AssetObjectFile.IsMatch(Path.GetFileNameWithoutExtension(file.Name))) continue;
                if (bucket.AssetHashes.Contains(file.Name.ToLowerInvariant())) continue;
                candidates.Add(
                    NormalizeRelative(Path.GetRelativePath(objectsRoot, file.FullName)));
            }
        }

        return Finish(KindAssets, objectsRoot, "assets/objects/", candidates, delete);
    }

    /// <summary>
    /// For a plan, reports the candidate totals; for a delete pass, deletes
    /// the candidates and reports what was actually removed.
    /// </summary>
    private static UnusedDependencyGroup Finish(
        string kind,
        string scopeRoot,
        string samplePrefix,
        List<string> candidates,
        bool delete)
    {
        var files = candidates.Count;
        long bytes = 0;
        foreach (var relative in candidates)
        {
            bytes += SafeLength(new FileInfo(
                Path.Combine(scopeRoot, relative.Replace('/', Path.DirectorySeparatorChar))));
        }

        if (!delete)
        {
            return new UnusedDependencyGroup(
                kind, files, bytes, Samples(candidates, samplePrefix));
        }

        var deleted = 0;
        long freed = 0;
        var total = candidates.Count;
        foreach (var (index, relative) in candidates.Index())
        {
            var full = Path.Combine(scopeRoot, relative.Replace('/', Path.DirectorySeparatorChar));
            try
            {
                var info = new FileInfo(full);
                if (!info.Exists || info.LinkTarget is not null) continue;
                var size = info.Length;
                info.Delete();
                deleted++;
                freed += size;
                PruneEmptyParents(Path.GetDirectoryName(full), scopeRoot);
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException)
            {
                // Locked or vanished mid-scan: keep going.
            }
            finally
            {
                if ((index + 1) % 128 == 0 || index + 1 == total)
                {
                    ModuleProgress.Report(index + 1, total, 0, kind);
                }
            }
        }

        return new UnusedDependencyGroup(
            kind, deleted, freed, Samples(candidates, samplePrefix));
    }

    private static IReadOnlyList<string> Samples(List<string> candidates, string prefix)
        => candidates.Take(MaxSamples).Select(candidate => prefix + candidate).ToList();

    /// <summary>Removes directories that a deletion emptied, never the scope root.</summary>
    private static void PruneEmptyParents(string? directory, string stopAt)
    {
        var stop = Path.TrimEndingDirectorySeparator(Path.GetFullPath(stopAt));
        var current = directory;
        while (current is not null)
        {
            string full;
            try
            {
                full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(current));
            }
            catch (Exception exception) when (
                exception is ArgumentException or NotSupportedException or PathTooLongException)
            {
                return;
            }

            if (PathComparer.Equals(full, stop)) return;
            if (!full.StartsWith(stop, StringComparison.Ordinal)) return;
            try
            {
                var info = new DirectoryInfo(full);
                if (info.LinkTarget is not null) return;
                if (info.EnumerateFileSystemInfos().Any()) return;
                info.Delete();
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException)
            {
                return;
            }

            current = Path.GetDirectoryName(full);
        }
    }

    private static UnusedDependencyGroup Group(string kind, List<string> candidates, bool limit)
        => limit
            ? new UnusedDependencyGroup(kind, 0, 0, [])
            : new UnusedDependencyGroup(
                kind, candidates.Count, 0, candidates.Take(MaxSamples).ToList());

    private static IEnumerable<FileSystemInfo> SafeEntries(string directory)
    {
        try
        {
            return new DirectoryInfo(directory).EnumerateFileSystemInfos();
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return [];
        }
    }

    private static IEnumerable<string> SafeDirectories(string directory)
    {
        try
        {
            return Directory.EnumerateDirectories(directory);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return [];
        }
    }

    internal static bool IsLink(FileSystemInfo info)
    {
        try
        {
            return info.LinkTarget is not null;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            return true;
        }
    }

    private static long SafeLength(FileInfo file)
    {
        try
        {
            return file.Length;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            return 0;
        }
    }

    private static string NormalizeRelative(string path)
        => path.Replace('\\', '/');

    /// <summary>
    /// Validates and normalizes a root. Filesystem roots and symbolic links are
    /// never scanned: a misconfigured root must not walk the whole disk.
    /// </summary>
    private static string? NormalizeRoot(string? root)
    {
        if (string.IsNullOrWhiteSpace(root)) return null;

        string full;
        try
        {
            full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        }
        catch (Exception exception) when (
            exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }

        if (IsFilesystemRoot(full)) return null;
        if (!Directory.Exists(full)) return null;
        if (IsLink(new DirectoryInfo(full))) return null;

        return full;
    }

    private static bool IsFilesystemRoot(string fullPath)
    {
        var pathRoot = Path.GetPathRoot(fullPath);
        return string.IsNullOrEmpty(pathRoot) ||
               PathComparer.Equals(
                   Path.TrimEndingDirectorySeparator(pathRoot),
                   fullPath);
    }

    /// <summary>Files one profile marks as used, per install root.</summary>
    private sealed class UsedFiles
    {
        internal readonly HashSet<string> Libraries = new(PathComparer);
        internal readonly HashSet<string> AssetHashes = new(StringComparer.OrdinalIgnoreCase);
        internal readonly HashSet<string> AssetIndexIds = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Set when any profile carries no asset reference at all: the used
        /// object set is then incomplete and assets fail closed.
        /// </summary>
        internal bool AssetIndexBroken;
    }

    /// <summary>Entry budget shared by the whole scan; exhausted means no delete.</summary>
    private sealed class EntryBudget
    {
        private int _remaining;

        internal EntryBudget(int maxEntries) => _remaining = maxEntries;

        internal bool Exhausted => _remaining <= 0;

        /// <returns>False once the budget ran out.</returns>
        internal bool Consume()
        {
            if (_remaining <= 0) return false;
            _remaining--;
            return true;
        }
    }
}
