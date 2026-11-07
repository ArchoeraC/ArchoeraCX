using System.IO.Compression;
using System.Text;
using Archoera.Application.Loaders;
using Archoera.Domain.Loaders;
using Archoera.Infrastructure.Loaders;

namespace Archoera.Tests;

/// <summary>
/// Regression coverage for the NeoForge 21.1 shape that made a repair resume at
/// the AutoRenamingTool: the splitter writes MC_SLIM/MC_EXTRA with
/// <c>--slim</c>/<c>--extra</c> instead of <c>--output</c>, so those artifacts
/// used to look like inputs nobody produces.
/// </summary>
public sealed class NeoForgeRepairTests
{
    private const string Installertools = "net.neoforged.installertools:installertools:2.1.2";
    private const string Jarsplitter = "net.neoforged.installertools:jarsplitter:2.1.2";
    private const string Renamer = "net.neoforged:AutoRenamingTool:2.0.3:all";
    private const string Binarypatcher =
        "net.neoforged.installertools:binarypatcher:2.1.2:fatjar";

    [Fact]
    public async Task Splitter_outputs_are_attributed_to_the_splitter()
    {
        using var temp = CreateFixture();
        var manifest = await temp.PlanAsync();

        Assert.Equal(3, ProducerOf(manifest, "MC_SLIM"));
        Assert.Equal(3, ProducerOf(manifest, "MC_EXTRA"));
        Assert.Equal(4, ProducerOf(manifest, "MC_SRG"));
        Assert.Equal(5, ProducerOf(manifest, "PATCHED"));
    }

    [Fact]
    public async Task Repair_resumes_at_the_splitter_when_the_mappings_are_cached()
    {
        using var temp = CreateFixture();
        var manifest = await temp.PlanAsync();

        // A previous install left the mappings behind; slim/extra/srg were
        // never produced. Starting at the AutoRenamingTool would fail its
        // "input {MC_SLIM} is missing" check instead of regenerating them.
        temp.WriteArtifact(manifest, "MAPPINGS");
        temp.WriteArtifact(manifest, "MOJMAPS");
        temp.WriteArtifact(manifest, "MERGED_MAPPINGS");
        temp.WriteVanillaJar();
        temp.WriteProcessorJars(manifest);

        var runner = new RecordingProcessorRunner();
        var service = new ForgeRepairService(
            runner,
            new LoaderArtifactVerifier(),
            TimeSpan.FromSeconds(30));

        var result = await service.RepairAsync(manifest, temp.Root, temp.JavaPath);

        Assert.True(result.Ok, string.Join(" | ", result.Details));
        Assert.Equal([3, 4, 5], result.ProcessorsRun);
        Assert.Equal([3, 4, 5], runner.Indices);
        Assert.DoesNotContain(
            result.Details,
            detail => detail.Contains("input check failed", StringComparison.Ordinal));
    }

    private static LoaderRepairTemp CreateFixture() => new(
        LoaderKind.NeoForge,
        "21.1.265",
        "1.21.1",
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
                "[net.neoforged:neoform:1.21.1-20240808.144430@zip]",
                "--output", "{MAPPINGS}", "--key", "mappings"),
            Processor(
                Installertools,
                "--task", "DOWNLOAD_MOJMAPS", "--version", "1.21.1",
                "--side", "{SIDE}", "--output", "{MOJMAPS}"),
            Processor(
                Installertools,
                "--task", "MERGE_MAPPING", "--left", "{MAPPINGS}",
                "--right", "{MOJMAPS}", "--output", "{MERGED_MAPPINGS}"),
            Processor(
                Jarsplitter,
                "--input", "{MINECRAFT_JAR}", "--slim", "{MC_SLIM}",
                "--extra", "{MC_EXTRA}", "--srg", "{MERGED_MAPPINGS}"),
            Processor(
                Renamer,
                "--input", "{MC_SLIM}", "--output", "{MC_SRG}",
                "--names", "{MERGED_MAPPINGS}"),
            Processor(
                Binarypatcher,
                "--clean", "{MC_SRG}", "--output", "{PATCHED}",
                "--apply", "{BINPATCH}"));

        return $$"""
            {
              "spec": 1,
              "profile": "neoforge",
              "version": "21.1.265",
              "minecraft": "1.21.1",
              "json": "/version.json",
              "data": {
                "MAPPINGS": { "client": "[net.neoforged:neoform:1.21.1-20240808.144430:mappings@txt]" },
                "MOJMAPS": { "client": "[net.minecraft:client:1.21.1-20240808.144430:mappings@txt]" },
                "MERGED_MAPPINGS": { "client": "[net.neoforged:neoform:1.21.1-20240808.144430:mappings-merged@txt]" },
                "BINPATCH": { "client": "/data/client.lzma" },
                "MC_SLIM": { "client": "[net.minecraft:client:1.21.1-20240808.144430:slim]" },
                "MC_EXTRA": { "client": "[net.minecraft:client:1.21.1-20240808.144430:extra]" },
                "MC_SRG": { "client": "[net.minecraft:client:1.21.1-20240808.144430:srg]" },
                "PATCHED": { "client": "[net.neoforged:neoforge:21.1.265:client]" }
              },
              "libraries": [],
              "processors": [
                {{processors}}
              ]
            }
            """;
    }
}

