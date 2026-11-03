using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using Archoera.Application.JavaRuntime;
using Archoera.Infrastructure.Java;
using Archoera.Infrastructure.Java.Distributions;

namespace Archoera.Tests;

public sealed partial class JavaDistributionTests
{
    [Fact]
    public void Java_version_parser_handles_vendor_shapes()
    {
        var cases = new (string Version, int Major, int Segments, int? Update)[]
        {
            ("21.0.13+9", 21, 3, null),
            ("21.0.13.0.3", 21, 5, null),
            ("8.0.402+6", 8, 3, 402),
            ("1.8.0_402", 8, 1, 402),
            ("1.8.0.491", 8, 1, 491),
            ("8u402", 8, 1, 402),
            ("25i4-25.0.4.1.1", 25, 5, null),
            ("21+35", 21, 1, null),
        };

        foreach (var item in cases)
        {
            var parsed = JavaCveVersion.Parse(item.Version);
            Assert.Equal(item.Major, parsed.Major);
            Assert.Equal(item.Segments, parsed.Segments.Count);
            Assert.Equal(item.Update, parsed.Update);
        }
    }

    [Fact]
    public void Cpe_range_matches_only_versions_below_the_fix()
    {
        var match = JavaCpeMatch.Parse(
            "cpe:2.3:a:oracle:jdk:*:*:*:*:*:*:*:*",
            vulnerable: true,
            versionStartIncluding: "21",
            versionStartExcluding: null,
            versionEndIncluding: null,
            versionEndExcluding: "21.0.2")!;

        Assert.True(match.Matches(JavaCveVersion.Parse("21+35"), "oracle:jdk"));
        Assert.True(match.Matches(JavaCveVersion.Parse("21.0.1+9"), "oracle:jdk"));
        Assert.True(match.Matches(JavaCveVersion.Parse("21.0.1.0.1"), "oracle:jdk"));
        Assert.False(match.Matches(JavaCveVersion.Parse("21.0.2"), "oracle:jdk"));
        Assert.False(match.Matches(JavaCveVersion.Parse("21.0.2.0.1"), "oracle:jdk"));
        Assert.False(match.Matches(JavaCveVersion.Parse("17.0.10"), "oracle:jdk"));
    }

    [Fact]
    public void Exact_cpe_is_treated_as_the_last_affected_cpu_version()
    {
        var match = JavaCpeMatch.Parse(
            "cpe:2.3:a:oracle:jdk:21.0.11:*:*:*:*:*:*:*",
            vulnerable: true,
            versionStartIncluding: null,
            versionStartExcluding: null,
            versionEndIncluding: null,
            versionEndExcluding: null)!;

        Assert.True(match.Matches(JavaCveVersion.Parse("21.0.10"), "oracle:jdk"));
        Assert.True(match.Matches(JavaCveVersion.Parse("21.0.11+9"), "oracle:jdk"));
        Assert.True(match.Matches(JavaCveVersion.Parse("21.0.11.9.1"), "oracle:jdk"));
        Assert.False(match.Matches(JavaCveVersion.Parse("21.0.12"), "oracle:jdk"));
    }

    [Fact]
    public void Java8_cpe_update_compares_the_update_number()
    {
        var match = JavaCpeMatch.Parse(
            "cpe:2.3:a:oracle:jdk:1.8.0:update491:*:*:-:*:*:*",
            vulnerable: true,
            versionStartIncluding: null,
            versionStartExcluding: null,
            versionEndIncluding: null,
            versionEndExcluding: null)!;

        Assert.True(match.Matches(JavaCveVersion.Parse("8.0.402+6"), "oracle:jdk"));
        Assert.True(match.Matches(JavaCveVersion.Parse("1.8.0_491"), "oracle:jdk"));
        Assert.False(match.Matches(JavaCveVersion.Parse("8.0.492"), "oracle:jdk"));
        Assert.False(match.Matches(JavaCveVersion.Parse("21.0.11"), "oracle:jdk"));

        var build = JavaCpeMatch.Parse(
            "cpe:2.3:a:oracle:jdk:1.8.0:update471_b50:*:*:-:*:*:*",
            vulnerable: true,
            versionStartIncluding: null,
            versionStartExcluding: null,
            versionEndIncluding: null,
            versionEndExcluding: null)!;
        Assert.True(build.Matches(JavaCveVersion.Parse("8.0.402"), "oracle:jdk"));
        Assert.False(build.Matches(JavaCveVersion.Parse("8.0.504"), "oracle:jdk"));
    }

