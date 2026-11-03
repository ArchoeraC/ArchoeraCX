using Archoera.Domain.Loaders;

namespace Archoera.Infrastructure.Loaders.Native;

/// <summary>
/// Reproduces <c>installertools --task MERGE_MAPPING</c> for the two mapping
/// files the installer passes (TSRG2 left, ProGuard right).
/// </summary>
internal sealed class MergeMappingProcessorHandler : INativeProcessorHandler
{
    private static readonly string[] MainClasses =
    [
        "net.minecraftforge.installertools.ConsoleTool",
        "net.neoforged.installertools.ConsoleTool",
    ];

    public bool CanRun(ProcessorStep step) => TryCreate(step, out _);

    public void Run(ProcessorStep step, string workingDir, CancellationToken cancellationToken)
    {
        if (!TryCreate(step, out var request))
        {
            throw new InvalidOperationException(
                "processor is not a supported MERGE_MAPPING invocation");
        }

        cancellationToken.ThrowIfCancellationRequested();
        MappingMerge.Run(
            NativeProcessorPaths.Resolve(request.LeftPath, workingDir),
            NativeProcessorPaths.Resolve(request.RightPath, workingDir),
            NativeProcessorPaths.Resolve(request.OutputPath, workingDir),
            request.ReverseLeft,
            request.ReverseRight,
            request.Options);
    }

    private static bool TryCreate(ProcessorStep step, out MergeRequest request)
    {
        request = null!;
        if (!MainClasses.Contains(step.MainClass, StringComparer.Ordinal))
        {
            return false;
        }

        string? task = null;
        string? left = null;
        string? right = null;
        string? output = null;
        var reverseLeft = false;
        var reverseRight = false;
        var classes = false;
        var fields = false;
        var methods = false;
        var parameters = false;

        for (var index = 0; index < step.Arguments.Count; index++)
        {
            switch (step.Arguments[index])
            {
                case "--task":
                    if (!TryNext(step.Arguments, ref index, out task))
                    {
                        return false;
                    }

                    break;
                case "--left":
                    if (!TryNext(step.Arguments, ref index, out left))
                    {
                        return false;
                    }

                    break;
                case "--right":
                    if (!TryNext(step.Arguments, ref index, out right))
                    {
                        return false;
                    }

                    break;
                case "--output":
                    if (!TryNext(step.Arguments, ref index, out output))
                    {
                        return false;
                    }

                    break;
                case "--reverse-left":
                    reverseLeft = true;
                    break;
                case "--reverse-right":
                    reverseRight = true;
                    break;
                case "--classes":
                    classes = true;
                    break;
                case "--fields":
                    fields = true;
                    break;
                case "--methods":
                    methods = true;
                    break;
                case "--params":
                    parameters = true;
                    break;
                default:
                    // --left-names/--right-names and unknown options fall back.
                    return false;
            }
        }

        if (!string.Equals(task, "MERGE_MAPPING", StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(left) ||
            string.IsNullOrWhiteSpace(right) ||
            string.IsNullOrWhiteSpace(output))
        {
            return false;
        }

        var selective = classes || fields || methods || parameters;
        request = new MergeRequest(
            left,
            right,
            output,
            reverseLeft,
            reverseRight,
            new MappingMerge.Options(
                classes || !selective,
                fields || !selective,
                methods || !selective,
                parameters || !selective));
        return true;
    }

    private static bool TryNext(
        IReadOnlyList<string> arguments,
        ref int index,
        out string? value)
    {
        value = null;
        if (index + 1 >= arguments.Count)
        {
            return false;
        }

        value = arguments[++index];
        return !string.IsNullOrWhiteSpace(value);
    }

    private sealed record MergeRequest(
        string LeftPath,
        string RightPath,
        string OutputPath,
        bool ReverseLeft,
        bool ReverseRight,
        MappingMerge.Options Options);
}
