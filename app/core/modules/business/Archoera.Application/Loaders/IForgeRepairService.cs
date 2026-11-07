using Archoera.Domain.Loaders;

namespace Archoera.Application.Loaders;

/// <summary>
/// Idempotent repair of the generated/extracted artifacts in a manifest.
/// </summary>
public interface IForgeRepairService
{
    /// <summary>
    /// Extracts missing installer-owned files, skips valid processor outputs,
    /// runs processors from the earliest incomplete step, verifies after each,
    /// and returns the final verification. Never downloads anything.
    /// </summary>
    Task<LoaderRepairResult> RepairAsync(
        LoaderArtifactManifest manifest,
        string root,
        string javaPath,
        CancellationToken cancellationToken = default);
}
