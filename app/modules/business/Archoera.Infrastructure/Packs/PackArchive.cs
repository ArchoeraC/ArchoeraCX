using System.IO.Compression;
using Archoera.Application.Packs;
using Archoera.Domain;
using Archoera.Domain.Downloads;

namespace Archoera.Infrastructure.Packs;

/// <summary>One file under <c>overrides/</c>, <c>client-overrides/</c> or <c>server-overrides/</c>.</summary>
/// <param name="EntryName">Raw archive entry name.</param>
/// <param name="RelativePath">Normalized instance-relative destination.</param>
/// <param name="Length">Declared uncompressed size.</param>
/// <param name="ExternalAttributes">ZIP external attributes (Unix mode in the high 16 bits).</param>
public sealed record PackArchiveEntry(
    string EntryName,
    string RelativePath,
    long Length,
    int ExternalAttributes);

/// <summary>Index + override inventory read from one archive.</summary>
public sealed record PackArchiveContents(
    ModpackIndex Index,
    IReadOnlyList<PackArchiveEntry> Overrides,
    long OverrideBytes,
    int IgnoredServerFiles);

/// <summary>
/// Reads and safely extracts Modrinth <c>.mrpack</c> archives. This is C#-owned
/// because the Rust executor deliberately stays a DownloadPlan-only consumer.
///
/// The archive is untrusted: symlink entries, absolute/`..` paths, writes into
/// the launcher metadata folder, and oversized payloads are rejected. Overrides
/// are applied before any index download, so bundled files win.
/// </summary>
public static class PackArchive
{
    public const int MaxOverrideEntries = 100_000;
    public const long MaxOverrideEntryBytes = 8L << 30; // 8 GiB
    public const long MaxOverrideTotalBytes = 16L << 30; // 16 GiB

    /// <summary>
    /// A single large entry may not expand more than this. Highly compressible
    /// payloads above the threshold are treated as a zip bomb even when the
    /// declared sizes stay below the absolute limits.
    /// </summary>
    internal const long CompressionRatioCheckBytes = 1L << 20; // 1 MiB
    internal const long MaxCompressionRatio = 200;

    /// <summary>Free space kept after writing all overrides.</summary>
    internal const long FreeSpaceReserveBytes = 64L << 20; // 64 MiB

    private const string OverridesPrefix = "overrides/";
    private const string ClientOverridesPrefix = "client-overrides/";
    private const string ServerOverridesPrefix = "server-overrides/";

    /// <summary>Reads the index and enumerates override entries without extracting.</summary>
    /// <param name="archivePath">Local <c>.mrpack</c> path.</param>
    /// <param name="clientSide">Use client overrides (true) or server overrides.</param>
    public static PackArchiveContents Read(string archivePath, bool clientSide = true)
    {
        var fullPath = Path.GetFullPath(archivePath);
        if (!File.Exists(fullPath))
        {
            throw new PackException(
                PackErrorCode.ArchiveInvalid,
                $"pack archive not found: {fullPath}");
        }

        ZipArchive archive;
        try
        {
            archive = ZipFile.OpenRead(fullPath);
        }
        catch (Exception exception) when (
            exception is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            throw new PackException(
                PackErrorCode.ArchiveInvalid,
                $"pack archive cannot be opened: {exception.Message}",
                exception);
        }

        using (archive)
        {
            try
            {
                if (archive.Entries.Count > 250_000)
                {
                    throw new PackException(
                        PackErrorCode.TooLarge,
                        "archive contains more than 250000 entries");
                }

                var indexBytes = ReadIndexBytes(archive);
                var index = ModpackIndexJson.Parse(indexBytes);
                var overrides = ReadOverrides(archive, clientSide, out var ignoredServer);

                return new PackArchiveContents(
                    index,
                    overrides.Entries,
                    overrides.TotalBytes,
                    ignoredServer);
            }
            catch (PackException)
            {
                throw;
            }
            catch (Exception exception) when (
                exception is InvalidDataException or IOException)
            {
                throw new PackException(
                    PackErrorCode.ArchiveInvalid,
                    $"pack archive is corrupt: {exception.Message}",
                    exception);
            }
        }
    }

