using Archoera.Domain.Loaders;
using Archoera.Infrastructure.Loaders;

namespace Archoera.Tests;

public sealed class LoaderJavaCoverageTests
{
    private static LoaderArtifactManifest Manifest(params ProcessorStep[] steps) => new()
    {
        LoaderId = new LoaderId(
            LoaderKind.Forge,
            "1.20.1",
            "47.2.0",
            new string('a', 40)),
        InstallerPath = "installer.jar",
        VersionJsonPath = "versions/test/test.json",
        VersionId = "test",
        Artifacts = [],
        Processors = steps,
    };

    private static ProcessorStep Step(string mainClass, params string[] arguments) => new(
        0,
        "net/minecraftforge/ForgeAutoRenamingTool/0.1.22/" +
        "ForgeAutoRenamingTool-0.1.22-all.jar",
        [],
        mainClass,
        arguments,
        []);

    /// <summary>
    /// A processor whose jar is not downloaded yet, so the manifest parser left
    /// <see cref="ProcessorStep.MainClass"/> empty.
    /// </summary>
    private static ProcessorStep PendingStep(string jarRelativePath, params string[] arguments) => new(
        0,
        jarRelativePath,
        [],
        string.Empty,
        arguments,
        []);

    [Fact]
    public void Art_0_1_22_is_covered_without_java()
    {
        var manifest = Manifest(Step(
            "net.minecraftforge.fart.Main",
            "--input", "a.jar",
            "--output", "b.jar",
            "--names", "m.tsrg",
            "--ann-fix", "--ids-fix", "--src-fix", "--record-fix"));

        var result = LoaderJavaCoverage.Evaluate(
            manifest,
            Path.GetTempPath(),
            "/tmp/installer.jar",
            "/tmp/minecraft.jar");

        Assert.False(result.RequiresJava);
        Assert.Empty(result.Steps);
    }

    [Fact]
    public void Binary_patcher_store_and_marker_need_java()
    {
        var manifest = Manifest(Step(
            "net.minecraftforge.binarypatcher.ConsoleTool",
            "--clean", "a.jar",
            "--output", "b.jar",
            "--apply", "p.lzma",
            "--data", "--unpatched", "--store",
            "--marker", ".forge_patched_minecraft"));

        var result = LoaderJavaCoverage.Evaluate(
            manifest,
            Path.GetTempPath(),
            "/tmp/installer.jar",
            "/tmp/minecraft.jar");

        Assert.True(result.RequiresJava);
        Assert.Contains(
            "net.minecraftforge.binarypatcher.ConsoleTool",
            Assert.Single(result.Steps));
    }

    [Fact]
    public void Process_minecraft_jar_needs_java()
    {
        var manifest = Manifest(Step(
            "net.neoforged.installertools.ConsoleTool",
            "--task", "PROCESS_MINECRAFT_JAR",
            "--input", "a.jar",
            "--output", "b.jar",
            "--apply-patches", "p.lzma"));

        var result = LoaderJavaCoverage.Evaluate(
            manifest,
            Path.GetTempPath(),
            "/tmp/installer.jar",
            "/tmp/minecraft.jar");

        Assert.True(result.RequiresJava);
        Assert.Contains(
            "net.neoforged.installertools.ConsoleTool",
            Assert.Single(result.Steps));
    }

    [Fact]
    public void Download_mojmaps_needs_java()
    {
        var manifest = Manifest(Step(
            "net.minecraftforge.installertools.ConsoleTool",
            "--task", "DOWNLOAD_MOJMAPS",
            "--version", "1.20.1",
            "--side", "client",
            "--output", "mappings.txt"));

        var result = LoaderJavaCoverage.Evaluate(
            manifest,
            Path.GetTempPath(),
            "/tmp/installer.jar",
            "/tmp/minecraft.jar");

        Assert.True(result.RequiresJava);
        Assert.Contains(
            "net.minecraftforge.installertools.ConsoleTool",
            Assert.Single(result.Steps));
    }

    [Fact]
    public void Pending_neoforge_mcp_data_is_covered_from_its_coordinate()
    {
        var manifest = Manifest(PendingStep(
            "net/neoforged/installertools/installertools/2.1.2/installertools-2.1.2.jar",
            "--task", "MCP_DATA",
            "--input", "a.zip",
            "--output", "mappings",
            "--key", "mappings"));

        var result = LoaderJavaCoverage.Evaluate(
            manifest,
            Path.GetTempPath(),
            "/tmp/installer.jar",
            "/tmp/minecraft.jar");

        Assert.False(result.RequiresJava);
        Assert.Empty(result.Steps);
    }

    [Fact]
    public void Pending_download_mojmaps_is_named_by_task()
    {
        var manifest = Manifest(PendingStep(
            "net/neoforged/installertools/installertools/2.1.2/installertools-2.1.2.jar",
            "--task", "DOWNLOAD_MOJMAPS",
            "--version", "1.21.1",
            "--side", "client",
            "--output", "mappings.txt"));

        var result = LoaderJavaCoverage.Evaluate(
            manifest,
            Path.GetTempPath(),
            "/tmp/installer.jar",
            "/tmp/minecraft.jar");

        Assert.True(result.RequiresJava);
        Assert.Equal(
            "0: net.neoforged.installertools.ConsoleTool --task DOWNLOAD_MOJMAPS",
            Assert.Single(result.Steps));
    }

    [Fact]
    public void Pending_reverse_art_is_not_covered()
    {
        var manifest = Manifest(PendingStep(
            "net/neoforged/AutoRenamingTool/2.0.3/AutoRenamingTool-2.0.3-all.jar",
            "--input", "a.jar",
            "--output", "b.jar",
            "--names", "m.tsrg",
            "--reverse"));

        var result = LoaderJavaCoverage.Evaluate(
            manifest,
            Path.GetTempPath(),
            "/tmp/installer.jar",
            "/tmp/minecraft.jar");

        Assert.True(result.RequiresJava);
        Assert.Contains(
            "net.neoforged.art.Main",
            Assert.Single(result.Steps));
    }

    [Fact]
    public void Unknown_pending_processor_falls_back_to_its_artifact_name()
    {
        var manifest = Manifest(PendingStep(
            "com/example/custom-tool/1.0.0/custom-tool-1.0.0.jar",
            "--do-something"));

        var result = LoaderJavaCoverage.Evaluate(
            manifest,
            Path.GetTempPath(),
            "/tmp/installer.jar",
            "/tmp/minecraft.jar");

        Assert.True(result.RequiresJava);
        Assert.Equal("0: custom-tool-1.0.0", Assert.Single(result.Steps));
    }
}
