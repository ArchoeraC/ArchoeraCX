using System.IO.Compression;
using System.Text;
using Archoera.Application.Loaders;
using Archoera.Domain.Loaders;
using Archoera.Infrastructure.Loaders.Native;

namespace Archoera.Tests;

public sealed class InstallerProcessorTests
{
    [Theory]
    [InlineData("1201")]
    [InlineData("1211")]
    public async Task Mcp_data_copies_the_configured_entry(string suffix)
    {
        var directory = Directory(suffix);
        if (directory is null)
        {
            return;
        }

        var output = TempFile();
        try
        {
            var result = await Run(
                directory,
                "net.minecraftforge.installertools.ConsoleTool",
                [
                    "--task", "MCP_DATA",
                    "--input", Path.Combine(directory, "mcp.zip"),
                    "--output", output,
                    "--key", "mappings",
                ]);

            Assert.Equal(0, result.ExitCode);
            Assert.Equal(
                File.ReadAllBytes(Path.Combine(directory, "mappings.txt")),
                File.ReadAllBytes(output));
        }
        finally
        {
            Delete(output);
        }
    }

    [Theory]
    [InlineData("1201", false, false)]
    [InlineData("1211", true, true)]
    public async Task Merge_mapping_matches_the_installer(
        string suffix,
        bool fields,
        bool methods)
    {
        var directory = Directory(suffix);
        if (directory is null)
        {
            return;
        }

        var output = TempFile();
        try
        {
            var arguments = new List<string>
            {
                "--task", "MERGE_MAPPING",
                "--left", Path.Combine(directory, "mappings.txt"),
                "--right", Path.Combine(directory, "right.txt"),
                "--output", output,
                "--classes",
                "--reverse-right",
            };
            if (fields)
            {
                arguments.Add("--fields");
            }

            if (methods)
            {
                arguments.Add("--methods");
            }

            var result = await Run(
                directory,
                "net.minecraftforge.installertools.ConsoleTool",
                arguments);

            Assert.Equal(0, result.ExitCode);
            Assert.Equal(
                File.ReadAllBytes(Path.Combine(directory, "merged.txt")),
                File.ReadAllBytes(output));
        }
        finally
        {
            Delete(output);
        }
    }

    [Theory]
    [InlineData("1201")]
    [InlineData("1211")]
    public async Task Jar_splitter_matches_the_installer(string suffix)
    {
        var directory = Directory(suffix);
        if (directory is null)
        {
            return;
        }

        var slim = TempFile();
        var extra = TempFile();
        try
        {
            var result = await Run(
                directory,
                "net.minecraftforge.jarsplitter.ConsoleTool",
                [
                    "--input", Path.Combine(directory, "input.jar"),
                    "--slim", slim,
                    "--extra", extra,
                    "--srg", Path.Combine(directory, "merged.txt"),
                ]);

            Assert.Equal(0, result.ExitCode);
            Assert.Equal(
                File.ReadAllBytes(Path.Combine(directory, "slim.jar")),
                File.ReadAllBytes(slim));
            Assert.Equal(
                File.ReadAllBytes(Path.Combine(directory, "extra.jar")),
                File.ReadAllBytes(extra));
        }
        finally
        {
            Delete(slim);
            Delete(extra);
        }
    }

    [Fact]
    public async Task Mcp_data_extracts_a_directory_key_without_traversal()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "archoera-mcpdata-" + Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(root);
        var input = Path.Combine(root, "data.zip");
        var output = Path.Combine(root, "out");
        try
        {
            using (var stream = File.Create(input))
            using (var archive = new ZipArchive(stream, ZipArchiveMode.Create))
            {
                WriteEntry(
                    archive,
                    "config.json",
                    Encoding.UTF8.GetBytes(
                        "{\"spec\":1,\"data\":{\"patches\":{\"client\":\"patches/client/\"}}}"));
                WriteEntry(archive, "patches/client/a.txt", "alpha"u8.ToArray());
                WriteEntry(archive, "patches/client/sub/b.txt", "beta"u8.ToArray());
                WriteEntry(archive, "patches/server/c.txt", "gamma"u8.ToArray());
            }

            var result = await Run(
                root,
                "net.minecraftforge.installertools.ConsoleTool",
                [
                    "--task", "MCP_DATA",
                    "--input", input,
                    "--output", output,
                    "--key", "patches.client",
                ]);

            Assert.Equal(0, result.ExitCode);
            Assert.Equal("alpha", File.ReadAllText(Path.Combine(output, "a.txt")));
            Assert.Equal("beta", File.ReadAllText(Path.Combine(output, "sub", "b.txt")));
            Assert.False(File.Exists(Path.Combine(output, "c.txt")));
        }
        finally
        {
            try
            {
                System.IO.Directory.Delete(root, recursive: true);
            }
            catch
            {
                // best effort
            }
        }
    }

    private static void WriteEntry(ZipArchive archive, string name, byte[] data)
    {
        var entry = archive.CreateEntry(name);
        using var stream = entry.Open();
        stream.Write(data);
    }

    private static async Task<ProcessorRunResult> Run(
        string directory,
        string mainClass,
        IEnumerable<string> arguments)
    {
        var step = new ProcessorStep(
            0,
            "/libraries/installertools.jar",
            [],
            mainClass,
            [.. arguments],
            []);
        return await new NativeProcessorRunner(new ThrowingRunner())
            .RunAsync(step, "/nonexistent/java", directory, TimeSpan.FromMinutes(10));
    }

    private static string? Directory(string suffix)
    {
        var directory = Environment.GetEnvironmentVariable("ARCHOERACX_INSTALLER_" + suffix);
        return string.IsNullOrWhiteSpace(directory) || !System.IO.Directory.Exists(directory)
            ? null
            : directory;
    }

    private static string TempFile()
        => Path.Combine(
            Path.GetTempPath(),
            "archoera-installer-" + Guid.NewGuid().ToString("N"));

    private static void Delete(string path)
    {
        try
        {
            File.Delete(path);
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
