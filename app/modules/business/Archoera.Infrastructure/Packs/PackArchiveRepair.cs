using System.IO.Compression;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Archoera.Application.Packs;
using Archoera.Domain.Downloads;

namespace Archoera.Infrastructure.Packs;

/// <summary>Outcome of normalizing a pack archive before parsing.</summary>
/// <param name="BackupPath">Copy of the untouched archive next to the original.</param>
/// <param name="RepairedPaths">Number of index paths that used backslashes.</param>
public sealed record PackArchiveRepairResult(string BackupPath, int RepairedPaths);

/// <summary>
/// Repairs the Windows path separators some third-party pack exporters write
/// into <c>modrinth.index.json</c> (for example
/// <c>mods\example.jar</c>). The Modrinth format uses forward slashes; the
/// strict download-plan rules reject backslashes so the same archive would be
/// refused on every platform.
///
/// The original archive is copied to <c>&lt;pack&gt;.mrpack.bak</c> before the
/// index is replaced, and the rewritten archive must parse cleanly before it
/// replaces the original. Paths that are still unsafe after normalization
/// (parent escapes, absolute paths, reserved roots) are never repaired, so the
/// strict parser reports them unchanged.
/// </summary>
public static class PackArchiveRepair
{
    /// <summary>Largest archive this repair will copy; larger packs are left alone.</summary>
    public const long MaxArchiveBytes = 32L << 30; // 32 GiB

    public const int MaxArchiveEntries = 250_000;

    /// <summary>
    /// Reads the index, repairing Windows separators once when the strict
    /// parser reports <see cref="PackErrorCode.Invalid"/>.
    /// </summary>
    public static (PackArchiveContents Contents, PackArchiveRepairResult? Repair) ReadWithRepair(
        string archivePath,
        bool clientSide = true)
    {
        var fullPath = Path.GetFullPath(archivePath);
        try
        {
            return (PackArchive.Read(fullPath, clientSide), null);
        }
        catch (PackException exception) when (exception.Code == PackErrorCode.Invalid)
        {
            if (!TryRepair(fullPath, clientSide, out var repair))
            {
                throw;
            }

            return (PackArchive.Read(fullPath, clientSide), repair);
        }
    }

    /// <summary>
    /// Backs up and rewrites <paramref name="archivePath"/> when its index
    /// contains backslash paths. Returns false (leaving the file untouched)
    /// for every other defect.
    /// </summary>
    public static bool TryRepair(
        string archivePath,
        bool clientSide,
        out PackArchiveRepairResult repair)
    {
        repair = default!;
        var fullPath = Path.GetFullPath(archivePath);
        if (!File.Exists(fullPath))
        {
            return false;
        }

        try
        {
            var info = new FileInfo(fullPath);
            if (info.Length <= 0 || info.Length > MaxArchiveBytes)
            {
                return false;
            }
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }

        byte[] repairedIndex;
        int repairedPaths;
        try
        {
            using var archive = ZipFile.OpenRead(fullPath);
            if (archive.Entries.Count > MaxArchiveEntries)
            {
                return false;
            }

            var indexBytes = PackArchive.ReadIndexBytes(archive);
            if (!TryRewriteIndex(indexBytes, out repairedIndex, out repairedPaths))
            {
                return false;
            }
        }
        catch (Exception exception) when (
            exception is IOException or InvalidDataException or UnauthorizedAccessException or
            PackException or JsonException or ArgumentException or
            InvalidOperationException or NotSupportedException)
        {
            return false;
        }

        string? backup = null;
        string? temporary = null;
        try
        {
            backup = CreateBackup(fullPath);
            temporary = $"{fullPath}.{Guid.NewGuid():N}.repair";
            File.Copy(fullPath, temporary, overwrite: false);
            ReplaceIndex(temporary, repairedIndex);

            // The repaired archive must pass the same strict parser before the
            // original is replaced; otherwise the user keeps the original file.
            _ = PackArchive.Read(temporary, clientSide);

            File.Move(temporary, fullPath, overwrite: true);
            temporary = null;
            repair = new PackArchiveRepairResult(backup, repairedPaths);
            return true;
        }
        catch (Exception exception) when (
            exception is IOException or InvalidDataException or UnauthorizedAccessException or
            PackException or ArgumentException or InvalidOperationException or
            NotSupportedException)
        {
            TryDelete(temporary);
            if (backup is not null)
            {
                TryDelete(backup);
            }

            return false;
        }
    }

    /// <summary>
    /// Rewrites only file paths that contain a backslash. Every rewritten path
    /// must still pass the normal destination rules.
    /// </summary>
    private static bool TryRewriteIndex(
        byte[] utf8,
        out byte[] repaired,
        out int repairedPaths)
    {
        repaired = [];
        repairedPaths = 0;

        JsonNode? root;
        try
        {
            root = JsonNode.Parse(utf8);
        }
        catch (JsonException)
        {
            return false;
        }
        catch (ArgumentException)
        {
            return false;
        }

        if (root is not JsonObject rootObject ||
            rootObject["files"] is not JsonArray files)
        {
            return false;
        }

        foreach (var node in files)
        {
            if (node is not JsonObject file ||
                file["path"] is not JsonValue value ||
                !value.TryGetValue<string>(out var rawPath) ||
                !rawPath.Contains('\\'))
            {
                continue;
            }

            var issue = DownloadPlanPaths.TryNormalizeDest(
                rawPath.Replace('\\', '/'),
                out var normalized);
            if (issue is not null)
            {
                return false;
            }

            try
            {
                ModpackIndexJson.RejectReservedTarget(normalized);
            }
            catch (PackException)
            {
                return false;
            }

            file["path"] = normalized;
            repairedPaths++;
        }

        if (repairedPaths == 0)
        {
            return false;
        }

        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };
        var text = rootObject.ToJsonString(options) + "\n";
        repaired = Encoding.UTF8.GetBytes(text);

        try
        {
            // Validate the whole index (hashes, URLs, duplicates) before it is
            // written back into the archive.
            _ = ModpackIndexJson.Parse(repaired);
        }
        catch (PackException)
        {
            return false;
        }

        return true;
    }

    private static void ReplaceIndex(string archivePath, byte[] indexBytes)
    {
        using var archive = ZipFile.Open(archivePath, ZipArchiveMode.Update);
        ZipArchiveEntry? entry = null;
        foreach (var candidate in archive.Entries)
        {
            if (!string.Equals(
                    candidate.FullName,
                    ModpackIndexJson.IndexEntryName,
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (entry is not null)
            {
                throw new InvalidDataException(
                    "archive contains more than one modrinth.index.json");
            }

            entry = candidate;
        }

        if (entry is null)
        {
            throw new InvalidDataException(
                $"archive does not contain {ModpackIndexJson.IndexEntryName}");
        }

        var name = entry.FullName;
        var timestamp = entry.LastWriteTime;
        var attributes = entry.ExternalAttributes;
        entry.Delete();

        var replacement = archive.CreateEntry(name, CompressionLevel.Optimal);
        if (timestamp != default)
        {
            replacement.LastWriteTime = timestamp;
        }

        replacement.ExternalAttributes = attributes;
        using var stream = replacement.Open();
        stream.Write(indexBytes);
    }

    private static string CreateBackup(string path)
    {
        var candidate = path + ".bak";
        var suffix = 2;
        while (File.Exists(candidate))
        {
            candidate = $"{path}.bak{suffix++}";
        }

        File.Copy(path, candidate, overwrite: false);
        return candidate;
    }

    private static void TryDelete(string? path)
    {
        if (path is null) return;
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            // Best effort; a leftover backup never hides the original file.
        }
    }
}
