using Archoera.Domain.Downloads;

namespace Archoera.Application.Packs;

/// <summary>
/// Builds the pack-only <see cref="DownloadPlan"/> from a parsed index. Files
/// provided by the archive itself (<c>overrides/</c>, <c>client-overrides/</c>)
/// win over index entries with the same destination, so they are excluded here
/// before the plan is validated.
/// </summary>
public static class ModpackPlanBuilder
{
    /// <summary>Path comparer used for exclusion, matching filesystem rules.</summary>
    public static StringComparer PathComparer =>
        OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;

    /// <summary>
    /// Builds a plan rooted at the instance directory.
    /// </summary>
    /// <param name="root">Absolute instance root that already exists.</param>
    /// <param name="index">Parsed pack index.</param>
    /// <param name="overridePaths">
    /// Relative paths extracted from the archive; matching index entries are
    /// skipped so the bundled copy is authoritative.
    /// </param>
    /// <param name="clientSide">Use the client environment filter (default).</param>
    /// <param name="planId">Optional idempotency key for logs/tests.</param>
    public static DownloadPlan Build(
        string root,
        ModpackIndex index,
        IEnumerable<string>? overridePaths = null,
        bool clientSide = true,
        string? planId = null)
    {
        ArgumentNullException.ThrowIfNull(index);

        var excluded = overridePaths is null
            ? new HashSet<string>(PathComparer)
            : new HashSet<string>(overridePaths, PathComparer);

        var builder = new DownloadPlanBuilder(root);
        if (!string.IsNullOrWhiteSpace(planId))
        {
            builder.WithPlanId(planId);
        }

        foreach (var file in index.Files)
        {
            if (!file.AppliesTo(clientSide)) continue;
            if (excluded.Contains(file.Path)) continue;

            builder.AddFile(
                file.Downloads[0],
                file.Path,
                file.Hash,
                file.Size,
                mirrors: file.Downloads.Skip(1).ToArray(),
                mode: DownloadFileMode.Ensure);
        }

        return builder.Build();
    }

    /// <summary>True when the index entry is supplied by the archive.</summary>
    public static bool IsProvidedByArchive(
        ModpackFile file,
        IReadOnlySet<string> overridePaths)
        => overridePaths.Contains(file.Path);
}
