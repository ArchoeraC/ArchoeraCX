using System.Security.Cryptography;
using System.Text;

namespace Archoera.Domain.Downloads;

/// <summary>
/// Deterministic <c>planId</c> generation. The id is a SHA-256 over the
/// canonical plan identity, so resubmitting the same work (in any file order)
/// produces the same idempotency key while a changed URL/hash/size does not.
/// </summary>
public static class DownloadPlanId
{
    private const string Domain = "archoera-download-plan/v1";

    /// <summary>Computes the lower-case hex plan id for <paramref name="plan"/>.</summary>
    public static string Compute(DownloadPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);

        var canonical = BuildCanonical(plan);
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
        return Convert.ToHexString(digest).ToLowerInvariant();
    }

    /// <summary>
    /// Canonical identity text. Exposed for diagnostics/tests; not a wire format.
    /// </summary>
    public static string BuildCanonical(DownloadPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);

        var builder = new StringBuilder(512);
        builder.Append(Domain).Append('\n');
        builder.Append("schema=").Append(plan.SchemaVersion).Append('\n');
        builder.Append("root=").Append(plan.Root).Append('\n');
        builder.Append("concurrency=").Append(plan.Concurrency).Append('\n');
        builder.Append("priority=").Append(plan.Priority).Append('\n');
        builder.Append("bandwidthLimit=").Append(plan.BandwidthLimit).Append('\n');

        var options = plan.Options ?? DownloadPlanOptions.Default;
        builder.Append("options=")
            .Append(options.Dedup ? '1' : '0').Append(',')
            .Append(options.VerifyExisting.ToWire()).Append(',')
            .Append(options.Durability.ToWire()).Append(',')
            .Append(options.RangeThreshold).Append(',')
            .Append(options.ChunkSize).Append(',')
            .Append(options.ProgressIntervalMs).Append('\n');

        var files = (plan.Files ?? [])
            .Where(file => file is not null)
            .OrderBy(file => file.Dest, StringComparer.Ordinal)
            .ThenBy(file => file.Url, StringComparer.Ordinal);

        foreach (var file in files)
        {
            builder.Append("file=")
                .Append(file.Dest).Append('\u001f')
                .Append(file.Url).Append('\u001f')
                .Append(file.Hash?.Algo ?? string.Empty).Append('\u001f')
                .Append(file.Hash?.Hex ?? string.Empty).Append('\u001f')
                .Append(file.Size).Append('\u001f')
                .Append(file.Mode.ToWire()).Append('\u001f')
                .Append(file.Priority).Append('\u001f')
                .Append(file.Executable ? '1' : '0').Append('\u001f');

            foreach (var mirror in file.Mirrors ?? [])
            {
                builder.Append(mirror).Append('\u001e');
            }

            // Headers are deliberately excluded: short-lived authorization
            // tokens must not change the idempotency key.
            builder.Append('\n');
        }

        return builder.ToString();
    }
}
