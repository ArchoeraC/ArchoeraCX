using Archoera.Application.Loaders;
using Archoera.Domain.Loaders;

namespace Archoera.Infrastructure.Loaders.Native;

/// <summary>
/// Runs the installer processors that have a native implementation and delegates
/// everything else to a Java runner. This is the single entry point the repair
/// service uses, so adding a processor never changes the repair flow.
/// </summary>
public sealed class NativeProcessorRunner : IProcessorRunner
{
    private readonly IProcessorRunner _fallback;
    private readonly INativeProcessorHandler[] _handlers;

    public NativeProcessorRunner(IProcessorRunner? fallback = null)
        : this(fallback, DefaultHandlers())
    {
    }

    internal NativeProcessorRunner(
        IProcessorRunner? fallback,
        IEnumerable<INativeProcessorHandler> handlers)
    {
        _fallback = fallback ?? new ForgeProcessorRunner();
        _handlers = [.. handlers];
    }

    public bool CanRunWithoutJava(ProcessorStep step)
        => Find(step) is not null || _fallback.CanRunWithoutJava(step);

    public Task<ProcessorRunResult> RunAsync(
        ProcessorStep step,
        string javaPath,
        string workingDir,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        var handler = Find(step);
        if (handler is null)
        {
            return _fallback.RunAsync(
                step,
                javaPath,
                workingDir,
                timeout,
                cancellationToken);
        }

        return RunNativeAsync(handler, step, workingDir, timeout, cancellationToken);
    }

    private INativeProcessorHandler? Find(ProcessorStep step)
    {
        foreach (var handler in _handlers)
        {
            if (handler.CanRun(step))
            {
                return handler;
            }
        }

        return null;
    }

    private static async Task<ProcessorRunResult> RunNativeAsync(
        INativeProcessorHandler handler,
        ProcessorStep step,
        string workingDir,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        if (timeout <= TimeSpan.Zero)
        {
            timeout = TimeSpan.FromMinutes(10);
        }

        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);

        try
        {
            await Task.Run(
                    () => handler.Run(step, workingDir, timeoutSource.Token),
                    timeoutSource.Token)
                .ConfigureAwait(false);
            return new ProcessorRunResult(0, string.Empty, string.Empty, TimedOut: false);
        }
        catch (OperationCanceledException)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                throw;
            }

            return new ProcessorRunResult(
                -1,
                string.Empty,
                "native processor timed out",
                TimedOut: true);
        }
        catch (Exception exception)
        {
            return new ProcessorRunResult(-1, string.Empty, exception.Message, TimedOut: false);
        }
    }

    private static INativeProcessorHandler[] DefaultHandlers() =>
    [
        new McpDataProcessorHandler(),
        new MergeMappingProcessorHandler(),
        new JarSplitterProcessorHandler(),
        new ArtProcessorHandler(),
        new BinaryPatcherProcessorHandler(),
    ];
}