    private static byte[] ReadIndexBytes(ZipArchive archive)
    {
        ZipArchiveEntry? indexEntry = null;
        foreach (var entry in archive.Entries)
        {
            if (!string.Equals(
                    entry.FullName,
                    ModpackIndexJson.IndexEntryName,
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (indexEntry is not null)
            {
                throw new PackException(
                    PackErrorCode.ArchiveInvalid,
                    "archive contains more than one modrinth.index.json");
            }

            indexEntry = entry;
        }

        if (indexEntry is null)
        {
            throw new PackException(
                PackErrorCode.ArchiveInvalid,
                $"archive does not contain {ModpackIndexJson.IndexEntryName}");
        }

        if (indexEntry.Length > ModpackIndexJson.MaxIndexBytes)
        {
            throw new PackException(
                PackErrorCode.TooLarge,
                $"{ModpackIndexJson.IndexEntryName} is larger than " +
                $"{ModpackIndexJson.MaxIndexBytes} bytes");
        }

        try
        {
            using var stream = indexEntry.Open();
            using var memory = new MemoryStream((int)Math.Max(0, indexEntry.Length));
            var buffer = new byte[16 * 1024];
            var total = 0;
            while (true)
            {
                var read = stream.Read(buffer, 0, buffer.Length);
                if (read == 0) break;
                total += read;
                if (total > ModpackIndexJson.MaxIndexBytes)
                {
                    throw new PackException(
                        PackErrorCode.TooLarge,
                        $"{ModpackIndexJson.IndexEntryName} exceeds the index size limit");
                }

                memory.Write(buffer, 0, read);
            }

            return memory.ToArray();
        }
        catch (PackException)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is IOException or InvalidDataException)
        {
            throw new PackException(
                PackErrorCode.ArchiveInvalid,
                $"cannot read {ModpackIndexJson.IndexEntryName}: {exception.Message}",
                exception);
        }
    }

    private static (IReadOnlyList<PackArchiveEntry> Entries, long TotalBytes, int IgnoredServer)
        ReadOverrides(ZipArchive archive, bool clientSide, out int ignoredServer)
    {
        ignoredServer = 0;
        var byPath = new Dictionary<string, PackArchiveEntry>(
            PathSemantics.Comparer);
        var insertionOrder = new List<string>();
        long totalBytes = 0;
        var entries = 0;

        foreach (var entry in archive.Entries)
        {
            var name = entry.FullName;
            var unixMode = (uint)entry.ExternalAttributes >> 16;
            if (name.EndsWith('/') ||
                (entry.ExternalAttributes & 0x10) != 0 ||
                (unixMode & 0xF000) == 0x4000)
            {
                continue;
            }

            var (prefix, applies) = Classify(name, clientSide);
            if (prefix is null) continue;
            if (!applies)
            {
                if (string.Equals(prefix, ServerOverridesPrefix, StringComparison.Ordinal))
                {
                    ignoredServer++;
                }

                continue;
            }
            entries++;
            if (entries > MaxOverrideEntries)
            {
                throw new PackException(
                    PackErrorCode.TooLarge,
                    $"archive contains more than {MaxOverrideEntries} override files");
            }

            RejectLinkEntry(entry);
            var relativeRaw = name[prefix.Length..];
            var issue = DownloadPlanPaths.TryNormalizeDest(relativeRaw, out var relative);
            if (issue is not null)
            {
                throw new PackException(
                    issue.Code == DownloadPlanErrorCode.PathEscape
                        ? PackErrorCode.PathEscape
                        : PackErrorCode.ArchiveInvalid,
                    $"unsafe override path '{name}': {issue.Message}");
            }

            ModpackIndexJson.RejectReservedTarget(relative);

            var length = entry.Length;
            if (length < 0 || length > MaxOverrideEntryBytes)
            {
                throw new PackException(
                    PackErrorCode.TooLarge,
                    $"{name}: override is larger than {MaxOverrideEntryBytes} bytes");
            }

            if (ExceedsCompressionRatio(length, entry.CompressedLength))
            {
                throw new PackException(
                    PackErrorCode.TooLarge,
                    $"{name}: override expands more than {MaxCompressionRatio}:1");
            }

            totalBytes += length;
            if (totalBytes > MaxOverrideTotalBytes)
            {
                throw new PackException(
                    PackErrorCode.TooLarge,
                    $"archive overrides exceed {MaxOverrideTotalBytes} bytes uncompressed");
            }

            var value = new PackArchiveEntry(
                name,
                relative,
                length,
                entry.ExternalAttributes);

            if (byPath.ContainsKey(relative))
            {
                // client-overrides wins over shared overrides; the archive order
                // is not guaranteed, so compare prefixes explicitly.
                var existing = byPath[relative];
                if (ShouldReplace(existing, value))
                {
                    byPath[relative] = value;
                }
            }
            else
            {
                byPath[relative] = value;
                insertionOrder.Add(relative);
            }
        }

        var result = new List<PackArchiveEntry>(byPath.Count);
        foreach (var relative in insertionOrder)
        {
            result.Add(byPath[relative]);
        }

        return (result, totalBytes, ignoredServer);
    }

    private static (string? Prefix, bool Applies) Classify(string name, bool clientSide)
    {
        if (name.StartsWith(OverridesPrefix, StringComparison.Ordinal))
        {
            return (OverridesPrefix, true);
        }

        if (name.StartsWith(ClientOverridesPrefix, StringComparison.Ordinal))
        {
            return (ClientOverridesPrefix, clientSide);
        }

        if (name.StartsWith(ServerOverridesPrefix, StringComparison.Ordinal))
        {
            return (ServerOverridesPrefix, !clientSide);
        }

        return (null, false);
    }

    private static bool ShouldReplace(PackArchiveEntry existing, PackArchiveEntry candidate)
    {
        var existingClient = existing.EntryName.StartsWith(
            ClientOverridesPrefix,
            StringComparison.Ordinal);
        var candidateClient = candidate.EntryName.StartsWith(
            ClientOverridesPrefix,
            StringComparison.Ordinal);

        if (candidateClient != existingClient)
        {
            return candidateClient;
        }

        return false;
    }

    private static void RejectLinkEntry(ZipArchiveEntry entry)
    {
        var mode = (uint)entry.ExternalAttributes >> 16;
        const uint fileTypeMask = 0xF000;
        const uint symbolicLink = 0xA000;

        if ((mode & fileTypeMask) == symbolicLink)
        {
            throw new PackException(
                PackErrorCode.ArchiveInvalid,
                $"archive contains a symlink entry: {entry.FullName}");
        }
    }

    /// <summary>
    /// Extracts the enumerated overrides into <paramref name="root"/>. Files are
    /// written through a sidecar temporary and moved into place atomically.
    /// </summary>
    /// <returns>Relative paths that were written.</returns>
    public static IReadOnlyList<string> Extract(
        string archivePath,
        string root,
        IReadOnlyList<PackArchiveEntry> entries,
        CancellationToken cancellationToken = default,
        Action<int, int>? progress = null)
    {
        ArgumentNullException.ThrowIfNull(entries);

        var rootFull = Path.GetFullPath(root);
        if (!Directory.Exists(rootFull))
        {
            throw new PackException(
                PackErrorCode.InstanceFailed,
                $"instance root does not exist: {rootFull}");
        }

        if (entries.Count > 0)
        {
            EnsureFreeSpace(rootFull, entries.Sum(entry => entry.Length));
        }

        var written = new List<string>(entries.Count);
        ZipArchive archive;
        try
        {
            archive = ZipFile.OpenRead(Path.GetFullPath(archivePath));
        }
        catch (Exception exception) when (
            exception is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            throw new PackException(
                PackErrorCode.ArchiveInvalid,
                $"pack archive cannot be opened: {exception.Message}",
                exception);
        }

        using (archive)
        {
            var byName = new Dictionary<string, ZipArchiveEntry>(StringComparer.Ordinal);
            foreach (var entry in archive.Entries)
            {
                if (!byName.TryAdd(entry.FullName, entry))
                {
                    throw new PackException(
                        PackErrorCode.ArchiveInvalid,
                        $"archive contains duplicate entry '{entry.FullName}'");
                }
            }

            var buffer = new byte[128 * 1024];
            var index = 0;
            long totalWritten = 0;
            foreach (var item in entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!byName.TryGetValue(item.EntryName, out var entry))
                {
                    throw new PackException(
                        PackErrorCode.ArchiveInvalid,
                        $"override entry disappeared: {item.EntryName}");
                }

                totalWritten += ExtractOne(
                    rootFull,
                    entry,
                    item.RelativePath,
                    buffer,
                    cancellationToken);
                if (totalWritten > MaxOverrideTotalBytes)
                {
                    throw new PackException(
                        PackErrorCode.TooLarge,
                        $"archive overrides exceed {MaxOverrideTotalBytes} bytes uncompressed");
                }

                written.Add(item.RelativePath);
                index++;
                if (index % 32 == 0 || index == entries.Count)
                {
                    progress?.Invoke(index, entries.Count);
                }
            }
        }

        return written;
    }

