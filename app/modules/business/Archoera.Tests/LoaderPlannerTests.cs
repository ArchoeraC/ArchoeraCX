using System.Text;
using Archoera.Application.Loaders;
using Archoera.DevHost;
using Archoera.Domain.Loaders;
using Archoera.Infrastructure.Loaders;

namespace Archoera.Tests;

public sealed class LoaderPlannerTests
{
    [Fact]
    public async Task PlanAsync_builds_a_relocatable_client_manifest()
    {
        using var temp = new TempFixture("planner");
        var fixture = temp.Loader;
        var manifest = await fixture.PlanAsync();

        Assert.Equal(LoaderArtifactManifest.CurrentSchemaVersion, manifest.SchemaVersion);
        Assert.Equal(LoaderKind.Forge, manifest.LoaderId.Kind);
        Assert.Equal("1.20.1", manifest.LoaderId.GameVersion);
        Assert.Equal("47.2.0", manifest.LoaderId.LoaderVersion);
        Assert.Equal(40, manifest.LoaderId.InstallerSha1.Length);
        Assert.Equal("installer.jar", manifest.InstallerPath);
        Assert.False(manifest.InstallerOutsideRoot);
        Assert.Equal("libraries", manifest.LibrariesPath);
        Assert.Equal("versions/1.20.1-forge-47.2.0/1.20.1-forge-47.2.0.json", manifest.VersionJsonPath);
        Assert.Equal(2, manifest.ProcessorCount);
        Assert.Equal(2, manifest.Processors.Count);

        var binpatch = manifest.Artifacts.Single(artifact =>
            artifact.RelativePath == fixture.BinpatchRelative);
        Assert.Equal(LoaderArtifactKind.Extracted, binpatch.Kind);
        Assert.Equal(LoaderArtifactLocation.Libraries, binpatch.Location);
        Assert.NotNull(binpatch.Sha1);
        Assert.Equal("installer:data/client.lzma", binpatch.Key);

        var shim = manifest.Artifacts.Single(artifact =>
            artifact.RelativePath == fixture.ShimRelative);
        Assert.Equal(LoaderArtifactKind.Extracted, shim.Kind);

        var patched = manifest.Artifacts.Single(artifact =>
            artifact.RelativePath == fixture.PatchedRelative);
        Assert.True(patched.RequiredForLaunch);
        Assert.Equal(1, patched.ProducerIndex);
        Assert.NotNull(patched.Sha1);
        Assert.NotNull(patched.Size);

        Assert.All(manifest.Artifacts, artifact => Assert.False(Path.IsPathRooted(artifact.RelativePath)));
        Assert.DoesNotContain(
            manifest.Artifacts,
            artifact => artifact.RelativePath.Contains("/server/", StringComparison.Ordinal));
    }

    [Fact]
    public async Task PlanAsync_does_not_add_an_unused_server_only_data_artifact()
    {
        using var temp = new TempFixture("planner-client-data-filter");
        var manifest = await temp.Loader.PlanAsync();

        Assert.DoesNotContain(
            manifest.Artifacts,
            artifact => string.Equals(artifact.Key, "MC_UNPACKED", StringComparison.Ordinal));
    }

    [Fact]
    public async Task PlanAsync_records_patched_when_neither_outputs_nor_version_json_declare_it()
    {
        using var temp = new TempFixture(
            "planner-unlisted-patched",
            includePatchedOutput: false,
            includePatchedLibrary: false);
        var fixture = temp.Loader;

        var manifest = await fixture.PlanAsync();

        var patched = manifest.Artifacts.Single(
            artifact => string.Equals(artifact.Key, "PATCHED", StringComparison.Ordinal));
        Assert.Equal(fixture.PatchedRelative, patched.RelativePath);
        Assert.Equal(LoaderArtifactLocation.Libraries, patched.Location);
        Assert.Equal(LoaderArtifactKind.Generated, patched.Kind);
        Assert.True(patched.RequiredForLaunch);
        Assert.Equal(1, patched.ProducerIndex);
        Assert.NotNull(patched.Sha1);
    }

