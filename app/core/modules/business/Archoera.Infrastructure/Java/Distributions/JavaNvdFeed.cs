using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Archoera.Infrastructure.Java.Distributions;

/// <summary>
/// Minimal NVD 2.0 client for the Java version currently being installed.
/// A keyword search by the selected version prefix is tens of kilobytes,
/// whereas the full <c>oracle:jdk</c> CPE feed is several megabytes and is
/// frequently throttled. Results are cached per prefix for a day.
/// </summary>
internal sealed partial class JavaNvdFeed
{
    private const int PageSize = 2000;
    private const int MaxPages = 5;
    private const int MaxBytes = 4 * 1024 * 1024;

    private static readonly TimeSpan CacheTtl = TimeSpan.FromHours(24);
    private static readonly TimeSpan FailureTtl = TimeSpan.FromMinutes(2);

    private readonly HttpClient _http;
    private readonly string _cacheRoot;
    private readonly Uri _base;
    private readonly Func<DateTimeOffset> _now;
    private readonly string? _apiKey;
    private readonly ConcurrentDictionary<string, CacheEntry> _cache =
        new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, DateTimeOffset> _failures =
        new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _gate = new(1, 1);

    public JavaNvdFeed(
        HttpClient http,
        string dataRoot,
        Uri? baseUri = null,
        Func<DateTimeOffset>? clock = null,
        string? apiKey = null)
    {
        _http = http;
        _cacheRoot = Path.Combine(dataRoot, "cache", "java-cve");
        _base = baseUri ?? new Uri("https://services.nvd.nist.gov/rest/json/cves/2.0");
        _now = clock ?? (() => DateTimeOffset.UtcNow);
        _apiKey = string.IsNullOrWhiteSpace(apiKey)
            ? Environment.GetEnvironmentVariable("ARCHOERACX_NVD_API_KEY")
            : apiKey;
    }

    /// <summary>NVD CVEs whose descriptions mention this Oracle Java SE prefix.</summary>
    public Task<IReadOnlyList<JavaNvdVulnerability>> GetOpenJdkAsync(
        string prefix,
        CancellationToken cancellationToken)
        => GetCoreAsync(
            $"nvd:openjdk:{prefix}",
            $"keywordSearch={Uri.EscapeDataString($"Oracle Java SE {prefix}")}",
            cancellationToken);

    /// <summary>GraalVM-for-JDK-only CVEs that do not mention Oracle Java SE.</summary>
    public Task<IReadOnlyList<JavaNvdVulnerability>> GetGraalVmAsync(
        string prefix,
        CancellationToken cancellationToken)
        => GetCoreAsync(
            $"nvd:graalvm:{prefix}",
            $"keywordSearch={Uri.EscapeDataString($"Oracle GraalVM for JDK {prefix}")}",
            cancellationToken);

