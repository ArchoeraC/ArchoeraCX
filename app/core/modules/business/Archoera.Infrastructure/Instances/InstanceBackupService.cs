using System.Security.Cryptography;
using System.Text.Json;
using Archoera.Application.Instances;
using Archoera.Domain.Instances;
using Archoera.Infrastructure.Settings;
using Microsoft.Data.Sqlite;

namespace Archoera.Infrastructure.Instances;

/// <summary>
/// Snapshot of one instance directory taken before an update. The snapshot
/// lives in <c>&lt;instance root&gt;/.archoera/backups/&lt;id&gt;</c>, carries a
/// manifest with a SHA-256 per file and can be restored atomically enough for
/// the launcher's update flow.
/// </summary>
/// <remarks>
/// The backup is deliberately stored next to the data it protects: a user can
/// find and delete it with the file manager, and it follows the instance when
/// the folder is moved. The manifest is written last, so an interrupted copy
/// is never treated as a restorable backup.
/// </remarks>
internal sealed class InstanceBackupService
{
    private const string ManifestName = "manifest.json";
    private const string BackupsDirectoryName = "backups";
    private const int ManifestSchemaVersion = 1;

    private readonly IInstanceRepository _repository;
    private readonly JsonAppSettingsStore _settings;

    public InstanceBackupService(
        IInstanceRepository repository,
        JsonAppSettingsStore settings)
    {
        _repository = repository;
        _settings = settings;
    }

    public string BackupsRoot(string instanceRoot) =>
        Path.Combine(instanceRoot, ".archoera", BackupsDirectoryName);

    /// <summary>
    /// Copy the instance directory (except earlier backups) into a new
    /// snapshot and write its manifest. Throws when the instance is not
    /// managed or the copy fails; a partial snapshot is removed.
    /// </summary>
    public async Task<InstanceBackupSnapshot> CreateAsync(
        string instanceId,
        CancellationToken cancellationToken)
    {
        var (record, root) = await ResolveInstanceAsync(instanceId, cancellationToken)
            .ConfigureAwait(false);

        var backupsRoot = BackupsRoot(root);
        Directory.CreateDirectory(backupsRoot);
        var backupId = NewBackupId();
        var backupDirectory = Path.Combine(backupsRoot, backupId);

        var files = new List<BackupFile>();
        try
        {
            Directory.CreateDirectory(backupDirectory);
            CopyTree(root, backupDirectory, backupsRoot, string.Empty, files,
                cancellationToken);
            await WriteManifestAsync(backupDirectory, record, files, cancellationToken)
                .ConfigureAwait(false);
        }
        catch
        {
            TryDeleteDirectory(backupDirectory);
            throw;
        }

        var totalBytes = files.Sum(file => file.Size);
        return new InstanceBackupSnapshot(
            instanceId,
            backupId,
            backupDirectory,
            files.Count,
            totalBytes);
    }

