using System.Diagnostics;
using Archoera.Infrastructure.Launch;

namespace Archoera.Tests;

/// <summary>
/// The executable side of the pre-load / pre-launch / post-exit hooks: argv
/// construction, the INST_* environment, bounded command logs, failure mapping
/// and timeouts. Real commands are limited to harmless OS utilities.
/// </summary>
public sealed class LaunchCommandRunnerTests
{
    [Fact]
    public void Builder_uses_argv_working_directory_and_instance_environment()
    {
        var root = Path.Combine(Path.GetTempPath(), "archoera-command-builder");
        var context = new LaunchCommandContext(
            LaunchCommandPhase.PreLaunch,
            "/opt/tool --flag \"two words\"",
            "inst-1",
            "My Instance",
            root,
            Path.Combine(root, "game"),
            "/opt/java/bin/java",
            [
                "-Xmx2G",
                "net.minecraft.client.main.Main",
                "--accessToken",
                "super-secret-token",
                "--clientId",
                "client-secret",
                "-Dfoo=bar",
            ],
            new Dictionary<string, string>
            {
                ["SAFE"] = "1",
                ["JAVA_TOOL_OPTIONS"] = "-Xmx1G",
            });

        var startInfo = LaunchCommandRunner.BuildStartInfo(context);

        Assert.Equal("/opt/tool", startInfo.FileName);
        Assert.Equal(["--flag", "two words"], startInfo.ArgumentList.ToArray());
        Assert.Equal(root, startInfo.WorkingDirectory);
        Assert.False(startInfo.UseShellExecute);
        Assert.True(startInfo.RedirectStandardOutput);
        Assert.True(startInfo.RedirectStandardError);

        Assert.Equal("My Instance", startInfo.Environment["INST_NAME"]);
        Assert.Equal("inst-1", startInfo.Environment["INST_ID"]);
        Assert.Equal(root, startInfo.Environment["INST_DIR"]);
        Assert.Equal(Path.Combine(root, "game"), startInfo.Environment["INST_MC_DIR"]);
        Assert.Equal("/opt/java/bin/java", startInfo.Environment["INST_JAVA"]);
        Assert.Equal("1", startInfo.Environment["SAFE"]);
        Assert.False(startInfo.Environment.ContainsKey("JAVA_TOOL_OPTIONS"));

        var javaArgs = startInfo.Environment["INST_JAVA_ARGS"];
        Assert.Contains("-Xmx2G", javaArgs);
        Assert.Contains("--accessToken <redacted>", javaArgs);
        Assert.Contains("--clientId <redacted>", javaArgs);
        Assert.Contains("-Dfoo=bar", javaArgs);
        Assert.DoesNotContain("super-secret-token", javaArgs);
        Assert.DoesNotContain("client-secret", javaArgs);
    }

    [Fact]
    public void Builder_does_not_invoke_a_shell()
    {
        var root = Path.Combine(Path.GetTempPath(), "archoera-command-no-shell");
        var context = new LaunchCommandContext(
            LaunchCommandPhase.PreLoad,
            "echo hello > redirected.txt",
            "inst-1",
            "No Shell",
            root,
            root,
            string.Empty,
            [],
            null);

        var startInfo = LaunchCommandRunner.BuildStartInfo(context);

        Assert.Equal("echo", startInfo.FileName);
        Assert.Equal(
            ["hello", ">", "redirected.txt"],
            startInfo.ArgumentList.ToArray());
    }

    [Fact]
    public void Join_arguments_redacts_equals_form_and_double_dash_secret_flags()
    {
        var joined = LaunchCommandRunner.JoinArguments(
        [
            "--accessToken=abc",
            "--auth_xuid",
            "12345",
            "--clientid=xyz",
            "--accessToken",
            "-dashed-token",
            "--username",
            "Player",
        ]);

        Assert.Equal(
            "--accessToken=<redacted> --auth_xuid <redacted> --clientid=<redacted> --accessToken <redacted> --username Player",
            joined);
    }

    [Fact]
    public async Task Harmless_command_runs_and_writes_a_log()
    {
        var command = HarmlessCommand();
        if (command is null) return;

        var directory = TempDirectory();
        try
        {
            var result = await LaunchCommandRunner.RunAsync(
                Context(LaunchCommandPhase.PreLoad, command, directory),
                TimeSpan.FromSeconds(10));

            Assert.True(result.Succeeded);
            Assert.Equal(0, result.ExitCode);
            Assert.True(File.Exists(result.LogPath));
            var log = await File.ReadAllTextAsync(result.LogPath);
            Assert.Contains("pre-load command:", log);
            Assert.Contains("exited with code 0", log);
            if (!OperatingSystem.IsWindows())
            {
                var mode = File.GetUnixFileMode(result.LogPath);
                Assert.Equal(
                    UnixFileMode.UserRead | UnixFileMode.UserWrite,
                    mode & (UnixFileMode.UserRead | UnixFileMode.UserWrite |
                            UnixFileMode.GroupRead | UnixFileMode.GroupWrite |
                            UnixFileMode.OtherRead | UnixFileMode.OtherWrite));
            }
        }
        finally
        {
            Delete(directory);
        }
    }

