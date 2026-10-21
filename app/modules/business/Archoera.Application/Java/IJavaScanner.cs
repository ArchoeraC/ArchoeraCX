using Archoera.Domain.Java;

namespace Archoera.Application.Java;

/// <summary>
/// Locates and validates Java runtimes installed on the local machine.
/// </summary>
public interface IJavaScanner
{
    /// <summary>
    /// Returns valid runtimes in discovery order, with duplicate executable paths removed.
    /// Unreadable or invalid candidates are skipped rather than failing the scan.
    /// Manual roots/executables in <paramref name="request"/> are appended after the
    /// default candidates and are only added when the request asks for them.
    /// </summary>
    IReadOnlyList<JavaRuntimeInfo> Scan(JavaScanRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Convenience overload for the regular machine-wide scan.
    /// </summary>
    IReadOnlyList<JavaRuntimeInfo> Scan(CancellationToken cancellationToken = default)
        => Scan(JavaScanRequest.Default, cancellationToken);
}
