using System.Text;
using Archoera.Application.Loaders;
using Archoera.Domain.Loaders;
using Archoera.Infrastructure.Loaders;

namespace Archoera.DevHost;

/// <summary>
/// Offline, Java-free loader acceptance checks. Also exercised by the xUnit
/// project through <see cref="SyntheticLoaderFixture"/>.
/// </summary>
public static class LoaderSelfTest
{
    public static bool Run(TextWriter output, TextWriter error)
    {
        var baseDirectory = Path.Combine(
            Path.GetTempPath(),
            $"archoera-loader-selftest-{Guid.NewGuid():N}");

        try
        {
            Directory.CreateDirectory(baseDirectory);
            var scenarios = 0;

            void Scenario(string name, Action action)
            {
                action();
                output.WriteLine($"ok - {name}");
            }

            Scenario("plan is relocatable and contains the expected artifacts", () =>
            {
                var fixture = SyntheticLoaderFixture.Create(Path.Combine(baseDirectory, "plan"));
                var manifest = fixture.PlanAsync().GetAwaiter().GetResult();

                Check(manifest.SchemaVersion == LoaderArtifactManifest.CurrentSchemaVersion, "schema version");
                Check(manifest.LoaderId.Kind == LoaderKind.Forge, "loader kind");
                Check(manifest.LoaderId.GameVersion == "1.20.1", "game version");
                Check(manifest.LoaderId.LoaderVersion == "47.2.0", "loader version");
                Check(manifest.LoaderId.InstallerSha1.Length == 40, "installer sha1");
                Check(manifest.InstallerPath == "installer.jar", "installer path is relative");
                Check(!manifest.InstallerOutsideRoot, "installer is inside root");
                Check(manifest.LibrariesPath == "libraries", "libraries path");
                Check(
                    manifest.VersionJsonPath == "versions/1.20.1-forge-47.2.0/1.20.1-forge-47.2.0.json",
                    "version json target");
                Check(manifest.ProcessorCount == 2, "server-only processor filtered");
                Check(manifest.Processors.Count == 2, "processor list");

                var binpatch = manifest.Artifacts.Single(artifact =>
                    artifact.RelativePath == fixture.BinpatchRelative &&
                    artifact.Kind == LoaderArtifactKind.Extracted);
                Check(binpatch.HasSha1, "client.lzma has a sha1");
                Check(binpatch.Location == LoaderArtifactLocation.Libraries, "client.lzma in libraries");

                var shim = manifest.Artifacts.Single(artifact =>
                    artifact.RelativePath == fixture.ShimRelative);
                Check(shim.Kind == LoaderArtifactKind.Extracted, "embedded maven shim extracted");

                var patched = manifest.Artifacts.Single(artifact =>
                    artifact.RelativePath == fixture.PatchedRelative);
                Check(patched.RequiredForLaunch, "patched client required for launch");
                Check(patched.ProducerIndex == 1, "patched client producer index");
                Check(patched.HasSha1, "patched client sha1");

                Check(manifest.Processors[0].MainClass == "com.example.Installertools", "processor 0 main class");
                Check(manifest.Processors[1].MainClass == "com.example.BinaryPatcher", "processor 1 main class");
                Check(
                    manifest.Processors[1].Arguments.Any(argument =>
                        argument.StartsWith("{LIBRARIES}/net/minecraftforge/forge/", StringComparison.Ordinal)),
                    "processor paths use the libraries token");

                foreach (var artifact in manifest.Artifacts)
                {
                    Check(!Path.IsPathRooted(artifact.RelativePath), $"relative artifact path: {artifact.RelativePath}");
                }

                foreach (var processor in manifest.Processors)
                {
                    Check(!Path.IsPathRooted(processor.JarRelativePath), "relative processor jar");
                    foreach (var argument in processor.Arguments)
                    {
                        Check(!Path.IsPathRooted(argument), $"relative processor argument: {argument}");
                    }
                }

                var json = Encoding.UTF8.GetString(LoaderManifestJson.BuildManifest(manifest));
                Check(!json.Contains(fixture.Root, StringComparison.Ordinal), "manifest JSON has no absolute root");
            });
            scenarios++;

            Scenario("valid artifacts verify ok", () =>
            {
                var fixture = SyntheticLoaderFixture.Create(Path.Combine(baseDirectory, "valid"));
                var manifest = fixture.PlanAsync().GetAwaiter().GetResult();
                fixture.WriteAllValid(manifest);

                var verification = new LoaderArtifactVerifier().Verify(manifest, fixture.Root);
                Check(verification.Ok, "verification ok");
                Check(verification.Missing.Count == 0, "no missing");
                Check(verification.Mismatched.Count == 0, "no mismatched");
            });
            scenarios++;

            Scenario("missing output is reported", () =>
            {
                var fixture = SyntheticLoaderFixture.Create(Path.Combine(baseDirectory, "missing"));
                var manifest = fixture.PlanAsync().GetAwaiter().GetResult();
                fixture.WriteExtracted(manifest);
                fixture.WriteGenerated(manifest, "MAPPINGS");

                var verification = new LoaderArtifactVerifier().Verify(manifest, fixture.Root);
                Check(!verification.Ok, "verification fails");
                Check(
                    verification.Missing.Any(artifact => artifact.RelativePath == fixture.PatchedRelative),
                    "patched client reported missing");
            });
            scenarios++;

            Scenario("corrupt hash is a mismatch", () =>
            {
                var fixture = SyntheticLoaderFixture.Create(Path.Combine(baseDirectory, "corrupt"));
                var manifest = fixture.PlanAsync().GetAwaiter().GetResult();
                fixture.WriteAllValid(manifest);
                File.WriteAllText(fixture.Resolve(fixture.Find(manifest, "PATCHED")), "corrupt");

                var verification = new LoaderArtifactVerifier().Verify(manifest, fixture.Root);
                Check(!verification.Ok, "verification fails");
                Check(
                    verification.Mismatched.Any(artifact => artifact.RelativePath == fixture.PatchedRelative),
                    "patched client reported mismatched");
                Check(
                    verification.Details.Any(detail => detail.StartsWith("sha1 mismatch:", StringComparison.Ordinal)),
                    "detail names sha1 mismatch");
            });
            scenarios++;

            Scenario("repair writes missing output then is idempotent", () =>
            {
                var fixture = SyntheticLoaderFixture.Create(Path.Combine(baseDirectory, "repair"));
                var manifest = fixture.PlanAsync().GetAwaiter().GetResult();
                fixture.WriteExtracted(manifest);
                fixture.WriteGenerated(manifest, "MAPPINGS");

                var runner = new FakeProcessorRunner(fixture);
                var verifier = new LoaderArtifactVerifier();
                var service = new ForgeRepairService(runner, verifier, TimeSpan.FromSeconds(30));

                var first = service.RepairAsync(manifest, fixture.Root, fixture.FakeJavaPath)
                    .GetAwaiter().GetResult();
                Check(first.Ok, "first repair ok: " + string.Join(" | ", first.Details));
                Check(first.Status == LoaderRepairStatus.Repaired, "first repair status");
                Check(first.ProcessorsRun.SequenceEqual([1]), "only processor 1 ran");
                Check(runner.Indices.SequenceEqual([1]), "fake runner saw processor 1");
                Check(verifier.Verify(manifest, fixture.Root).Ok, "artifacts valid after repair");

                var second = service.RepairAsync(manifest, fixture.Root, fixture.FakeJavaPath)
                    .GetAwaiter().GetResult();
                Check(second.Ok, "second repair ok");
                Check(second.Status == LoaderRepairStatus.AlreadyValid, "second repair already valid");
                Check(second.ProcessorsRun.Count == 0, "second repair ran no processors");
                Check(runner.Indices.SequenceEqual([1]), "fake runner was not called again");
            });
            scenarios++;

            Scenario("repair resumes at the earliest missing processor", () =>
            {
                var fixture = SyntheticLoaderFixture.Create(Path.Combine(baseDirectory, "resume"));
                var manifest = fixture.PlanAsync().GetAwaiter().GetResult();
                fixture.WriteExtracted(manifest);

                var runner = new FakeProcessorRunner(fixture);
                var service = new ForgeRepairService(
                    runner,
                    new LoaderArtifactVerifier(),
                    TimeSpan.FromSeconds(30));

                var result = service.RepairAsync(manifest, fixture.Root, fixture.FakeJavaPath)
                    .GetAwaiter().GetResult();

                Check(result.Ok, "repair ok");
                Check(result.ProcessorsRun.SequenceEqual([0, 1]), "both processors ran");
                Check(runner.Indices.SequenceEqual([0, 1]), "fake runner order");
            });
            scenarios++;

            Scenario("path escape is rejected without touching files", () =>
            {
                var fixture = SyntheticLoaderFixture.Create(Path.Combine(baseDirectory, "escape"));
                var manifest = fixture.PlanAsync().GetAwaiter().GetResult();
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
                Check(!verification.Ok, "escaping manifest fails");
                Check(verification.Unsafe?.Any(artifact => artifact.Key == "escape") == true, "escape listed unsafe");
                Check(
                    verification.Details.Any(detail =>
                        detail.Contains("traversal", StringComparison.Ordinal) ||
                        detail.Contains("escapes", StringComparison.Ordinal)),
                    "escape detail");
                Check(!File.Exists(Path.Combine(fixture.BaseDirectory, "escape.txt")), "no file created outside root");
            });
            scenarios++;

            Scenario("libraries path escape is rejected", () =>
            {
                var fixture = SyntheticLoaderFixture.Create(Path.Combine(baseDirectory, "libescape"));
                var manifest = fixture.PlanAsync().GetAwaiter().GetResult();
                var tampered = manifest with { LibrariesPath = "../shared-libraries" };

                var verification = new LoaderArtifactVerifier().Verify(tampered, fixture.Root);
                Check(!verification.Ok, "libraries escape fails");
                Check(
                    verification.Details.Any(detail => detail.Contains("unsafe libraries path", StringComparison.Ordinal)),
                    "libraries escape detail");
            });
            scenarios++;

            Scenario("installer sha1 mismatch blocks repair", () =>
            {
                var fixture = SyntheticLoaderFixture.Create(Path.Combine(baseDirectory, "installer-sha"));
                var manifest = fixture.PlanAsync().GetAwaiter().GetResult();
                File.AppendAllText(fixture.InstallerPath, "tampered");

                var runner = new FakeProcessorRunner(fixture);
                var service = new ForgeRepairService(
                    runner,
                    new LoaderArtifactVerifier(),
                    TimeSpan.FromSeconds(30));
                var result = service.RepairAsync(manifest, fixture.Root, fixture.FakeJavaPath)
                    .GetAwaiter().GetResult();

                Check(!result.Ok, "repair fails");
                Check(result.Status == LoaderRepairStatus.Failed, "repair status");
                Check(
                    result.Details.Any(detail => detail.Contains("installer SHA-1 mismatch", StringComparison.Ordinal)),
                    "installer mismatch detail");
                Check(runner.Indices.Count == 0, "no processor ran");
            });
            scenarios++;

            Scenario("missing Java blocks processor repair", () =>
            {
                var fixture = SyntheticLoaderFixture.Create(Path.Combine(baseDirectory, "no-java"));
                var manifest = fixture.PlanAsync().GetAwaiter().GetResult();
                fixture.WriteExtracted(manifest);
                fixture.WriteGenerated(manifest, "MAPPINGS");

                var runner = new FakeProcessorRunner(fixture);
                var service = new ForgeRepairService(
                    runner,
                    new LoaderArtifactVerifier(),
                    TimeSpan.FromSeconds(30));
                var result = service.RepairAsync(
                        manifest,
                        fixture.Root,
                        Path.Combine(fixture.BaseDirectory, "missing-java"))
                    .GetAwaiter().GetResult();

                Check(!result.Ok, "repair fails");
                Check(
                    result.Details.Any(detail => detail.Contains("does not exist", StringComparison.Ordinal)),
                    "missing java detail");
                Check(runner.Indices.Count == 0, "no processor ran");
            });
            scenarios++;

            Scenario("processor failure is surfaced", () =>
            {
                var fixture = SyntheticLoaderFixture.Create(Path.Combine(baseDirectory, "processor-fail"));
                var manifest = fixture.PlanAsync().GetAwaiter().GetResult();
                fixture.WriteExtracted(manifest);

                var runner = new FakeProcessorRunner(fixture) { FailOnIndex = 0 };
                var service = new ForgeRepairService(
                    runner,
                    new LoaderArtifactVerifier(),
                    TimeSpan.FromSeconds(30));
                var result = service.RepairAsync(manifest, fixture.Root, fixture.FakeJavaPath)
                    .GetAwaiter().GetResult();

                Check(!result.Ok, "repair fails");
                Check(
                    result.Details.Any(detail => detail.Contains("exited with code 7", StringComparison.Ordinal)),
                    "exit code detail");
            });
            scenarios++;

            output.WriteLine($"ok: loader self-test passed ({scenarios} scenarios)");
            return true;
        }
        catch (Exception exception)
        {
            error.WriteLine($"loader self-test failed: {exception}");
            return false;
        }
        finally
        {
            try
            {
                if (Directory.Exists(baseDirectory))
                {
                    Directory.Delete(baseDirectory, recursive: true);
                }
            }
            catch
            {
                // Temp cleanup must not mask the test result.
            }
        }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