    [Fact]
    public async Task Non_zero_pre_launch_exit_aborts_with_launch_command_failed()
    {
        var command = FailingCommand("boom");
        if (command is null) return;

        var directory = TempDirectory();
        try
        {
            var result = await LaunchCommandRunner.RunAsync(
                Context(LaunchCommandPhase.PreLaunch, command, directory),
                TimeSpan.FromSeconds(10));

            Assert.Equal(LaunchCommandFailure.NonZeroExit, result.Failure);
            Assert.Equal(7, result.ExitCode);
            Assert.Contains("boom", result.StandardErrorTail);
            Assert.True(File.Exists(result.LogPath));

            var exception = Assert.Throws<LaunchException>(
                () => LaunchCommandRunner.EnsureSuccess(result));
            Assert.Equal("LAUNCH_COMMAND_FAILED", exception.Code);
            Assert.Contains("pre-launch", exception.Message);
            Assert.Contains("7", exception.Message);
            Assert.Contains("boom", exception.Message);
            Assert.Contains(result.LogPath, exception.Message);
        }
        finally
        {
            Delete(directory);
        }
    }

    [Fact]
    public async Task Post_exit_failure_is_swallowed_and_logged()
    {
        var command = FailingCommand("post-fail");
        if (command is null) return;

        var directory = TempDirectory();
        try
        {
            var result = await LaunchCommandRunner.RunPostExitAsync(
                Context(LaunchCommandPhase.PostExit, command, directory));

            Assert.NotNull(result);
            Assert.Equal(LaunchCommandFailure.NonZeroExit, result!.Failure);
            Assert.Equal(7, result.ExitCode);
            Assert.True(File.Exists(result.LogPath));
            var log = await File.ReadAllTextAsync(result.LogPath);
            Assert.Contains("post-exit command:", log);
            Assert.Contains("exited with code 7", log);
            Assert.Contains("post-fail", log);
        }
        finally
        {
            Delete(directory);
        }
    }

    [Fact]
    public async Task Start_failure_is_reported_without_executing_the_command()
    {
        var directory = TempDirectory();
        try
        {
            var result = await LaunchCommandRunner.RunAsync(
                Context(
                    LaunchCommandPhase.PreLaunch,
                    Path.Combine(directory, "missing", "program"),
                    directory),
                TimeSpan.FromSeconds(10));

            Assert.Equal(LaunchCommandFailure.StartFailed, result.Failure);
            Assert.True(File.Exists(result.LogPath));
            var exception = Assert.Throws<LaunchException>(
                () => LaunchCommandRunner.EnsureSuccess(result));
            Assert.Equal("LAUNCH_COMMAND_FAILED", exception.Code);
            Assert.Contains("could not start", exception.Message);
        }
        finally
        {
            Delete(directory);
        }
    }

    [Fact]
    public async Task Timeout_kills_the_process_tree_and_reports_the_phase()
    {
        var command = LongRunningCommand();
        if (command is null) return;

        var directory = TempDirectory();
        try
        {
            var stopwatch = Stopwatch.StartNew();
            var result = await LaunchCommandRunner.RunAsync(
                Context(LaunchCommandPhase.PreLaunch, command, directory),
                TimeSpan.FromMilliseconds(200));
            stopwatch.Stop();

            Assert.Equal(LaunchCommandFailure.Timeout, result.Failure);
            Assert.Null(result.ExitCode);
            Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(10));
            var log = await File.ReadAllTextAsync(result.LogPath);
            Assert.Contains("timed out after", log);
            Assert.Contains("process tree killed", log);

            var exception = Assert.Throws<LaunchException>(
                () => LaunchCommandRunner.EnsureSuccess(result));
            Assert.Equal("LAUNCH_COMMAND_FAILED", exception.Code);
            Assert.Contains("pre-launch command timed out", exception.Message);
        }
        finally
        {
            Delete(directory);
        }
    }

    [Fact]
    public async Task Invalid_command_quoting_is_rejected_by_the_builder()
    {
        var context = Context(
            LaunchCommandPhase.PreLoad,
            "\"unclosed",
            Path.GetTempPath());
        var exception = Assert.Throws<FormatException>(
            () => LaunchCommandRunner.BuildStartInfo(context));
        Assert.Contains("quote", exception.Message);
    }

    private static LaunchCommandContext Context(
        LaunchCommandPhase phase,
        string command,
        string root)
        => new(
            phase,
            command,
            "inst-test",
            "Command Test",
            root,
            root,
            "/opt/java/bin/java",
            ["-Xmx1G", "Main"],
            null);

    /// <summary>
    /// A command that exits successfully without touching the instance. The
    /// user command is always a program plus arguments; no shell is implied.
    /// </summary>
    private static string? HarmlessCommand()
    {
        if (OperatingSystem.IsWindows())
        {
            return File.Exists(@"C:\Windows\System32\cmd.exe")
                ? "cmd.exe /c exit 0"
                : null;
        }

        return File.Exists("/bin/true")
            ? "/bin/true"
            : File.Exists("/usr/bin/true")
                ? "/usr/bin/true"
                : null;
    }

    private static string? FailingCommand(string message)
    {
        if (OperatingSystem.IsWindows())
        {
            return File.Exists(@"C:\Windows\System32\cmd.exe")
                ? $"cmd.exe /c \"echo {message} 1>&2 & exit 7\""
                : null;
        }

        return File.Exists("/bin/sh")
            ? $"/bin/sh -c \"echo {message} 1>&2; exit 7\""
            : null;
    }

    private static string? LongRunningCommand()
    {
        if (OperatingSystem.IsWindows())
        {
            return File.Exists(@"C:\Windows\System32\cmd.exe")
                ? "cmd.exe /c ping -n 6 127.0.0.1"
                : null;
        }

        return File.Exists("/bin/sh")
            ? "/bin/sh -c \"sleep 30\""
            : null;
    }

    private static string TempDirectory()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            "archoera-launch-command-runner",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void Delete(string path)
    {
        try
        {
            if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
        }
        catch
        {
            // best effort
        }
    }
}
