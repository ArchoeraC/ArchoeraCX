using Archoera.Domain.Loaders;

namespace Archoera.Infrastructure.Loaders.Native;

/// <summary>
/// One installer processor that can be reproduced without a JVM. A handler must
/// only claim steps whose complete command line it understands; the runner
/// falls back to Java for everything else.
/// </summary>
internal interface INativeProcessorHandler
{
    /// <summary>True when <see cref="Run"/> implements this exact step.</summary>
    bool CanRun(ProcessorStep step);

    /// <summary>
    /// Produces the step's outputs. Arguments and the working directory follow
    /// the resolved <see cref="ProcessorStep"/> contract; implementations must
    /// resolve relative paths against <paramref name="workingDir"/>.
    /// </summary>
    void Run(ProcessorStep step, string workingDir, CancellationToken cancellationToken);
}
