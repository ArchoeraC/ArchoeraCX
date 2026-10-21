using System.Runtime.Versioning;
using Archoera.Application.Java;
using Archoera.Infrastructure.Processes;
using Microsoft.Win32;

namespace Archoera.Infrastructure.Java;

internal sealed record JavaCandidate(string ExecutablePath, string Source);

/// <summary>
/// Enumerates plausible Java executables without running a shell or walking whole disks.
/// Every candidate still has to pass <see cref="JavaProbe"/> before it is reported.
/// </summary>
internal static class JavaCandidateLocator
{
    private static readonly StringComparer PathComparer =
        OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;

    private static readonly string[] LinuxRoots = ["/usr/lib/jvm", "/usr/java", "/opt/java", "/opt"];
    private static readonly string[] LinuxUserRoots =
    [
        ".sdkman/candidates/java",
        ".asdf/installs/java",
        ".mise/installs/java",
        ".jdks",
    ];

    private static readonly string[] MacVmRoots =
    [
        "/Library/Java/JavaVirtualMachines",
        "/System/Library/Java/JavaVirtualMachines",
    ];

    private static string JavaExecutableName => OperatingSystem.IsWindows() ? "java.exe" : "java";

    // Manual paths come from UI input, so they are bounded and sanitized before
    // any filesystem access. This keeps a bad selection from turning into a
    // whole-disk walk.
    private const int MaxManualEntries = 64;
    private const int MaxManualPathLength = 4096;
    private const int ManualSearchMaxDepth = 5;
    private const int ManualSearchMaxDirectories = 2000;

    private static readonly string[] ManualSearchSkippedNames =
    [
        ".git",
        "node_modules",
        "build",
        "bin",
    ];

    public static IReadOnlyList<JavaCandidate> FindCandidates(
        JavaScanRequest? request = null,
        CancellationToken cancellationToken = default)
    {
        request ??= JavaScanRequest.Default;
        var collector = new CandidateCollector();

        if (request.IncludeDefaults)
        {
            AddEnvironmentCandidates(collector);

            if (OperatingSystem.IsWindows())
            {
                AddWindowsCandidates(collector);
            }
            else if (OperatingSystem.IsMacOS())
            {
                AddMacCandidates(collector);
            }
            else
            {
                AddLinuxCandidates(collector);
            }
        }

        AddManualExecutables(collector, request.Executables);
        AddManualRoots(collector, request.Roots, cancellationToken);

        return collector.Build();
    }

    private static void AddManualExecutables(CandidateCollector collector, IReadOnlyList<string>? executables)
    {
        foreach (var executable in SanitizeManualPaths(executables))
        {
            collector.Add(executable, "MANUAL_FILE");
        }
    }

    private static void AddManualRoots(
        CandidateCollector collector,
        IReadOnlyList<string>? roots,
        CancellationToken cancellationToken)
    {
        foreach (var root in SanitizeManualPaths(roots))
        {
            cancellationToken.ThrowIfCancellationRequested();
            AddManualRoot(root, collector, cancellationToken);
        }
    }

