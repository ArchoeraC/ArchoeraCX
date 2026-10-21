namespace Archoera.Domain.Loaders;

/// <summary>Outcome of an idempotent repair request.</summary>
public enum LoaderRepairStatus
{
    /// <summary>Every artifact was already valid; no processor ran and no file was written.</summary>
    AlreadyValid,

    /// <summary>Repair wrote or regenerated something and the final verification passed.</summary>
    Repaired,

    /// <summary>Repair could not reach a valid state. See details and verification.</summary>
    Failed,
}

/// <summary>
/// Repair result. <see cref="ProcessorsRun"/> lists the indices that actually
/// executed, in order; an empty list on the second run proves idempotence.
/// </summary>
/// <param name="Status">Overall outcome.</param>
/// <param name="Verification">Verification taken after the repair attempt.</param>
/// <param name="ProcessorsRun">Processor indices executed by this call.</param>
/// <param name="Details">Failure or progress diagnostics (relative paths only).</param>
public sealed record LoaderRepairResult(
    LoaderRepairStatus Status,
    LoaderVerification Verification,
    IReadOnlyList<int> ProcessorsRun,
    IReadOnlyList<string> Details)
{
    /// <summary>True for <see cref="LoaderRepairStatus.AlreadyValid"/> and <see cref="LoaderRepairStatus.Repaired"/>.</summary>
    public bool Ok => Status is LoaderRepairStatus.AlreadyValid or LoaderRepairStatus.Repaired;
}
