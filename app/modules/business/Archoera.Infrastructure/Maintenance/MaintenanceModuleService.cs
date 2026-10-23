using System.Text.Json;
using Archoera.Application.Modules;
using Archoera.Infrastructure.Modules;
using Archoera.Infrastructure.Settings;

namespace Archoera.Infrastructure.Maintenance;

/// <summary>
/// Registered handler for <c>maintenance.cleanPartials</c>: removes stale
/// unfinished-download temp files left inside the configured instance roots.
///
/// The Rust executor keeps its in-flight payload next to the destination
/// (<c>&lt;dest&gt;.part</c>), with a resume sidecar (<c>.part.meta</c>) and a
/// writer lock (<c>.part.lock</c>). Those files are only safe to delete once
/// the download is gone, so this handler:
/// <list type="bullet">
///   <item>only removes entries whose last write is older than the requested
///         age (an active stream keeps refreshing its <c>.part</c>);</item>
///   <item>keeps a whole group when its <c>.part.lock</c> was refreshed within
///         the last ten minutes (a paused resume may not rewrite the payload);</item>
///   <item>skips symbolic links and reparse points, and ignores unreadable or
///         locked entries instead of failing the scan;</item>
///   <item>stops after <see cref="MaxScannedEntries"/> filesystem entries
///         rather than running unbounded over a large asset tree.</item>
/// </list>
/// </summary>
public sealed class MaintenanceModuleService : IModuleMethodHandler
{
    private const string CleanPartialsMethod = "maintenance.cleanPartials";

    /// <summary>Upper bound of the requested age, in days.</summary>
    private const int MinOlderThanDays = 1;

    /// <summary>Lower bound of the requested age, in days.</summary>
    private const int MaxOlderThanDays = 365;

    /// <summary>Age used when the caller omits <c>olderThanDays</c>.</summary>
    private const int DefaultOlderThanDays = 3;

    /// <summary>
    /// Filesystem entries (directories and files) visited per call. Instance
    /// roots can hold a full asset tree; the scan stops quietly at this cap.
    /// </summary>
    internal const int MaxScannedEntries = 200_000;

    /// <summary>A lock refreshed this recently protects its <c>.part</c> group.</summary>
    private static readonly TimeSpan ActiveLockWindow = TimeSpan.FromMinutes(10);

    private const string PartSuffix = ".part";
    private const string MetaTail = ".meta";
    private const string LockSuffix = ".lock";

    private static readonly StringComparer PathComparer =
        OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;

    private static readonly string[] SupportedMethods = [CleanPartialsMethod];

    private readonly JsonAppSettingsStore _settings;

    /// <summary>The registry builds the default store; tests inject a temp one.</summary>
    public MaintenanceModuleService(JsonAppSettingsStore? settings = null)
        => _settings = settings ?? new JsonAppSettingsStore();

    public IReadOnlyCollection<string> Methods => SupportedMethods;

    public async Task<byte[]> HandleAsync(
        string method,
        string? paramsJson,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(method, CleanPartialsMethod, StringComparison.Ordinal))
        {
            return ModuleJson.Error(
                "E_UNSUPPORTED",
                $"unsupported maintenance method '{method}'");
        }

        try
        {
            var olderThanDays = ReadOlderThanDays(paramsJson);
            var appSettings = await _settings
                .LoadAsync(cancellationToken)
                .ConfigureAwait(false);
            var report = ScanRoots(
                appSettings.Roots,
                DateTime.UtcNow,
                olderThanDays,
                MaxScannedEntries,
                cancellationToken);

            return ModuleJson.Envelope(writer =>
            {
                writer.WriteNumber("deletedFiles", report.DeletedFiles);
                writer.WriteNumber("freedBytes", report.FreedBytes);
                writer.WriteNumber("scannedFiles", report.ScannedFiles);
            });
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (exception is ArgumentException or JsonException)
        {
            return ModuleJson.Error("E_INVALID", exception.Message);
        }
        catch (Exception exception)
        {
            return ModuleJson.Error("E_INTERNAL", exception.Message);
        }
    }

