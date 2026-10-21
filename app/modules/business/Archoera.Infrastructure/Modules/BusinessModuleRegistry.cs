using Archoera.Application.Modules;
using Archoera.Infrastructure.Accounts;
using Archoera.Infrastructure.Instances;
using Archoera.Infrastructure.Java;
using Archoera.Infrastructure.Loaders;
using Archoera.Infrastructure.Mojang;
using Archoera.Infrastructure.Resources;
using Archoera.Infrastructure.Settings;

namespace Archoera.Infrastructure.Modules;

/// <summary>
/// The single composition root for the C# business module. Every public
/// method family is registered here; the NativeAOT export layer only marshals
/// and dispatches through the resulting registry.
/// </summary>
public static class BusinessModuleRegistry
{
    public static ModuleMethodRegistry CreateDefault(string? dataRoot = null)
        => new(
        [
            new JavaModuleService(),
            new VersionModuleService(),
            new LoaderModuleService(),
            new ResourceModuleService(),
            new AccountModuleService(dataRoot: dataRoot),
            new SettingsModuleService(
                dataRoot is null ? null : new JsonAppSettingsStore(dataRoot)),
            new InstanceModuleService(
                dataRoot is null
                    ? null
                    : new SqliteInstanceRepository(
                        dataRoot,
                        new JsonAppSettingsStore(dataRoot)),
                dataRoot is null ? null : new JsonAppSettingsStore(dataRoot)),
        ]);
}