    private static long ExtractOne(
        string rootFull,
        ZipArchiveEntry entry,
        string relative,
        byte[] buffer,
        CancellationToken cancellationToken)
    {
        if (ExceedsCompressionRatio(entry.Length, entry.CompressedLength))
        {
            throw new PackException(
                PackErrorCode.TooLarge,
                $"{relative}: override expands more than {MaxCompressionRatio}:1");
        }

        var destination = ResolveDestination(rootFull, relative);
        RejectSymlinkedParents(rootFull, relative);
        var parent = Path.GetDirectoryName(destination)
            ?? throw new PackException(
                PackErrorCode.ArchiveInvalid,
                $"{relative}: destination has no parent");

        Directory.CreateDirectory(parent);

        if (Directory.Exists(destination))
        {
            throw new PackException(
                PackErrorCode.ArchiveInvalid,
                $"{relative}: destination is a directory");
        }

        if (File.Exists(destination) && IsLink(destination))
        {
            throw new PackException(
                PackErrorCode.PathEscape,
                $"{relative}: destination is a symlink");
        }

        var temporary = Path.Combine(
            parent,
            $".{Path.GetFileName(destination)}.{Guid.NewGuid():N}.packtmp");
        long total = 0;
        try
        {
            using (var source = entry.Open())
            using (var target = new FileStream(
                       temporary,
                       FileMode.Create,
                       FileAccess.Write,
                       FileShare.None,
                       bufferSize: buffer.Length,
                       useAsync: false))
            {
                while (true)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var read = source.Read(buffer, 0, buffer.Length);
                    if (read == 0) break;
                    total += read;
                    if (total > MaxOverrideEntryBytes)
                    {
                        throw new PackException(
                            PackErrorCode.TooLarge,
                            $"{relative}: override exceeds the uncompressed size limit");
                    }

                    target.Write(buffer, 0, read);
                }
            }

            File.Move(temporary, destination, overwrite: true);
        }
        catch (PackException)
        {
            TryDelete(temporary);
            throw;
        }
        catch (OperationCanceledException)
        {
            TryDelete(temporary);
            throw;
        }
        catch (Exception exception) when (
            exception is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            TryDelete(temporary);
            throw new PackException(
                PackErrorCode.ArchiveInvalid,
                $"{relative}: cannot extract: {exception.Message}",
                exception);
        }

