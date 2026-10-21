using Archoera.Application.Loaders;
using Archoera.DevHost;
using Archoera.Domain.Loaders;
using Archoera.Infrastructure.Loaders;

namespace Archoera.Tests;

public sealed class LoaderRepairTests
{
    private static ForgeRepairService CreateService(
        SyntheticLoaderFixture fixture,
        IProcessorRunner runner,
        TimeSpan? timeout = null)
        => new(runner, new LoaderArtifactVerifier(), timeout ?? TimeSpan.FromSeconds(30));

    [Fact]
    public async Task Repair_writes_missing_output_and_second_run_is_idempotent()
    {
        using var temp = new TempFixture("repair-idempotent");
        var fixture = temp.Loader;
        var manifest = await fixture.PlanAsync();
        fixture.WriteExtracted(manifest);
        fixture.WriteGenerated(manifest, "MAPPINGS");

        var runner = new FakeProcessorRunner(fixture);
        var service = CreateService(fixture, runner);

        var first = await service.RepairAsync(manifest, fixture.Root, fixture.FakeJavaPath);

        Assert.True(first.Ok);
        Assert.Equal(LoaderRepairStatus.Repaired, first.Status);
        Assert.Equal([1], first.ProcessorsRun);
        Assert.Equal([1], runner.Indices);
        Assert.True(new LoaderArtifactVerifier().Verify(manifest, fixture.Root).Ok);

        var second = await service.RepairAsync(manifest, fixture.Root, fixture.FakeJavaPath);

        Assert.True(second.Ok);
        Assert.Equal(LoaderRepairStatus.AlreadyValid, second.Status);
        Assert.Empty(second.ProcessorsRun);
        Assert.Equal([1], runner.Indices);
    }

    [Fact]
    public async Task Repair_resumes_at_the_earliest_missing_processor()
    {
        using var temp = new TempFixture("repair-resume");
        var fixture = temp.Loader;
        var manifest = await fixture.PlanAsync();
        fixture.WriteExtracted(manifest);

        var runner = new FakeProcessorRunner(fixture);
        var service = CreateService(fixture, runner);

        var result = await service.RepairAsync(manifest, fixture.Root, fixture.FakeJavaPath);

        Assert.True(result.Ok);
        Assert.Equal([0, 1], result.ProcessorsRun);
        Assert.Equal([0, 1], runner.Indices);
    }

    [Fact]
    public async Task Repair_regenerates_a_corrupt_existing_output()
    {
        using var temp = new TempFixture("repair-corrupt-output");
        var fixture = temp.Loader;
        var manifest = await fixture.PlanAsync();
        fixture.WriteAllValid(manifest);
        File.WriteAllText(fixture.Resolve(fixture.Find(manifest, "PATCHED")), "corrupt");

        var runner = new FakeProcessorRunner(fixture);
        var service = CreateService(fixture, runner);

        var result = await service.RepairAsync(manifest, fixture.Root, fixture.FakeJavaPath);

        Assert.True(result.Ok);
        Assert.Equal([1], result.ProcessorsRun);
        Assert.True(new LoaderArtifactVerifier().Verify(manifest, fixture.Root).Ok);
    }

    [Fact]
    public async Task Repair_extracts_missing_installer_files_without_running_processors()
    {
        using var temp = new TempFixture("repair-extract");
        var fixture = temp.Loader;
        var manifest = await fixture.PlanAsync();
        fixture.WriteAllValid(manifest);

        var binpatch = fixture.Find(manifest, "installer:data/client.lzma");
        File.Delete(fixture.Resolve(binpatch));

        var runner = new FakeProcessorRunner(fixture);
        var service = CreateService(fixture, runner);

        var result = await service.RepairAsync(manifest, fixture.Root, fixture.FakeJavaPath);

        Assert.True(result.Ok);
        Assert.Equal(LoaderRepairStatus.Repaired, result.Status);
        Assert.Empty(result.ProcessorsRun);
        Assert.Empty(runner.Indices);
        Assert.True(File.Exists(fixture.Resolve(binpatch)));
    }

