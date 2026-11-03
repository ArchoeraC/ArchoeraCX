using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;

namespace Archoera.Infrastructure.Java.Distributions;

/// <summary>
/// NVD-backed fallback used when <c>services.nvd.nist.gov</c> throttles or is
/// unreachable. CIRCL's vulnerability-lookup mirrors NVD CVE records and
/// exposes Oracle Java SE affected versions as plain version strings, which is
/// both smaller and faster than the full NVD CPE feed.
/// </summary>
internal sealed class JavaCveCirclFeed
{
    private const int PageSize = 100;
    private const int BatchSize = 5;
    private const int MaxPages = 25;
    private const int MaxBytes = 4 * 1024 * 1024;
    private const string CacheProduct = "circl:oracle:jdk";

    private static readonly TimeSpan CacheTtl = TimeSpan.FromHours(24);
    private static readonly TimeSpan FailureTtl = TimeSpan.FromMinutes(2);

    private readonly HttpClient _http;
    private readonly string _cachePath;
    private readonly Uri _base;
    private readonly Func<DateTimeOffset> _now;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private CacheEntry? _cache;
    private DateTimeOffset _failureUntil;

    public JavaCveCirclFeed(
        HttpClient http,
        string dataRoot,
        Uri? baseUri = null,
        Func<DateTimeOffset>? clock = null)
    {
        _http = http;
        _cachePath = Path.Combine(
            dataRoot,
            "cache",
            "java-cve",
            "circl-oracle-jdk.json");
        _base = baseUri ?? new Uri("https://cve.circl.lu/api/search/oracle/jdk");
        _now = clock ?? (() => DateTimeOffset.UtcNow);
    }

