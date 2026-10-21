using Archoera.Application.Java;
using Archoera.Domain.Java;

namespace Archoera.Infrastructure.Java;

/// <summary>
/// Discovers Java runtimes from environment variables, well-known install
/// directories, (on Windows) the registry and optional caller-supplied
/// roots/executables, then validates each one.
/// </summary>
public sealed class JavaScanner : IJavaScanner
{
    private const int MaxProbeConcurrency = 4;

    public IReadOnlyList<JavaRuntimeInfo> Scan(JavaScanRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var candidates = JavaCandidateLocator.FindCandidates(request, cancellationToken);
        if (candidates.Count == 0)
        {
            return [];
        }

        var results = new JavaRuntimeInfo?[candidates.Count];
        var options = new ParallelOptions
        {
            CancellationToken = cancellationToken,
            MaxDegreeOfParallelism = Math.Max(1, Math.Min(MaxProbeConcurrency, Environment.ProcessorCount)),
        };

        Parallel.For(0, candidates.Count, options, index =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            results[index] = JavaProbe.TryProbe(candidates[index]);
        });

        var items = new List<JavaRuntimeInfo>(results.Length);
        var seen = new HashSet<string>(
            OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
                ? StringComparer.OrdinalIgnoreCase
                : StringComparer.Ordinal);

        foreach (var item in results)
        {
            if (item is not null && seen.Add(item.Path))
            {
                items.Add(item);
            }
        }

        return items;
    }

    public IReadOnlyList<JavaRuntimeInfo> Scan(CancellationToken cancellationToken = default)
        => Scan(JavaScanRequest.Default, cancellationToken);
}
