using Archoera.Application.Loaders;
using Archoera.Domain.Downloads;

namespace Archoera.Infrastructure.Loaders;

/// <summary>
/// Applies a Forge/NeoForge Maven mirror to an already-combined plan. The
/// vanilla/merge plan also contains loader libraries, so rewriting only the
/// installer plan is not enough.
/// </summary>
internal static class ForgePlanMirror
{
    public static DownloadPlan Apply(DownloadPlan plan, Uri mirror)
    {
        ArgumentNullException.ThrowIfNull(plan);

        var builder = new DownloadPlanBuilder(plan.Root)
            .WithPlanId(plan.PlanId)
            .WithConcurrency(plan.Concurrency)
            .WithPriority(plan.Priority)
            .WithBandwidthLimit(plan.BandwidthLimit)
            .WithOptions(plan.Options);

        foreach (var file in plan.Files)
        {
            var primary = file.Url;
            var mirrors = file.Mirrors.ToList();
            if (ForgeMavenMirror.TryRewrite(
                    file.Url,
                    mirror,
                    out var mapped,
                    out var fallback))
            {
                primary = mapped;
                foreach (var item in fallback)
                {
                    if (!mirrors.Contains(item, StringComparer.Ordinal))
                    {
                        mirrors.Insert(0, item);
                    }
                }
            }

            builder.AddFile(file with
            {
                Url = primary,
                Mirrors = mirrors,
            });
        }

        return builder.Build();
    }
}
