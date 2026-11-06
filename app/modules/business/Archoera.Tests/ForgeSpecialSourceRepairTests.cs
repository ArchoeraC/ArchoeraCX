using Archoera.Domain.Loaders;
using Archoera.Infrastructure.Loaders;

namespace Archoera.Tests;

/// <summary>
/// Forge 1.13–1.16 runs SpecialSource between the splitter and binarypatcher.
/// Its output flag is <c>--out-jar</c>, so MC_SRG used to look producerless and
/// a repair started at the binarypatcher with a missing input.
/// </summary>
public sealed class ForgeSpecialSourceRepairTests
{
    private const string Installertools = "net.minecraftforge:installertools:1.2.6";
    private const string Jarsplitter = "net.minecraftforge:jarsplitter:1.1.4";
    private const string SpecialSource = "net.md-5:SpecialSource:1.8.5";
    private const string Binarypatcher = "net.minecraftforge:binarypatcher:1.0.12";

    [Fact]
    public async Task SpecialSource_output_is_attributed_to_its_step()
    {
        using var temp = CreateFixture();
        var manifest = await temp.PlanAsync();

        Assert.Equal(2, ProducerOf(manifest, "MC_SRG"));
        Assert.Equal(1, ProducerOf(manifest, "MC_SLIM"));
        Assert.Equal(3, ProducerOf(manifest, "PATCHED"));
    }

    [Fact]
    public async Task Repair_resumes_at_specialsource_when_the_srg_jar_is_missing()
    {
        using var temp = CreateFixture();
        var manifest = await temp.PlanAsync();

        temp.WriteArtifact(manifest, "MAPPINGS");
        temp.WriteArtifact(manifest, "MERGED_MAPPINGS");
        temp.WriteArtifact(manifest, "MC_SLIM");
        temp.WriteArtifact(manifest, "MC_EXTRA");
        temp.WriteVanillaJar();
        temp.WriteProcessorJars(manifest);

        var runner = new RecordingProcessorRunner();
        var service = new ForgeRepairService(
            runner,
            new LoaderArtifactVerifier(),
            TimeSpan.FromSeconds(30));

        var result = await service.RepairAsync(manifest, temp.Root, temp.JavaPath);

        Assert.True(result.Ok, string.Join(" | ", result.Details));
        Assert.Equal([2, 3], result.ProcessorsRun);
        Assert.DoesNotContain(
            result.Details,
            detail => detail.Contains("input check failed", StringComparison.Ordinal));
    }

    private static LoaderRepairTemp CreateFixture() => new(
        LoaderKind.Forge,
        "1.16.5-forge-36.2.39",
        "1.16.5",
        Profile());

    private static int ProducerOf(LoaderArtifactManifest manifest, string key) =>
        manifest.Artifacts.Single(artifact => artifact.Key == key).ProducerIndex;

    private static string Profile()
    {
        static string Processor(string jar, params string[] args) =>
            $$"""
                {
                  "jar": "{{jar}}",
                  "classpath": ["{{jar}}"],
                  "args": [{{string.Join(", ", args.Select(arg => $"\"{arg}\""))}}]
                }
                """;

        var processors = string.Join(
            ",\n",
            Processor(
                Installertools,
                "--task", "MCP_DATA", "--input",
                "[net.minecraft:client:1.16.5-20210315.092515:mappings@tsrg]",
                "--output", "{MAPPINGS}", "--key", "mappings"),
            Processor(
                Jarsplitter,
                "--input", "{MINECRAFT_JAR}", "--slim", "{MC_SLIM}",
                "--extra", "{MC_EXTRA}", "--srg", "{MERGED_MAPPINGS}"),
            Processor(
                SpecialSource,
                "--in-jar", "{MC_SLIM}", "--out-jar", "{MC_SRG}",
                "--srg-in", "{MERGED_MAPPINGS}"),
            Processor(
                Binarypatcher,
                "--clean", "{MC_SRG}", "--output", "{PATCHED}",
                "--apply", "{BINPATCH}"));

        return $$"""
            {
              "spec": 0,
              "profile": "forge",
              "version": "1.16.5-forge-36.2.39",
              "minecraft": "1.16.5",
              "json": "/version.json",
              "path": "net.minecraftforge:forge:1.16.5-36.2.39",
              "data": {
                "MAPPINGS": { "client": "[net.minecraft:client:1.16.5-20210315.092515:mappings@tsrg]" },
                "MERGED_MAPPINGS": { "client": "[net.minecraft:client:1.16.5-20210315.092515:mappings-merged@tsrg]" },
                "BINPATCH": { "client": "/data/client.lzma" },
                "MC_SLIM": { "client": "[net.minecraft:client:1.16.5-20210315.092515:slim]" },
                "MC_EXTRA": { "client": "[net.minecraft:client:1.16.5-20210315.092515:extra]" },
                "MC_SRG": { "client": "[net.minecraft:client:1.16.5-20210315.092515:srg]" },
                "PATCHED": { "client": "[net.minecraftforge:forge:1.16.5-36.2.39:client]" }
              },
              "libraries": [],
              "processors": [
                {{processors}}
              ]
            }
            """;
    }
}
