using Archoera.Application.Instances;

namespace Archoera.Infrastructure.Instances;

/// <summary>
/// Persists the "last played" timestamp after a successful launch.
///
/// The launch module calls this once the JVM is running, so the timestamp no
/// longer depends on the UI staying alive (the window may be hidden right
/// after the start). A failed write is reported to the caller but must never
/// turn a running game into a launch error.
/// </summary>
public static class InstancePlayHistory
{
    /// <summary>Records <paramref name="at"/> and returns false when the write failed.</summary>
    public static async Task<bool> MarkPlayedAsync(
        IInstanceRepository instances,
        string instanceId,
        DateTimeOffset at,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(instances);
        if (string.IsNullOrWhiteSpace(instanceId)) return false;

        try
        {
            var updated = await instances
                .UpdateAsync(
                    instanceId,
                    name: null,
                    favorite: null,
                    lastPlayedAt: at,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            return updated is not null;
        }
        catch (Exception)
        {
            // Cancellation after the process started and a busy registry both
            // leave the launch successful; the label is best effort.
            return false;
        }
    }
}
