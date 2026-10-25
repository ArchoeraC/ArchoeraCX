using System.Runtime.InteropServices;
using Archoera.Domain;
using Archoera.Infrastructure.Modules;
using SQLitePCL;

namespace Archoera.Infrastructure.Storage;

/// <summary>
/// Owns the process-wide SQLite provider binding.
///
/// WHY: the business module is a NativeAOT shared library that ships as a
/// portable bundle, so it must not depend on the host's <c>libsqlite3</c>.
/// Relying on the system library makes the same build work on one Linux/macOS
/// machine and fail on another with <c>Unable to load shared library
/// 'sqlite3'</c>. SQLitePCLRaw's bundled <c>e_sqlite3</c> is shipped on every
/// platform (Windows has no system SQLite either) and loaded from a resolved
/// absolute path, so the module binds to exactly that file instead of an
/// ambient soname and the whole app keeps one global symbol set.
/// </summary>
internal static class SqliteNativeLibrary
{
    /// <summary>
    /// Full file path of the bundled native library, for builds that cannot
    /// place it next to the module.
    /// </summary>
    internal const string OverrideVariable = "ARCHOERACX_SQLITE_LIB";

    private static readonly object Gate = new();
    private static volatile bool _initialized;

    /// <summary>Bundled library file name for the current platform.</summary>
    internal static string FileName => OperatingSystem.IsWindows()
        ? "e_sqlite3.dll"
        : OperatingSystem.IsMacOS()
            ? "libe_sqlite3.dylib"
            : "libe_sqlite3.so";

    /// <summary>
    /// Installs SQLitePCLRaw's bundled <c>e_sqlite3</c> provider once per
    /// process. Idempotent and thread-safe; repositories call it from their
    /// lazy init path.
    /// </summary>
    internal static void EnsureProvider()
    {
        if (_initialized) return;
        lock (Gate)
        {
            if (_initialized) return;

            var fileName = FileName;
            var baseDirectory = AppContext.BaseDirectory;
            string? processDirectory = Environment.ProcessPath is { } processPath
                ? Path.GetDirectoryName(processPath)
                : null;

            string? cwd = null;
            try
            {
                cwd = Environment.CurrentDirectory;
            }
            catch
            {
                // Deleted/replaced working directory; packaged paths still work.
            }

            var directories = CandidatePaths(baseDirectory, processDirectory, cwd);
            var path = Resolve(
                Environment.GetEnvironmentVariable(OverrideVariable),
                directories,
                fileName);
            if (path is null) throw Missing(fileName, directories);

            var handle = LoadBundled(path);
            BindResolver(handle);
            raw.SetProvider(new SQLite3Provider_e_sqlite3());
            _initialized = true;
        }
    }

    /// <summary>
    /// Loads the bundled library without letting it bind to another SQLite copy
    /// already present in the process.
    ///
    /// WHY Linux needs this: GTK/GDK (and therefore a running launcher) can load
    /// the host's <c>libsqlite3.so.0</c> before the C# module initializes. A
    /// later plain <c>dlopen</c> of <c>libe_sqlite3.so</c> keeps that first copy
    /// in the global lookup scope, so the bundled library's own internal call to
    /// <c>sqlite3_initialize</c> resolves to the <em>other</em> copy: the system
    /// globals get initialized while the bundled globals stay null and the first
    /// <c>sqlite3_open_v2</c> jumps through a null allocator pointer (SIGSEGV in
    /// <c>sqlite3Malloc</c>). <c>RTLD_DEEPBIND</c> makes the bundled object prefer
    /// its own definitions for its self-references, keeping the two copies from
    /// interleaving. macOS resolves internal references with the two-level
    /// namespace and Windows has no second SQLite, so both use the plain loader.
    /// </summary>
    private static IntPtr LoadBundled(string path)
    {
        if (OperatingSystem.IsLinux())
        {
            var handle = TryDeepBindLoad(path);
            if (handle != IntPtr.Zero) return handle;
        }

        return NativeLibrary.Load(path);
    }

    // glibc dlopen flags. RTLD_LOCAL keeps the bundled copy out of the global
    // scope; RTLD_DEEPBIND makes its own definitions win for its self-reference.
    private const int RtldNow = 0x00002;
    private const int RtldLocal = 0x00000;
    private const int RtldDeepBind = 0x00008;

    private static IntPtr TryDeepBindLoad(string path)
    {
        try
        {
            return LinuxDlOpen(path, RtldNow | RtldLocal | RtldDeepBind);
        }
        catch (DllNotFoundException)
        {
            // libdl is unavailable (musl, unusual container); the plain loader
            // still works when the process has no other SQLite loaded.
            return IntPtr.Zero;
        }
        catch (EntryPointNotFoundException)
        {
            return IntPtr.Zero;
        }
    }

    [DllImport("libdl.so.2", EntryPoint = "dlopen")]
    private static extern IntPtr LinuxDlOpen(
        [MarshalAs(UnmanagedType.LPUTF8Str)] string fileName,
        int flags);

