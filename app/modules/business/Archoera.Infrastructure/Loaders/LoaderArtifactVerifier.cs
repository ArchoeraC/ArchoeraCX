using Archoera.Application.Loaders;
using Archoera.Domain.Loaders;

namespace Archoera.Infrastructure.Loaders;

/// <summary>
/// Read-only manifest verifier. Path containment is checked before any file
/// is opened, so a hostile manifest cannot probe files outside the root.
/// </summary>
public sealed class LoaderArtifactVerifier : ILoaderArtifactVerifier
{
    public LoaderVerification Verify(
        LoaderArtifactManifest manifest,
        string root,
        LoaderVerifyMode mode = LoaderVerifyMode.Full)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentException.ThrowIfNullOrWhiteSpace(root);

        var rootFull = Path.GetFullPath(root);
        var missing = new List<LoaderArtifact>();
        var mismatched = new List<LoaderArtifact>();
        var unsafeArtifacts = new List<LoaderArtifact>();
        var details = new List<string>();

        var librariesUsable = LoaderPaths.TryResolveWithin(
            rootFull,
            manifest.LibrariesPath,
            out var librariesFull,
            out _,
            out var librariesError);

        if (!librariesUsable)
        {
            details.Add($"unsafe libraries path '{manifest.LibrariesPath}': {librariesError}");
            foreach (var artifact in manifest.Artifacts)
            {
                if (artifact.Location == LoaderArtifactLocation.Libraries)
                {
                    unsafeArtifacts.Add(artifact);
                    details.Add($"unsafe: {Display(artifact)}");
                }
            }
        }

        foreach (var artifact in manifest.Artifacts)
        {
            if (artifact.Location == LoaderArtifactLocation.Libraries && !librariesUsable)
            {
                continue;
            }

            var baseFull = artifact.Location == LoaderArtifactLocation.Libraries
                ? librariesFull
                : rootFull;

            if (!LoaderPaths.TryResolveWithin(
                    baseFull,
                    artifact.RelativePath,
                    out var fullPath,
                    out var normalized,
                    out var pathError))
            {
                unsafeArtifacts.Add(artifact);
                details.Add($"unsafe: {Display(artifact)}: {pathError}");
                continue;
            }

            if (normalized == ".")
            {
                unsafeArtifacts.Add(artifact);
                details.Add($"unsafe: {Display(artifact)}: path resolves to its base directory");
                continue;
            }

            if (!File.Exists(fullPath))
            {
                missing.Add(artifact);
                details.Add($"missing: {Display(artifact)}");
                continue;
            }

            var size = new FileInfo(fullPath).Length;
            var sizeMismatch = artifact.Size is not null && artifact.Size.Value != size;
            var hashMismatch = false;
            string? actualSha1 = null;

            var shouldHash = artifact.HasSha1 &&
                             (mode == LoaderVerifyMode.Full ||
                              (artifact.Kind == LoaderArtifactKind.Generated &&
                               artifact.RequiredForLaunch));
            if (shouldHash)
            {
                try
                {
                    actualSha1 = LoaderPaths.Sha1File(fullPath);
                    hashMismatch = !string.Equals(actualSha1, artifact.Sha1, StringComparison.OrdinalIgnoreCase);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    mismatched.Add(artifact);
                    details.Add($"unreadable: {Display(artifact)}: {exception.Message}");
                    continue;
                }
            }

            if (sizeMismatch || hashMismatch)
            {
                mismatched.Add(artifact);
                if (sizeMismatch)
                {
                    details.Add(
                        $"size mismatch: {Display(artifact)}: expected {artifact.Size}, actual {size}");
                }

                if (hashMismatch)
                {
                    details.Add(
                        $"sha1 mismatch: {Display(artifact)}: expected {artifact.Sha1}, actual {actualSha1}");
                }
            }
        }

        return new LoaderVerification(
            librariesUsable &&
            missing.Count == 0 &&
            mismatched.Count == 0 &&
            unsafeArtifacts.Count == 0,
            missing,
            mismatched,
            details,
            unsafeArtifacts);
    }

    private static string Display(LoaderArtifact artifact)
        => $"{(artifact.Location == LoaderArtifactLocation.Root ? "root" : "libraries")}/{artifact.RelativePath}";
}
