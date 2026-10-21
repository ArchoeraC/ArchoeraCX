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
        var byHome = new Dictionary<string, JavaRuntimeInfo>(
            OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
                ? StringComparer.OrdinalIgnoreCase
                : StringComparer.Ordinal);

        foreach (var item in results)
        {
            if (item is null) continue;
            var key = IdentityKey(item);
            if (!byHome.TryGetValue(key, out var existing))
            {
                byHome[key] = item;
                continue;
            }

            // Prefer the canonical bin/java path over a bundled jre alias.
            if (Prefer(item, existing))
            {
                byHome[key] = item;
            }
        }

        items.AddRange(byHome.Values);
        items.Sort(CompareRuntimes);
        return items;
    }

    public IReadOnlyList<JavaRuntimeInfo> Scan(CancellationToken cancellationToken = default)
        => Scan(JavaScanRequest.Default, cancellationToken);

    private static string IdentityKey(JavaRuntimeInfo item)
    {
        var home = item.Home;
        if (!string.IsNullOrWhiteSpace(home))
        {
            try
            {
                var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(home));
                var parent = Directory.GetParent(full);
                // Java 8's JDK reports <jdk>/jre; collapse it onto the JDK root.
                if (parent is not null &&
                    string.Equals(Path.GetFileName(full), "jre", StringComparison.OrdinalIgnoreCase) &&
                    File.Exists(Path.Combine(parent.FullName, "bin", JavaExecutableName())))
                {
                    full = parent.FullName;
                }

                return full;
            }
            catch
            {
                // Fall through to the executable path.
            }
        }

        try
        {
            var target = File.ResolveLinkTarget(item.Path, returnFinalTarget: true);
            if (target is not null)
            {
                return Path.TrimEndingDirectorySeparator(Path.GetFullPath(target.FullName));
            }
        }
        catch
        {
            // Symlink resolution is best effort.
        }

        try
        {
            return Path.TrimEndingDirectorySeparator(Path.GetFullPath(item.Path));
        }
        catch
        {
            return item.Path;
        }
    }

    private static bool Prefer(JavaRuntimeInfo candidate, JavaRuntimeInfo existing)
    {
        var candidateJre = IsJrePath(candidate.Path);
        var existingJre = IsJrePath(existing.Path);
        if (candidateJre != existingJre) return !candidateJre;
        return candidate.Path.Length < existing.Path.Length;
    }

    private static bool IsJrePath(string path)
        => path.Replace('\\', '/').Contains("/jre/", StringComparison.OrdinalIgnoreCase);

    private static int CompareRuntimes(JavaRuntimeInfo left, JavaRuntimeInfo right)
    {
        var major = right.Major.CompareTo(left.Major);
        if (major != 0) return major;
        var version = string.Compare(right.Version, left.Version, StringComparison.OrdinalIgnoreCase);
        if (version != 0) return version;
        return string.Compare(left.Path, right.Path, StringComparison.OrdinalIgnoreCase);
    }

    private static string JavaExecutableName()
        => OperatingSystem.IsWindows() ? "java.exe" : "java";
}
