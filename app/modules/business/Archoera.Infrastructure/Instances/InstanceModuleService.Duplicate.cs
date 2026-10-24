using System.Text.Json;
using Archoera.Application.Modules;
using Archoera.Domain.Instances;

namespace Archoera.Infrastructure.Instances;

/// <summary>
/// The <c>instance.duplicate</c> handler: registers a new record and copies the
/// source instance tree into it, with rollback when the copy is rejected.
/// </summary>
public sealed partial class InstanceModuleService
{
    private async Task<byte[]> DuplicateAsync(
        string? paramsJson,
        CancellationToken cancellationToken)
    {
        using var document = Parse(paramsJson);
        var rootElement = document.RootElement;
        var id = ReadString(rootElement, "id") ?? throw new ArgumentException("id is required");
        var name = ReadString(rootElement, "name") ?? throw new ArgumentException("name is required");

        var items = await _repository.ListAsync(cancellationToken).ConfigureAwait(false);
        var source = items.FirstOrDefault(item => string.Equals(item.Id, id, StringComparison.Ordinal))
            ?? throw new ArgumentException("instance not found");
        if (!Directory.Exists(source.RootPath))
        {
            throw new ArgumentException("instance directory is missing");
        }

        // A repository may resolve the requested name onto the source directory
        // itself (the JSON repository does not de-duplicate roots), so keep the
        // original metadata to restore the source if the copy is rejected.
        var sourceMetadataPath = Path.Combine(source.RootPath, ".archoera", "instance.json");
        var sourceMetadata = File.Exists(sourceMetadataPath)
            ? await File.ReadAllBytesAsync(sourceMetadataPath, cancellationToken).ConfigureAwait(false)
            : null;

        var settings = await _settings.LoadAsync(cancellationToken).ConfigureAwait(false);
        var created = await _repository
            .CreateAsync(
                name,
                source.GameVersion,
                source.Loader,
                source.LoaderVersion,
                rootPath: null,
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        // Never copy an instance into itself or into a directory that hosts it:
        // either direction would overwrite the source or unrelated files.
        var overlapsSource =
            IsSameOrNested(source.RootPath, created.RootPath) ||
            IsSameOrNested(created.RootPath, source.RootPath);
        var collidesWithSibling = !overlapsSource && items.Any(
            item => !string.Equals(item.Id, created.Id, StringComparison.Ordinal) &&
                    SamePath(item.RootPath, created.RootPath));
        if (overlapsSource || collidesWithSibling)
        {
            if (SamePath(source.RootPath, created.RootPath))
            {
                RestoreInstanceMetadata(sourceMetadataPath, sourceMetadata);
            }

            await RollbackDuplicateAsync(created, items, settings.Roots).ConfigureAwait(false);
            throw new ArgumentException(overlapsSource
                ? "duplicate target must be outside the source instance directory"
                : "duplicate target directory is already used by another instance");
        }

        try
        {
            CopyInstanceTree(source.RootPath, created.RootPath, cancellationToken);
        }
        catch
        {
            await RollbackDuplicateAsync(created, items, settings.Roots).ConfigureAwait(false);
            throw;
        }

        var record = await _repository
            .UpdateAsync(
                created.Id,
                name: null,
                favorite: null,
                lastPlayedAt: null,
                overrides: source.Overrides,
                setOverrides: source.Overrides is { IsEmpty: false },
                cancellationToken: cancellationToken)
            .ConfigureAwait(false)
            ?? throw new ArgumentException("instance not found");

        var roots = (await _settings.LoadAsync(cancellationToken).ConfigureAwait(false)).Roots;
        return BuildEnvelope(writer =>
        {
            writer.WritePropertyName("instance");
            WriteRecord(writer, record, roots);
        });
    }

    /// <summary>
    /// Copies the source instance tree into the freshly created target root.
    /// Per-instance logs and the portable metadata stay behind: the target owns
    /// its own <c>instance.json</c> and must not inherit the source's log history.
    /// Symbolic links are skipped so a copy cannot escape the instance or loop.
    /// </summary>
    private static void CopyInstanceTree(
        string sourceRoot,
        string targetRoot,
        CancellationToken cancellationToken)
    {
        string[] excluded =
        [
            Path.Combine(".archoera", "logs"),
            Path.Combine(".archoera", "instance.json"),
        ];
        var pending = new Stack<(string Source, string Target)>();
        pending.Push((sourceRoot, targetRoot));
        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var (from, to) = pending.Pop();
            Directory.CreateDirectory(to);
            foreach (var entry in Directory.EnumerateFileSystemEntries(from))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (excluded.Contains(
                        Path.GetRelativePath(sourceRoot, entry),
                        StringComparer.Ordinal))
                {
                    continue;
                }

                var isDirectory = Directory.Exists(entry);
                FileSystemInfo info = isDirectory
                    ? new DirectoryInfo(entry)
                    : new FileInfo(entry);
                // Symlinks may escape the instance or form cycles; never follow them.
                if (info.LinkTarget is not null) continue;

                var destination = Path.Combine(to, info.Name);
                if (isDirectory)
                {
                    pending.Push((entry, destination));
                }
                else
                {
                    File.Copy(entry, destination, overwrite: true);
                }
            }
        }
    }

    /// <summary>
    /// Best-effort rollback after a failed duplicate: remove the new directory
    /// through the guarded cleaner (which refuses shared roots, sibling
    /// instances and unmanaged directories) and drop the registry row so the
    /// half-created instance never shows up in the list.
    /// </summary>
    private async Task RollbackDuplicateAsync(
        GameInstanceRecord created,
        IReadOnlyList<GameInstanceRecord> existing,
        IReadOnlyList<string> roots)
    {
        InstanceDirectoryCleaner.TryDelete(
            created.RootPath,
            roots,
            existing
                .Where(item => !string.Equals(item.Id, created.Id, StringComparison.Ordinal))
                .Select(item => item.RootPath)
                .ToArray(),
            out _,
            // The copy was created by this call, so rollback may remove it even
            // when the source was a custom instance outside the configured roots.
            allowOutsideConfiguredRoots: true);
        try
        {
            await _repository.DeleteAsync(created.Id, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Rollback is best effort; the original failure is what matters.
        }
    }

    private static void RestoreInstanceMetadata(string path, byte[]? content)
    {
        try
        {
            if (content is null)
            {
                if (File.Exists(path)) File.Delete(path);
                return;
            }

            File.WriteAllBytes(path, content);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Rollback is best effort; the original failure is what matters.
        }
    }

    private static bool SamePath(string left, string right)
    {
        try
        {
            return Path.TrimEndingDirectorySeparator(Path.GetFullPath(left))
                .Equals(
                    Path.TrimEndingDirectorySeparator(Path.GetFullPath(right)),
                    PathComparison);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException
                                              or PathTooLongException)
        {
            return false;
        }
    }

    private static bool IsSameOrNested(string candidate, string ancestor)
    {
        try
        {
            var fullCandidate = Path.TrimEndingDirectorySeparator(Path.GetFullPath(candidate));
            var fullAncestor = Path.TrimEndingDirectorySeparator(Path.GetFullPath(ancestor));
            return fullCandidate.Equals(fullAncestor, PathComparison) ||
                   fullCandidate.StartsWith(
                       fullAncestor + Path.DirectorySeparatorChar,
                       PathComparison);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException
                                              or PathTooLongException)
        {
            return false;
        }
    }
}
