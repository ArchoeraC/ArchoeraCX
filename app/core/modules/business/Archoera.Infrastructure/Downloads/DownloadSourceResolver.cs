using Archoera.Application.Downloads;
using Archoera.Infrastructure.Modules;

namespace Archoera.Infrastructure.Downloads;

/// <summary>
/// Turns the stored preference plus the detected region into a concrete
/// source order. `auto` follows the region (mainland China prefers mirrors);
/// explicit choices always win.
/// </summary>
public static class DownloadSourceResolver
{
    public static async Task<DownloadSourceMode> ResolveAsync(
        string? preference,
        RegionPolicy? regionPolicy,
        CancellationToken cancellationToken = default)
    {
        var mode = DownloadSourceModes.Parse(preference);
        if (mode != DownloadSourceMode.Auto || regionPolicy is null)
        {
            return mode;
        }

        try
        {
            var state = await regionPolicy.ReadAsync(cancellationToken).ConfigureAwait(false);
            return string.Equals(state.Region, RegionPolicy.China, StringComparison.Ordinal)
                ? DownloadSourceMode.Mirror
                : DownloadSourceMode.Official;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is HttpRequestException or IOException or TaskCanceledException)
        {
            return DownloadSourceMode.Official;
        }
    }
}
