using Archoera.Domain.Loaders;
using Archoera.Infrastructure.Loaders;

namespace Archoera.Tests;

public sealed class ForgeProcessorRunnerTests
{
    [Fact]
    public async Task RunAsync_uses_an_argv_list_and_clears_inherited_java_options()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var directory = Path.Combine(
            Path.GetTempPath(),
            "archoera-tests",
            $"runner-{Guid.NewGuid():N}",
            "fake java dir");
        Directory.CreateDirectory(directory);

        var script = Path.Combine(directory, "fake-java");
        var argumentsFile = Path.Combine(directory, "args.txt");
        var environmentFile = Path.Combine(directory, "env.txt");

        await File.WriteAllTextAsync(script, """
            #!/bin/sh
            : > "$ARCHOERACX_ARGS_OUT"
            for a in "$@"; do printf '%s\n' "$a" >> "$ARCHOERACX_ARGS_OUT"; done
            env > "$ARCHOERACX_ENV_OUT"
            exit 0
            """);
        File.SetUnixFileMode(
            script,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

        var previousArgs = Environment.GetEnvironmentVariable("ARCHOERACX_ARGS_OUT");
        var previousEnv = Environment.GetEnvironmentVariable("ARCHOERACX_ENV_OUT");
        var previousToolOptions = Environment.GetEnvironmentVariable("JAVA_TOOL_OPTIONS");
        var previousOptions = Environment.GetEnvironmentVariable("_JAVA_OPTIONS");
        var previousClassPath = Environment.GetEnvironmentVariable("CLASSPATH");

        try
        {
            Environment.SetEnvironmentVariable("ARCHOERACX_ARGS_OUT", argumentsFile);
            Environment.SetEnvironmentVariable("ARCHOERACX_ENV_OUT", environmentFile);
            Environment.SetEnvironmentVariable("JAVA_TOOL_OPTIONS", "should-not-appear");
            Environment.SetEnvironmentVariable("_JAVA_OPTIONS", "should-not-appear");
            Environment.SetEnvironmentVariable("CLASSPATH", "should-not-appear");

            var step = new ProcessorStep(
                Index: 0,
                JarRelativePath: Path.Combine(directory, "processor jar.jar"),
                ClassPath: [Path.Combine(directory, "lib with space.jar")],
                MainClass: "com.example.Main",
                Arguments: ["arg with space", "中文", "--output", Path.Combine(directory, "out file.jar")],
                Outputs: []);

            var result = await new ForgeProcessorRunner().RunAsync(
                step,
                script,
                directory,
                TimeSpan.FromSeconds(20));

            Assert.Equal(0, result.ExitCode);
            Assert.False(result.TimedOut);

            var lines = await File.ReadAllLinesAsync(argumentsFile);
            Assert.Equal("-cp", lines[0]);
            Assert.Equal(
                string.Join(
                    Path.PathSeparator,
                    [step.JarRelativePath, step.ClassPath[0]]),
                lines[1]);
            Assert.Equal("com.example.Main", lines[2]);
            Assert.Equal(["arg with space", "中文", "--output", step.Arguments[3]], lines[3..]);

            var environment = await File.ReadAllTextAsync(environmentFile);
            Assert.DoesNotContain("JAVA_TOOL_OPTIONS=", environment, StringComparison.Ordinal);
            Assert.DoesNotContain("_JAVA_OPTIONS=", environment, StringComparison.Ordinal);
            Assert.DoesNotContain("CLASSPATH=should-not-appear", environment, StringComparison.Ordinal);
        }
        finally
        {
            Environment.SetEnvironmentVariable("ARCHOERACX_ARGS_OUT", previousArgs);
            Environment.SetEnvironmentVariable("ARCHOERACX_ENV_OUT", previousEnv);
            Environment.SetEnvironmentVariable("JAVA_TOOL_OPTIONS", previousToolOptions);
            Environment.SetEnvironmentVariable("_JAVA_OPTIONS", previousOptions);
            Environment.SetEnvironmentVariable("CLASSPATH", previousClassPath);

            try
            {
                Directory.Delete(Path.GetDirectoryName(directory)!, recursive: true);
            }
            catch
            {
                // best effort
            }
        }
    }

    [Fact]
    public async Task RunAsync_returns_a_result_when_the_executable_is_missing()
    {
        var step = new ProcessorStep(
            Index: 0,
            JarRelativePath: "/nonexistent/processor.jar",
            ClassPath: [],
            MainClass: "com.example.Main",
            Arguments: [],
            Outputs: []);

        var result = await new ForgeProcessorRunner().RunAsync(
            step,
            "/nonexistent/java",
            Path.GetTempPath(),
            TimeSpan.FromSeconds(5));

        Assert.Equal(-1, result.ExitCode);
        Assert.False(result.TimedOut);
        Assert.False(string.IsNullOrWhiteSpace(result.StandardError));
    }
}