    /// <summary>
    /// Fixed probe order built only from well-known directories (never
    /// <c>..</c>), so a hostile working directory cannot redirect the load.
    /// The framework-dependent <c>runtimes/&lt;rid&gt;/native</c> folder keeps
    /// the DevHost and unit tests working when the payload is not copied next
    /// to the module.
    /// </summary>
    internal static IReadOnlyList<string> CandidatePaths(
        string baseDirectory,
        string? processDirectory,
        string? cwd)
    {
        var directories = new List<string>();
        // The hub-reported core directory is the authoritative bundle location.
        if (ModulePaths.NativeDirectory is { } nativeDirectory)
        {
            directories.Add(nativeDirectory);
        }
        directories.AddRange(new[]
        {
            baseDirectory,
            Path.Combine(baseDirectory, "lib"),
            // macOS .app layout: Contents/MacOS -> Contents/Frameworks.
            Path.Combine(baseDirectory, "Frameworks"),
            Path.GetFullPath(Path.Combine(baseDirectory, "..", "Frameworks")),
        });

        if (!string.IsNullOrWhiteSpace(processDirectory))
        {
            directories.Add(processDirectory);
            directories.Add(Path.Combine(processDirectory, "lib"));
        }

        if (!string.IsNullOrWhiteSpace(cwd))
        {
            directories.Add(Path.Combine(cwd, "build", "native"));
            directories.Add(Path.Combine(cwd, "app", "build", "native"));
        }

        foreach (var rid in NativeRids())
        {
            directories.Add(Path.Combine(baseDirectory, "runtimes", rid, "native"));
        }

        return directories
            .Where(directory => !string.IsNullOrWhiteSpace(directory))
            .Distinct(PathSemantics.Comparer)
            .ToArray();
    }

    /// <summary>
    /// RIDs used by the framework-dependent layout. The runtime RID can be
    /// distro-specific (Arch Linux reports <c>arch-x64</c>), so the portable
    /// <c>{os}-{arch}</c> names that NuGet packages actually use are probed
    /// too.
    /// </summary>
    private static IEnumerable<string> NativeRids()
    {
        yield return RuntimeInformation.RuntimeIdentifier;

        var architecture = RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.X64 => "x64",
            Architecture.Arm64 => "arm64",
            Architecture.Arm => "arm",
            Architecture.X86 => "x86",
            var other => other.ToString().ToLowerInvariant(),
        };
        if (OperatingSystem.IsWindows()) yield return $"win-{architecture}";
        else if (OperatingSystem.IsMacOS()) yield return $"osx-{architecture}";
        else yield return $"linux-{architecture}";
    }

    /// <summary>
    /// Picks the first existing candidate. A missing explicit override is an
    /// error and never falls through to the bundled copies; no file anywhere
    /// returns <see langword="null"/> so the loader can report the actionable
    /// missing-payload message instead of a raw dlopen error.
    /// </summary>
    internal static string? Resolve(
        string? overridePath,
        IEnumerable<string> directories,
        string fileName)
    {
        ArgumentNullException.ThrowIfNull(directories);

        if (!string.IsNullOrWhiteSpace(overridePath))
        {
            if (File.Exists(overridePath)) return Path.GetFullPath(overridePath);
            throw new InvalidOperationException(
                $"{OverrideVariable} does not point to an existing file. " +
                $"Unset it to use the bundled {fileName}, or rebuild with " +
                "app/tool/build-cs.sh.");
        }

        foreach (var directory in directories)
        {
            var candidate = Path.Combine(directory, fileName);
            if (File.Exists(candidate)) return Path.GetFullPath(candidate);
        }

        return null;
    }

    /// <summary>
    /// Actionable failure used when the bundle was built without the SQLite
    /// payload. It names the expected file, the searched directories and the
    /// command that repairs the build; callers surface it as <c>E_INTERNAL</c>.
    /// </summary>
    internal static InvalidOperationException Missing(
        string fileName,
        IEnumerable<string> directories)
    {
        var searched = string.Join(
            Environment.NewLine,
            directories.Select(directory => $"  {directory}"));
        return new InvalidOperationException(
            $"The bundled SQLite native library {fileName} is missing. " +
            "ArchoeraCX ships SQLitePCLRaw's e_sqlite3 next to archoera_cs and " +
            "does not use the host's libsqlite3." +
            Environment.NewLine + "Searched directories:" +
            Environment.NewLine + searched +
            Environment.NewLine +
            $"Fix: run app/tool/build-cs.sh; it installs {fileName} into " +
            "build/native and the bundle copies it to lib/.");
    }

    /// <summary>
    /// Binds the provider's <c>DllImport("e_sqlite3")</c> to the exact handle
    /// we loaded. NativeAOT might refuse a resolver for an already compiled
    /// P/Invoke; the preload still satisfies a plain soname lookup in that
    /// case, so only the documented unsupported outcomes are tolerated and any
    /// other failure surfaces.
    /// </summary>
    private static void BindResolver(IntPtr handle)
    {
        try
        {
            NativeLibrary.SetDllImportResolver(
                typeof(SQLite3Provider_e_sqlite3).Assembly,
                (libraryName, _, _) =>
                    string.Equals(libraryName, "e_sqlite3", StringComparison.Ordinal) ||
                    string.Equals(libraryName, FileName, StringComparison.Ordinal)
                        ? handle
                        : IntPtr.Zero);
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or NotSupportedException)
        {
            // A resolver is already registered or unsupported; the preloaded
            // handle still satisfies the provider's soname lookup.
        }
    }
}