    [Fact]
    public void Major_only_cpe_does_not_flag_later_updates()
    {
        var match = JavaCpeMatch.Parse(
            "cpe:2.3:a:oracle:jdk:21:*:*:*:*:*:*:*",
            vulnerable: true,
            versionStartIncluding: null,
            versionStartExcluding: null,
            versionEndIncluding: null,
            versionEndExcluding: null)!;

        Assert.True(match.Matches(JavaCveVersion.Parse("21"), "oracle:jdk"));
        Assert.True(match.Matches(JavaCveVersion.Parse("21.0.0"), "oracle:jdk"));
        Assert.False(match.Matches(JavaCveVersion.Parse("21.0.5"), "oracle:jdk"));
    }

    [Fact]
    public void Cpe_for_another_major_or_a_bare_wildcard_is_ignored()
    {
        var exact = JavaCpeMatch.Parse(
            "cpe:2.3:a:oracle:jdk:25.0.3:*:*:*:*:*:*:*",
            vulnerable: true,
            versionStartIncluding: null,
            versionStartExcluding: null,
            versionEndIncluding: null,
            versionEndExcluding: null)!;
        Assert.False(exact.Matches(JavaCveVersion.Parse("21.0.13"), "oracle:jdk"));
        Assert.True(exact.Matches(JavaCveVersion.Parse("25.0.3"), "oracle:jdk"));

        var wildcard = JavaCpeMatch.Parse(
            "cpe:2.3:a:oracle:jdk:*:*:*:*:*:*:*:*",
            vulnerable: true,
            versionStartIncluding: null,
            versionStartExcluding: null,
            versionEndIncluding: null,
            versionEndExcluding: null)!;
        Assert.False(wildcard.Matches(JavaCveVersion.Parse("21.0.13"), "oracle:jdk"));
    }

    [Fact]
    public void Oracle_description_parser_extracts_jdk_and_graalvm_versions()
    {
        var matches = JavaOracleDescriptionParser.Parse(
            "Vulnerability in the Oracle Java SE product. Supported versions " +
            "that are affected are Oracle Java SE: 8u511, 11.0.33, 21.0.13; " +
            "Oracle GraalVM for JDK: 17.0.21 and 21.0.13; " +
            "Oracle GraalVM Enterprise Edition: 21.3.20.");

        Assert.Contains(
            matches,
            match => match.Product == "oracle:jdk" && match.Version == "8u511");
        Assert.Contains(
            matches,
            match => match.Product == "oracle:jdk" && match.Version == "21.0.13");
        Assert.Contains(
            matches,
            match => match.Product == "oracle:graalvm_for_jdk" &&
                     match.Version == "21.0.13");
        Assert.DoesNotContain(
            matches,
            match => match.Version == "21.3.20");
    }

    [Fact]
    public async Task Nvd_query_is_scoped_to_the_selected_version_decade()
    {
        var root = TempDirectory();
        try
        {
            var handler = new FakeHttpMessageHandler();
            handler.EnqueueJson(NvdAdvisoryJson());
            handler.EnqueueJson("""{"summary":"OSV"}""");
            var catalog = new JavaCveCatalog(
                new HttpClient(handler),
                root,
                new Uri("https://nvd.test/cves/2.0"),
                new Uri("https://osv.test/v1/vulns/"));

            await catalog.GetReportAsync(
                "adoptium",
                "21.0.13+9",
                "21.0.13+9",
                CancellationToken.None);

            var query = handler.Requests[0].Uri.Query;
            Assert.Contains("keywordSearch=Oracle%20Java%20SE%2021.0.1", query);
            Assert.DoesNotContain("virtualMatchString", query);
        }
        finally
        {
            Delete(root);
        }
    }

