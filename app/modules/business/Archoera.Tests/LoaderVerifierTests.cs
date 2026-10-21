using Archoera.Domain.Loaders;
using Archoera.Infrastructure.Loaders;

namespace Archoera.Tests;

public sealed class LoaderVerifierTests
{
    [Fact]
    public async Task Verify_ok_when_all_artifacts_match()
    {
        using var temp = new TempFixture("verify-ok");
        var fixture = temp.Loader;
        var manifest = await fixture.PlanAsync();
        fixture.WriteAllValid(manifest);

        var verification = new LoaderArtifactVerifier().Verify(manifest, fixture.Root);

        Assert.True(verification.Ok);
        Assert.Empty(verification.Missing);
        Assert.Empty(verification.Mismatched);
        Assert.Empty(verification.Unsafe ?? []);
        Assert.Empty(verification.Details);
    }

    [Fact]
    public async Task Verify_reports_missing_output()
    {
        using var temp = new TempFixture("verify-missing");
        var fixture = temp.Loader;
        var manifest = await fixture.PlanAsync();
        fixture.WriteExtracted(manifest);
        fixture.WriteGenerated(manifest, "MAPPINGS");

        var verification = new LoaderArtifactVerifier().Verify(manifest, fixture.Root);

        Assert.False(verification.Ok);
        Assert.Contains(verification.Missing, artifact => artifact.RelativePath == fixture.PatchedRelative);
        Assert.Contains(verification.Details, detail => detail.StartsWith("missing:", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Verify_reports_corrupt_hash()
    {
        using var temp = new TempFixture("verify-corrupt");
        var fixture = temp.Loader;
        var manifest = await fixture.PlanAsync();
        fixture.WriteAllValid(manifest);
        File.WriteAllText(fixture.Resolve(fixture.Find(manifest, "PATCHED")), "corrupt");

        var verification = new LoaderArtifactVerifier().Verify(manifest, fixture.Root);

        Assert.False(verification.Ok);
        Assert.Contains(verification.Mismatched, artifact => artifact.RelativePath == fixture.PatchedRelative);
        Assert.Contains(
            verification.Details,
            detail => detail.StartsWith("sha1 mismatch:", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Verify_rejects_parent_traversal_without_reading_outside_root()
    {
        using var temp = new TempFixture("verify-escape");
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

        var verification = new LoaderArtifactVerifier().Verify(tampered, fixture.Root);

        Assert.False(verification.Ok);
        Assert.Contains(verification.Unsafe ?? [], artifact => artifact.Key == "escape");
        Assert.Contains(
            verification.Details,
            detail => detail.Contains("traversal", StringComparison.Ordinal) ||
                      detail.Contains("escapes", StringComparison.Ordinal));
        Assert.False(File.Exists(Path.Combine(fixture.BaseDirectory, "escape.txt")));
    }

    [Fact]
    public async Task Verify_rejects_absolute_artifact_path()
    {
        using var temp = new TempFixture("verify-absolute");
        var fixture = temp.Loader;
        var manifest = await fixture.PlanAsync();
        var absolute = new LoaderArtifact(
            "absolute",
            Path.Combine(fixture.Root, "somewhere", "file.txt"),
            null,
            null,
            ProducerIndex: -1,
            RequiredForLaunch: false,
            Kind: LoaderArtifactKind.Generated);

        var tampered = manifest with
        {
            Artifacts = [.. manifest.Artifacts, absolute],
            Processors = [],
        };

        var verification = new LoaderArtifactVerifier().Verify(tampered, fixture.Root);

        Assert.False(verification.Ok);
        Assert.Contains(verification.Unsafe ?? [], artifact => artifact.Key == "absolute");
    }

    [Fact]
    public async Task Verify_rejects_libraries_path_escape()
    {
        using var temp = new TempFixture("verify-libs");
        var fixture = temp.Loader;
        var manifest = await fixture.PlanAsync();
        fixture.WriteAllValid(manifest);

        var tampered = manifest with { LibrariesPath = "../shared-libraries" };
        var verification = new LoaderArtifactVerifier().Verify(tampered, fixture.Root);

        Assert.False(verification.Ok);
        Assert.Contains(
            verification.Details,
            detail => detail.Contains("unsafe libraries path", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Verify_reports_size_mismatch_even_without_hash()
    {
        using var temp = new TempFixture("verify-size");
        var fixture = temp.Loader;
        var manifest = await fixture.PlanAsync();
        var artifact = new LoaderArtifact(
            "size-only",
            "size-only.bin",
            Sha1: null,
            Size: 10,
            ProducerIndex: -1,
            RequiredForLaunch: false,
            Kind: LoaderArtifactKind.Generated,
            Location: LoaderArtifactLocation.Root);

        File.WriteAllText(Path.Combine(fixture.Root, "size-only.bin"), "short");
        var tampered = manifest with
        {
            Artifacts = [artifact],
            Processors = [],
        };

        var verification = new LoaderArtifactVerifier().Verify(tampered, fixture.Root);

        Assert.False(verification.Ok);
        Assert.Contains(verification.Mismatched, candidate => candidate.Key == "size-only");
        Assert.Contains(
            verification.Details,
            detail => detail.StartsWith("size mismatch:", StringComparison.Ordinal));
    }
}
