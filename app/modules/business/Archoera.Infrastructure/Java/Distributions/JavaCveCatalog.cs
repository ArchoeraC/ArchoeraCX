using System.Collections.Concurrent;
using System.Text.Json;
using Archoera.Application.JavaRuntime;

namespace Archoera.Infrastructure.Java.Distributions;

/// <summary>Read-only source of CVE data for a resolved JDK build.</summary>
internal interface IJavaCveSource
{
    Task<JavaCveReport> GetReportAsync(
        string providerId,
        string version,
        string baselineVersion,
        CancellationToken cancellationToken);
}

/// <summary>
/// Looks up only the JDK build being installed. NVD keyword search is scoped
/// to the newest patch's decade for that major (for example <c>21.0.1</c>),
/// and both CPE configurations and Oracle's affected-version description are
/// used; when NVD
/// throttles or times out, CIRCL's NVD mirror supplies the same CVE records as
/// affected version strings. OSV supplies aliases and a fallback summary for
/// the same CVE ids. Any feed failure is reported as an unavailable result so a
/// catalogue page keeps working offline.
/// </summary>
internal sealed class JavaCveCatalog : IJavaCveSource
{
    private const int MaxOsvLookups = 8;

    private static readonly TimeSpan ReportTtl = TimeSpan.FromHours(6);
    private static readonly TimeSpan UnavailableTtl = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan OsvTtl = TimeSpan.FromHours(24);
    private static readonly TimeSpan OsvTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan NvdAttemptTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan NvdFailureTtl = TimeSpan.FromMinutes(5);

    private static readonly string[] OpenJdkAccepted = ["oracle:jdk", "oracle:jre"];
    private static readonly string[] GraalVmAccepted =
        ["oracle:jdk", "oracle:jre", "oracle:graalvm_for_jdk"];

    private readonly JavaNvdFeed _nvd;
    private readonly JavaCveCirclFeed _circl;
    private readonly HttpClient _http;
    private readonly Uri _osvBase;
    private readonly Func<DateTimeOffset> _now;
    private readonly ConcurrentDictionary<string, CacheEntry> _reports =
        new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, OsvCacheEntry> _osv =
        new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _gate = new(1, 1);
    private DateTimeOffset _nvdUnavailableUntil;

    public JavaCveCatalog(
        HttpClient http,
        string dataRoot,
        Uri? nvdBase = null,
        Uri? osvBase = null,
        Uri? circlBase = null,
        Func<DateTimeOffset>? clock = null,
        string? apiKey = null)
    {
        _http = http;
        _now = clock ?? (() => DateTimeOffset.UtcNow);
        _nvd = new JavaNvdFeed(http, dataRoot, nvdBase, _now, apiKey);
        _circl = new JavaCveCirclFeed(http, dataRoot, circlBase, _now);
        _osvBase = osvBase ?? new Uri("https://api.osv.dev/v1/vulns/");
    }