    [Fact]
    public async Task Description_only_nvd_cve_still_matches_the_last_affected_version()
    {
        var root = TempDirectory();
        try
        {
            var handler = new FakeHttpMessageHandler();
            handler.EnqueueJson(NvdDescriptionOnlyJson());
            handler.EnqueueJson("""{"summary":"OSV"}""");
            var catalog = new JavaCveCatalog(
                new HttpClient(handler),
                root,
                new Uri("https://nvd.test/cves/2.0"),
                new Uri("https://osv.test/v1/vulns/"));

            var report = await catalog.GetReportAsync(
                "adoptium",
                "21.0.13+9",
                "21.0.13+9",
                CancellationToken.None);

            Assert.Equal(JavaCveReport.OkStatus, report.Status);
            var advisory = Assert.Single(report.Advisories);
            Assert.Equal("CVE-2026-91403", advisory.Id);
        }
        finally
        {
            Delete(root);
        }
    }

    [Fact]
    public async Task Empty_nvd_keyword_result_is_a_valid_zero_report()
    {
        var root = TempDirectory();
        try
        {
            var handler = new FakeHttpMessageHandler();
            handler.EnqueueJson("""{"totalResults":0,"vulnerabilities":[]}""");
            var catalog = new JavaCveCatalog(
                new HttpClient(handler),
                root,
                new Uri("https://nvd.test/cves/2.0"),
                new Uri("https://osv.test/v1/vulns/"));

            var report = await catalog.GetReportAsync(
                "adoptium",
                "26.0.2+9",
                "26.0.2+9",
                CancellationToken.None);

            Assert.Equal(JavaCveReport.OkStatus, report.Status);
            Assert.Empty(report.Advisories);
            Assert.Contains("nvd", report.Sources);
        }
        finally
        {
            Delete(root);
        }
    }

    [Fact]
    public async Task Catalog_merges_nvd_ranges_with_osv_aliases()
    {
        var root = TempDirectory();
        try
        {
            var handler = new FakeHttpMessageHandler();
            handler.EnqueueJson(NvdAdvisoryJson());
            handler.EnqueueJson(
                """
                {"summary":"OSV fallback summary",
                 "aliases":["GHSA-aaaa-bbbb-cccc"],
                 "database_specific":{"severity":"HIGH"}}
                """);
            var catalog = new JavaCveCatalog(
                new HttpClient(handler),
                root,
                new Uri("https://nvd.test/cves/2.0"),
                new Uri("https://osv.test/v1/vulns/"));

            var report = await catalog.GetReportAsync(
                "adoptium",
                "21.0.1+9",
                "21.0.1+9",
                CancellationToken.None);

            Assert.Equal(JavaCveReport.OkStatus, report.Status);
            var advisory = Assert.Single(report.Advisories);
            Assert.Equal("CVE-2024-21131", advisory.Id);
            Assert.Equal("high", advisory.Severity);
            Assert.Equal(7.5, advisory.Score);
            Assert.Equal("OSV fallback summary", advisory.Summary);
            Assert.Contains("GHSA-aaaa-bbbb-cccc", advisory.Aliases);
            Assert.Contains("nvd", report.Sources);
            Assert.Contains("osv", report.Sources);

            await catalog.GetReportAsync(
                "adoptium",
                "21.0.1+9",
                "21.0.1+9",
                CancellationToken.None);
            Assert.Equal(2, handler.Requests.Count);
        }
        finally
        {
            Delete(root);
        }
    }