    [Fact]
    public async Task PlanAsync_resolves_data_variables_and_processor_main_classes()
    {
        using var temp = new TempFixture("planner-vars");
        var fixture = temp.Loader;
        var manifest = await fixture.PlanAsync();

        Assert.Equal("com.example.Installertools", manifest.Processors[0].MainClass);
        Assert.Equal("com.example.BinaryPatcher", manifest.Processors[1].MainClass);

        var first = manifest.Processors[0];
        var outputIndex = first.Arguments.ToList().IndexOf("--output");
        Assert.True(outputIndex >= 0);
        Assert.StartsWith("{LIBRARIES}/net/minecraft/client/1.20.1/", first.Arguments[outputIndex + 1]);

        var second = manifest.Processors[1];
        Assert.Contains("{LIBRARIES}/" + fixture.BinpatchRelative, second.Arguments);
        Assert.Contains("{LIBRARIES}/" + fixture.PatchedRelative, second.Arguments);
        Assert.DoesNotContain(second.Arguments, argument => argument.Contains(fixture.Root, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Manifest_json_contains_no_absolute_instance_paths()
    {
        using var temp = new TempFixture("planner-json");
        var fixture = temp.Loader;
        var manifest = await fixture.PlanAsync();

        var json = Encoding.UTF8.GetString(LoaderManifestJson.BuildManifest(manifest));
        Assert.DoesNotContain(fixture.Root, json, StringComparison.Ordinal);
        Assert.DoesNotContain(fixture.BaseDirectory, json, StringComparison.Ordinal);
        Assert.Contains("\"processorCount\": 2", json);
    }

    [Fact]
    public async Task PlanAsync_rejects_a_missing_installer()
    {
        using var temp = new TempFixture("planner-missing");
        var fixture = temp.Loader;

        var exception = await Assert.ThrowsAsync<LoaderManifestException>(() =>
            new ForgeArtifactPlanner().ParseAsync(
                Path.Combine(temp.DirectoryPath, "nope.jar"),
                fixture.Root,
                fixture.Libraries,
                LoaderKind.Forge));

        Assert.Equal("E_INSTALLER_MISSING", exception.Code);
    }

    [Fact]
    public async Task Real_installer_plans_without_java_when_explicitly_available()
    {
        // Optional network/manual test: point ARCHOERACX_FORGE_INSTALLER at a
        // real forge-*-installer.jar. Offline runs skip it without failing.
        var installer = Environment.GetEnvironmentVariable("ARCHOERACX_FORGE_INSTALLER");
        if (string.IsNullOrWhiteSpace(installer) || !File.Exists(installer))
        {
            return;
        }

        var directory = Path.Combine(Path.GetTempPath(), $"archoera-real-{Guid.NewGuid():N}");
        var root = Path.Combine(directory, "instance");
        var libraries = Path.Combine(root, "libraries");
        Directory.CreateDirectory(libraries);

        try
        {
            var manifest = await new ForgeArtifactPlanner().ParseAsync(
                installer,
                root,
                libraries,
                LoaderKind.Forge);

            Assert.True(manifest.ProcessorCount > 0);
            Assert.NotEmpty(manifest.Artifacts);
            Assert.All(manifest.Artifacts, artifact => Assert.False(Path.IsPathRooted(artifact.RelativePath)));
            Assert.All(manifest.Processors, processor => Assert.False(Path.IsPathRooted(processor.JarRelativePath)));
            Assert.All(
                manifest.Processors.SelectMany(processor => processor.Arguments),
                argument => Assert.False(Path.IsPathRooted(argument)));
        }
        finally
        {
            try
            {
                Directory.Delete(directory, recursive: true);
            }
            catch
            {
                // best effort
            }
        }
    }
}