    /// <summary>
    /// Restore a snapshot over the instance directory. The snapshot is only
    /// removed after every restored file matches the manifest; on any failure
    /// it stays on disk so the caller can retry or point the user at it.
    /// </summary>
    public async Task<RestoreOutcome> RestoreAsync(
        string instanceId,
        string backupId,
        CancellationToken cancellationToken)
    {
        var (record, root) = await ResolveInstanceAsync(instanceId, cancellationToken)
            .ConfigureAwait(false);
        var backupDirectory = ResolveBackupDirectory(root, backupId);
        var manifestPath = Path.Combine(backupDirectory, ManifestName);
        if (!File.Exists(manifestPath))
        {
            return RestoreOutcome.Failed(
                backupDirectory,
                "backup manifest is missing");
        }

        BackupManifest manifest;
        try
        {
            manifest = await ReadManifestAsync(manifestPath, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is JsonException
                                              or ArgumentException
                                              or IOException)
        {
            return RestoreOutcome.Failed(backupDirectory, exception.Message);
        }

        if (!string.Equals(manifest.InstanceId, instanceId, StringComparison.Ordinal))
        {
            return RestoreOutcome.Failed(backupDirectory, "backup belongs to another instance");
        }

        // Verify the snapshot before touching the live directory. A damaged
        // backup must not destroy the current files.
        var damaged = await FindDamagedFileAsync(backupDirectory, manifest, cancellationToken)
            .ConfigureAwait(false);
        if (damaged is not null)
        {
            return RestoreOutcome.Failed(
                backupDirectory,
                $"backup file is damaged: {damaged}");
        }

        try
        {
            ClearDirectoryExceptBackups(root);
            RestoreTree(backupDirectory, root, manifest, cancellationToken);
        }
        catch (Exception exception) when (exception is IOException
                                              or UnauthorizedAccessException)
        {
            return RestoreOutcome.Failed(backupDirectory, exception.Message);
        }

        var mismatch = await FindDamagedFileAsync(root, manifest, cancellationToken)
            .ConfigureAwait(false);
        if (mismatch is not null)
        {
            return RestoreOutcome.Failed(
                backupDirectory,
                $"restored file is damaged: {mismatch}");
        }

        // The database row mirrors the instance file; put the metadata back
        // before declaring the rollback successful. A failure here keeps the
        // backup so nothing is discarded on an incomplete rollback.
        try
        {
            await _repository.UpdateAsync(
                    record.Id,
                    manifest.Name,
                    favorite: null,
                    lastPlayedAt: null,
                    loader: manifest.Loader,
                    loaderVersion: manifest.LoaderVersion,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException
                                              or SqliteException
                                              or UnauthorizedAccessException
                                              or ArgumentException)
        {
            return RestoreOutcome.Failed(backupDirectory, exception.Message);
        }

        // Only a manifest-verified restore may discard the package.
        var discarded = TryDeleteDirectory(backupDirectory);
        return new RestoreOutcome(true, discarded, backupDirectory, null);
    }

    /// <summary>
    /// Remove one snapshot after a successful update. Refuses ids that could
    /// escape the backups directory.
    /// </summary>
    public async Task<bool> DiscardAsync(
        string instanceId,
        string backupId,
        CancellationToken cancellationToken)
    {
        var (_, root) = await ResolveInstanceAsync(instanceId, cancellationToken)
            .ConfigureAwait(false);
        var backupDirectory = ResolveBackupDirectory(root, backupId);
        return TryDeleteDirectory(backupDirectory);
    }

    private async Task<(GameInstanceRecord Record, string Root)> ResolveInstanceAsync(
        string instanceId,
        CancellationToken cancellationToken)
    {
        var items = await _repository.ListAsync(cancellationToken).ConfigureAwait(false);
        var record = items.FirstOrDefault(item => item.Id == instanceId)
                     ?? throw new ArgumentException("instance not found");
        var settings = await _settings.LoadAsync(cancellationToken).ConfigureAwait(false);
        var siblings = items
            .Where(item => !string.Equals(item.Id, instanceId, StringComparison.Ordinal))
            .Select(item => item.RootPath)
            .ToArray();
        if (!InstanceDirectoryCleaner.TryResolveForTrash(
                record.RootPath,
                settings.Roots,
                siblings,
                out var root,
                out var skipReason))
        {
            throw new ArgumentException($"instance directory is not backupable: {skipReason}");
        }

        return (record, root!);
    }

    private static string NewBackupId()
    {
        var stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmssfff");
        var suffix = RandomNumberGenerator.GetInt32(0x10000).ToString("x4");
        return $"{stamp}-{suffix}";
    }

    private static string ResolveBackupDirectory(string root, string backupId)
    {
        if (string.IsNullOrWhiteSpace(backupId) ||
            backupId.IndexOfAny(['/', '\\', '.']) >= 0)
        {
            throw new ArgumentException("invalid backup id");
        }

        return Path.Combine(root, ".archoera", BackupsDirectoryName, backupId);
    }

    private static void CopyTree(
        string source,
        string destination,
        string backupsRoot,
        string relativeBase,
        List<BackupFile> files,
        CancellationToken cancellationToken)
    {
        foreach (var entry in Directory.EnumerateFileSystemEntries(source))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var full = Path.GetFullPath(entry);
            if (IsWithin(full, backupsRoot)) continue;
            var info = new FileInfo(full);
            // Symbolic links are not followed: copying a link could escape the
            // instance directory or create a cycle.
            if (info.LinkTarget is not null) continue;

            var name = Path.GetRelativePath(source, full);
            // The manifest path is relative to the instance root, not to the
            // directory currently being copied, so nested files keep their
            // parent prefix on restore.
            var relative = Path.Combine(relativeBase, name);
            var target = Path.Combine(destination, name);
            if (Directory.Exists(full))
            {
                Directory.CreateDirectory(target);
                CopyTree(full, target, backupsRoot, relative, files,
                    cancellationToken);
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(full, target, overwrite: false);
            files.Add(new BackupFile(NormalizeRelative(relative), info.Length,
                HashFile(target)));
        }
    }

    private static void RestoreTree(
        string backupDirectory,
        string root,
        BackupManifest manifest,
        CancellationToken cancellationToken)
    {
        foreach (var file in manifest.Files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var source = Path.Combine(backupDirectory, file.Path);
            var target = Path.Combine(root, file.Path);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(source, target, overwrite: true);
        }
    }

    private static void ClearDirectoryExceptBackups(string root)
    {
        var backupsRoot = Path.Combine(root, ".archoera", BackupsDirectoryName);
        var archoeraRoot = Path.Combine(root, ".archoera");
        foreach (var entry in Directory.EnumerateFileSystemEntries(root))
        {
            var full = Path.GetFullPath(entry);
            if (IsWithin(full, backupsRoot)) continue;
            if (Path.TrimEndingDirectorySeparator(full)
                .Equals(Path.TrimEndingDirectorySeparator(archoeraRoot),
                    StringComparison.OrdinalIgnoreCase))
            {
                // Keep the snapshot directory itself while restoring the rest
                // of .archoera (marker, overrides, backups metadata).
                foreach (var child in Directory.EnumerateFileSystemEntries(archoeraRoot))
                {
                    if (IsWithin(Path.GetFullPath(child), backupsRoot)) continue;
                    DeleteEntry(child);
                }

                continue;
            }

            DeleteEntry(full);
        }
    }

    private static void DeleteEntry(string path)
    {
        if (Directory.Exists(path))
        {
            var info = new DirectoryInfo(path);
            if (info.LinkTarget is not null)
            {
                DeleteSymbolicLink(path);
            }
            else
            {
                Directory.Delete(path, recursive: true);
            }
        }
        else
        {
            File.Delete(path);
        }
    }

    private static void DeleteSymbolicLink(string path)
    {
        // Directory.Delete removes the link itself on Unix; File.Delete covers
        // file links and Windows links. Try both without following the target.
        try
        {
            Directory.Delete(path);
        }
        catch (Exception exception) when (exception is IOException
                                              or UnauthorizedAccessException)
        {
            File.Delete(path);
        }
    }

    private static async Task<string?> FindDamagedFileAsync(
        string directory,
        BackupManifest manifest,
        CancellationToken cancellationToken)
    {
        foreach (var file in manifest.Files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var path = Path.Combine(directory, file.Path);
            if (!File.Exists(path)) return file.Path;
            var hash = await HashFileAsync(path, cancellationToken).ConfigureAwait(false);
            if (!string.Equals(hash, file.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                return file.Path;
            }
        }

        return null;
    }

    private static async Task WriteManifestAsync(
        string backupDirectory,
        GameInstanceRecord record,
        IReadOnlyList<BackupFile> files,
        CancellationToken cancellationToken)
    {
        var path = Path.Combine(backupDirectory, ManifestName);
        await using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write);
        using var writer = new Utf8JsonWriter(stream);
        writer.WriteStartObject();
        writer.WriteNumber("schemaVersion", ManifestSchemaVersion);
        writer.WriteString("createdAt", DateTimeOffset.UtcNow.ToString("O"));
        writer.WriteString("instanceId", record.Id);
        writer.WriteString("name", record.Name);
        writer.WriteString("gameVersion", record.GameVersion);
        writer.WriteString("loader", record.Loader);
        writer.WriteString("loaderVersion", record.LoaderVersion);
        writer.WriteStartArray("files");
        foreach (var file in files)
        {
            writer.WriteStartObject();
            writer.WriteString("path", file.Path);
            writer.WriteNumber("size", file.Size);
            writer.WriteString("sha256", file.Sha256);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
        await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<BackupManifest> ReadManifestAsync(
        string path,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read);
        using var document = await JsonDocument.ParseAsync(
            stream,
            cancellationToken: cancellationToken).ConfigureAwait(false);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new ArgumentException("manifest root must be an object");
        }

        var schemaVersion = root.TryGetProperty("schemaVersion", out var schema)
                            && schema.ValueKind == JsonValueKind.Number
            ? schema.GetInt32()
            : 0;
        if (schemaVersion != ManifestSchemaVersion)
        {
            throw new ArgumentException("unsupported backup manifest schema");
        }

        var instanceId = ReadString(root, "instanceId")
                         ?? throw new ArgumentException("manifest instanceId is missing");
        var files = new List<BackupFile>();
        if (root.TryGetProperty("files", out var filesElement) &&
            filesElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var file in filesElement.EnumerateArray())
            {
                var relative = ReadString(file, "path")
                               ?? throw new ArgumentException("manifest file path is missing");
                var size = file.TryGetProperty("size", out var sizeElement) &&
                           sizeElement.ValueKind == JsonValueKind.Number
                    ? sizeElement.GetInt64()
                    : 0;
                var sha = ReadString(file, "sha256")
                          ?? throw new ArgumentException("manifest file hash is missing");
                // A relative path from a JSON file is untrusted; refuse
                // absolute paths and traversal before it touches the disk.
                if (Path.IsPathRooted(relative) ||
                    relative.Split(['/', '\\']).Any(part => part == ".."))
                {
                    throw new ArgumentException("manifest contains an unsafe path");
                }

                files.Add(new BackupFile(NormalizeRelative(relative), size, sha));
            }
        }

        return new BackupManifest(
            instanceId,
            ReadString(root, "name") ?? string.Empty,
            ReadString(root, "gameVersion") ?? string.Empty,
            ReadString(root, "loader") ?? string.Empty,
            ReadString(root, "loaderVersion") ?? string.Empty,
            files);
    }

    private static string? ReadString(JsonElement element, string name)
        => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static string NormalizeRelative(string relative)
        => relative.Replace(Path.DirectorySeparatorChar, '/');

    private static string HashFile(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var sha = SHA256.Create();
        return Convert.ToHexString(sha.ComputeHash(stream)).ToLowerInvariant();
    }

    private static async Task<string> HashFileAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
            FileShare.Read, 81920, useAsync: true);
        using var sha = SHA256.Create();
        var hash = await sha.ComputeHashAsync(stream, cancellationToken).ConfigureAwait(false);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static bool TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
            return !Directory.Exists(path);
        }
        catch (Exception exception) when (exception is IOException
                                              or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static bool IsWithin(string path, string root)
    {
        var fullPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        var fullRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        return fullPath.Equals(fullRoot, StringComparison.OrdinalIgnoreCase) ||
               fullPath.StartsWith(fullRoot + Path.DirectorySeparatorChar,
                   StringComparison.OrdinalIgnoreCase);
    }
}

internal sealed record BackupFile(string Path, long Size, string Sha256);

internal sealed record BackupManifest(
    string InstanceId,
    string Name,
    string GameVersion,
    string Loader,
    string LoaderVersion,
    IReadOnlyList<BackupFile> Files);

internal sealed record InstanceBackupSnapshot(
    string InstanceId,
    string BackupId,
    string BackupPath,
    int FileCount,
    long ByteCount);

internal sealed record RestoreOutcome(
    bool Restored,
    bool BackupDiscarded,
    string BackupPath,
    string? Error)
{
    public static RestoreOutcome Failed(string backupPath, string error) =>
        new(false, false, backupPath, error);
}
