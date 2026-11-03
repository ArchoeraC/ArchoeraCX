using System.Formats.Tar;
using System.IO.Compression;
using Archoera.Application.JavaRuntime;
using Archoera.Domain.Downloads;

namespace Archoera.Infrastructure.Java.Distributions;

/// <summary>
/// Safe extractor for JDK binary archives. Vendors ship tar.gz on Linux/macOS
/// and zip on Windows; both are untrusted input, so absolute paths, <c>..</c>,
/// symlinks/hardlinks, device nodes and oversized payloads are rejected before
/// anything is written outside the staging directory.
/// </summary>
internal static class JavaArchiveExtractor
{
    public const int MaxEntries = 200_000;
    public const long MaxEntryBytes = 1L << 30; // 1 GiB
    public const long MaxTotalBytes = 3L << 30; // 3 GiB

    private const long CompressionRatioCheckBytes = 1L << 20;
    private const long MaxCompressionRatio = 200;

    /// <summary>Extracts <paramref name="archivePath"/> into an empty directory.</summary>
    public static void Extract(
        string archivePath,
        string archiveType,
        string destination,
        CancellationToken cancellationToken)
    {
        var root = Path.GetFullPath(destination);
        Directory.CreateDirectory(root);
        if (Directory.EnumerateFileSystemEntries(root).Any())
        {
            throw new JavaDistributionException(
                "JAVA_ARCHIVE_INVALID",
                "extraction directory is not empty");
        }

        if (string.Equals(archiveType, "zip", StringComparison.OrdinalIgnoreCase))
        {
            ExtractZip(archivePath, root, cancellationToken);
        }
        else if (string.Equals(archiveType, "tar.gz", StringComparison.OrdinalIgnoreCase))
        {
            ExtractTarGz(archivePath, root, cancellationToken);
        }
        else
        {
            throw new JavaDistributionException(
                "JAVA_ARCHIVE_INVALID",
                $"unsupported archive type '{archiveType}'");
        }
    }