    public async Task<IReadOnlyList<JavaNvdVulnerability>> GetAsync(
        CancellationToken cancellationToken)
    {
        var now = _now();
        if (_cache is { } cached && cached.Expires > now)
        {
            return cached.Vulnerabilities;
        }

        if (_failureUntil > now)
        {
            throw new JavaCveFetchException(
                "JAVA_CVE_RATE_LIMITED",
                "the CVE mirror asked clients to retry later");
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            now = _now();
            if (_cache is { } memory && memory.Expires > now)
            {
                return memory.Vulnerabilities;
            }

            var disk = JavaCveCacheFile.Read(
                _cachePath,
                CacheProduct,
                CacheTtl,
                now);
            if (disk is not null)
            {
                _cache = new CacheEntry(now.Add(CacheTtl), disk);
                return disk;
            }

            try
            {
                var fetched = await FetchAsync(cancellationToken).ConfigureAwait(false);
                if (fetched.Count == 0)
                {
                    throw new JavaCveFetchException(
                        "JAVA_CVE_INVALID",
                        "the CVE mirror returned no usable Java records");
                }

                _cache = new CacheEntry(now.Add(CacheTtl), fetched);
                JavaCveCacheFile.Write(_cachePath, CacheProduct, fetched, now);
                return fetched;
            }
            catch (JavaCveFetchException exception)
            {
                if (exception.Code == "JAVA_CVE_RATE_LIMITED")
                {
                    _failureUntil = now.Add(FailureTtl);
                }

                throw;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (HttpRequestException)
            {
                _failureUntil = now.Add(FailureTtl);
                throw new JavaCveFetchException(
                    "JAVA_CVE_UNAVAILABLE",
                    "the CVE mirror is unreachable");
            }
            catch (JsonException exception)
            {
                throw new JavaCveFetchException(
                    "JAVA_CVE_INVALID",
                    $"the CVE mirror returned invalid JSON: {exception.Message}");
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<IReadOnlyList<JavaNvdVulnerability>> FetchAsync(
        CancellationToken cancellationToken)
    {
        var results = new Dictionary<string, JavaNvdVulnerability>(StringComparer.Ordinal);
        for (var start = 1; start <= MaxPages; start += BatchSize)
        {
            var pages = Enumerable
                .Range(start, Math.Min(BatchSize, MaxPages - start + 1))
                .Select(page => FetchPageAsync(page, cancellationToken))
                .ToArray();
            var batches = await Task.WhenAll(pages).ConfigureAwait(false);

            var empty = false;
            foreach (var page in batches)
            {
                if (page.Count == 0) empty = true;
                foreach (var vulnerability in page)
                {
                    results.TryAdd(vulnerability.Id, vulnerability);
                }
            }

            // Pages are contiguous; an empty page means the list ended.
            if (empty) break;
        }

        return results.Values
            .OrderByDescending(vulnerability => vulnerability.Score ?? 0)
            .ThenBy(vulnerability => vulnerability.Id, StringComparer.Ordinal)
            .ToArray();
    }

    private async Task<IReadOnlyList<JavaNvdVulnerability>> FetchPageAsync(
        int page,
        CancellationToken cancellationToken)
    {
        var uri = new Uri(_base, $"?per_page={PageSize}&page={page}");
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.Accept.ParseAdd("application/json");
        using var response = await _http
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        if (response.StatusCode is System.Net.HttpStatusCode.Forbidden or
            System.Net.HttpStatusCode.TooManyRequests)
        {
            throw new JavaCveFetchException(
                "JAVA_CVE_RATE_LIMITED",
                "the CVE mirror rejected the request rate");
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new JavaCveFetchException(
                "JAVA_CVE_UNAVAILABLE",
                $"the CVE mirror returned HTTP {(int)response.StatusCode}");
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
                    "the CVE mirror response is too large");
            }

            memory.Write(buffer, 0, read);
        }

        using var document = JsonDocument.Parse(
            Encoding.UTF8.GetString(memory.GetBuffer(), 0, (int)memory.Length));
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty("results", out var results) ||
            !results.TryGetProperty("nvd", out var list) ||
            list.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var vulnerabilities = new List<JavaNvdVulnerability>();
        foreach (var item in list.EnumerateArray())
        {
            var parsed = ParseItem(item);
            if (parsed is not null) vulnerabilities.Add(parsed);
        }

        return vulnerabilities;
    }

    private static JavaNvdVulnerability? ParseItem(JsonElement item)
    {
        string? id = null;
        JsonElement record;
        if (item.ValueKind == JsonValueKind.Array && item.GetArrayLength() >= 2)
        {
            id = item[0].ValueKind == JsonValueKind.String
                ? item[0].GetString()
                : null;
            record = item[1];
        }
        else if (item.ValueKind == JsonValueKind.Object)
        {
            record = item;
        }
        else
        {
            return null;
        }

        if (record.ValueKind != JsonValueKind.Object) return null;
        if (!record.TryGetProperty("cveMetadata", out var metadata) ||
            metadata.ValueKind != JsonValueKind.Object)
        {
            metadata = default;
        }

        id ??= Text(metadata, "cveId");
        if (string.IsNullOrWhiteSpace(id) ||
            !id.StartsWith("CVE-", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        id = id.ToUpperInvariant();
        if (!record.TryGetProperty("containers", out var containers) ||
            containers.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var cna = Child(containers, "cna");
        var nvd = Child(containers, "nvd");
        var summary = Description(cna) ?? Description(nvd);
        var (severity, score) = Metric(cna);
        if (score is null && severity is null)
        {
            (severity, score) = Metric(nvd);
        }

        var matches = Matches(id, cna);
        if (matches.Count == 0) matches = Matches(id, nvd);
        if (matches.Count == 0) return null;

        return new JavaNvdVulnerability(
            id,
            summary,
            Text(metadata, "datePublished"),
            JavaCveSeverity.Normalize(severity, score),
            score,
            $"https://nvd.nist.gov/vuln/detail/{id}",
            matches);
    }

    private static IReadOnlyList<JavaCpeMatch> Matches(
        string id,
        JsonElement container)
    {
        if (container.ValueKind != JsonValueKind.Object ||
            !container.TryGetProperty("affected", out var affected) ||
            affected.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var matches = new List<JavaCpeMatch>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in affected.EnumerateArray())
        {
            if (entry.ValueKind != JsonValueKind.Object) continue;
            var entryProduct = Text(entry, "product");
            if (!entry.TryGetProperty("versions", out var versions) ||
                versions.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var version in versions.EnumerateArray())
            {
                var text = version.ValueKind == JsonValueKind.String
                    ? version.GetString()
                    : Text(version, "version");
                var status = version.ValueKind == JsonValueKind.Object
                    ? Text(version, "status")
                    : null;
                if (string.IsNullOrWhiteSpace(text) ||
                    (status is not null &&
                     !string.Equals(status, "affected", StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                var affectedVersion = ParseAffectedVersion(entryProduct, text);
                if (affectedVersion is null) continue;
                var (product, versionText) = affectedVersion.Value;
                var parsed = JavaCveVersion.Parse(versionText);
                if (parsed.Major is < 8 or > 99) continue;
                var key = $"{product}|{versionText}";
                if (!seen.Add(key)) continue;
                matches.Add(JavaCpeMatch.Synthetic(product, versionText));
            }
        }

        return matches;
    }

    /// <summary>
    /// CIRCL puts product qualifiers inside the version string for mixed
    /// entries ("Java SE:8u291", "Oracle GraalVM for JDK:21.0.3"). Resolve the
    /// qualifier per version so an Enterprise GraalVM release is never
    /// mistaken for an OpenJDK 21 update.
    /// </summary>
    private static (string Product, string Version)? ParseAffectedVersion(
        string? entryProduct,
        string version)
    {
        var text = version.Trim();
        string? qualifier = null;
        var colon = text.IndexOf(':');
        if (colon > 0)
        {
            var prefix = text[..colon].Trim();
            if (prefix.Contains("java", StringComparison.OrdinalIgnoreCase) ||
                prefix.Contains("graalvm", StringComparison.OrdinalIgnoreCase))
            {
                qualifier = prefix;
                text = text[(colon + 1)..].Trim();
            }
        }

        var productText = qualifier ?? entryProduct ?? string.Empty;
        var lower = productText.ToLowerInvariant();
        if (lower.Contains("graalvm", StringComparison.Ordinal))
        {
            if (lower.Contains("enterprise", StringComparison.Ordinal) ||
                !lower.Contains("jdk", StringComparison.Ordinal))
            {
                return null;
            }

            return ("oracle:graalvm_for_jdk", text);
        }

        if (lower.Contains("java se", StringComparison.Ordinal) ||
            lower.Contains("jdk", StringComparison.Ordinal) ||
            lower.Contains("jre", StringComparison.Ordinal))
        {
            return ("oracle:jdk", text);
        }

        return null;
    }

        private static string? Description(JsonElement container)
    {
        if (container.ValueKind != JsonValueKind.Object ||
            !container.TryGetProperty("descriptions", out var descriptions) ||
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

    private static (string? Severity, double? Score) Metric(JsonElement container)
    {
        if (container.ValueKind != JsonValueKind.Object ||
            !container.TryGetProperty("metrics", out var metrics))
        {
            return (null, null);
        }

        if (metrics.ValueKind == JsonValueKind.Array)
        {
            foreach (var metric in metrics.EnumerateArray())
            {
                foreach (var key in new[]
                         {
                             "cvssV4_0", "cvssV3_1", "cvssV3_0", "cvssV2_0",
                         })
                {
                    var data = Child(metric, key);
                    if (data.ValueKind != JsonValueKind.Object) continue;
                    var score = Number(data, "baseScore");
                    var severity = Text(data, "baseSeverity");
                    if (score is not null || severity is not null)
                    {
                        return (severity, score);
                    }
                }
            }

            return (null, null);
        }

        if (metrics.ValueKind != JsonValueKind.Object) return (null, null);
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
                var data = Child(metric, "cvssData");
                if (data.ValueKind != JsonValueKind.Object) continue;
                var score = Number(data, "baseScore");
                var severity = Text(data, "baseSeverity");
                if (score is not null || severity is not null)
                {
                    return (severity, score);
                }
            }
        }

        return (null, null);
    }

    private static string? Clean(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var text = System.Text.RegularExpressions.Regex.Replace(
            value,
            @"\s+",
            " ").Trim();
        return text.Length <= 500 ? text : text[..500].TrimEnd() + "…";
    }

    private static JsonElement Child(JsonElement element, string name)
        => element.ValueKind == JsonValueKind.Object &&
           element.TryGetProperty(name, out var value)
            ? value
            : default;

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

    private sealed record CacheEntry(
        DateTimeOffset Expires,
        IReadOnlyList<JavaNvdVulnerability> Vulnerabilities);
}
