using System.Collections.ObjectModel;

namespace Archoera.Domain.Downloads;

/// <summary>
/// One remote file in a <see cref="DownloadPlan"/>. Field names and semantics
/// match <c>FileSpec</c> in <c>app/modules/downloader/src/plan.rs</c> exactly;
/// no provider/audit metadata is emitted here because the v1 Rust parser does
/// not read it (docs/08 §4.2).
/// </summary>
/// <param name="Url">Primary HTTPS URL. Loopback HTTP is test-only.</param>
/// <param name="Mirrors">
/// Byte-identical mirrors. Validated with the same hash as <paramref name="Url"/>.
/// </param>
/// <param name="Hash">Required digest; SHA-1/SHA-256/SHA-512.</param>
/// <param name="Size">Expected size in bytes; <c>0</c> is a valid empty file.</param>
/// <param name="Dest">Relative, forward-slash, path-safe destination.</param>
/// <param name="Mode">Existing-file policy.</param>
/// <param name="Priority">Plan-local ordering hint (lower runs first).</param>
/// <param name="Executable">Unix chmod hint; ignored on Windows.</param>
public sealed record PlanFile(
    string Url,
    IReadOnlyList<string> Mirrors,
    DownloadHash Hash,
    long Size,
    string Dest,
    DownloadFileMode Mode = DownloadFileMode.Ensure,
    int Priority = 0,
    bool Executable = false)
{
    private static readonly IReadOnlyDictionary<string, string> NoHeaders =
        new ReadOnlyDictionary<string, string>(
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));

    /// <summary>
    /// Short-lived request headers. The executor only accepts the
    /// <c>authorization</c>/<c>cookie</c> allowlist; producers should normally
    /// leave this empty and never place provider API keys here.
    /// </summary>
    public IReadOnlyDictionary<string, string> Headers { get; init; } = NoHeaders;

    /// <summary>Convenience factory for a single-source file with no headers.</summary>
    public static PlanFile Create(
        string url,
        string dest,
        DownloadHash hash,
        long size,
        IReadOnlyList<string>? mirrors = null,
        DownloadFileMode mode = DownloadFileMode.Ensure,
        int priority = 0,
        bool executable = false,
        IReadOnlyDictionary<string, string>? headers = null)
        => new(
            url,
            mirrors ?? [],
            hash,
            size,
            dest,
            mode,
            priority,
            executable)
        {
            Headers = headers ?? NoHeaders,
        };
}
