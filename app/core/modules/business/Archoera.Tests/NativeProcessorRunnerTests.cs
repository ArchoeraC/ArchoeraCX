using System.IO.Compression;
using Archoera.Application.Loaders;
using Archoera.Domain.Loaders;
using Archoera.Infrastructure.Loaders;
using Archoera.Infrastructure.Loaders.Native;
using Archoera.Infrastructure.Loaders.Remap;

namespace Archoera.Tests;

public sealed class NativeProcessorRunnerTests
{
    [Fact]
    public void Art_step_with_known_version_runs_without_java()
    {
        var runner = new NativeProcessorRunner(new ThrowingRunner());

        Assert.True(runner.CanRunWithoutJava(Step(
            "ForgeAutoRenamingTool-0.1.22-all.jar",
            "--input", "in.jar",
            "--output", "out.jar",
            "--names", "m.tsrg",
            "--ann-fix", "--ids-fix", "--src-fix", "--record-fix")));
    }

    [Fact]
    public void Unknown_art_version_falls_back_to_java()
    {
        var runner = new NativeProcessorRunner(new ThrowingRunner());
        var step = new ProcessorStep(
            0,
            "/libraries/unknown/ForgeAutoRenamingTool-9.9.9-all.jar",
            [],
            "net.minecraftforge.fart.Main",
            ["--input", "in.jar", "--output", "out.jar", "--names", "m.tsrg"],
            []);

        Assert.False(runner.CanRunWithoutJava(step));
    }

    [Theory]
    [InlineData("ForgeAutoRenamingTool-0.1.22-all.jar", "--input", "in.jar", "--output", "out.jar", "--names", "m.tsrg", "--cfg", "c.json")]
    [InlineData("ForgeAutoRenamingTool-0.1.22-all.jar", "--input", "in.jar", "--output", "out.jar", "--names", "m.tsrg", "--reverse")]
    [InlineData("ForgeAutoRenamingTool-0.1.22-all.jar", "--input", "in.jar", "--output", "out.jar")]
    public void Unsupported_art_steps_fall_back_to_java(string jar, params string[] arguments)
    {
        var runner = new NativeProcessorRunner(new ThrowingRunner());

        Assert.False(runner.CanRunWithoutJava(Step(jar, arguments)));
    }

    [Fact]
    public void Non_art_processor_is_not_claimed()
    {
        var runner = new NativeProcessorRunner(new ThrowingRunner());
        var step = new ProcessorStep(
            0,
            "/libs/installertools-2.1.2.jar",
            [],
            "net.neoforged.installertools.Main",
            ["--input", "in.jar"],
            []);

        Assert.False(runner.CanRunWithoutJava(step));
    }

    [Fact]
    public async Task RunAsync_remaps_the_jar_without_java()
    {
        var directory = TempDirectory();
        try
        {
            var input = Path.Combine(directory, "in.jar");
            using (var archive = ZipFile.Open(input, ZipArchiveMode.Create))
            {
                var entry = archive.CreateEntry("a.class");
                using var stream = entry.Open();
                stream.Write(NativeArtFixtureBuilder.EmptyClass("a", major: 61));
            }

            var mapping = Path.Combine(directory, "m.tsrg");
            File.WriteAllText(mapping, "a fixture/Sample\n");

            var output = Path.Combine(directory, "out.jar");
            var step = Step(
                "ForgeAutoRenamingTool-0.1.22-all.jar",
                "--input", input,
                "--output", output,
                "--names", mapping,
                "--ann-fix", "--ids-fix", "--src-fix", "--record-fix");

            var result = await new NativeProcessorRunner(new ThrowingRunner())
                .RunAsync(step, "/nonexistent/java", directory, TimeSpan.FromSeconds(30));

            Assert.Equal(0, result.ExitCode);
            using var outputArchive = ZipFile.OpenRead(output);
            var outputEntry = outputArchive.GetEntry("fixture/Sample.class");
            Assert.NotNull(outputEntry);
            using var outputStream = outputEntry!.Open();
            using var buffer = new MemoryStream();
            outputStream.CopyTo(buffer);
            Assert.Equal("fixture/Sample", ClassFileReader.Read(buffer.ToArray()).ClassName);
        }
        finally
        {
            Delete(directory);
        }
    }