    [Fact]
    public async Task Repair_rejects_a_tampered_installer_before_running_java()
    {
        using var temp = new TempFixture("repair-installer-sha");
        var fixture = temp.Loader;
        var manifest = await fixture.PlanAsync();
        File.AppendAllText(fixture.InstallerPath, "tampered");

        var runner = new FakeProcessorRunner(fixture);
        var service = CreateService(fixture, runner);

        var result = await service.RepairAsync(manifest, fixture.Root, fixture.FakeJavaPath);

        Assert.False(result.Ok);
        Assert.Equal(LoaderRepairStatus.Failed, result.Status);
        Assert.Contains(
            result.Details,
            detail => detail.Contains("installer SHA-1 mismatch", StringComparison.Ordinal));
        Assert.Empty(runner.Indices);
    }

    [Fact]
    public async Task Repair_requires_java_only_when_processors_are_needed()
    {
        using var temp = new TempFixture("repair-no-java");
        var fixture = temp.Loader;
        var manifest = await fixture.PlanAsync();
        fixture.WriteExtracted(manifest);
        fixture.WriteGenerated(manifest, "MAPPINGS");

        var runner = new FakeProcessorRunner(fixture);
        var service = CreateService(fixture, runner);
        var missingJava = Path.Combine(fixture.BaseDirectory, "missing-java");

        var result = await service.RepairAsync(manifest, fixture.Root, missingJava);

        Assert.False(result.Ok);
        Assert.Contains(result.Details, detail => detail.Contains("does not exist", StringComparison.Ordinal));
        Assert.Empty(runner.Indices);
    }

    [Fact]
    public async Task Repair_does_not_require_java_when_only_extraction_is_missing()
    {
        using var temp = new TempFixture("repair-extract-no-java");
        var fixture = temp.Loader;
        var manifest = await fixture.PlanAsync();
        fixture.WriteAllValid(manifest);
        File.Delete(fixture.Resolve(fixture.Find(manifest, "installer:data/client.lzma")));

        var runner = new FakeProcessorRunner(fixture);
        var service = CreateService(fixture, runner);
        var missingJava = Path.Combine(fixture.BaseDirectory, "missing-java");

        var result = await service.RepairAsync(manifest, fixture.Root, missingJava);

        Assert.True(result.Ok);
        Assert.Empty(result.ProcessorsRun);
    }