    private static void ExtractZip(
        string archivePath,
        string root,
        CancellationToken cancellationToken)
    {
        ZipArchive archive;
        try
        {
            archive = ZipFile.OpenRead(Path.GetFullPath(archivePath));
        }
        catch (Exception exception) when (
            exception is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            throw new JavaDistributionException(
                "JAVA_ARCHIVE_INVALID",
                $"JDK archive cannot be opened: {exception.Message}");
        }

        using (archive)
        {
            if (archive.Entries.Count > MaxEntries)
            {
                throw TooLarge("archive contains too many entries");
            }

            long total = 0;
            foreach (var entry in archive.Entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var name = NormalizeEntryName(entry.FullName);
                if (name.Length == 0) continue;

                var mode = (uint)entry.ExternalAttributes >> 16;
                if ((mode & 0xF000) == 0xA000)
                {
                    var linkRelative = RequireSafeRelative(name);
                    if (entry.Length is < 0 or > 4096)
                    {
                        throw Unsafe($"{name}: symlink target is too long");
                    }

                    using var stream = entry.Open();
                    using var buffer = new MemoryStream();
                    CopyCapped(stream, buffer, 4096, cancellationToken);
                    var linkTarget = System.Text.Encoding.UTF8
                        .GetString(buffer.GetBuffer(), 0, (int)buffer.Length)
                        .TrimEnd('\0', '\r', '\n');
                    CreateSymlink(root, linkRelative, linkTarget);
                    continue;
                }

                if (name.EndsWith('/') ||
                    (entry.ExternalAttributes & 0x10) != 0 ||
                    (mode & 0xF000) == 0x4000)
                {
                    Directory.CreateDirectory(
                        Resolve(root, RequireSafeRelative(name.TrimEnd('/'))));
                    continue;
                }

                var relative = RequireSafeRelative(name);
                var target = Resolve(root, relative);
                if (entry.Length < 0 || entry.Length > MaxEntryBytes)
                {
                    throw TooLarge($"{name}: entry is larger than {MaxEntryBytes} bytes");
                }

                if (ExceedsCompressionRatio(entry.Length, entry.CompressedLength))
                {
                    throw TooLarge($"{name}: entry expands more than {MaxCompressionRatio}:1");
                }

                if (total + entry.Length > MaxTotalBytes)
                {
                    throw TooLarge($"archive expands beyond {MaxTotalBytes} bytes");
                }

                RejectSymlinkedParents(root, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                try
                {
                    long written;
                    using (var source = entry.Open())
                    using (var destination = new FileStream(
                               target,
                               FileMode.CreateNew,
                               FileAccess.Write,
                               FileShare.None,
                               bufferSize: 128 * 1024,
                               useAsync: false))
                    {
                        written = CopyCapped(
                            source,
                            destination,
                            Math.Min(MaxEntryBytes, MaxTotalBytes - total),
                            cancellationToken);
                    }

                    total += written;
                }
                catch (JavaDistributionException)
                {
                    throw;
                }
                catch (Exception exception) when (
                    exception is IOException or InvalidDataException or UnauthorizedAccessException)
                {
                    throw new JavaDistributionException(
                        "JAVA_ARCHIVE_INVALID",
                        $"{name}: cannot extract: {exception.Message}");
                }

                ApplyUnixMode(target, mode & 0x1FF);
            }
        }
    }

    private static void ExtractTarGz(
        string archivePath,
        string root,
        CancellationToken cancellationToken)
    {
        try
        {
            using var file = File.OpenRead(Path.GetFullPath(archivePath));
            using var gzip = new GZipStream(file, CompressionMode.Decompress);
            using var reader = new TarReader(gzip);
            long total = 0;
            var entries = 0;
            var deferredLinks = new List<(string Link, string Target)>();

            while (reader.GetNextEntry(copyData: false) is { } entry)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (++entries > MaxEntries)
                {
                    throw TooLarge("archive contains too many entries");
                }

                var name = NormalizeEntryName(entry.Name);
                if (name.Length == 0) continue;

                switch (entry.EntryType)
                {
                    case TarEntryType.Directory:
                        Directory.CreateDirectory(
                            Resolve(root, RequireSafeRelative(name.TrimEnd('/'))));
                        continue;

                    case TarEntryType.SymbolicLink:
                        CreateSymlink(
                            root,
                            RequireSafeRelative(name),
                            entry.LinkName);
                        continue;

                    case TarEntryType.HardLink:
                        deferredLinks.Add((
                            RequireSafeRelative(name),
                            RequireSafeRelative(entry.LinkName ?? string.Empty)));
                        continue;

                    case TarEntryType.CharacterDevice:
                    case TarEntryType.BlockDevice:
                    case TarEntryType.Fifo:
                        throw Unsafe($"archive contains a device entry: {entry.Name}");

                    case TarEntryType.RegularFile:
                    case TarEntryType.V7RegularFile:
                    case TarEntryType.ContiguousFile:
                        break;

                    default:
                        // PAX/global headers are consumed by TarReader and do
                        // not carry payload the JDK needs.
                        continue;
                }

                var relative = RequireSafeRelative(name);
                var target = Resolve(root, relative);
                if (entry.Length < 0 || entry.Length > MaxEntryBytes)
                {
                    throw TooLarge($"{name}: entry is larger than {MaxEntryBytes} bytes");
                }

                if (total + entry.Length > MaxTotalBytes)
                {
                    throw TooLarge($"archive expands beyond {MaxTotalBytes} bytes");
                }

                RejectSymlinkedParents(root, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                var stream = entry.DataStream;
                if (stream is null)
                {
                    throw new JavaDistributionException(
                        "JAVA_ARCHIVE_INVALID",
                        $"{name}: archive entry has no payload");
                }

                long written;
                using (var destination = new FileStream(
                           target,
                           FileMode.CreateNew,
                           FileAccess.Write,
                           FileShare.None,
                           bufferSize: 128 * 1024,
                           useAsync: false))
                {
                    written = CopyCapped(
                        stream,
                        destination,
                        Math.Min(MaxEntryBytes, MaxTotalBytes - total),
                        cancellationToken);
                }

                total += written;
                ApplyUnixMode(target, (uint)entry.Mode & 0x1FF);
            }

            // Hardlinks may point at an entry that appears later in the tar.
            // Copy the final file (content-identical, no cross-root link).
            foreach (var (link, linkTarget) in deferredLinks)
            {
                var source = Resolve(root, linkTarget);
                if (!File.Exists(source))
                {
                    throw new JavaDistributionException(
                        "JAVA_ARCHIVE_INVALID",
                        $"{link}: hardlink target '{linkTarget}' is missing");
                }

                RejectSymlinkedParents(root, link);
                var destination = Resolve(root, link);
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                if (File.Exists(destination) || Directory.Exists(destination))
                {
                    throw new JavaDistributionException(
                        "JAVA_ARCHIVE_INVALID",
                        $"{link}: hardlink destination already exists");
                }

                total += new FileInfo(source).Length;
                if (total > MaxTotalBytes)
                {
                    throw TooLarge($"archive expands beyond {MaxTotalBytes} bytes");
                }

                File.Copy(source, destination);
            }
        }
        catch (JavaDistributionException)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            throw new JavaDistributionException(
                "JAVA_ARCHIVE_INVALID",
                $"JDK archive is corrupt: {exception.Message}");
        }
    }