        ApplyPermissions(destination, entry);
        return total;
    }

    /// <summary>
    /// True when a large entry compresses so well that extracting it would
    /// amplify disk use disproportionately. Small entries are exempt because
    /// their absolute cost is negligible.
    /// </summary>
    internal static bool ExceedsCompressionRatio(long uncompressed, long compressed)
    {
        if (uncompressed <= CompressionRatioCheckBytes) return false;
        if (compressed <= 0) return true;
        return uncompressed / (double)compressed > MaxCompressionRatio;
    }

    /// <summary>
    /// Bytes that must be free before extracting <paramref name="totalUncompressed"/>
    /// bytes: the payload plus 10% for filesystem overhead and a fixed reserve.
    /// </summary>
    internal static long RequiredFreeBytes(long totalUncompressed)
    {
        if (totalUncompressed < 0) return long.MaxValue;
        if (totalUncompressed > (long.MaxValue - FreeSpaceReserveBytes) / 2)
        {
            return long.MaxValue;
        }

        var withOverhead = totalUncompressed + (totalUncompressed / 10);
        if (totalUncompressed > 0) withOverhead++;
        return withOverhead + FreeSpaceReserveBytes;
    }

    private static void EnsureFreeSpace(string rootFull, long totalUncompressed)
    {
        var required = RequiredFreeBytes(totalUncompressed);
        try
        {
            var volumeRoot = Path.GetPathRoot(rootFull);
            if (string.IsNullOrEmpty(volumeRoot)) return;

            var drive = new DriveInfo(volumeRoot);
            if (!drive.IsReady) return;
            if (drive.AvailableFreeSpace >= required) return;
        }
        catch (Exception exception) when (
            exception is ArgumentException or IOException or UnauthorizedAccessException or
            NotSupportedException)
        {
            // Unknown/virtual volumes cannot be measured; extraction still
            // enforces the entry and total byte limits while writing.
            return;
        }

        throw new PackException(
            PackErrorCode.TooLarge,
            $"not enough free space for {totalUncompressed} bytes of overrides");
    }

    private static string ResolveDestination(string rootFull, string relative)
    {
        var destination = Path.GetFullPath(
            Path.Combine(
                rootFull,
                relative.Replace('/', Path.DirectorySeparatorChar)));

        var prefix = rootFull.EndsWith(Path.DirectorySeparatorChar)
            ? rootFull
            : rootFull + Path.DirectorySeparatorChar;
        if (!destination.StartsWith(prefix, PathComparison))
        {
            throw new PackException(
                PackErrorCode.PathEscape,
                $"{relative}: destination escapes the instance root");
        }

        return destination;
    }

    private static void RejectSymlinkedParents(string rootFull, string relative)
    {
        var parts = relative.Split('/');
        var current = rootFull;
        for (var index = 0; index < parts.Length - 1; index++)
        {
            current = Path.Combine(current, parts[index]);
            if (!Directory.Exists(current) && !File.Exists(current)) break;
            if (IsLink(current))
            {
                throw new PackException(
                    PackErrorCode.PathEscape,
                    $"{relative}: parent '{current}' is a symlink");
            }
        }
    }

    private static bool IsLink(string path)
    {
        try
        {
            return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static void ApplyPermissions(string path, ZipArchiveEntry entry)
    {
        if (OperatingSystem.IsWindows()) return;

        try
        {
            var mode = ((uint)entry.ExternalAttributes >> 16) & 0x1FF;
            if (mode != 0)
            {
                File.SetUnixFileMode(path, (UnixFileMode)mode);
            }

            if (entry.LastWriteTime != default)
            {
                File.SetLastWriteTimeUtc(path, entry.LastWriteTime.UtcDateTime);
            }
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or
            ArgumentException or NotSupportedException)
        {
            // Permissions/timestamps are best-effort; content is already there.
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            // Best effort; the next attempt overwrites the sidecar.
        }
    }

    private static StringComparison PathComparison =>
        OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
}