    private static void AddManualRoot(
        string root,
        CandidateCollector collector,
        CancellationToken cancellationToken)
    {
        string normalizedRoot;
        try
        {
            normalizedRoot = NormalizeDirectory(root);
        }
        catch
        {
            return;
        }

        if (!Directory.Exists(normalizedRoot))
        {
            return;
        }

        // The selection may already be a JDK/JRE home (for example
        // /opt/java/Adoptium/jdk-21.0.9+10).
        AddManualHome(normalizedRoot, collector);

        var visited = new HashSet<string>(PathComparer) { normalizedRoot };
        var pending = new Queue<(string Directory, int Depth)>();
        pending.Enqueue((normalizedRoot, 0));

        var visitedDirectories = 0;

        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var (directory, depth) = pending.Dequeue();
            visitedDirectories++;

            if (visitedDirectories > ManualSearchMaxDirectories)
            {
                break;
            }

            if (depth >= ManualSearchMaxDepth)
            {
                continue;
            }

            foreach (var child in SafeGetDirectories(directory))
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (IsSkippedManualDirectory(child))
                {
                    continue;
                }

                string normalizedChild;
                try
                {
                    normalizedChild = NormalizeDirectory(child);
                }
                catch
                {
                    continue;
                }

                if (!visited.Add(normalizedChild))
                {
                    continue;
                }

                AddManualHome(normalizedChild, collector);
                pending.Enqueue((normalizedChild, depth + 1));
            }
        }
    }

    private static void AddManualHome(string home, CandidateCollector collector)
    {
        collector.Add(Path.Combine(home, "bin", JavaExecutableName), "MANUAL_ROOT");
        collector.Add(Path.Combine(home, "jre", "bin", JavaExecutableName), "MANUAL_ROOT");
    }

    /// <summary>
    /// Trims, de-quotes, drops empty/oversized entries and stops after
    /// <see cref="MaxManualEntries"/> values.
    /// </summary>
    private static IEnumerable<string> SanitizeManualPaths(IReadOnlyList<string>? paths)
    {
        if (paths is null)
        {
            yield break;
        }

        var accepted = 0;
        foreach (var raw in paths)
        {
            if (accepted >= MaxManualEntries)
            {
                yield break;
            }

            var value = raw?.Trim();
            if (string.IsNullOrEmpty(value))
            {
                continue;
            }

            if (value.Length >= 2 &&
                ((value[0] == '"' && value[^1] == '"') || (value[0] == '\'' && value[^1] == '\'')))
            {
                value = value[1..^1].Trim();
            }

            if (value.Length == 0 || value.Length > MaxManualPathLength)
            {
                continue;
            }

            accepted++;
            yield return value;
        }
    }

    private static string NormalizeDirectory(string path)
        => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));

    private static bool IsSkippedManualDirectory(string path)
    {
        try
        {
            // Explicit roots may be symlinks (the user picked them), but the
            // recursive walk never follows links/reparse points below the root.
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            {
                return true;
            }
        }
        catch
        {
            return true;
        }

        var name = Path.GetFileName(Path.TrimEndingDirectorySeparator(path));
        foreach (var skipped in ManualSearchSkippedNames)
        {
            if (string.Equals(name, skipped, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static void AddEnvironmentCandidates(CandidateCollector collector)
    {
        var javaHome = Environment.GetEnvironmentVariable("JAVA_HOME");
        if (!string.IsNullOrWhiteSpace(javaHome))
        {
            collector.Add(Path.Combine(javaHome.Trim().Trim('"'), "bin", JavaExecutableName), "JAVA_HOME");
        }

        var path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(path))
        {
            return;
        }

        foreach (var rawEntry in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var entry = rawEntry.Trim().Trim('"');
            if (entry.Length == 0)
            {
                continue;
            }

            collector.Add(Path.Combine(entry, JavaExecutableName), "PATH");
        }
    }

    private static void AddLinuxCandidates(CandidateCollector collector)
    {
        foreach (var root in LinuxRoots)
        {
            foreach (var directory in SafeGetDirectories(root))
            {
                AddLinuxHome(directory, collector);

                // /opt/java is commonly laid out as <vendor>/<jdk>/bin/java,
                // unlike /usr/lib/jvm where the runtime is one level deep.
                if (string.Equals(root, "/opt/java", StringComparison.Ordinal))
                {
                    foreach (var nested in SafeGetDirectories(directory))
                    {
                        AddLinuxHome(nested, collector);
                    }
                }
            }
        }

        var userHome = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrWhiteSpace(userHome))
        {
            return;
        }

        foreach (var relative in LinuxUserRoots)
        {
            var root = Path.Combine(userHome, relative);
            foreach (var directory in SafeGetDirectories(root))
            {
                AddLinuxHome(directory, collector);
                foreach (var nested in SafeGetDirectories(directory))
                {
                    AddLinuxHome(nested, collector);
                }
            }
        }
    }

    private static void AddLinuxHome(string home, CandidateCollector collector)
    {
        collector.Add(Path.Combine(home, "bin", JavaExecutableName), "SYSTEM");
        collector.Add(Path.Combine(home, "jre", "bin", JavaExecutableName), "SYSTEM");
    }

    private static void AddMacCandidates(CandidateCollector collector)
    {
        foreach (var root in MacVmRoots)
        {
            foreach (var vm in SafeGetDirectories(root))
            {
                collector.Add(
                    Path.Combine(vm, "Contents", "Home", "bin", JavaExecutableName),
                    "SYSTEM");
            }
        }

        // /usr/libexec/java_home is the canonical macOS lookup. It is a direct
        // executable invocation, not a shell, and failure is non-fatal.
        var result = ProcessRunner.Run("/usr/libexec/java_home", [], TimeSpan.FromSeconds(3));
        if (result is not { ExitCode: 0 })
        {
            return;
        }

        foreach (var line in result.StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            collector.Add(Path.Combine(line, "bin", JavaExecutableName), "JAVA_HOME_UTIL");
            break;
        }
    }

    [SupportedOSPlatform("windows")]
    private static void AddWindowsCandidates(CandidateCollector collector)
    {
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);

        AddWindowsHomeRoot(Path.Combine(programFiles, "Java"), collector);
        AddWindowsHomeRoot(Path.Combine(programFiles, "Eclipse Adoptium"), collector);
        AddWindowsHomeRoot(Path.Combine(programFiles, "Zulu"), collector);
        AddWindowsHomeRoot(Path.Combine(programFilesX86, "Java"), collector);
        AddWindowsHomeRoot(Path.Combine(programFilesX86, "Eclipse Adoptium"), collector);
        AddWindowsHomeRoot(Path.Combine(programFilesX86, "Zulu"), collector);

        AddMicrosoftJdks(Path.Combine(programFiles, "Microsoft"), collector);
        AddMicrosoftJdks(Path.Combine(programFilesX86, "Microsoft"), collector);

        AddWindowsRegistryCandidates(collector);
    }

    private static void AddWindowsHomeRoot(string root, CandidateCollector collector)
    {
        if (string.IsNullOrWhiteSpace(root))
        {
            return;
        }

        // Some installers put the JDK directly in the vendor folder.
        collector.Add(Path.Combine(root, "bin", JavaExecutableName), "SYSTEM");

        foreach (var home in SafeGetDirectories(root))
        {
            collector.Add(Path.Combine(home, "bin", JavaExecutableName), "SYSTEM");
            collector.Add(Path.Combine(home, "jre", "bin", JavaExecutableName), "SYSTEM");
        }
    }

    private static void AddMicrosoftJdks(string microsoftRoot, CandidateCollector collector)
    {
        if (string.IsNullOrWhiteSpace(microsoftRoot))
        {
            return;
        }

        foreach (var home in SafeGetDirectories(microsoftRoot))
        {
            var name = Path.GetFileName(home);
            if (name.StartsWith("jdk", StringComparison.OrdinalIgnoreCase))
            {
                collector.Add(Path.Combine(home, "bin", JavaExecutableName), "SYSTEM");
            }
        }
    }

    [SupportedOSPlatform("windows")]
    private static void AddWindowsRegistryCandidates(CandidateCollector collector)
    {
        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            try
            {
                using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
                AddJavaSoftValues(baseKey, @"SOFTWARE\JavaSoft\Java Runtime Environment", collector);
                AddJavaSoftValues(baseKey, @"SOFTWARE\JavaSoft\JDK", collector);
                AddJavaSoftValues(baseKey, @"SOFTWARE\JavaSoft\Java Development Kit", collector);
            }
            catch
            {
                // Registry access is best effort; scanner still works without it.
            }
        }
    }

    [SupportedOSPlatform("windows")]
    private static void AddJavaSoftValues(RegistryKey baseKey, string keyPath, CandidateCollector collector)
    {
        try
        {
            using var root = baseKey.OpenSubKey(keyPath);
            if (root is null)
            {
                return;
            }

            foreach (var subKeyName in root.GetSubKeyNames())
            {
                try
                {
                    using var versionKey = root.OpenSubKey(subKeyName);
                    if (versionKey?.GetValue("JavaHome") is string home && !string.IsNullOrWhiteSpace(home))
                    {
                        collector.Add(Path.Combine(home, "bin", JavaExecutableName), "REGISTRY");
                    }
                }
                catch
                {
                    // Ignore an individual unreadable key.
                }
            }
        }
        catch
        {
            // Ignore a missing/unreadable JavaSoft branch.
        }
    }

    private static string[] SafeGetDirectories(string root)
    {
        try
        {
            return Directory.Exists(root)
                ? Directory.GetDirectories(root)
                    .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                    .ToArray()
                : [];
        }
        catch
        {
            return [];
        }
    }

    private sealed class CandidateCollector
    {
        private readonly HashSet<string> _seen = new(PathComparer);
        private readonly List<JavaCandidate> _items = [];

        public void Add(string? candidate, string source)
        {
            if (string.IsNullOrWhiteSpace(candidate))
            {
                return;
            }

            try
            {
                var fullPath = Path.GetFullPath(candidate);
                if (!File.Exists(fullPath) || !_seen.Add(fullPath))
                {
                    return;
                }

                _items.Add(new JavaCandidate(fullPath, source));
            }
            catch
            {
                // Invalid path syntax, unreadable directory, etc.
            }
        }

        public IReadOnlyList<JavaCandidate> Build() => _items;
    }
}