    [Fact]
    public async Task Catalog_falls_back_to_circl_when_nvd_is_rate_limited()
    {
        var root = TempDirectory();
        try
        {
            var handler = new FakeHttpMessageHandler();
            handler.EnqueueStatus(HttpStatusCode.TooManyRequests);
            for (var page = 0; page < 5; page++)
            {
                handler.EnqueueJson(
                    page == 0 ? CirclPageJson("Java SE:21.0.1") : CirclEmptyPageJson());
            }

            handler.EnqueueJson(
                """
                {"summary":"OSV mirror summary",
                 "aliases":["GHSA-cccc-dddd-eeee"],
                 "database_specific":{"severity":"HIGH"}}
                """);
            var catalog = new JavaCveCatalog(
                new HttpClient(handler),
                root,
                new Uri("https://nvd.test/cves/2.0"),
                new Uri("https://osv.test/v1/vulns/"),
                new Uri("https://circl.test/api/search/oracle/jdk"));

            var report = await catalog.GetReportAsync(
                "adoptium",
                "21.0.1+9",
                "21.0.1+9",
                CancellationToken.None);

            Assert.Equal(JavaCveReport.OkStatus, report.Status);
            var advisory = Assert.Single(report.Advisories);
            Assert.Equal("CVE-2024-21131", advisory.Id);
            Assert.Contains("circl", report.Sources);
            Assert.Contains("osv", report.Sources);
        }
        finally
        {
            Delete(root);
        }
    }

    [Fact]
    public async Task Circl_does_not_treat_graalvm_enterprise_as_openjdk()
    {
        var root = TempDirectory();
        try
        {
            var handler = new FakeHttpMessageHandler();
            handler.EnqueueStatus(HttpStatusCode.TooManyRequests);
            for (var page = 0; page < 5; page++)
            {
                handler.EnqueueJson(
                    page == 0
                        ? CirclPageJson("Java SE:21.0.0",
                            "Oracle GraalVM Enterprise Edition:21.3.1")
                        : CirclEmptyPageJson());
            }

            var catalog = new JavaCveCatalog(
                new HttpClient(handler),
                root,
                new Uri("https://nvd.test/cves/2.0"),
                new Uri("https://osv.test/v1/vulns/"),
                new Uri("https://circl.test/api/search/oracle/jdk"));

            var report = await catalog.GetReportAsync(
                "adoptium",
                "21.0.1+9",
                "21.0.1+9",
                CancellationToken.None);

            Assert.Equal(JavaCveReport.OkStatus, report.Status);
            Assert.Empty(report.Advisories);
            Assert.Contains("circl", report.Sources);
        }
        finally
        {
            Delete(root);
        }
    }

    [Fact]
    public void Cache_round_trips_a_synthetic_cpe_match()
    {
        var root = TempDirectory();
        try
        {
            var path = Path.Combine(root, "cache.json");
            var now = DateTimeOffset.UtcNow;
            var vulnerability = new JavaNvdVulnerability(
                "CVE-2024-21131",
                "summary",
                "2024-04-16T22:15:42.477",
                "high",
                7.5,
                "https://nvd.nist.gov/vuln/detail/CVE-2024-21131",
                [
                    new JavaCpeMatch(
                        "cpe:2.3:a:oracle:jdk:21.0.1:*:*:*:*:*:*:*",
                        "oracle:jdk",
                        "21.0.1",
                        null,
                        true,
                        null,
                        null,
                        null,
                        null),
                ]);

            JavaCveCacheFile.Write(path, "test", [vulnerability], now);
            var read = JavaCveCacheFile.Read(
                path,
                "test",
                TimeSpan.FromHours(1),
                now);

            Assert.NotNull(read);
            var parsed = Assert.Single(read!);
            Assert.True(parsed.Affects(
                JavaCveVersion.Parse("21.0.1+9"),
                ["oracle:jdk"]));
        }
        finally
        {
            Delete(root);
        }
    }