    private async Task<IReadOnlyList<JavaNvdVulnerability>> GetCoreAsync(
        string cacheKey,
        string filter,
        CancellationToken cancellationToken)
    {
        var now = _now();
        if (TryGetMemory(cacheKey, now, out var cached)) return cached;
        if (_failures.TryGetValue(cacheKey, out var retryAt) && retryAt > now)
        {
            throw new JavaCveFetchException(
                "JAVA_CVE_RATE_LIMITED",
                "the vulnerability feed asked clients to retry later");
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            now = _now();
            if (TryGetMemory(cacheKey, now, out cached)) return cached;

            var disk = JavaCveCacheFile.Read(
                CachePath(cacheKey),
                cacheKey,
                CacheTtl,
                now);
            if (disk is not null)
            {
                Remember(cacheKey, disk, now.Add(CacheTtl));
                return disk;
            }

            try
            {
                // An empty keyword result is a valid answer: the selected
                // patch may simply have no published CVEs yet.
                var fetched = await FetchAsync(filter, cancellationToken)
                    .ConfigureAwait(false);
                Remember(cacheKey, fetched, now.Add(CacheTtl));
                JavaCveCacheFile.Write(
                    CachePath(cacheKey),
                    cacheKey,
                    fetched,
                    now);
                _failures.TryRemove(cacheKey, out _);
                return fetched;
            }
            catch (JavaCveFetchException exception)
            {
                if (exception.Code == "JAVA_CVE_RATE_LIMITED")
                {
                    _failures[cacheKey] = now.Add(FailureTtl);
                }

                throw;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception) when (
                exception is HttpRequestException or TaskCanceledException)
            {
                _failures[cacheKey] = now.Add(FailureTtl);
                throw new JavaCveFetchException(
                    "JAVA_CVE_UNAVAILABLE",
                    "the vulnerability feed is unreachable");
            }
            catch (JsonException exception)
            {
                throw new JavaCveFetchException(
                    "JAVA_CVE_INVALID",
                    $"the vulnerability feed returned invalid JSON: {exception.Message}");
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<IReadOnlyList<JavaNvdVulnerability>> FetchAsync(
        string filter,
        CancellationToken cancellationToken)
    {
        var results = new Dictionary<string, JavaNvdVulnerability>(StringComparer.Ordinal);
        var startIndex = 0;
        for (var page = 0; page < MaxPages; page++)
        {
            var uri = new Uri(
                _base,
                $"?{filter}&resultsPerPage={PageSize}&startIndex={startIndex}");
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.Accept.ParseAdd("application/json");
            if (!string.IsNullOrWhiteSpace(_apiKey))
            {
                request.Headers.TryAddWithoutValidation("apiKey", _apiKey);
            }

            using var response = await _http
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);
            if (response.StatusCode is System.Net.HttpStatusCode.Forbidden or
                System.Net.HttpStatusCode.TooManyRequests)
            {
                throw new JavaCveFetchException(
                    "JAVA_CVE_RATE_LIMITED",
                    "the vulnerability feed rejected the request rate");
            }

            if (!response.IsSuccessStatusCode)
            {
                throw new JavaCveFetchException(
                    "JAVA_CVE_UNAVAILABLE",
                    $"the vulnerability feed returned HTTP " +
                    $"{(int)response.StatusCode}");
            }

            await using var stream = await response.Content
                .ReadAsStreamAsync(cancellationToken)
                .ConfigureAwait(false);
            using var memory = new MemoryStream();
            var buffer = new byte[32 * 1024];
            while (true)
            {
                var read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
                if (read == 0) break;
                if (memory.Length + read > MaxBytes)
                {
                    throw new JavaCveFetchException(
                        "JAVA_CVE_INVALID",
                        "the vulnerability feed response is too large");
                }

                memory.Write(buffer, 0, read);
            }

            using var document = JsonDocument.Parse(
                Encoding.UTF8.GetString(memory.GetBuffer(), 0, (int)memory.Length));
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("vulnerabilities", out var vulnerabilities) ||
                vulnerabilities.ValueKind != JsonValueKind.Array)
            {
                throw new JavaCveFetchException(
                    "JAVA_CVE_INVALID",
                    "the vulnerability feed has no vulnerabilities array");
            }

            var pageCount = 0;
            foreach (var item in vulnerabilities.EnumerateArray())
            {
                pageCount++;
                var parsed = ParseVulnerability(item);
                if (parsed is not null) results[parsed.Id] = parsed;
            }

            var total = root.TryGetProperty("totalResults", out var totalValue) &&
                        totalValue.TryGetInt32(out var totalNumber)
                ? totalNumber
                : 0;
            startIndex += pageCount;
            if (pageCount == 0 || total <= startIndex) break;
        }

        return results.Values
            .OrderByDescending(vulnerability => vulnerability.Score ?? 0)
            .ThenBy(vulnerability => vulnerability.Id, StringComparer.Ordinal)
            .ToArray();
    }

    private static JavaNvdVulnerability? ParseVulnerability(JsonElement item)
    {
        if (item.ValueKind != JsonValueKind.Object ||
            !item.TryGetProperty("cve", out var cve) ||
            cve.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var id = Text(cve, "id");
        if (string.IsNullOrWhiteSpace(id) || !id.StartsWith("CVE-", StringComparison.Ordinal))
        {
            return null;
        }

        var description = Description(cve);
        var summary = Truncate(description);
        var published = Text(cve, "published");
        var (severity, score) = Metric(cve);
        var matches = Matches(cve);
        MergeMatches(matches, JavaOracleDescriptionParser.Parse(description));
        if (matches.Count == 0) return null;

        return new JavaNvdVulnerability(
            id,
            summary,
            published,
            JavaCveSeverity.Normalize(severity, score),
            score,
            $"https://nvd.nist.gov/vuln/detail/{id}",
            matches);
    }

    private static void MergeMatches(
        List<JavaCpeMatch> matches,
        IReadOnlyList<JavaCpeMatch> extra)
    {
        if (extra.Count == 0) return;
        var seen = new HashSet<string>(
            matches.Select(match => $"{match.Product}|{match.Version}"),
            StringComparer.Ordinal);
        foreach (var match in extra)
        {
            if (seen.Add($"{match.Product}|{match.Version}"))
            {
                matches.Add(match);
            }
        }
    }

    private static string? Description(JsonElement cve)
    {
        if (!cve.TryGetProperty("descriptions", out var descriptions) ||
            descriptions.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var description in descriptions.EnumerateArray())
        {
            if (Text(description, "lang") == "en")
            {
                return Clean(Text(description, "value"));
            }
        }

        return null;
    }

    private static (string? Severity, double? Score) Metric(JsonElement cve)
    {
        if (!cve.TryGetProperty("metrics", out var metrics) ||
            metrics.ValueKind != JsonValueKind.Object)
        {
            return (null, null);
        }

        foreach (var key in new[]
                 {
                     "cvssMetricV40", "cvssMetricV31", "cvssMetricV30", "cvssMetricV2",
                 })
        {
            if (!metrics.TryGetProperty(key, out var list) ||
                list.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var metric in list.EnumerateArray())
            {
                if (!metric.TryGetProperty("cvssData", out var data) ||
                    data.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                var score = Number(data, "baseScore");
                var severity = Text(data, "baseSeverity");
                if (score is not null || !string.IsNullOrWhiteSpace(severity))
                {
                    return (severity, score);
                }
            }
        }

        return (null, null);
    }

    private static List<JavaCpeMatch> Matches(JsonElement cve)
    {
        if (!cve.TryGetProperty("configurations", out var configurations) ||
            configurations.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var matches = new List<JavaCpeMatch>();
        foreach (var configuration in configurations.EnumerateArray())
        {
            if (!configuration.TryGetProperty("nodes", out var nodes) ||
                nodes.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var node in nodes.EnumerateArray())
            {
                CollectMatches(node, matches);
            }
        }

        return matches;
    }

    private static void CollectMatches(JsonElement node, List<JavaCpeMatch> matches)
    {
        if (node.ValueKind != JsonValueKind.Object) return;
        if (node.TryGetProperty("cpeMatch", out var cpeMatches) &&
            cpeMatches.ValueKind == JsonValueKind.Array)
        {
            foreach (var match in cpeMatches.EnumerateArray())
            {
                var parsed = JavaCpeMatch.Parse(
                    Text(match, "criteria"),
                    match.TryGetProperty("vulnerable", out var vulnerable) &&
                    vulnerable.ValueKind == JsonValueKind.True,
                    Text(match, "versionStartIncluding"),
                    Text(match, "versionStartExcluding"),
                    Text(match, "versionEndIncluding"),
                    Text(match, "versionEndExcluding"));
                if (parsed is not null) matches.Add(parsed);
            }
        }

        if (node.TryGetProperty("children", out var children) &&
            children.ValueKind == JsonValueKind.Array)
        {
            foreach (var child in children.EnumerateArray())
            {
                CollectMatches(child, matches);
            }
        }
    }

    private static string? Clean(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        return Whitespace().Replace(value, " ").Trim();
    }

    private static string? Truncate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        return value.Length <= 500 ? value : value[..500].TrimEnd() + "…";
    }

    private static string? Text(JsonElement element, string name)
        => element.ValueKind == JsonValueKind.Object &&
           element.TryGetProperty(name, out var value) &&
           value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static double? Number(JsonElement element, string name)
        => element.ValueKind == JsonValueKind.Object &&
           element.TryGetProperty(name, out var value) &&
           value.ValueKind == JsonValueKind.Number &&
           value.TryGetDouble(out var number)
            ? number
            : null;

    private bool TryGetMemory(
        string product,
        DateTimeOffset now,
        out IReadOnlyList<JavaNvdVulnerability> vulnerabilities)
    {
        if (_cache.TryGetValue(product, out var entry) && entry.Expires > now)
        {
            vulnerabilities = entry.Vulnerabilities;
            return true;
        }

        vulnerabilities = [];
        return false;
    }

    private void Remember(
        string product,
        IReadOnlyList<JavaNvdVulnerability> vulnerabilities,
        DateTimeOffset expires)
    {
        if (_cache.Count > 16) _cache.Clear();
        _cache[product] = new CacheEntry(expires, vulnerabilities);
    }

    private string CachePath(string cacheKey)
        => Path.Combine(_cacheRoot, $"{Slug(cacheKey)}.json");

    private static string Slug(string product)
        => ProductSlug().Replace(product, "-");

    [GeneratedRegex("[^A-Za-z0-9._-]+")]
    private static partial Regex ProductSlug();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    private sealed record CacheEntry(
        DateTimeOffset Expires,
        IReadOnlyList<JavaNvdVulnerability> Vulnerabilities);
}

/// <summary>One NVD CVE reduced to the Java CPE data the catalogue needs.</summary>
internal sealed record JavaNvdVulnerability(
    string Id,
    string? Summary,
    string? Published,
    string Severity,
    double? Score,
    string? Url,
    IReadOnlyList<JavaCpeMatch> Matches)
{
    public bool Affects(
        JavaCveVersion version,
        IReadOnlyCollection<string> products)
    {
        foreach (var match in Matches)
        {
            if (!products.Contains(match.Product, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            if (match.Matches(version, match.Product)) return true;
        }

        return false;
    }
}

/// <summary>Stable failure raised by the CVE feeds.</summary>
internal sealed class JavaCveFetchException(string code, string message)
    : Exception(message)
{
    public string Code { get; } = code;
}