    [Fact]
    public async Task Repair_surfaces_nonzero_processor_exit()
    {
        using var temp = new TempFixture("repair-exit");
        var fixture = temp.Loader;
        var manifest = await fixture.PlanAsync();
        fixture.WriteExtracted(manifest);

        var runner = new FakeProcessorRunner(fixture) { FailOnIndex = 0 };
        var service = CreateService(fixture, runner);

        var result = await service.RepairAsync(manifest, fixture.Root, fixture.FakeJavaPath);

        Assert.False(result.Ok);
        Assert.Contains(
            result.Details,
            detail => detail.Contains("exited with code 7", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Repair_fails_when_a_processor_succeeds_but_output_hash_is_wrong()
    {
        using var temp = new TempFixture("repair-bad-output");
        var fixture = temp.Loader;
        var manifest = await fixture.PlanAsync();
        fixture.WriteExtracted(manifest);

        var runner = new CorruptingRunner();
        var service = CreateService(fixture, runner);

        var result = await service.RepairAsync(manifest, fixture.Root, fixture.FakeJavaPath);

        Assert.False(result.Ok);
        Assert.Equal(LoaderRepairStatus.Failed, result.Status);
        Assert.Equal(1, runner.Calls);
        Assert.Contains(
            result.Details,
            detail => detail.Contains("output verification failed", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Repair_reports_missing_processor_classpath_input()
    {
        using var temp = new TempFixture("repair-missing-input");
        var fixture = temp.Loader;
        var manifest = await fixture.PlanAsync();
        fixture.WriteExtracted(manifest);

        var runner = new FakeProcessorRunner(fixture);
        var service = CreateService(fixture, runner);

        File.Delete(Path.Combine(
            fixture.Libraries,
            "net/minecraft/client/1.20.1/client-1.20.1-srg.jar"));

        var result = await service.RepairAsync(manifest, fixture.Root, fixture.FakeJavaPath);

        Assert.False(result.Ok);
        Assert.Contains(
            result.Details,
            detail => detail.Contains("processor input is missing", StringComparison.Ordinal));
        Assert.Empty(runner.Indices);
    }

    [Fact]
    public async Task Repair_rejects_a_manifest_with_an_escaping_artifact()
    {
        using var temp = new TempFixture("repair-escape");
        var fixture = temp.Loader;
        var manifest = await fixture.PlanAsync();
        var escape = new LoaderArtifact(
            "escape",
            "../escape.txt",
            null,
            null,
            ProducerIndex: -1,
            RequiredForLaunch: false,
            Kind: LoaderArtifactKind.Generated);

        var tampered = manifest with
        {
            Artifacts = [.. manifest.Artifacts, escape],
            Processors = [],
        };

        var runner = new FakeProcessorRunner(fixture);
        var service = CreateService(fixture, runner);

        var result = await service.RepairAsync(tampered, fixture.Root, fixture.FakeJavaPath);

        Assert.False(result.Ok);
        Assert.Contains(
            result.Details,
            detail => detail.Contains("unsafe artifact paths", StringComparison.Ordinal));
        Assert.Empty(runner.Indices);
        Assert.False(File.Exists(Path.Combine(fixture.BaseDirectory, "escape.txt")));
    }

    [Fact]
    public async Task Repair_runs_a_processor_that_only_declares_an_unlisted_launch_artifact()
    {
        using var temp = new TempFixture(
            "repair-unlisted-patched",
            includePatchedOutput: false,
            includePatchedLibrary: false);
        var fixture = temp.Loader;
        var manifest = await fixture.PlanAsync();
        fixture.WriteAllValid(manifest);

        var patchedPath = fixture.Resolve(fixture.Find(manifest, "PATCHED"));
        File.Delete(patchedPath);

        var runner = new WritesPatchedRunner(fixture);
        var service = CreateService(fixture, runner);

        var result = await service.RepairAsync(manifest, fixture.Root, fixture.FakeJavaPath);

        Assert.True(result.Ok);
        Assert.Equal(LoaderRepairStatus.Repaired, result.Status);
        Assert.Equal([1], result.ProcessorsRun);
        Assert.Equal([1], runner.Indices);
        Assert.True(File.Exists(patchedPath));
        Assert.True(new LoaderArtifactVerifier().Verify(manifest, fixture.Root).Ok);
    }

    private sealed class CorruptingRunner : IProcessorRunner
    {
        public int Calls { get; private set; }

        public Task<ProcessorRunResult> RunAsync(
            ProcessorStep step,
            string javaPath,
            string workingDir,
            TimeSpan timeout,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            foreach (var output in step.Outputs)
            {
                var directory = Path.GetDirectoryName(output.RelativePath);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                File.WriteAllText(output.RelativePath, "wrong bytes");
            }

            return Task.FromResult(new ProcessorRunResult(0, "ok", string.Empty, false));
        }
    }

    private sealed class WritesPatchedRunner : IProcessorRunner
    {
        private readonly SyntheticLoaderFixture _fixture;

        public WritesPatchedRunner(SyntheticLoaderFixture fixture)
        {
            _fixture = fixture;
        }

        public List<int> Indices { get; } = [];

        public Task<ProcessorRunResult> RunAsync(
            ProcessorStep step,
            string javaPath,
            string workingDir,
            TimeSpan timeout,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Indices.Add(step.Index);

            if (step.Index == 1)
            {
                var target = Path.Combine(_fixture.Libraries, _fixture.PatchedRelative);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.WriteAllBytes(target, _fixture.OutputBytesForProcessor(1));
            }

            return Task.FromResult(new ProcessorRunResult(0, "ok", string.Empty, false));
        }
    }
}