    /// <summary>
    /// JDK tar archives contain relative symlinks such as
    /// <c>legal/jdk.jdeps/LICENSE -> ../java.base/LICENSE</c>. Links are
    /// allowed only when the resolved target stays inside the staging root.
    /// </summary>
    private static void CreateSymlink(string root, string relative, string? rawTarget)
    {
        var linkTarget = NormalizeEntryName(rawTarget);
        if (string.IsNullOrWhiteSpace(linkTarget) ||
            linkTarget.Contains('\0') ||
            linkTarget.Contains(':') ||
            linkTarget[0] == '/' ||
            Path.IsPathRooted(linkTarget))
        {
            throw Unsafe($"{relative}: symlink target must be a relative archive path");
        }

        RejectSymlinkedParents(root, relative);
        var destination = Resolve(root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        if (File.Exists(destination) || Directory.Exists(destination))
        {
            throw new JavaDistributionException(
                "JAVA_ARCHIVE_INVALID",
                $"{relative}: symlink destination already exists");
        }

        var resolvedTarget = Path.GetFullPath(Path.Combine(
            Path.GetDirectoryName(destination)!,
            linkTarget.Replace('/', Path.DirectorySeparatorChar)));
        if (!IsUnder(root, resolvedTarget))
        {
            throw Unsafe($"{relative}: symlink target escapes the staging directory");
        }

        try
        {
            // NTFS stores the target verbatim and Win32 resolves it with path
            // rules that reject '/', so a relative link written as
            // "../java.base/LICENSE" opens with ERROR_INVALID_NAME. Unix keeps
            // the archive's literal forward slashes.
            var nativeTarget = OperatingSystem.IsWindows()
                ? linkTarget.Replace('/', Path.DirectorySeparatorChar)
                : linkTarget;
            File.CreateSymbolicLink(destination, nativeTarget);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or
            ArgumentException or NotSupportedException)
        {
            // Windows without symlink privilege, or a filesystem without
            // link support: copy the already-extracted target instead of
            // failing an otherwise usable JDK.
            if (File.Exists(resolvedTarget))
            {
                File.Copy(resolvedTarget, destination);
                return;
            }

            throw new JavaDistributionException(
                "JAVA_ARCHIVE_INVALID",
                $"{relative}: cannot create symlink: {exception.Message}");
        }
    }

    private static void RejectSymlinkedParents(string root, string relative)
    {
        var parts = relative.Split('/');
        var current = root;
        for (var index = 0; index < parts.Length - 1; index++)
        {
            current = Path.Combine(current, parts[index]);
            if (!Directory.Exists(current) && !File.Exists(current)) break;
            if (IsLink(current))
            {
                throw Unsafe($"{relative}: parent '{current}' is a symlink");
            }
        }
    }

    private static bool IsUnder(string root, string path)
    {
        var prefix = root.EndsWith(Path.DirectorySeparatorChar)
            ? root
            : root + Path.DirectorySeparatorChar;
        return path.StartsWith(
            prefix,
            OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal);
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

    private static long CopyCapped(
        Stream source,
        Stream destination,
        long cap,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[128 * 1024];
        long written = 0;
        while (true)
        {
            var read = source.Read(buffer, 0, buffer.Length);
            if (read == 0) break;
            written += read;
            if (written > cap)
            {
                throw TooLarge("archive entry exceeds the uncompressed size limit");
            }

            destination.Write(buffer, 0, read);
            cancellationToken.ThrowIfCancellationRequested();
        }

        return written;
    }

    private static string NormalizeEntryName(string? name)
    {
        var normalized = (name ?? string.Empty).Replace('\\', '/');
        while (normalized.StartsWith("./", StringComparison.Ordinal))
        {
            normalized = normalized[2..];
        }

        return normalized;
    }

    private static string RequireSafeRelative(string name)
    {
        var issue = DownloadPlanPaths.TryNormalizeDest(name, out var relative);
        return issue is null
            ? relative
            : throw Unsafe($"unsafe archive path '{name}': {issue.Message}");
    }

    private static string Resolve(string root, string relative)
    {
        var destination = Path.GetFullPath(
            Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
        var prefix = root.EndsWith(Path.DirectorySeparatorChar)
            ? root
            : root + Path.DirectorySeparatorChar;
        if (!destination.StartsWith(
                prefix,
                OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
                    ? StringComparison.OrdinalIgnoreCase
                    : StringComparison.Ordinal))
        {
            throw Unsafe($"archive path '{relative}' escapes the staging directory");
        }

        return destination;
    }

    private static bool ExceedsCompressionRatio(long uncompressed, long compressed)
    {
        if (uncompressed <= CompressionRatioCheckBytes) return false;
        if (compressed <= 0) return true;
        return uncompressed / (double)compressed > MaxCompressionRatio;
    }

    private static void ApplyUnixMode(string path, uint mode)
    {
        if (OperatingSystem.IsWindows() || mode == 0) return;
        try
        {
            File.SetUnixFileMode(path, (UnixFileMode)(mode & 0x1FF));
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or
            ArgumentException or NotSupportedException)
        {
            // Preserved permissions are best-effort; bin/java is normalized
            // after extraction.
        }
    }

    private static JavaDistributionException Unsafe(string message)
        => new("JAVA_ARCHIVE_UNSAFE", message);

    private static JavaDistributionException TooLarge(string message)
        => new("JAVA_ARCHIVE_TOO_LARGE", message);
}
