using System.Globalization;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using Archoera.Application.Modules;

namespace Archoera.Infrastructure.Modules;

/// <summary>Coarse account-region policy. No precise location is stored.</summary>
public sealed record RegionPolicyState(
    string Region,
    bool MicrosoftOnly,
    string Source,
    string? CountryCode);

/// <summary>
/// Best-effort region detection for account policy:
/// env override -> coarse IP lookup -> system locale/timezone fallback.
/// The IP request only reads a country code and is not persisted.
/// </summary>
public sealed class RegionPolicy
{
    public const string China = "CN";
    public const string Global = "GLOBAL";

    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromSeconds(4),
    };

    private static readonly MediaTypeWithQualityHeaderValue AcceptJson =
        new("application/json");

    private readonly HttpClient _http;
    private readonly string? _override;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private volatile RegionPolicyState? _cached;

    public RegionPolicy(HttpClient? http = null, string? regionOverride = null)
    {
        _http = http ?? Http;
        _override = regionOverride ?? Environment.GetEnvironmentVariable("ARCHOERACX_REGION");
    }

    public async Task<RegionPolicyState> ReadAsync(CancellationToken cancellationToken = default)
    {
        if (_cached is not null) return _cached;

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_cached is not null) return _cached;
            _cached = await DetectAsync(cancellationToken).ConfigureAwait(false);
            return _cached;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<RegionPolicyState> DetectAsync(CancellationToken cancellationToken)
    {
        var explicitRegion = _override?.Trim();
        if (!string.IsNullOrWhiteSpace(explicitRegion))
        {
            var isChina = explicitRegion.Equals(China, StringComparison.OrdinalIgnoreCase) ||
                          explicitRegion.Equals("CHINA", StringComparison.OrdinalIgnoreCase);
            return new RegionPolicyState(
                isChina ? China : Global,
                !isChina,
                "override",
                isChina ? China : null);
        }

        var ipCountry = await TryReadIpCountryAsync(cancellationToken).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(ipCountry))
        {
            var isChina = ipCountry.Equals(China, StringComparison.OrdinalIgnoreCase);
            return new RegionPolicyState(
                isChina ? China : Global,
                !isChina,
                "ip",
                ipCountry.ToUpperInvariant());
        }

        var localeCountry = ReadLocaleCountry();
        var isChinaLocale = string.Equals(localeCountry, China, StringComparison.OrdinalIgnoreCase);
        return new RegionPolicyState(
            isChinaLocale ? China : Global,
            !isChinaLocale,
            "locale",
            localeCountry);
    }

    private async Task<string?> TryReadIpCountryAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(4));
            using var request = new HttpRequestMessage(
                HttpMethod.Get,
                "https://www.cloudflare.com/cdn-cgi/trace");
            request.Headers.Accept.Add(AcceptJson);
            using var response = await _http
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token)
                .ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) return null;
            var text = await response.Content
                .ReadAsStringAsync(timeout.Token)
                .ConfigureAwait(false);
            foreach (var line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                if (!line.StartsWith("loc=", StringComparison.Ordinal)) continue;
                var value = line[4..].Trim();
                return value.Length is 2 or 3 ? value : null;
            }
        }
        catch (Exception exception) when (
            exception is HttpRequestException or IOException or OperationCanceledException)
        {
            // Fall through to locale/timezone. No IP or response is logged.
        }

        return null;
    }

    private static string? ReadLocaleCountry()
    {
        var zone = TimeZoneInfo.Local;
        if (zone.Id.Equals("Asia/Shanghai", StringComparison.OrdinalIgnoreCase) ||
            zone.Id.Equals("China Standard Time", StringComparison.OrdinalIgnoreCase))
        {
            return China;
        }

        // A UTC+8 clock is not enough by itself: Singapore, Hong Kong,
        // Macau, Taipei and Perth share the offset. Require the system
        // region to say mainland China, with the Windows GeoID as backup.
        if (zone.BaseUtcOffset == TimeSpan.FromHours(8))
        {
            var region = RegionInfo.CurrentRegion.TwoLetterISORegionName;
            if (string.Equals(region, China, StringComparison.OrdinalIgnoreCase)) return China;

            if (OperatingSystem.IsWindows())
            {
                try
                {
                    if (GetUserGeoId(GeoClassNation) == 45) return China;
                }
                catch (Exception exception) when (
                    exception is DllNotFoundException or EntryPointNotFoundException)
                {
                    // No native geo API on this system; keep the locale fallback.
                }
            }
        }

        return null;
    }

    private const int GeoClassNation = 16;

    [DllImport("kernel32.dll", EntryPoint = "GetUserGeoID")]
    private static extern int GetUserGeoId(int geoClass);
}

/// <summary>Read-only region policy for the account UI.</summary>
public sealed class RegionPolicyModuleService : IModuleMethodHandler
{
    private static readonly string[] SupportedMethods = ["region.policy"];
    private readonly RegionPolicy _policy;

    public RegionPolicyModuleService(RegionPolicy policy)
    {
        _policy = policy;
    }

    public IReadOnlyCollection<string> Methods => SupportedMethods;

    public async Task<byte[]> HandleAsync(
        string method,
        string? paramsJson,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(method, "region.policy", StringComparison.Ordinal))
        {
            return ModuleJson.Error("E_UNSUPPORTED", $"unsupported region method '{method}'");
        }

        var state = await _policy.ReadAsync(cancellationToken).ConfigureAwait(false);
        return ModuleJson.Envelope(writer =>
        {
            writer.WriteString("region", state.Region);
            writer.WriteBoolean("microsoftOnly", state.MicrosoftOnly);
            writer.WriteString("source", state.Source);
            writer.WriteString("countryCode", state.CountryCode);
        });
    }
}
