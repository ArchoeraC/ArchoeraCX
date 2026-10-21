namespace Archoera.Domain.Settings;

/// <summary>
/// User-visible storage locations. The data root owns settings/cache/logs;
/// instances can live on a different drive or folder.
/// </summary>
/// <param name="InstancesRoot">Absolute directory containing instance folders.</param>
/// <param name="DataRoot">Absolute application data directory (read-only here).</param>
/// <param name="SettingsPath">Where this settings document is stored (diagnostics only).</param>
public sealed record AppSettings(
    string InstancesRoot,
    string DataRoot,
    string SettingsPath)
{
    /// <summary>Current settings document schema.</summary>
    public const int CurrentSchemaVersion = 1;
}
