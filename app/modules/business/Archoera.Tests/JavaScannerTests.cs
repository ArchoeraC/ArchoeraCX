using Archoera.Application.Java;
using Archoera.Infrastructure.Java;

namespace Archoera.Tests;

public sealed class JavaScannerTests
{
    [Fact]
    public void Scan_collapses_symlink_aliases_and_sorts_distinct_majors()
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS()) return;

        var directory = TempDirectory();
        try
        {
            var first = CreateFakeJdk(directory, "jdk21", "21.0.9");
            var second = CreateFakeJdk(directory, "jdk17", "17.0.12");
            var alias = Path.Combine(directory, "java-alias");
            File.CreateSymbolicLink(alias, first);

            var result = new JavaScanner().Scan(new JavaScanRequest
            {
                IncludeDefaults = false,
                Executables = [first, alias, second],
            });

            Assert.Equal(2, result.Count);
            Assert.Equal(21, result[0].Major);
            Assert.Equal(17, result[1].Major);
            Assert.Equal(result.Count, result.Select(item => item.Home).Distinct().Count());
        }
        finally
        {
            Delete(directory);
        }
    }

    [Fact]
    public void Scan_finds_nested_manual_root_jdks()
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS()) return;

        var directory = TempDirectory();
        try
        {
            var jdk = CreateFakeJdk(
                Path.Combine(directory, "vendor"),
                "jdk-21",
                "21.0.2");
            var root = Path.Combine(directory, "vendor");

            var result = new JavaScanner().Scan(new JavaScanRequest
            {
                IncludeDefaults = false,
                Roots = [root],
            });

            var item = Assert.Single(result);
            Assert.Equal(Path.GetFullPath(jdk), item.Path);
            Assert.Equal(21, item.Major);
        }
        finally
        {
            Delete(directory);
        }
    }

    private static string CreateFakeJdk(string parent, string name, string version)
    {
        var home = Path.Combine(parent, name);
        var bin = Path.Combine(home, "bin");
        Directory.CreateDirectory(bin);
        var executable = Path.Combine(bin, "java");
        File.WriteAllText(
            executable,
            $"#!/bin/sh\necho \"java.version={version}\"\necho \"java.home={home}\"\necho \"java.vendor=TestVendor\"\necho \"os.arch=amd64\"\nexit 0\n");
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(
                executable,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
        }
        return executable;
    }

    private static string TempDirectory()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            "archoera-java-scan",
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