    [Theory]
    [InlineData("1201", "MC_SLIM.jar", "MERGED.tsrg", "RENAME_ONLY_FIXED.jar", "ForgeAutoRenamingTool-0.1.22-all.jar")]
    [InlineData("1211", "client.jar", "MAPPINGS.tsrg", "oracle-fixed.jar", "AutoRenamingTool-2.0.3-all.jar")]
    public async Task Native_runner_matches_the_installer_jar(
        string suffix,
        string inputName,
        string mappingName,
        string expectedName,
        string jarName)
    {
        var directory = Environment.GetEnvironmentVariable("ARCHOERACX_ART_NATIVE_" + suffix);
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
        {
            return;
        }

        var output = Path.Combine(
            Path.GetTempPath(),
            "archoera-native-art-" + Guid.NewGuid().ToString("N") + ".jar");
        try
        {
            var step = Step(
                jarName,
                "--input", Path.Combine(directory, inputName),
                "--output", output,
                "--names", Path.Combine(directory, mappingName),
                "--ann-fix", "--ids-fix", "--src-fix", "--record-fix");

            var result = await new NativeProcessorRunner(new ThrowingRunner())
                .RunAsync(step, "/nonexistent/java", directory, TimeSpan.FromMinutes(5));

            Assert.Equal(0, result.ExitCode);
            using var expectedArchive = ZipFile.OpenRead(Path.Combine(directory, expectedName));
            using var actualArchive = ZipFile.OpenRead(output);
            var actual = actualArchive.Entries
                .Where(entry => !entry.FullName.EndsWith("/", StringComparison.Ordinal))
                .ToDictionary(entry => entry.FullName, entry => entry, StringComparer.Ordinal);

            var compared = 0;
            foreach (var entry in expectedArchive.Entries)
            {
                if (entry.FullName.EndsWith("/", StringComparison.Ordinal) ||
                    entry.FullName == "fernflower_abstract_parameter_names.txt")
                {
                    continue;
                }

                Assert.True(actual.TryGetValue(entry.FullName, out var actualEntry), entry.FullName);
                Assert.Equal(ReadEntry(entry), ReadEntry(actualEntry!));
                compared++;
            }

            var expectedClasses = expectedArchive.Entries.Count(
                entry => entry.FullName.EndsWith(".class", StringComparison.Ordinal));
            var actualClasses = actual.Keys.Count(
                name => name.EndsWith(".class", StringComparison.Ordinal));
            Assert.Equal(expectedClasses, actualClasses);
            Assert.True(compared >= expectedClasses);

            // binarypatcher copies its input order into the hashed client jar,
            // so ART must reproduce the installer's sorted jar layout.
            var expectedOrder = expectedArchive.Entries
                .Select(entry => entry.FullName)
                .Where(name => name != "fernflower_abstract_parameter_names.txt")
                .ToList();
            var actualOrder = actualArchive.Entries
                .Select(entry => entry.FullName)
                .ToList();
            Assert.Equal(expectedOrder, actualOrder);
        }
        finally
        {
            try
            {
                File.Delete(output);
            }
            catch
            {
                // best effort
            }
        }
    }

    [Theory]
    [InlineData("1201")]
    [InlineData("1211")]
    [InlineData("1211FORGE")]
    public async Task Binary_patcher_matches_the_installer_jar(string suffix)
    {
        var directory = Environment.GetEnvironmentVariable("ARCHOERACX_BINARYPATCH_" + suffix);
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
        {
            return;
        }

        var output = Path.Combine(
            Path.GetTempPath(),
            "archoera-binarypatch-" + Guid.NewGuid().ToString("N") + ".jar");
        try
        {
            var arguments = new List<string>
            {
                "--clean", Path.Combine(directory, "input.jar"),
                "--output", output,
                "--apply", Path.Combine(directory, "patch.lzma"),
            };
            if (suffix == "1211FORGE")
            {
                // Forge 1.21.1 passes both flags; the official client jar
                // includes data entries and unpatched classes.
                arguments.Add("--data");
                arguments.Add("--unpatched");
            }

            var step = new ProcessorStep(
                0,
                BinaryPatcherJar(suffix),
                [],
                suffix == "1211"
                    ? "net.neoforged.binarypatcher.ConsoleTool"
                    : "net.minecraftforge.binarypatcher.ConsoleTool",
                arguments,
                []);

            var result = await new NativeProcessorRunner(new ThrowingRunner())
                .RunAsync(step, "/nonexistent/java", directory, TimeSpan.FromMinutes(5));

            Assert.Equal(0, result.ExitCode);
            Assert.Equal(
                File.ReadAllBytes(Path.Combine(directory, "expected.jar")),
                File.ReadAllBytes(output));
        }
        finally
        {
            try
            {
                File.Delete(output);
            }
            catch
            {
                // best effort
            }
        }
    }

    private static string BinaryPatcherJar(string suffix) => suffix switch
    {
        "1201" => "net/minecraftforge/binarypatcher/1.1.1/binarypatcher-1.1.1.jar",
        "1211" => "net/neoforged/installertools/binarypatcher/2.1.2/" +
                  "binarypatcher-2.1.2-fatjar.jar",
        _ => "net/minecraftforge/binarypatcher/1.2.0/binarypatcher-1.2.0.jar",
    };

    private static byte[] ReadEntry(ZipArchiveEntry entry)
    {
        using var stream = entry.Open();
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }

    private static ProcessorStep Step(string jar, params string[] arguments)
        => new(
            0,
            "/libraries/net/minecraftforge/ForgeAutoRenamingTool/0.1.22/" + jar,
            [],
            "net.minecraftforge.fart.Main",
            arguments,
            []);

    private static string TempDirectory()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            "archoera-native-art-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void Delete(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch
        {
            // best effort
        }
    }

    private sealed class ThrowingRunner : IProcessorRunner
    {
        public Task<ProcessorRunResult> RunAsync(
            ProcessorStep step,
            string javaPath,
            string workingDir,
            TimeSpan timeout,
            CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("the native runner must not fall back");
    }
}