    [Fact]
    public async Task Catalog_reports_unavailable_instead_of_throwing_on_rate_limit()
    {
        var root = TempDirectory();
        try
        {
            var handler = new FakeHttpMessageHandler();
            handler.EnqueueStatus(HttpStatusCode.TooManyRequests);
            for (var page = 0; page < 5; page++)
            {
                handler.EnqueueStatus(HttpStatusCode.TooManyRequests);
            }

            var catalog = new JavaCveCatalog(
                new HttpClient(handler),
                root,
                new Uri("https://nvd.test/cves/2.0"),
                new Uri("https://osv.test/v1/vulns/"),
                new Uri("https://circl.test/api/search/oracle/jdk"));

            var report = await catalog.GetReportAsync(
                "liberica",
                "21.0.6+8",
                "21.0.6+8",
                CancellationToken.None);

            Assert.Equal(JavaCveReport.UnavailableStatus, report.Status);
            Assert.Equal("JAVA_CVE_RATE_LIMITED", report.Reason);
            Assert.Empty(report.Advisories);
        }
        finally
        {
            Delete(root);
        }
    }

    [Fact]
    public async Task Install_requires_acknowledgement_for_a_known_cve()
    {
        var root = TempDirectory();
        try
        {
            var tar = Path.Combine(root, "source.tar.gz");
            WriteTarGz(tar, [("jdk-21/bin/java", "#!/bin/sh\n", true)]);
            var bytes = await File.ReadAllBytesAsync(tar);
            var sha = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
            var registry = new JavaDistributionRegistry(
                [new FakeProvider(sha, bytes.LongLength)]);
            var cve = new FakeCveSource(
                JavaCveReport.Ok(
                    "fake",
                    "21.0.1",
                    [
                        new JavaCveAdvisory(
                            "CVE-2024-21131",
                            "high",
                            7.5,
                            "test",
                            null,
                            null,
                            []),
                    ],
                    ["nvd"],
                    DateTimeOffset.UtcNow));
            var service = new JavaDistributionService(
                registry,
                root,
                new WritingRunner(bytes),
                cve);
            var target = Path.Combine(root, "ack-required");

            var exception = await Assert.ThrowsAsync<JavaDistributionException>(() =>
                service.InstallAsync(
                    "fake",
                    "v1",
                    target,
                    autoExtract: true,
                    autoDelete: true,
                    allowOutdated: false,
                    acknowledgeCves: false,
                    CancellationToken.None));
            Assert.Equal("JAVA_CVE_UNACKNOWLEDGED", exception.Code);
            Assert.False(Directory.Exists(target));

            var result = await service.InstallAsync(
                "fake",
                "v1",
                target,
                autoExtract: true,
                autoDelete: true,
                allowOutdated: false,
                acknowledgeCves: true,
                CancellationToken.None);
            Assert.Equal(target, result.Home);
        }
        finally
        {
            Delete(root);
        }
    }

    [Fact]
    public async Task Advisories_resolve_the_provider_version_before_the_lookup()
    {
        var root = TempDirectory();
        try
        {
            var tar = Path.Combine(root, "source.tar.gz");
            WriteTarGz(tar, [("jdk-21/bin/java", "#!/bin/sh\n", true)]);
            var bytes = await File.ReadAllBytesAsync(tar);
            var sha = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
            var registry = new JavaDistributionRegistry(
                [new FakeProvider(sha, bytes.LongLength)]);
            var cve = new FakeCveSource(
                JavaCveReport.Ok("fake", "21.0.1", [], ["nvd"], DateTimeOffset.UtcNow));
            var service = new JavaDistributionService(
                registry,
                root,
                new WritingRunner(bytes),
                cve);

            var report = await service.GetAdvisoriesAsync(
                "fake",
                21,
                "v1",
                CancellationToken.None);

            Assert.Equal("fake", report.Provider);
            Assert.Equal("21.0.1", cve.LastVersion);
        }
        finally
        {
            Delete(root);
        }
    }

