using Archoera.Domain.Loaders;

namespace Archoera.Application.Loaders;

/// <summary>
/// Result of one processor process.
/// </summary>
/// <param name="ExitCode">Process exit code, or <c>-1</c> when it could not start or timed out.</param>
/// <param name="StandardOutput">Captured stdout.</param>
/// <param name="StandardError">Captured stderr (or the startup error).</param>
/// <param name="TimedOut">True when the timeout killed the process tree.</param>
public sealed record ProcessorRunResult(
    int ExitCode,
    string StandardOutput,
    string StandardError,
    bool TimedOut);

/// <summary>
/// Runs exactly one Forge/NeoForge processor. Implementations must use an
/// argv list (no shell), redirect stdio, and kill the process tree on timeout.
/// </summary>
public interface IProcessorRunner
{
    /// <summary>
    /// Runs <paramref name="step"/>. The repair service has already expanded
    /// path placeholders, so path fields and arguments are absolute.
    /// </summary>
    Task<ProcessorRunResult> RunAsync(
        ProcessorStep step,
        string javaPath,
        string workingDir,
        TimeSpan timeout,
        CancellationToken cancellationToken = default);
}
