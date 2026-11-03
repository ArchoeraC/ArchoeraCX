using Archoera.Infrastructure.Diagnostics;

namespace Archoera.Tests;

public sealed class CrashAnalyzerTests
{
    [Fact]
    public void Analyze_detects_an_old_java_runtime()
    {
        var directory = NewDirectory();
        try
        {
            var log = WriteLaunchLog(
                directory,
                """
                Exception in thread "main" java.lang.UnsupportedClassVersionError:
                net/minecraft/client/main/Main has been compiled by a more recent
                version of the Java Runtime (class file version 61.0), this version
                of the Java Runtime only recognizes class file versions up to 52.0
                [exited with code 1]
                """);

            var analysis = CrashAnalyzer.Analyze(
                directory,
                log,
                startedAt: DateTimeOffset.UtcNow.AddMinutes(-1));

            Assert.Equal("crashed", analysis.Outcome);
            Assert.Equal(1, analysis.ExitCode);
            var finding = Assert.Single(analysis.Findings);
            Assert.Equal("java.too_old", finding.RuleId);
            Assert.Equal("17", finding.Args["needed"]);
            Assert.Equal("8", finding.Args["current"]);
        }
        finally
        {
            Delete(directory);
        }
    }

    [Fact]
    public void Analyze_reads_the_newest_crash_report_description()
    {
        var directory = NewDirectory();
        try
        {
            var log = WriteLaunchLog(
                directory,
                """
                [12:00:00] [Render thread/INFO]: Stopping!
                [exited with code 0]
                """);
            var crashDir = Path.Combine(directory, "crash-reports");
            Directory.CreateDirectory(crashDir);
            File.WriteAllText(
                Path.Combine(crashDir, "crash-2026-10-30_12.00.00-client.txt"),
                """
                ---- Minecraft Crash Report ----
                // Who set us up the TNT?

                Time: 2026-10-30 12:00:00
                Description: Rendering overlay

                java.lang.NullPointerException: Cannot invoke renderer
                """);

            var analysis = CrashAnalyzer.Analyze(
                directory,
                log,
                startedAt: DateTimeOffset.UtcNow.AddMinutes(-1));

            Assert.Equal("crashed", analysis.Outcome);
            Assert.NotNull(analysis.CrashReportPath);
            var finding = Assert.Single(analysis.Findings);
            Assert.Equal("crash.report", finding.RuleId);
            Assert.Equal("Rendering overlay", finding.Args["description"]);
        }
        finally
        {
            Delete(directory);
        }
    }

    [Fact]
    public void Analyze_detects_a_graphics_driver_native_crash()
    {
        var directory = NewDirectory();
        try
        {
            var log = WriteLaunchLog(directory, "[exited with code -1073741819]");
            File.WriteAllText(
                Path.Combine(directory, "hs_err_pid4242.log"),
                """
                # A fatal error has been detected by the Java Runtime Environment:
                #  EXCEPTION_ACCESS_VIOLATION (0xc0000005)
                # Problematic frame:
                # C  [nvoglv64.dll+0x123456]
                """);

            var analysis = CrashAnalyzer.Analyze(
                directory,
                log,
                -1073741819,
                DateTimeOffset.UtcNow.AddMinutes(-1));

            Assert.Equal("crashed", analysis.Outcome);
            Assert.NotNull(analysis.HsErrPath);
            Assert.Contains(
                analysis.Findings,
                finding => finding.RuleId == "gfx.driver_crash");
        }
        finally
        {
            Delete(directory);
        }
    }

    [Fact]
    public void Analyze_reports_a_clean_exit_without_findings()
    {
        var directory = NewDirectory();
        try
        {
            var log = WriteLaunchLog(
                directory,
                """
                [12:00:00] [Render thread/INFO]: Stopping!
                [exited with code 0]
                """);

            var analysis = CrashAnalyzer.Analyze(
                directory,
                log,
                startedAt: DateTimeOffset.UtcNow.AddMinutes(-1));

            Assert.Equal("clean", analysis.Outcome);
            Assert.Equal(0, analysis.ExitCode);
            Assert.Empty(analysis.Findings);
        }
        finally
        {
            Delete(directory);
        }
    }

    [Fact]
    public void Analyze_redacts_tokens_from_evidence()
    {
        var directory = NewDirectory();
        try
        {
            var log = WriteLaunchLog(
                directory,
                """
                java.lang.OutOfMemoryError: Java heap space --accessToken supersecretvalue
                [exited with code 1]
                """);

            var analysis = CrashAnalyzer.Analyze(
                directory,
                log,
                1,
                DateTimeOffset.UtcNow.AddMinutes(-1));

            var finding = Assert.Single(analysis.Findings);
            Assert.Equal("jvm.heap", finding.RuleId);
            Assert.DoesNotContain(
                "supersecretvalue",
                string.Join("\n", finding.Evidence),
                StringComparison.Ordinal);
        }
        finally
        {
            Delete(directory);
        }
    }

    private static string WriteLaunchLog(string root, string text)
    {
        var directory = Path.Combine(root, ".archoera", "logs");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "launch-20261030-120000.log");
        File.WriteAllText(path, text);
        return path;
    }

    private static string NewDirectory()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            "archoera-crash-analyzer",
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
