using System.Text.Json;
using Archoera.Domain.Instances;
using Archoera.Infrastructure.Instances;
using Archoera.Infrastructure.Settings;

namespace Archoera.Tests;

/// <summary>
/// Per-instance wrapper/environment/auto-join fields share the global limits
/// and add the inherit (null) versus explicit-empty distinction.
/// </summary>
public sealed class InstanceOverrideRuntimeOptionsTests
{
    [Fact]
    public async Task Wrapper_environment_and_join_round_trip()
    {
        var directory = TempDirectory();
        try
        {
            var (service, root, id) = await CreateAsync(directory, "Runtime Overrides");

            using (var patch = await UpdateAsync(
                       service,
                       id,
                       """
                       {"wrapperCommand":"prime-run --keep \"my wrapper\"",
                        "environment":{"DRI_PRIME":"1","__GLX_VENDOR_LIBRARY_NAME":"nvidia"},
                        "joinServerOnLaunch":true,
                        "joinServerAddress":"mc.example.org:25566"}
                       """))
            {
                Assert.True(patch.RootElement.GetProperty("ok").GetBoolean());
                var overrides = Overrides(patch);
                Assert.Equal(
                    "prime-run --keep \"my wrapper\"",
                    overrides.GetProperty("wrapperCommand").GetString());
                Assert.Equal(
                    "1",
                    overrides.GetProperty("environment").GetProperty("DRI_PRIME").GetString());
                Assert.True(overrides.GetProperty("joinServerOnLaunch").GetBoolean());
                Assert.Equal(
                    "mc.example.org:25566",
                    overrides.GetProperty("joinServerAddress").GetString());
            }

            var repository = new SqliteInstanceRepository(
                Path.Combine(directory, "data"),
                new JsonAppSettingsStore(Path.Combine(directory, "data")));
            var listed = Assert.Single(await repository.ListAsync());
            Assert.Equal(
                "prime-run --keep \"my wrapper\"",
                listed.Overrides!.WrapperCommand);
            Assert.Equal("1", listed.Overrides.Environment!["DRI_PRIME"]);
            Assert.True(listed.Overrides.JoinServerOnLaunch);
            Assert.Equal("mc.example.org:25566", listed.Overrides.JoinServerAddress);
            Assert.Null(listed.Overrides.JoinWorldName);

            using var document = JsonDocument.Parse(await File.ReadAllTextAsync(
                Path.Combine(root, ".archoera", "instance.json")));
            var stored = document.RootElement.GetProperty("overrides");
            Assert.Equal(
                "mc.example.org:25566",
                stored.GetProperty("joinServerAddress").GetString());
            Assert.Equal(
                "1",
                stored.GetProperty("environment").GetProperty("DRI_PRIME").GetString());
        }
        finally
        {
            Delete(directory);
        }
    }

    [Fact]
    public async Task Empty_wrapper_and_environment_are_preserved_as_explicit_overrides()
    {
        var directory = TempDirectory();
        try
        {
            var (service, root, id) = await CreateAsync(directory, "Clear Overrides");

            using (var patch = await UpdateAsync(
                       service,
                       id,
                       """{"wrapperCommand":"","environment":{}}"""))
            {
                var overrides = Overrides(patch);
                Assert.Equal(string.Empty, overrides.GetProperty("wrapperCommand").GetString());
                Assert.Empty(overrides.GetProperty("environment").EnumerateObject());
            }

            var repository = new SqliteInstanceRepository(
                Path.Combine(directory, "data"),
                new JsonAppSettingsStore(Path.Combine(directory, "data")));
            var listed = Assert.Single(await repository.ListAsync());
            Assert.Equal(string.Empty, listed.Overrides!.WrapperCommand);
            Assert.NotNull(listed.Overrides.Environment);
            Assert.Empty(listed.Overrides.Environment);

            using var document = JsonDocument.Parse(await File.ReadAllTextAsync(
                Path.Combine(root, ".archoera", "instance.json")));
            var stored = document.RootElement.GetProperty("overrides");
            Assert.Equal(string.Empty, stored.GetProperty("wrapperCommand").GetString());
            Assert.Empty(stored.GetProperty("environment").EnumerateObject());
        }
        finally
        {
            Delete(directory);
        }
    }