/// <summary>
/// Temp instance root plus a synthetic installer for a client processor chain.
/// The planner reads Main-Class from the jars; the repair only needs a
/// non-empty class name, so tests patch it in.
/// </summary>
internal sealed class LoaderRepairTemp : IDisposable
{
    public LoaderRepairTemp(
        LoaderKind kind,
        string loaderVersion,
        string gameVersion,
        string profile)
    {
        Kind = kind;
        GameVersion = gameVersion;
        Base = Path.Combine(
            Path.GetTempPath(),
            "archoera-tests",
            $"loader-repair-{Guid.NewGuid():N}");
        Root = Path.Combine(Base, "instance");
        Libraries = Path.Combine(Root, "libraries");
        InstallerPath = Path.Combine(Root, "installer.jar");
        JavaPath = Path.Combine(Base, "fake-java");
        Directory.CreateDirectory(Libraries);
        File.WriteAllText(JavaPath, "synthetic JVM path; the runner ignores it");

        using var file = File.Create(InstallerPath);
        using var archive = new ZipArchive(file, ZipArchiveMode.Create);
        Add(
            archive,
            "install_profile.json",
            profile);
        Add(
            archive,
            "version.json",
            $$"""{ "id": "{{loaderVersion}}", "inheritsFrom": "{{gameVersion}}", "libraries": [] }""");
        Add(archive, "data/client.lzma", "synthetic binpatch");
    }

    public LoaderKind Kind { get; }
    public string GameVersion { get; }
    public string Base { get; }
    public string Root { get; }
    public string Libraries { get; }
    public string InstallerPath { get; }
    public string JavaPath { get; }

    public async Task<LoaderArtifactManifest> PlanAsync()
    {
        var manifest = await new ForgeArtifactPlanner().ParseAsync(
            InstallerPath,
            Root,
            Libraries,
            Kind);

        return manifest with
        {
            Processors = manifest.Processors
                .Select(processor => processor with { MainClass = "synthetic.Main" })
                .ToArray(),
        };
    }

    public void WriteVanillaJar()
    {
        var path = Path.Combine(Root, "versions", GameVersion, $"{GameVersion}.jar");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "synthetic client jar");
    }

    /// <summary>Processor jars and classpath entries must exist but are never run.</summary>
    public void WriteProcessorJars(LoaderArtifactManifest manifest)
    {
        foreach (var processor in manifest.Processors)
        {
            Touch(processor.JarRelativePath);
            foreach (var entry in processor.ClassPath)
            {
                Touch(entry);
            }
        }
    }

    public void WriteArtifact(LoaderArtifactManifest manifest, string key)
    {
        var artifact = manifest.Artifacts.Single(candidate => candidate.Key == key);
        var path = Path.Combine(Libraries, artifact.RelativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, $"synthetic {key}");
    }

    private void Touch(string relative)
    {
        var path = Path.Combine(Libraries, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        if (!File.Exists(path)) File.WriteAllText(path, "synthetic library");
    }

    private static void Add(ZipArchive archive, string name, string text)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.Fastest);
        using var stream = entry.Open();
        using var writer = new StreamWriter(stream, new UTF8Encoding(false));
        writer.Write(text);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(Base, recursive: true);
        }
        catch (IOException)
        {
            // best effort
        }
        catch (UnauthorizedAccessException)
        {
            // best effort
        }
    }
}

/// <summary>Writes the files a processor would produce and records the order.</summary>
internal sealed class RecordingProcessorRunner : IProcessorRunner
{
    public List<int> Indices { get; } = [];

    public Task<ProcessorRunResult> RunAsync(
        ProcessorStep step,
        string javaPath,
        string workingDir,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        Indices.Add(step.Index);
        for (var index = 0; index + 1 < step.Arguments.Count; index++)
        {
            if (step.Arguments[index] is not ("--output" or "--slim" or "--extra" or "--out-jar"))
            {
                continue;
            }

            var path = step.Arguments[index + 1];
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            File.WriteAllText(path, $"synthetic output of processor {step.Index}");
        }

        return Task.FromResult(new ProcessorRunResult(0, string.Empty, string.Empty, false));
    }
}
