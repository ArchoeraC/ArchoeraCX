namespace Archoera.Domain.Downloads;

/// <summary>
/// Combines several producer plans that target the same root into one plan.
/// This is the install-time bridge between a vanilla plan and a loader plan:
/// the two are built independently, then merged before the single download
/// task is submitted.
/// </summary>
public static class DownloadPlanCombiner
{
    /// <summary>
    /// Combines plans in order. Identical destinations with the same hash/size
    /// are deduplicated; a destination with different bytes is rejected. The
    /// first plan supplies options/priority/bandwidth, and the maximum
    /// concurrency is retained.
    /// </summary>
    /// <exception cref="DownloadPlanException">
    /// Empty input, differing roots, or a conflicting destination.
    /// </exception>
    public static DownloadPlan Combine(IReadOnlyList<DownloadPlan> plans)
    {
        ArgumentNullException.ThrowIfNull(plans);
        if (plans.Count == 0)
        {
            throw new DownloadPlanException(
                DownloadPlanErrorCode.InvalidPlan,
                "at least one plan is required");
        }

        DownloadPlanValidator.EnsureValid(plans[0]);
        var root = plans[0].Root;
        var byDestination = new Dictionary<string, PlanFile>(StringComparer.Ordinal);

        var concurrency = plans[0].Concurrency;
        var priority = plans[0].Priority;
        var bandwidthLimit = plans[0].BandwidthLimit;
        var options = plans[0].Options;

        foreach (var plan in plans)
        {
            ArgumentNullException.ThrowIfNull(plan);
            DownloadPlanValidator.EnsureValid(plan);

            if (!string.Equals(plan.Root, root, StringComparison.Ordinal))
            {
                throw new DownloadPlanException(
                    DownloadPlanErrorCode.InvalidPlan,
                    $"plans target different roots: '{root}' and '{plan.Root}'");
            }

            concurrency = Math.Max(concurrency, plan.Concurrency);
            priority = Math.Min(priority, plan.Priority);
            bandwidthLimit = Math.Max(bandwidthLimit, plan.BandwidthLimit);

            foreach (var file in plan.Files)
            {
                if (byDestination.TryGetValue(file.Dest, out var existing))
                {
                    if (existing.Size != file.Size ||
                        !string.Equals(existing.Hash.Algo, file.Hash.Algo, StringComparison.OrdinalIgnoreCase) ||
                        !string.Equals(existing.Hash.Hex, file.Hash.Hex, StringComparison.OrdinalIgnoreCase))
                    {
                        throw new DownloadPlanException(
                            DownloadPlanErrorCode.DuplicateDest,
                            $"destination '{file.Dest}' has conflicting bytes");
                    }

                    continue;
                }

                byDestination.Add(file.Dest, file);
            }
        }

        var builder = new DownloadPlanBuilder(root)
            .WithConcurrency(concurrency)
            .WithPriority(priority)
            .WithBandwidthLimit(bandwidthLimit)
            .WithOptions(options);

        foreach (var file in byDestination.Values.OrderBy(static file => file.Dest, StringComparer.Ordinal))
        {
            builder.AddFile(file);
        }

        return builder.Build();
    }
}