    [Fact]
    public async Task Module_serializes_the_advisories_payload()
    {
        var root = TempDirectory();
        try
        {
            var registry = new JavaDistributionRegistry(
                [new FakeProvider(new string('a', 64), 1)]);
            var cve = new FakeCveSource(
                JavaCveReport.Ok(
                    "fake",
                    "21.0.1",
                    [
                        new JavaCveAdvisory(
                            "CVE-2024-21131",
                            "high",
                            7.5,
                            "test summary",
                            "https://nvd.nist.gov/vuln/detail/CVE-2024-21131",
                            "2024-04-16T22:15:42.477",
                            ["GHSA-aaaa-bbbb-cccc"]),
                    ],
                    ["nvd", "osv"],
                    DateTimeOffset.UtcNow));
            var service = new JavaDistributionService(
                registry,
                root,
                new WritingRunner([]),
                cve);
            var module = new JavaModuleService(null, null, root, null, service);

            var result = JsonDocument.Parse(
                await module.HandleAsync(
                    "java.distributions.advisories",
                    """{"provider":"fake","major":21,"versionId":"v1"}""",
                    CancellationToken.None)).RootElement;

            Assert.True(result.GetProperty("ok").GetBoolean());
            var data = result.GetProperty("data");
            Assert.Equal("ok", data.GetProperty("status").GetString());
            Assert.Equal(2, data.GetProperty("sources").GetArrayLength());
            var advisory = data.GetProperty("advisories")[0];
            Assert.Equal("CVE-2024-21131", advisory.GetProperty("id").GetString());
            Assert.Equal("high", advisory.GetProperty("severity").GetString());
            Assert.Equal(7.5, advisory.GetProperty("score").GetDouble());
            Assert.Equal(
                "GHSA-aaaa-bbbb-cccc",
                advisory.GetProperty("aliases")[0].GetString());
        }
        finally
        {
            Delete(root);
        }
    }

    [Fact]
    public async Task Module_install_reads_the_acknowledge_cves_flag()
    {
        var root = TempDirectory();
        try
        {
            var tar = Path.Combine(root, "source.tar.gz");
            WriteTarGz(tar, [("jdk-21/bin/java", "#!/bin/sh\n", true)]);
            var bytes = await File.ReadAllBytesAsync(tar);
            var sha = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
            var registry = new JavaDistributionRegistry(
                [new FakeProvider(sha, bytes.LongLength)]);
            var cve = new FakeCveSource(
                JavaCveReport.Ok(
                    "fake",
                    "21.0.1",
                    [
                        new JavaCveAdvisory(
                            "CVE-2024-21131",
                            "high",
                            7.5,
                            "test",
                            null,
                            null,
                            []),
                    ],
                    ["nvd"],
                    DateTimeOffset.UtcNow));
            var service = new JavaDistributionService(
                registry,
                root,
                new WritingRunner(bytes),
                cve);
            var module = new JavaModuleService(null, null, root, null, service);
            var target = Path.Combine(root, "module-ack");

            var blocked = JsonDocument.Parse(
                await module.HandleAsync(
                    "java.distributions.install",
                    $$"""
                    {"provider":"fake","versionId":"v1","directory":"{{target}}"}
                    """,
                    CancellationToken.None)).RootElement;
            Assert.False(blocked.GetProperty("ok").GetBoolean());
            Assert.Equal(
                "JAVA_CVE_UNACKNOWLEDGED",
                blocked.GetProperty("error").GetProperty("code").GetString());

            var installed = JsonDocument.Parse(
                await module.HandleAsync(
                    "java.distributions.install",
                    $$"""
                    {"provider":"fake","versionId":"v1","directory":"{{target}}","acknowledgeCves":true}
                    """,
                    CancellationToken.None)).RootElement;
            Assert.True(installed.GetProperty("ok").GetBoolean());
            Assert.Equal(
                target,
                installed.GetProperty("data").GetProperty("home").GetString());
        }
        finally
        {
            Delete(root);
        }
    }