    public async Task<JavaCveReport> GetReportAsync(
        string providerId,
        string version,
        string baselineVersion,
        CancellationToken cancellationToken)
    {
        // A slow NVD endpoint must not hold the install page open forever.
        // The page still gets a deterministic "unavailable" answer and can
        // let the user decide with a visible warning.
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(25));
        try
        {
            return await GetReportCoreAsync(
                    providerId,
                    version,
                    baselineVersion,
                    timeout.Token)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return JavaCveReport.Unavailable(
                providerId,
                version,
                "JAVA_CVE_TIMEOUT",
                _now());
        }
    }

    private async Task<JavaCveReport> GetReportCoreAsync(
        string providerId,
        string version,
        string baselineVersion,
        CancellationToken cancellationToken)
    {
        var key = $"{providerId}|{version}|{baselineVersion}";
        var now = _now();
        if (TryGetReport(key, now, out var cached)) return cached;

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            now = _now();
            if (TryGetReport(key, now, out cached)) return cached;

            var parsedVersion = JavaCveVersion.Parse(version);
            var parsedBaseline = JavaCveVersion.Parse(baselineVersion);
            var nvd = await TryNvdAsync(
                    providerId,
                    parsedVersion,
                    parsedBaseline,
                    cancellationToken)
                .ConfigureAwait(false);
            var matches = nvd.Matches;
            var source = "nvd";
            if (matches is null)
            {
                var circl = await TryCirclAsync(
                        providerId,
                        parsedVersion,
                        cancellationToken)
                    .ConfigureAwait(false);
                matches = circl.Matches;
                source = "circl";
                if (matches is null)
                {
                    var unavailable = JavaCveReport.Unavailable(
                        providerId,
                        version,
                        circl.Reason ?? nvd.Reason ?? "JAVA_CVE_UNAVAILABLE",
                        now);
                    RememberReport(key, unavailable, now.Add(UnavailableTtl));
                    return unavailable;
                }
            }

            var advisories = await BuildAdvisoriesAsync(
                    matches.Values,
                    cancellationToken)
                .ConfigureAwait(false);
            var sources = new List<string> { source };
            if (advisories.OsvUsed) sources.Add("osv");
            var report = JavaCveReport.Ok(
                providerId,
                version,
                advisories.Items,
                sources,
                now);
            RememberReport(key, report, now.Add(ReportTtl));
            return report;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<(
        IReadOnlyDictionary<string, JavaNvdVulnerability>? Matches,
        string? Reason)> TryNvdAsync(
            string providerId,
            JavaCveVersion version,
            JavaCveVersion baselineVersion,
            CancellationToken cancellationToken)
    {
        if (_nvdUnavailableUntil > _now())
        {
            return (null, "JAVA_CVE_UNAVAILABLE");
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken);
        timeout.CancelAfter(NvdAttemptTimeout);
        var matches = new Dictionary<string, JavaNvdVulnerability>(
            StringComparer.Ordinal);
        var failures = new List<JavaCveFetchException>();
        var succeeded = 0;
        // GraalVM releases lag the upstream JDK and often stop at a minor
        // patch that never contains the current decade. Query the whole major
        // line for GraalVM so its old builds still see current Java CVEs.
        var prefix = string.Equals(providerId, "graalvm", StringComparison.Ordinal)
            ? $"{baselineVersion.Major}.0"
            : NvdPrefix(baselineVersion);
        try
        {
            try
            {
                var openJdk = await _nvd
                    .GetOpenJdkAsync(prefix, timeout.Token)
                    .ConfigureAwait(false);
                succeeded++;
                AddMatches(matches, openJdk, version, OpenJdkAccepted);
            }
            catch (JavaCveFetchException exception)
            {
                failures.Add(exception);
            }

            if (string.Equals(providerId, "graalvm", StringComparison.Ordinal))
            {
                try
                {
                    var graalVm = await _nvd
                        .GetGraalVmAsync(prefix, timeout.Token)
                        .ConfigureAwait(false);
                    succeeded++;
                    AddMatches(matches, graalVm, version, GraalVmAccepted);
                }
                catch (JavaCveFetchException exception)
                {
                    failures.Add(exception);
                }
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // If one prefix already produced data, keep it instead of
            // discarding the whole answer for one slow extra query.
            if (succeeded > 0)
            {
                _nvdUnavailableUntil = default;
                return (matches, null);
            }

            _nvdUnavailableUntil = _now().Add(NvdFailureTtl);
            return (null, "JAVA_CVE_TIMEOUT");
        }

        if (succeeded == 0)
        {
            _nvdUnavailableUntil = _now().Add(NvdFailureTtl);
            return (
                null,
                failures.FirstOrDefault()?.Code ?? "JAVA_CVE_UNAVAILABLE");
        }

        _nvdUnavailableUntil = default;
        return (matches, null);
    }

    private static void AddMatches(
        Dictionary<string, JavaNvdVulnerability> target,
        IEnumerable<JavaNvdVulnerability> vulnerabilities,
        JavaCveVersion version,
        IReadOnlyCollection<string> accepted)
    {
        foreach (var vulnerability in vulnerabilities)
        {
            if (vulnerability.Affects(version, accepted))
            {
                target.TryAdd(vulnerability.Id, vulnerability);
            }
        }
    }

    /// <summary>
    /// NVD keyword search is scoped to the newest patch's version decade:
    /// <c>21.0.13</c> queries <c>21.0.1</c>, <c>11.0.33</c> queries
    /// <c>11.0.3</c>, and <c>8u504</c> queries <c>8u5</c>. This keeps the
    /// response in the tens of kilobytes instead of the full Java SE feed.
    /// </summary>
    private static string NvdPrefix(JavaCveVersion version)
    {
        if (version.Major == 8)
        {
            return $"8u{Math.Clamp((version.Update ?? 0) / 100, 0, 99)}";
        }

        if (version.Segments.Count >= 3)
        {
            var third = version.Segments[2];
            var group = third >= 10 ? third / 10 : third;
            return $"{version.Major}.0.{group}";
        }

        return $"{version.Major}.0";
    }

    private async Task<(
        IReadOnlyDictionary<string, JavaNvdVulnerability>? Matches,
        string? Reason)> TryCirclAsync(
            string providerId,
            JavaCveVersion version,
            CancellationToken cancellationToken)
    {
        var accepted = providerId == "graalvm"
            ? GraalVmAccepted
            : OpenJdkAccepted;
        try
        {
            var vulnerabilities = await _circl
                .GetAsync(cancellationToken)
                .ConfigureAwait(false);
            var matches = new Dictionary<string, JavaNvdVulnerability>(
                StringComparer.Ordinal);
            foreach (var vulnerability in vulnerabilities)
            {
                if (vulnerability.Affects(version, accepted))
                {
                    matches.TryAdd(vulnerability.Id, vulnerability);
                }
            }

            return (matches, null);
        }
        catch (JavaCveFetchException exception)
        {
            return (null, exception.Code);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return (null, "JAVA_CVE_TIMEOUT");
        }
    }

    private async Task<(IReadOnlyList<JavaCveAdvisory> Items, bool OsvUsed)>
        BuildAdvisoriesAsync(
            IEnumerable<JavaNvdVulnerability> vulnerabilities,
            CancellationToken cancellationToken)
    {
        var ordered = vulnerabilities
            .OrderByDescending(item => JavaCveSeverity.Rank(item.Severity))
            .ThenByDescending(item => item.Score ?? 0)
            .ThenBy(item => item.Id, StringComparer.Ordinal)
            .ToArray();

        var osvUsed = false;
        var items = new List<JavaCveAdvisory>(ordered.Length);
        var lookups = ordered.Take(MaxOsvLookups).ToArray();
        var enriched = new Dictionary<string, OsvInfo?>(StringComparer.Ordinal);
        await Task.WhenAll(lookups.Select(async vulnerability =>
        {
            var info = await GetOsvAsync(vulnerability.Id, cancellationToken)
                .ConfigureAwait(false);
            lock (enriched)
            {
                enriched[vulnerability.Id] = info;
            }
        })).ConfigureAwait(false);

        foreach (var vulnerability in ordered)
        {
            enriched.TryGetValue(vulnerability.Id, out var info);
            if (info is not null) osvUsed = true;
            var severity = vulnerability.Severity;
            if (severity == "unknown" && info?.Severity is { } osvSeverity)
            {
                severity = JavaCveSeverity.Normalize(osvSeverity, vulnerability.Score);
            }

            var summary = vulnerability.Summary;
            if (string.IsNullOrWhiteSpace(summary))
            {
                summary = info?.Summary ?? string.Empty;
            }

            items.Add(new JavaCveAdvisory(
                vulnerability.Id,
                severity,
                vulnerability.Score,
                summary,
                vulnerability.Url,
                vulnerability.Published,
                info?.Aliases ?? []));
        }

        return (items, osvUsed);
    }

    private async Task<OsvInfo?> GetOsvAsync(
        string id,
        CancellationToken cancellationToken)
    {
        var now = _now();
        if (_osv.TryGetValue(id, out var cached) && cached.Expires > now)
        {
            return cached.Info;
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken);
        timeout.CancelAfter(OsvTimeout);
        try
        {
            var uri = new Uri(_osvBase, Uri.EscapeDataString(id));
            using var response = await _http
                .GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, timeout.Token)
                .ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                _osv[id] = new OsvCacheEntry(now.Add(OsvTtl), null);
                return null;
            }

            await using var stream = await response.Content
                .ReadAsStreamAsync(timeout.Token)
                .ConfigureAwait(false);
            using var document = await JsonDocument
                .ParseAsync(stream, cancellationToken: timeout.Token)
                .ConfigureAwait(false);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            var summary = Text(root, "summary") ?? Text(root, "details");
            var aliases = new List<string>();
            if (root.TryGetProperty("aliases", out var aliasList) &&
                aliasList.ValueKind == JsonValueKind.Array)
            {
                foreach (var alias in aliasList.EnumerateArray())
                {
                    if (alias.ValueKind == JsonValueKind.String &&
                        alias.GetString() is { Length: > 0 } value &&
                        aliases.Count < 8)
                    {
                        aliases.Add(value);
                    }
                }
            }

            string? severity = null;
            if (root.TryGetProperty("database_specific", out var specific) &&
                specific.ValueKind == JsonValueKind.Object)
            {
                severity = Text(specific, "severity");
            }

            var info = new OsvInfo(
                Truncate(summary),
                severity,
                aliases);
            _osv[id] = new OsvCacheEntry(now.Add(OsvTtl), info);
            return info;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is HttpRequestException or
                TaskCanceledException or
                JsonException)
        {
            _osv[id] = new OsvCacheEntry(now.Add(OsvTtl), null);
            return null;
        }
    }

    private bool TryGetReport(
        string key,
        DateTimeOffset now,
        out JavaCveReport report)
    {
        if (_reports.TryGetValue(key, out var entry) && entry.Expires > now)
        {
            report = entry.Report;
            return true;
        }

        report = null!;
        return false;
    }

    private void RememberReport(
        string key,
        JavaCveReport report,
        DateTimeOffset expires)
    {
        if (_reports.Count > 256)
        {
            foreach (var pair in _reports)
            {
                if (pair.Value.Expires <= _now()) _reports.TryRemove(pair.Key, out _);
            }
        }

        _reports[key] = new CacheEntry(expires, report);
    }

    private static string? Text(JsonElement element, string name)
        => element.ValueKind == JsonValueKind.Object &&
           element.TryGetProperty(name, out var value) &&
           value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static string? Truncate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var text = value.Trim();
        return text.Length <= 500 ? text : text[..500].TrimEnd() + "…";
    }

    private sealed record CacheEntry(DateTimeOffset Expires, JavaCveReport Report);

    private sealed record OsvInfo(
        string? Summary,
        string? Severity,
        IReadOnlyList<string> Aliases);

    private sealed record OsvCacheEntry(DateTimeOffset Expires, OsvInfo? Info);
}
