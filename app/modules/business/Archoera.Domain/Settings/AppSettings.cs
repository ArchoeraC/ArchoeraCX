namespace Archoera.Domain.Settings;

/// <summary>
/// User-visible storage locations. The data root owns settings/cache/logs;
/// instances can live on multiple drives or folders.
/// </summary>
/// <param name="InstancesRoot">Primary instances directory (legacy, always first root).</param>
/// <param name="DataRoot">Absolute application data directory (read-only here).</param>
/// <param name="SettingsPath">Where this settings document is stored (diagnostics only).</param>
/// <param name="InstancesRoots">All configured instance directories; first is the default.</param>
public sealed record AppSettings(
    string InstancesRoot,
    string DataRoot,
    string SettingsPath,
    IReadOnlyList<string>? InstancesRoots = null)
{
    /// <summary>Current settings document schema.</summary>
    public const int CurrentSchemaVersion = 2;

    /// <summary>Configured roots, falling back to the legacy primary root.</summary>
    public IReadOnlyList<string> Roots =>
        InstancesRoots is { Count: > 0 } ? InstancesRoots : [InstancesRoot];
}