    [Fact]
    public async Task Null_wrapper_and_environment_clear_the_override_and_inherit_again()
    {
        var directory = TempDirectory();
        try
        {
            var (service, _, id) = await CreateAsync(directory, "Inherit Again");

            await UpdateAsync(
                service,
                id,
                """{"wrapperCommand":"prime-run","environment":{"A":"1"}}""");

            using var cleared = await UpdateAsync(
                service,
                id,
                """{"wrapperCommand":null,"environment":null}""");
            var overrides = Overrides(cleared);
            Assert.False(overrides.TryGetProperty("wrapperCommand", out _));
            Assert.False(overrides.TryGetProperty("environment", out _));
        }
        finally
        {
            Delete(directory);
        }
    }

    [Theory]
    [InlineData("""{"joinServerOnLaunch":true}""")]
    [InlineData("""{"joinServerOnLaunch":true,"joinServerAddress":"http://example.org"}""")]
    [InlineData("""{"joinServerOnLaunch":true,"joinServerAddress":"example.org/path"}""")]
    [InlineData("""{"joinServerOnLaunch":true,"joinServerAddress":"example.org:0"}""")]
    [InlineData("""{"joinServerOnLaunch":true,"joinServerAddress":"example.org:65536"}""")]
    [InlineData("""{"joinServerOnLaunch":true,"joinServerAddress":"exa mple.org"}""")]
    [InlineData("""{"joinServerOnLaunch":true,"joinServerAddress":"::1"}""")]
    [InlineData("""{"joinServerOnLaunch":true,"joinServerAddress":"mc.example.org","joinWorldName":"My World"}""")]
    [InlineData("""{"joinServerOnLaunch":true,"joinWorldName":"../world"}""")]
    [InlineData("""{"joinServerOnLaunch":true,"joinWorldName":"a/b"}""")]
    [InlineData("""{"joinWorldName":"a\\b"}""")]
    [InlineData("""{"wrapperCommand":"\"unclosed"}""")]
    [InlineData("""{"wrapperCommand":"\"\""}""")]
    [InlineData("""{"environment":{"JAVA_TOOL_OPTIONS":"-Xmx1G"}}""")]
    [InlineData("""{"environment":{"_JAVA_OPTIONS":"-Xmx1G"}}""")]
    [InlineData("""{"environment":{"JDK_JAVA_OPTIONS":"-Xmx1G"}}""")]
    [InlineData("""{"environment":{"CLASSPATH":"/tmp/a.jar"}}""")]
    [InlineData("""{"environment":{"BAD-KEY":"x"}}""")]
    [InlineData("""{"environment":[]}""")]
    public async Task Invalid_override_fields_are_rejected(string patch)
    {
        var directory = TempDirectory();
        try
        {
            var (service, _, id) = await CreateAsync(directory, "Invalid Runtime Overrides");

            using (var baseline = await UpdateAsync(service, id, """{"windowWidth":800}"""))
            {
                Assert.True(baseline.RootElement.GetProperty("ok").GetBoolean());
            }

            using var document = JsonDocument.Parse(
                await service.HandleAsync(
                    "instance.update",
                    $$"""{"id":"{{id}}","overrides":{{patch}}}""",
                    CancellationToken.None));
            Assert.False(document.RootElement.GetProperty("ok").GetBoolean());
            Assert.Equal(
                "E_INVALID",
                document.RootElement.GetProperty("error").GetProperty("code").GetString());
        }
        finally
        {
            Delete(directory);
        }
    }

    [Theory]
    [InlineData("mc.example.org", "mc.example.org", 25565)]
    [InlineData("mc.example.org:25566", "mc.example.org", 25566)]
    [InlineData("[2001:db8::1]", "2001:db8::1", 25565)]
    [InlineData("[2001:db8::1]:25567", "2001:db8::1", 25567)]
    public void Server_addresses_parse_into_host_and_port(
        string text,
        string host,
        int port)
    {
        Assert.True(ServerAddress.TryParse(text, out var address));
        Assert.Equal(host, address.Host);
        Assert.Equal(port, address.Port);
    }

