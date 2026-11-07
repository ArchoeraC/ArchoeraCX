using System.IO.Compression;
using Archoera.Application.Loaders;
using Archoera.Domain.Loaders;
using Archoera.Infrastructure.Loaders;

namespace Archoera.Tests;

/// <summary>
/// DOWNLOAD_MOJMAPS is optional on installers that do not sanitize the
/// mappings: the vanilla <c>client_mappings</c> download is byte-identical, so
/// a failed processor should not abort the whole repair.
/// </summary>
public sealed class MojMapsFallbackTests
{
    [Fact]
    public async Task Repair_downloads_mappings_when_the_processor_fails()
    {
        using var temp = new TempRoot();
        var manifest = temp.Manifest(sanitize: false);
        var fallback = new RecordingFallback();
        var service = new ForgeRepairService(
            new FailingRunner(),
            new LoaderArtifactVerifier(),
            TimeSpan.FromSeconds(30),
            fallback);

        var result = await service.RepairAsync(manifest, temp.Root, temp.JavaPath);

        Assert.True(result.Ok, string.Join(" | ", result.Details));
        Assert.Equal(LoaderRepairStatus.Repaired, result.Status);
        Assert.Equal([0], result.ProcessorsRun);
        Assert.Equal(1, fallback.Calls);
        Assert.Contains(
            result.Details,
            detail => detail.Contains("client mappings download", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Sanitized_mappings_never_use_the_download_fallback()
    {
        using var temp = new TempRoot();
        var manifest = temp.Manifest(sanitize: true);
        var fallback = new RecordingFallback();
        var service = new ForgeRepairService(
            new FailingRunner(),
            new LoaderArtifactVerifier(),
            TimeSpan.FromSeconds(30),
            fallback);

        var result = await service.RepairAsync(manifest, temp.Root, temp.JavaPath);

        // Forge 1.21.1 re-encodes the mappings, so the raw client_mappings file
        // is not a valid substitute; the processor failure has to surface.
        Assert.Equal(LoaderRepairStatus.Failed, result.Status);
        Assert.Equal(0, fallback.Calls);
        Assert.Contains(
            result.Details,
            detail => detail.Contains("exited with code 1", StringComparison.Ordinal));
    }

    private sealed class TempRoot : IDisposable
    {
        private const string MappingsRelative =
            "net/minecraft/client/fallback-mappings.txt";

        public TempRoot()
        {
            Root = Path.Combine(
                Path.GetTempPath(),
                "archoera-tests",
                $"mojmaps-fallback-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path.Combine(Root, "libraries", "jars"));
            JavaPath = Path.Combine(Root, "fake-java");
            File.WriteAllText(JavaPath, "synthetic JVM path");

            var installer = Path.Combine(Root, "installer.jar");
            using (var file = File.Create(installer))
            using (var archive = new ZipArchive(file, ZipArchiveMode.Create))
            {
                archive.CreateEntry("install_profile.json");
            }

            File.WriteAllText(
                Path.Combine(Root, "libraries", "jars", "tool.jar"),
                "synthetic processor jar");
        }

        public string Root { get; }
        public string JavaPath { get; }

        public LoaderArtifactManifest Manifest(bool sanitize)
        {
            var arguments = new List<string>
            {
                "--task", "DOWNLOAD_MOJMAPS", "--version", "1.21.1",
                "--side", "{SIDE}", "--output", $"{{LIBRARIES}}/{MappingsRelative}",
            };
            if (sanitize) arguments.Add("--sanitize");

            return new LoaderArtifactManifest
            {
                LoaderId = new LoaderId(
                    LoaderKind.NeoForge,
                    "1.21.1",
                    "21.1.209",
                    string.Empty),
                InstallerPath = "installer.jar",
                VersionJsonPath = "versions/test/test.json",
                VersionId = "test",
                LibrariesPath = "libraries",
                Artifacts =
                [
                    new LoaderArtifact(
                        "MOJMAPS",
                        MappingsRelative,
                        Sha1: null,
                        Size: null,
                        ProducerIndex: 0,
                        RequiredForLaunch: false,
                        Kind: LoaderArtifactKind.Generated),
                ],
                ProcessorCount = 1,
                Processors =
                [
                    new ProcessorStep(
                        0,
                        "jars/tool.jar",
                        ["jars/tool.jar"],
                        "synthetic.Main",
                        arguments,
                        []),
                ],
            };
        }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Root, recursive: true);
            }
            catch (IOException)
            {
                // best effort
            }
        }
    }

    private sealed class FailingRunner : IProcessorRunner
    {
        public Task<ProcessorRunResult> RunAsync(
            ProcessorStep step,
            string javaPath,
            string workingDir,
            TimeSpan timeout,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(
                new ProcessorRunResult(
                    1,
                    string.Empty,
                    "synthetic mappings host blocked",
                    false));
    }

    private sealed class RecordingFallback : IMojMapsFallback
    {
        public int Calls { get; private set; }

        public Task<bool> TryResolveAsync(
            string gameVersion,
            string targetPath,
            CancellationToken cancellationToken)
        {
            Calls++;
            var directory = Path.GetDirectoryName(targetPath);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            File.WriteAllText(targetPath, "synthetic mappings");
            return Task.FromResult(true);
        }
    }
}
