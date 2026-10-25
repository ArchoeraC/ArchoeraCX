namespace Archoera.Domain.Settings;

/// <summary>
/// User-visible storage and runtime locations. The data root owns
/// settings/cache/logs; instances can live on multiple drives or folders.
/// </summary>
/// <param name="InstancesRoot">Primary instances directory (legacy alias).</param>
/// <param name="DataRoot">Absolute application data directory (read-only here).</param>
/// <param name="SettingsPath">Where this settings document is stored (diagnostics only).</param>
/// <param name="InstancesRoots">All configured instance directories; first is the default.</param>
/// <param name="JavaPath">Manually selected Java executable, or null for automatic selection.</param>
/// <param name="JavaAuto">True when launch should choose Java automatically.</param>
/// <param name="DownloadSource">`auto` (region based), `official` or `mirror`.</param>
/// <param name="DownloadConcurrency">Global download streams (1..64).</param>
/// <param name="DownloadPerHost">Streams per host (0..64, 0 = unlimited).</param>
/// <param name="DownloadProxy">Manual proxy URL (http/https); null follows the system.</param>
/// <param name="WindowWidth">Game window width in pixels when not fullscreen.</param>
/// <param name="WindowHeight">Game window height in pixels when not fullscreen.</param>
/// <param name="Fullscreen">Starts the game with the window maximized to fullscreen.</param>
/// <param name="DownloadChunkSizeKb">Chunk size for ranged downloads, in KiB.</param>
/// <param name="DownloadRangeThresholdKb">Files above this size use ranged chunks (0 disables).</param>
/// <param name="LogRetentionFiles">Launch logs kept per instance.</param>
/// <param name="LogRetentionMegabytes">Total launch-log budget per instance, in MiB.</param>
/// <param name="ShareLoaderFiles">
/// Installs Forge/NeoForge libraries and the merged loader profile once in the
/// game root so every instance of that loader build reuses them. When false,
/// each instance keeps its own <c>libraries/</c> and <c>versions/&lt;id&gt;/</c>.
/// </param>
/// <param name="MemoryMode">
/// <c>auto</c> derives the heap from the game version, loader, mod count and
/// installed RAM; <c>manual</c> uses <paramref name="MemoryMinMb"/> /
/// <paramref name="MemoryMaxMb"/> and only warns when they are too small.
/// </param>
/// <param name="GameOptimization">
/// Lets the launcher add JVM tuning and prefer the discrete GPU when starting
/// the game. Off keeps the launch line minimal.
/// </param>
/// <param name="WrapperCommand">
/// Optional command that wraps the JVM (for example a GPU launcher or a
/// sandbox helper). Null or empty means no wrapper; otherwise the command is
/// tokenized and receives the Java path and all JVM/game arguments.
/// </param>
/// <param name="Environment">
/// Extra environment variables for the child JVM. Null means none. Hostile
/// JVM option variables are rejected at the module boundary and stripped again
/// before the process starts.
/// </param>
/// <param name="PreLoadCommand">
/// Explicit launcher-side command run after the instance and effective
/// settings are known but before the version and libraries are resolved.
/// Tokenized without a shell; the <c>INST_*</c> environment variables describe
/// the instance. Empty or whitespace means no command.
/// </param>
/// <param name="PreLaunchCommand">
/// Explicit launcher-side command run immediately before the JVM starts, after
/// the launch plan is built. A non-zero exit, timeout or start failure aborts
/// the launch. Empty or whitespace means no command.
/// </param>
/// <param name="PostExitCommand">
/// Explicit launcher-side command run after the JVM exits. Best effort: a
/// failure is logged and never blocks or crashes the launcher. Empty or
/// whitespace means no command.
/// </param>
/// <param name="Maximized">
/// Starts the game in a maximized desktop window. The UI stores the primary
/// display size in <paramref name="WindowWidth"/> / <paramref name="WindowHeight"/>
/// so the game receives real numbers. Mutually exclusive with
/// <paramref name="Fullscreen"/>: the module rejects a document where both are
/// true, and fullscreen wins if a hand-edited file still contains both.
/// </param>
/// <param name="EnableGameMode">
/// Linux only. Prefixes the wrapper with <c>gamemoderun</c> (unless it is
/// already the first wrapper token). Ignored entirely on other platforms.
/// </param>
/// <param name="EnableMangoHud">
/// Linux only. Sets <c>MANGOHUD=1</c> in the child environment before the
/// user's own environment, so an explicit user value still wins.
/// </param>
/// <param name="UseZink">
/// Linux only. Sets <c>MESA_LOADER_DRIVER_OVERRIDE=zink</c> and
/// <c>GALLIUM_DRIVER=zink</c> before the user's own environment.
/// </param>
/// <param name="DeveloperMode">
/// Enables the advanced native-library overrides below. This is deliberately
/// unrelated to the download/compliance "developer mode" consent switch: it
/// only allows replacing the LWJGL GLFW/OpenAL/SDL native libraries on Linux.
/// Off means the saved paths are ignored.
/// </param>
/// <param name="NativeGlfwPath">
/// Absolute path of a GLFW native library used through
/// <c>-Dorg.lwjgl.glfw.libname</c>. Applied only when
/// <paramref name="DeveloperMode"/> is on, the OS is Linux and the file
/// exists; invalid or missing paths are ignored at launch.
/// </param>
/// <param name="NativeOpenALPath">
/// Absolute path of an OpenAL native library used through
/// <c>-Dorg.lwjgl.openal.libname</c>. Same gating as
/// <paramref name="NativeGlfwPath"/>.
/// </param>
/// <param name="NativeSdlPath">
/// Absolute path of an SDL native library used through
/// <c>-Dorg.lwjgl.sdl.libname</c>. Same gating as
/// <paramref name="NativeGlfwPath"/>.
/// </param>
public sealed record AppSettings(
    string InstancesRoot,
    string DataRoot,
    string SettingsPath,
    IReadOnlyList<string>? InstancesRoots = null,
    string? JavaPath = null,
    bool JavaAuto = true,
    int MemoryMinMb = 512,
    int MemoryMaxMb = 2048,
    string? JvmArgs = null,
    string GcMode = "auto",
    string? GameArgs = null,
    string DownloadSource = "auto",
    int DownloadConcurrency = 16,
    int DownloadPerHost = 8,
    string? DownloadProxy = null,
    int WindowWidth = 854,
    int WindowHeight = 480,
    bool Fullscreen = false,
    int DownloadChunkSizeKb = 4096,
    int DownloadRangeThresholdKb = 8192,
    int LogRetentionFiles = 10,
    int LogRetentionMegabytes = 32,
    bool ShareLoaderFiles = true,
    string MemoryMode = "auto",
    bool GameOptimization = true,
    string? WrapperCommand = null,
    IReadOnlyDictionary<string, string>? Environment = null,
    string? PreLoadCommand = null,
    string? PreLaunchCommand = null,
    string? PostExitCommand = null,
    bool Maximized = false,
    bool EnableGameMode = false,
    bool EnableMangoHud = false,
    bool UseZink = false,
    bool DeveloperMode = false,
    string? NativeGlfwPath = null,
    string? NativeOpenALPath = null,
    string? NativeSdlPath = null)
{
    /// <summary>Current settings document schema.</summary>
    public const int CurrentSchemaVersion = 15;

    /// <summary>Supported heap sizing modes.</summary>
    public static class MemoryModes
    {
        public const string Auto = "auto";
        public const string Manual = "manual";

        public static readonly string[] All = [Auto, Manual];
    }

    /// <summary>Configured roots, falling back to the legacy primary root.</summary>
    public IReadOnlyList<string> Roots =>
        InstancesRoots is { Count: > 0 } ? InstancesRoots : [InstancesRoot];
}