    private sealed class FakeCveSource(JavaCveReport report) : IJavaCveSource
    {
        public string? LastVersion { get; private set; }

        public Task<JavaCveReport> GetReportAsync(
            string providerId,
            string version,
            string baselineVersion,
            CancellationToken cancellationToken)
        {
            LastVersion = version;
            LastBaseline = baselineVersion;
            return Task.FromResult(report);
        }

        public string? LastBaseline { get; private set; }
    }

    private static string CirclEmptyPageJson() =>
        """{"results":{"nvd":[]}}""";

    private static string CirclPageJson(params string[] versions)
    {
        var versionsJson = string.Join(
            ",",
            versions.Select(version =>
                "{\"status\":\"affected\",\"version\":" +
                JsonSerializer.Serialize(version) +
                "}"));
        return $$$"""
        {
          "results": {
            "nvd": [
              [
                "cve-2024-21131",
                {
                  "cveMetadata": {
                    "cveId": "CVE-2024-21131",
                    "datePublished": "2024-04-16T22:15:42.477"
                  },
                  "containers": {
                    "cna": {
                      "descriptions": [
                        {"lang": "en", "value": "Vulnerability in Oracle Java SE."}
                      ],
                      "metrics": [
                        {"cvssV3_1": {"baseScore": 7.5, "baseSeverity": "HIGH"}}
                      ],
                      "affected": [
                        {
                          "vendor": "Oracle Corporation",
                          "product": "Java SE JDK and JRE",
                          "versions": [{{{versionsJson}}}]
                        }
                      ]
                    }
                  }
                }
              ]
            ]
          }
        }
        """;
    }

    private static string NvdDescriptionOnlyJson() =>
        """
        {
          "totalResults": 1,
          "vulnerabilities": [
            {
              "cve": {
                "id": "CVE-2026-91403",
                "published": "2026-10-20T22:15:42.477",
                "descriptions": [
                  {
                    "lang": "en",
                    "value": "Vulnerability in the Oracle Java SE product. Supported versions that are affected are Oracle Java SE: 8u511, 11.0.33, 21.0.13; Oracle GraalVM for JDK: 21.0.13; Oracle GraalVM Enterprise Edition: 21.3.20."
                  }
                ],
                "metrics": {
                  "cvssMetricV31": [
                    {
                      "type": "Primary",
                      "cvssData": {"baseScore": 9.1, "baseSeverity": "CRITICAL"}
                    }
                  ]
                },
                "configurations": []
              }
            }
          ]
        }
        """;

    private static string NvdAdvisoryJson() =>
        """
        {
          "totalResults": 1,
          "vulnerabilities": [
            {
              "cve": {
                "id": "CVE-2024-21131",
                "published": "2024-04-16T22:15:42.477",
                "descriptions": [
                  {"lang": "en", "value": "   "}
                ],
                "metrics": {
                  "cvssMetricV31": [
                    {
                      "type": "Primary",
                      "cvssData": {"baseScore": 7.5, "baseSeverity": "HIGH"}
                    }
                  ]
                },
                "configurations": [
                  {
                    "nodes": [
                      {
                        "operator": "OR",
                        "cpeMatch": [
                          {
                            "vulnerable": true,
                            "criteria": "cpe:2.3:a:oracle:jdk:*:*:*:*:*:*:*:*",
                            "versionStartIncluding": "21",
                            "versionEndExcluding": "21.0.2"
                          },
                          {
                            "vulnerable": true,
                            "criteria": "cpe:2.3:a:oracle:jdk:8.0:update411:*:*:-:*:*:*"
                          }
                        ]
                      }
                    ]
                  }
                ],
                "references": [{"url": "https://example.test/advisory"}]
              }
            }
          ]
        }
        """;
}
