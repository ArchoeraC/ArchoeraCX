using System.Text;
using System.Text.Json;
using Archoera.Application.Downloads;
using Archoera.Domain.Downloads;

namespace Archoera.Tests;

public sealed class DownloadPlanTests
{
    private static readonly string Root = Path.Combine(
        Path.GetTempPath(),
        "archoera-plan-tests");

    private static readonly string Sha512A =
        new('a', 128);

    private static readonly string Sha1B =
        new('b', 40);

    [Fact]
    public void Build_emits_only_fields_the_rust_executor_parses()
    {
        var plan = SamplePlan();
        var bytes = DownloadPlanJson.Build(plan);

        using var document = JsonDocument.Parse(bytes);
        var root = document.RootElement;

        Assert.Equal(
            [
                "schemaVersion",
                "planId",
                "root",
                "concurrency",
                "priority",
                "bandwidthLimit",
                "totalBytes",
                "options",
                "files",
                "post",
            ],
            root.EnumerateObject().Select(property => property.Name).ToArray());

        Assert.Equal(1, root.GetProperty("schemaVersion").GetInt32());
        Assert.Equal(Root, root.GetProperty("root").GetString());
        Assert.Equal(4, root.GetProperty("concurrency").GetInt32());
        Assert.Equal(7, root.GetProperty("priority").GetInt32());
        Assert.Equal(1024 * 1024, root.GetProperty("bandwidthLimit").GetInt64());
        Assert.Equal(123, root.GetProperty("totalBytes").GetInt64());

        var options = root.GetProperty("options");
        Assert.Equal(
            [
                "dedup",
                "verifyExisting",
                "durability",
                "rangeThreshold",
                "chunkSize",
                "progressIntervalMs",
                "perHostConcurrency",
            ],
            options.EnumerateObject().Select(property => property.Name).ToArray());
        Assert.Equal(
            DownloadPlanLimits.DefaultPerHostConcurrency,
            options.GetProperty("perHostConcurrency").GetInt32());
        Assert.Equal("size", options.GetProperty("verifyExisting").GetString());
        Assert.Equal("normal", options.GetProperty("durability").GetString());

        var files = root.GetProperty("files");
        Assert.Equal(2, files.GetArrayLength());

        var file = files[0];
        Assert.Equal(
            [
                "url",
                "mirrors",
                "hash",
                "size",
                "dest",
                "mode",
                "priority",
                "executable",
                "headers",
            ],
            file.EnumerateObject().Select(property => property.Name).ToArray());

        Assert.Equal("https://cdn.example.com/mods/a.jar", file.GetProperty("url").GetString());
        Assert.Equal("mods/a.jar", file.GetProperty("dest").GetString());
        Assert.Equal("ensure", file.GetProperty("mode").GetString());
        Assert.Equal(Sha512A, file.GetProperty("hash").GetProperty("hex").GetString());
        Assert.Equal("sha512", file.GetProperty("hash").GetProperty("algo").GetString());
        Assert.Equal(123, file.GetProperty("size").GetInt64());
        Assert.Equal(
            "https://mirror.example.com/mods/a.jar",
            file.GetProperty("mirrors")[0].GetString());
        Assert.Equal(
            "Bearer short-lived",
            file.GetProperty("headers").GetProperty("Authorization").GetString());

        Assert.Equal(0, root.GetProperty("post").GetArrayLength());
        Assert.Equal(64, plan.PlanId.Length);
        Assert.True(plan.PlanId.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f'));
    }

    [Fact]
    public void PlanId_is_deterministic_and_independent_of_file_order()
    {
        var forward = SamplePlan();

        var reversed = new DownloadPlanBuilder(Root)
            .WithConcurrency(4)
            .WithPriority(7)
            .WithBandwidthLimit(1024 * 1024)
            .AddFile(
                "https://cdn.example.com/mods/b.jar",
                "mods/b.jar",
                DownloadHashAlgorithm.Sha1,
                Sha1B,
                0)
            .AddFile(
                "https://cdn.example.com/mods/a.jar",
                "mods/a.jar",
                DownloadHashAlgorithm.Sha512,
                Sha512A,
                123,
                mirrors: ["https://mirror.example.com/mods/a.jar"],
                headers: new Dictionary<string, string>
                {
                    ["Authorization"] = "Bearer short-lived",
                })
            .Build();

        Assert.Equal(forward.PlanId, reversed.PlanId);

        var rotatedToken = new DownloadPlanBuilder(Root)
            .WithConcurrency(4)
            .WithPriority(7)
            .WithBandwidthLimit(1024 * 1024)
            .AddFile(
                "https://cdn.example.com/mods/a.jar",
                "mods/a.jar",
                DownloadHashAlgorithm.Sha512,
                Sha512A,
                123,
                mirrors: ["https://mirror.example.com/mods/a.jar"],
                headers: new Dictionary<string, string>
                {
                    ["Authorization"] = "Bearer rotated-token",
                })
            .AddFile(
                "https://cdn.example.com/mods/b.jar",
                "mods/b.jar",
                DownloadHashAlgorithm.Sha1,
                Sha1B,
                0)
            .Build();

        Assert.Equal(forward.PlanId, rotatedToken.PlanId);

        var changed = new DownloadPlanBuilder(Root)
            .WithConcurrency(4)
            .AddFile(
                "https://cdn.example.com/mods/a.jar",
                "mods/a.jar",
                DownloadHashAlgorithm.Sha512,
                Sha512A,
                124)
            .Build();

        Assert.NotEqual(forward.PlanId, changed.PlanId);
    }

    [Fact]
    public void Json_round_trips_every_wire_field()
    {
        var plan = SamplePlan();
        var bytes = DownloadPlanJson.Build(plan);

        Assert.True(DownloadPlanJson.TryParse(bytes, out var parsed, out var error), error?.ToString());
        Assert.NotNull(parsed);
        Assert.Null(error);

        Assert.Equal(plan.SchemaVersion, parsed.SchemaVersion);
        Assert.Equal(plan.PlanId, parsed.PlanId);
        Assert.Equal(plan.Root, parsed.Root);
        Assert.Equal(plan.Concurrency, parsed.Concurrency);
        Assert.Equal(plan.Priority, parsed.Priority);
        Assert.Equal(plan.BandwidthLimit, parsed.BandwidthLimit);
        Assert.Equal(plan.TotalBytes, parsed.TotalBytes);
        Assert.Equal(plan.Options, parsed.Options);
        Assert.Equal(2, parsed.Files.Count);

        var file = parsed.Files[0];
        Assert.Equal("https://cdn.example.com/mods/a.jar", file.Url);
        Assert.Equal("mods/a.jar", file.Dest);
        Assert.Equal(Sha512A, file.Hash.Hex);
        Assert.Equal(DownloadHashAlgorithm.Sha512, file.Hash.Algorithm);
        Assert.Equal(123, file.Size);
        Assert.Equal(DownloadFileMode.Ensure, file.Mode);
        Assert.Equal(
            new[] { "https://mirror.example.com/mods/a.jar" },
            file.Mirrors);
        Assert.Equal("Bearer short-lived", file.Headers["Authorization"]);

        Assert.Equal(bytes, DownloadPlanJson.Build(parsed));
    }

    [Fact]
    public void Json_round_trips_proxy_and_per_host_options()
    {
        var options = DownloadPlanOptions.Default with
        {
            PerHostConcurrency = 4,
            Proxy = new DownloadProxyOptions(
                "http://user:secret@127.0.0.1:7890",
                ["localhost", ".lan"]),
        };
        var plan = new DownloadPlanBuilder(Root)
            .WithConcurrency(4)
            .WithOptions(options)
            .AddFile(
                "https://cdn.example.com/mods/a.jar",
                "mods/a.jar",
                DownloadHashAlgorithm.Sha1,
                Sha1B,
                1)
            .Build();

        var bytes = DownloadPlanJson.Build(plan);
        Assert.True(DownloadPlanJson.TryParse(bytes, out var parsed, out var error), error?.ToString());
        Assert.NotNull(parsed);
        Assert.Equal(4, parsed!.Options.PerHostConcurrency);
        Assert.NotNull(parsed.Options.Proxy);
        Assert.Equal(options.Proxy!.Url, parsed.Options.Proxy!.Url);
        Assert.Equal(options.Proxy.NoProxy, parsed.Options.Proxy.NoProxy);
        Assert.Equal(bytes, DownloadPlanJson.Build(parsed));
    }

    [Fact]
    public void Accepts_a_socks5_proxy()
    {
        var plan = new DownloadPlanBuilder(
            Path.Combine(Path.GetTempPath(), "archoera-plan"))
            .WithOptions(DownloadPlanOptions.Default with
            {
                Proxy = new DownloadProxyOptions("socks5h://127.0.0.1:7890", []),
            })
            .AddFile(
                "https://cdn.example.com/mods/a.jar",
                "mods/a.jar",
                DownloadHashAlgorithm.Sha1,
                new string('a', 40),
                10)
            .Build();

        Assert.Null(DownloadPlanValidator.Validate(plan));
    }

    [Fact]
    public void Rejects_a_non_http_proxy()
    {
        var issue = RequireIssue(builder => builder
            .WithOptions(DownloadPlanOptions.Default with
            {
                Proxy = new DownloadProxyOptions("ftp://127.0.0.1:1080", []),
            })
            .AddFile(
                "https://cdn.example.com/mods/a.jar",
                "mods/a.jar",
                DownloadHashAlgorithm.Sha1,
                Sha1B,
                1));

        Assert.Equal(DownloadPlanErrorCode.InvalidPlan, issue.Code);
    }

    [Fact]
    public void Rejects_per_host_concurrency_above_the_limit()
    {
        var issue = RequireIssue(builder => builder
            .WithOptions(DownloadPlanOptions.Default with
            {
                PerHostConcurrency = DownloadPlanLimits.MaxPerHostConcurrency + 1,
            })
            .AddFile(
                "https://cdn.example.com/mods/a.jar",
                "mods/a.jar",
                DownloadHashAlgorithm.Sha1,
                Sha1B,
                1));

        Assert.Equal(DownloadPlanErrorCode.InvalidPlan, issue.Code);
    }

    [Fact]
    public void Json_round_trips_post_actions_verbatim()
    {
        var plan = new DownloadPlanBuilder(Root)
            .AddFile(
                "https://cdn.example.com/mods/a.jar",
                "mods/a.jar",
                DownloadHashAlgorithm.Sha1,
                Sha1B,
                1)
            .AddPostAction(new DownloadPostAction("""{"type":"chmod","dest":"mods/a.jar"}"""))
            .Build();

        var bytes = DownloadPlanJson.Build(plan);
        Assert.True(DownloadPlanJson.TryParse(bytes, out var parsed, out var error), error?.ToString());
        Assert.Single(parsed!.Post);
        Assert.Equal(
            """{"type":"chmod","dest":"mods/a.jar"}""",
            parsed.Post[0].Json);
    }

    [Fact]
    public void Json_reads_legacy_top_level_hash_fields()
    {
        var json = $$"""
            {
              "schemaVersion": 1,
              "planId": "legacy",
              "root": {{JsonSerializer.Serialize(Root)}},
              "files": [
                {
                  "url": "https://cdn.example.com/mods/a.jar",
                  "sha1": "{{Sha1B}}",
                  "size": 7,
                  "dest": "mods/a.jar"
                }
              ]
            }
            """;

        Assert.True(DownloadPlanJson.TryParse(json, out var plan, out var error), error?.ToString());
        Assert.Equal(DownloadHashAlgorithm.Sha1, plan!.Files[0].Hash.Algorithm);
        Assert.Equal(Sha1B, plan.Files[0].Hash.Hex);
        Assert.Equal(7, plan.TotalBytes);
        Assert.Equal(DownloadPlanLimits.DefaultConcurrency, plan.Concurrency);
        Assert.Equal(DownloadVerifyExisting.Size, plan.Options.VerifyExisting);
    }

    [Fact]
    public void Json_rejects_malformed_and_invalid_plans()
    {
        Assert.False(DownloadPlanJson.TryParse("{", out _, out var malformed));
        Assert.Equal(DownloadPlanErrorCode.InvalidPlan, malformed!.Code);

        Assert.False(DownloadPlanJson.TryParse("{}", out _, out var missingId));
        Assert.Equal(DownloadPlanErrorCode.InvalidPlan, missingId!.Code);

        var valid = DownloadPlanJson.Build(SamplePlan());
        var text = Encoding.UTF8.GetString(valid)
            .Replace("\"dest\":\"mods/a.jar\"", "\"dest\":\"../a.jar\"", StringComparison.Ordinal);

        Assert.False(DownloadPlanJson.TryParse(text, out _, out var escape));
        Assert.Equal(DownloadPlanErrorCode.PathEscape, escape!.Code);
    }

    [Theory]
    [InlineData("../escape", DownloadPlanErrorCode.PathEscape)]
    [InlineData("/etc/passwd", DownloadPlanErrorCode.PathEscape)]
    [InlineData("mods\\a.jar", DownloadPlanErrorCode.InvalidPlan)]
    [InlineData("mods//a.jar", DownloadPlanErrorCode.InvalidPlan)]
    [InlineData("mods/./a.jar", DownloadPlanErrorCode.InvalidPlan)]
    [InlineData("mods/a.", DownloadPlanErrorCode.InvalidPlan)]
    [InlineData("mods/a ", DownloadPlanErrorCode.InvalidPlan)]
    [InlineData("mods/CON.jar", DownloadPlanErrorCode.InvalidPlan)]
    [InlineData("mods/aux", DownloadPlanErrorCode.InvalidPlan)]
    [InlineData("mods/COM1.txt", DownloadPlanErrorCode.InvalidPlan)]
    [InlineData("mods/a.jar:stream", DownloadPlanErrorCode.InvalidPlan)]
    [InlineData("mods/a.jar.part", DownloadPlanErrorCode.InvalidPlan)]
    [InlineData("", DownloadPlanErrorCode.InvalidPlan)]
    public void Validation_rejects_unsafe_destinations(string dest, string expectedCode)
    {
        var issue = RequireIssue(builder => builder.AddFile(
            "https://cdn.example.com/a.jar",
            dest,
            DownloadHashAlgorithm.Sha1,
            Sha1B,
            1));

        Assert.Equal(expectedCode, issue.Code);
    }

    [Fact]
    public void Validation_rejects_nul_and_control_characters_in_dest()
    {
        Assert.Equal(
            DownloadPlanErrorCode.InvalidPlan,
            RequireIssue(builder => builder.AddFile(
                "https://cdn.example.com/a.jar",
                "mods/a\0.jar",
                DownloadHashAlgorithm.Sha1,
                Sha1B,
                1)).Code);

        Assert.Equal(
            DownloadPlanErrorCode.InvalidPlan,
            RequireIssue(builder => builder.AddFile(
                "https://cdn.example.com/a.jar",
                "mods/a\u0001.jar",
                DownloadHashAlgorithm.Sha1,
                Sha1B,
                1)).Code);
    }

    [Theory]
    [InlineData("http://example.com/a.jar", DownloadPlanErrorCode.UnsupportedScheme)]
    [InlineData("ftp://example.com/a.jar", DownloadPlanErrorCode.UnsupportedScheme)]
    [InlineData("https://user:secret@example.com/a.jar", DownloadPlanErrorCode.InvalidPlan)]
    [InlineData("https:///a.jar", DownloadPlanErrorCode.InvalidPlan)]
    public void Validation_rejects_unsafe_urls(string url, string expectedCode)
    {
        var issue = RequireIssue(builder => builder.AddFile(
            url,
            "mods/a.jar",
            DownloadHashAlgorithm.Sha1,
            Sha1B,
            1));

        Assert.Equal(expectedCode, issue.Code);
    }

    [Theory]
    [InlineData("http://127.0.0.1:8080/a.jar")]
    [InlineData("http://localhost/a.jar")]
    [InlineData("http://[::1]/a.jar")]
    public void Validation_accepts_loopback_http(string url)
    {
        AssertValid(builder => builder.AddFile(
            url,
            "mods/a.jar",
            DownloadHashAlgorithm.Sha1,
            Sha1B,
            1));
    }

    [Fact]
    public void Validation_rejects_duplicate_destinations_and_mirrors()
    {
        var duplicateDest = RequireIssue(builder => builder
            .AddFile("https://a.example.com/a.jar", "mods/a.jar", DownloadHashAlgorithm.Sha1, Sha1B, 1)
            .AddFile("https://b.example.com/a.jar", "mods/a.jar", DownloadHashAlgorithm.Sha1, Sha1B, 1));
        Assert.Equal(DownloadPlanErrorCode.DuplicateDest, duplicateDest.Code);

        var duplicateMirror = RequireIssue(builder => builder.AddFile(
            "https://a.example.com/a.jar",
            "mods/a.jar",
            DownloadHashAlgorithm.Sha1,
            Sha1B,
            1,
            mirrors: ["https://a.example.com/a.jar"]));
        Assert.Equal(DownloadPlanErrorCode.InvalidPlan, duplicateMirror.Code);
    }

    [Fact]
    public void Validation_rejects_bad_hashes()
    {
        var shortHash = RequireIssue(builder => builder
            .AddFile(
                "https://a.example.com/a.jar",
                "mods/a.jar",
                new DownloadHash(DownloadHashAlgorithm.Sha1, "abc"),
                1));
        Assert.Equal(DownloadPlanErrorCode.InvalidHash, shortHash.Code);

        var upperCase = RequireIssue(builder => builder
            .AddFile(
                "https://a.example.com/a.jar",
                "mods/a.jar",
                new DownloadHash(DownloadHashAlgorithm.Sha1, Sha1B.ToUpperInvariant()),
                1));
        Assert.Equal(DownloadPlanErrorCode.InvalidHash, upperCase.Code);

        var notHex = RequireIssue(builder => builder
            .AddFile(
                "https://a.example.com/a.jar",
                "mods/a.jar",
                new DownloadHash(DownloadHashAlgorithm.Sha512, new string('z', 128)),
                1));
        Assert.Equal(DownloadPlanErrorCode.InvalidHash, notHex.Code);
    }

    [Fact]
    public void Validation_rejects_bad_root_and_limits()
    {
        Assert.Equal(
            DownloadPlanErrorCode.InvalidPlan,
            RequireIssue(builder => builder.WithRoot("relative/root").AddFile(
                "https://a.example.com/a.jar", "mods/a.jar", DownloadHashAlgorithm.Sha1, Sha1B, 1)).Code);

        Assert.Equal(
            DownloadPlanErrorCode.InvalidPlan,
            RequireIssue(builder => builder.WithConcurrency(0).AddFile(
                "https://a.example.com/a.jar", "mods/a.jar", DownloadHashAlgorithm.Sha1, Sha1B, 1)).Code);

        Assert.Equal(
            DownloadPlanErrorCode.InvalidPlan,
            RequireIssue(builder => builder.WithConcurrency(65).AddFile(
                "https://a.example.com/a.jar", "mods/a.jar", DownloadHashAlgorithm.Sha1, Sha1B, 1)).Code);

        Assert.Equal(
            DownloadPlanErrorCode.InvalidPlan,
            RequireIssue(builder => builder.AddFile(
                "https://a.example.com/a.jar", "mods/a.jar", DownloadHashAlgorithm.Sha1, Sha1B, -1)).Code);

        Assert.Equal(
            DownloadPlanErrorCode.PlanTooLarge,
            RequireIssue(builder => builder.AddFile(
                "https://a.example.com/a.jar",
                "mods/a.jar",
                DownloadHashAlgorithm.Sha1,
                Sha1B,
                DownloadPlanLimits.MaxFileBytes + 1)).Code);

        Assert.Equal(
            DownloadPlanErrorCode.InvalidPlan,
            RequireIssue(builder => builder.AddFile(
                "https://a.example.com/a.jar",
                "mods/a.jar",
                DownloadHashAlgorithm.Sha1,
                Sha1B,
                1,
                mode: DownloadFileMode.Symlink)).Code);

        Assert.Equal(
            DownloadPlanErrorCode.InvalidPlan,
            RequireIssue(builder => builder.AddFile(
                "https://a.example.com/a.jar",
                "mods/a.jar",
                DownloadHashAlgorithm.Sha1,
                Sha1B,
                1,
                headers: new Dictionary<string, string> { ["X-Api-Key"] = "secret" })).Code);
    }

    [Fact]
    public void Validation_rejects_more_than_the_file_limit()
    {
        var file = PlanFile.Create(
            "https://a.example.com/a.jar",
            "mods/a.jar",
            DownloadHash.Create(DownloadHashAlgorithm.Sha1, Sha1B),
            1);

        var plan = new DownloadPlan
        {
            PlanId = "too-many",
            Root = Root,
            Files = Enumerable
                .Repeat(file, DownloadPlanLimits.MaxFiles + 1)
                .ToArray(),
        };

        var issue = DownloadPlanValidator.Validate(plan);
        Assert.Equal(DownloadPlanErrorCode.PlanTooLarge, issue!.Code);
    }

    [Fact]
    public void Build_refuses_to_serialize_an_invalid_plan()
    {
        var plan = new DownloadPlan
        {
            PlanId = "bad",
            Root = Root,
            Files =
            [
                PlanFile.Create(
                    "http://example.com/a.jar",
                    "mods/a.jar",
                    DownloadHash.Create(DownloadHashAlgorithm.Sha1, Sha1B),
                    1),
            ],
        };

        var exception = Assert.Throws<DownloadPlanException>(
            () => DownloadPlanJson.Build(plan));
        Assert.Equal(DownloadPlanErrorCode.UnsupportedScheme, exception.Code);
    }

    [Fact]
    public void Dest_rules_accept_normal_paths()
    {
        Assert.Null(DownloadPlanPaths.TryNormalizeDest(
            "versions/1.21.4/1.21.4.jar",
            out var normalized));
        Assert.Equal("versions/1.21.4/1.21.4.jar", normalized);

        Assert.True(DownloadPlanPaths.IsPlainFileName("sodium-0.6.0.jar"));
        Assert.False(DownloadPlanPaths.IsPlainFileName("../sodium.jar"));
        Assert.False(DownloadPlanPaths.IsPlainFileName("mods/sodium.jar"));
        Assert.False(DownloadPlanPaths.IsPlainFileName("CON.jar"));
    }

    private static DownloadPlan SamplePlan()
        => new DownloadPlanBuilder(Root)
            .WithConcurrency(4)
            .WithPriority(7)
            .WithBandwidthLimit(1024 * 1024)
            .AddFile(
                "https://cdn.example.com/mods/a.jar",
                "mods/a.jar",
                DownloadHashAlgorithm.Sha512,
                Sha512A,
                123,
                mirrors: ["https://mirror.example.com/mods/a.jar"],
                headers: new Dictionary<string, string>
                {
                    ["Authorization"] = "Bearer short-lived",
                })
            .AddFile(
                "https://cdn.example.com/mods/b.jar",
                "mods/b.jar",
                DownloadHashAlgorithm.Sha1,
                Sha1B,
                0)
            .Build();

    private static DownloadPlanIssue RequireIssue(Func<DownloadPlanBuilder, DownloadPlanBuilder> configure)
    {
        var builder = configure(new DownloadPlanBuilder(Root));
        Assert.False(builder.TryBuild(out _, out var issue));
        Assert.NotNull(issue);
        return issue;
    }

    private static void AssertValid(Func<DownloadPlanBuilder, DownloadPlanBuilder> configure)
    {
        var builder = configure(new DownloadPlanBuilder(Root));
        Assert.True(builder.TryBuild(out _, out var issue), issue?.ToString());
    }
}
