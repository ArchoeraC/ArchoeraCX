using System.Runtime.Versioning;
using Microsoft.Win32;

namespace Archoera.Infrastructure.Launch;

/// <summary>
/// Windows only: records the "high performance" GPU preference for the Java
/// executable we are about to start. It is the same per-user switch that
/// Settings → System → Display → Graphics writes, and the display driver reads
/// it for OpenGL applications too. Linux and macOS do not need it: Linux uses
/// the PRIME environment variables, macOS switches GPUs on its own.
/// </summary>
internal static class WindowsGpuPreference
{
    private const string SubKey = @"Software\Microsoft\DirectX\UserGpuPreferences";
    private const string HighPerformance = "GpuPreference=2;";

    /// <summary>
    /// Best effort: returns true when the preference was newly written. Never
    /// throws, because a missing preference must not block a launch.
    /// </summary>
    public static bool TryPreferHighPerformance(string executablePath)
    {
        if (!OperatingSystem.IsWindows() ||
            string.IsNullOrWhiteSpace(executablePath))
        {
            return false;
        }

        try
        {
            return Apply(executablePath);
        }
        catch (Exception exception) when (
            exception is UnauthorizedAccessException or
            IOException or
            System.Security.SecurityException or
            PlatformNotSupportedException)
        {
            return false;
        }
    }

    [SupportedOSPlatform("windows")]
    private static bool Apply(string executablePath)
    {
        var full = Path.GetFullPath(executablePath);
        using var key = Registry.CurrentUser.CreateSubKey(SubKey, writable: true);
        if (key is null) return false;
        if (key.GetValue(full) is string existing &&
            string.Equals(existing, HighPerformance, StringComparison.Ordinal))
        {
            return false;
        }

        key.SetValue(full, HighPerformance, RegistryValueKind.String);
        return true;
    }
}