    [Theory]
    [InlineData("")]
    [InlineData("http://example.org")]
    [InlineData("example.org/path")]
    [InlineData("example.org:0")]
    [InlineData("example.org:65536")]
    [InlineData("example.org:port")]
    [InlineData("::1")]
    [InlineData("[::1]:")]
    [InlineData("[]")]
    [InlineData("host name")]
    public void Malformed_server_addresses_do_not_parse(string text)
    {
        Assert.False(ServerAddress.TryParse(text, out _));
    }

    [Fact]
    public void Overrides_apply_wrapper_and_merge_environment_global_then_instance()
    {
        var global = new Archoera.Domain.Settings.AppSettings(
            InstancesRoot: Path.GetTempPath(),
            DataRoot: Path.GetTempPath(),
            SettingsPath: string.Empty,
            WrapperCommand: "global-wrapper",
            Environment: new Dictionary<string, string>
            {
                ["A"] = "global",
                ["B"] = "global",
                ["JAVA_TOOL_OPTIONS"] = "-Xmx1G",
            });

        var effective = new InstanceOverrides(
            WrapperCommand: string.Empty,
            Environment: new Dictionary<string, string>
            {
                ["B"] = "instance",
                ["C"] = "instance",
            }).ApplyTo(global);

        Assert.Equal(string.Empty, effective.WrapperCommand);
        Assert.Equal("global", effective.Environment!["A"]);
        Assert.Equal("instance", effective.Environment["B"]);
        Assert.Equal("instance", effective.Environment["C"]);
        Assert.DoesNotContain("JAVA_TOOL_OPTIONS", effective.Environment.Keys);
        // The global document stays untouched.
        Assert.Equal("global-wrapper", global.WrapperCommand);
        Assert.Equal("global", global.Environment!["B"]);
    }

    [Fact]
    public void An_empty_instance_environment_clears_the_global_map()
    {
        var global = new Archoera.Domain.Settings.AppSettings(
            InstancesRoot: Path.GetTempPath(),
            DataRoot: Path.GetTempPath(),
            SettingsPath: string.Empty,
            Environment: new Dictionary<string, string> { ["A"] = "global" });

        var effective = new InstanceOverrides(
            Environment: new Dictionary<string, string>()).ApplyTo(global);

        Assert.NotNull(effective.Environment);
        Assert.Empty(effective.Environment);
    }

    private static JsonElement Overrides(JsonDocument document)
        => document.RootElement
            .GetProperty("data")
            .GetProperty("instance")
            .GetProperty("overrides");

    private static async Task<JsonDocument> UpdateAsync(
        InstanceModuleService service,
        string id,
        string overridesJson)
        => JsonDocument.Parse(
            await service.HandleAsync(
                "instance.update",
                $$"""{"id":"{{id}}","overrides":{{overridesJson}}}""",
                CancellationToken.None));

    private static async Task<(InstanceModuleService Service, string Root, string Id)> CreateAsync(
        string directory,
        string name)
    {
        var dataRoot = Path.Combine(directory, "data");
        var root = Path.Combine(directory, "instance-" + Guid.NewGuid().ToString("N"));
        var settings = new JsonAppSettingsStore(dataRoot);
        var service = new InstanceModuleService(
            new SqliteInstanceRepository(dataRoot, settings),
            settings);

        using var document = JsonDocument.Parse(
            await service.HandleAsync(
                "instance.create",
                JsonSerializer.Serialize(new
                {
                    name,
                    gameVersion = "1.21.4",
                    loader = "vanilla",
                    loaderVersion = string.Empty,
                    rootPath = root,
                }),
                CancellationToken.None));
        var id = document.RootElement
            .GetProperty("data")
            .GetProperty("instance")
            .GetProperty("id")
            .GetString()!;
        return (service, root, id);
    }

    private static string TempDirectory()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            "archoera-instance-runtime",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void Delete(string path)
    {
        try
        {
            if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
        }
        catch
        {
            // best effort
        }
    }
}