    /// <summary>
    /// Walks every configured root breadth-first (deepest-first stack) and
    /// deletes expired partial files. Unreadable directories and vanished
    /// entries are skipped; the remaining roots are still scanned.
    /// </summary>
    internal static PartialCleanupReport ScanRoots(
        IReadOnlyList<string> roots,
        DateTime utcNow,
        int olderThanDays,
        int maxEntries,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(roots);
        if (maxEntries < 1) throw new ArgumentOutOfRangeException(nameof(maxEntries));

        var cutoff = utcNow.AddDays(-olderThanDays);
        var scannedFiles = 0;
        var deletedFiles = 0;
        long freedBytes = 0;
        var entries = 0;

        var pending = new Stack<string>();
        foreach (var root in roots)
        {
            var directory = NormalizeRoot(root);
            if (directory is not null) pending.Push(directory);
        }

        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var directoryPath = pending.Pop();

            try
            {
                foreach (var child in new DirectoryInfo(directoryPath).EnumerateFileSystemInfos())
                {
                    if (++entries > maxEntries)
                    {
                        return new PartialCleanupReport(
                            scannedFiles,
                            deletedFiles,
                            freedBytes,
                            LimitReached: true);
                    }

                    if (IsLink(child))
                    {
                        // Linked files are counted as seen but never deleted;
                        // linked directories are not descended into.
                        if (child is FileInfo) scannedFiles++;
                        continue;
                    }

                    if (child is DirectoryInfo subdirectory)
                    {
                        pending.Push(subdirectory.FullName);
                        continue;
                    }

                    if (child is not FileInfo file) continue;
                    scannedFiles++;

                    var kind = Classify(file.Name);
                    if (kind == PartialKind.None) continue;

                    if (SafeLastWriteUtc(file) is not { } lastWrite ||
                        lastWrite >= cutoff)
                    {
                        continue;
                    }

                    var lockPath = LockPathFor(file.Name, kind, directoryPath);
                    if (lockPath is not null && IsGroupLocked(lockPath, utcNow))
                    {
                        continue;
                    }

                    if (!TryDelete(file, out var size)) continue;
                    deletedFiles++;
                    freedBytes += size;
                }
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException)
            {
                // The directory disappeared or is not readable: keep scanning.
            }
        }

        return new PartialCleanupReport(
            scannedFiles,
            deletedFiles,
            freedBytes,
            LimitReached: false);
    }

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

    /// <summary>True for symbolic links and reparse points; errors read as links.</summary>
    private static bool IsLink(FileSystemInfo info)
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

    private static DateTime? SafeLastWriteUtc(FileInfo file)
    {
        try
        {
            return file.LastWriteTimeUtc;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static PartialKind Classify(string name)
    {
        // ".part.meta" and ".part.lock" do not also end with ".part", so the
        // order only matters for readability.
        if (name.EndsWith(PartSuffix + LockSuffix, StringComparison.OrdinalIgnoreCase))
        {
            return PartialKind.Lock;
        }

        if (name.EndsWith(PartSuffix + MetaTail, StringComparison.OrdinalIgnoreCase))
        {
            return PartialKind.Meta;
        }

        return name.EndsWith(PartSuffix, StringComparison.OrdinalIgnoreCase)
            ? PartialKind.Part
            : PartialKind.None;
    }

    /// <summary>
    /// Writer lock that guards <paramref name="name"/>. Both the payload and its
    /// sidecar share the payload's lock (<c>x.jar.part.lock</c>).
    /// </summary>
    private static string? LockPathFor(string name, PartialKind kind, string directory)
    {
        var lockName = kind switch
        {
            PartialKind.Part => name + LockSuffix,
            PartialKind.Meta => name[..^MetaTail.Length] + LockSuffix,
            _ => null,
        };

        return lockName is null ? null : Path.Combine(directory, lockName);
    }

    /// <summary>
    /// True when the group's lock was refreshed recently, so the download may
    /// still be resumed even though the payload is old. An unreadable lock is
    /// treated as active rather than risking an in-flight download.
    /// </summary>
    private static bool IsGroupLocked(string lockPath, DateTime utcNow)
    {
        if (!File.Exists(lockPath)) return false;

        try
        {
            var info = new FileInfo(lockPath);
            if (info.LinkTarget is not null) return true;
            return info.LastWriteTimeUtc >= utcNow - ActiveLockWindow;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            return true;
        }
    }

    private static bool TryDelete(FileInfo file, out long size)
    {
        size = 0;
        try
        {
            size = file.Length;
            file.Delete();
            return true;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            size = 0;
            return false;
        }
    }

    /// <summary>
    /// Reads the optional <c>olderThanDays</c> (1..365, default 3). A malformed
    /// body or an out-of-range value is reported as E_INVALID by the caller.
    /// </summary>
    private static int ReadOlderThanDays(string? paramsJson)
    {
        if (string.IsNullOrWhiteSpace(paramsJson)) return DefaultOlderThanDays;

        using var document = JsonDocument.Parse(paramsJson);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new ArgumentException("params must be a JSON object");
        }

        if (!root.TryGetProperty("olderThanDays", out var element) ||
            element.ValueKind == JsonValueKind.Null)
        {
            return DefaultOlderThanDays;
        }

        if (element.ValueKind != JsonValueKind.Number ||
            !element.TryGetInt32(out var days) ||
            days is < MinOlderThanDays or > MaxOlderThanDays)
        {
            throw new ArgumentException(
                $"olderThanDays must be an integer between {MinOlderThanDays} and {MaxOlderThanDays}");
        }

        return days;
    }

    private enum PartialKind
    {
        None = 0,
        Part = 1,
        Meta = 2,
        Lock = 3,
    }
}

/// <summary>
/// Outcome of one partial-file sweep. <see cref="ScannedFiles"/> counts file
/// entries visited (directories are not reported); <see cref="FreedBytes"/>
/// only includes files that were actually deleted.
/// </summary>
internal sealed record PartialCleanupReport(
    int ScannedFiles,
    int DeletedFiles,
    long FreedBytes,
    bool LimitReached);
